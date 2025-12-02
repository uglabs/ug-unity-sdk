using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UG.Services.UGApiService;
using UG.Services.WebSocket;
using UG.Services;
using UG.Settings;
using UnityEngine;
using UG.Services.AudioStreamingService;
using Newtonsoft.Json.Linq;
using UG.Models.WebSocketRequestMessages;
using UG.Models.WebSocketResponseMessages;
using UG.Utils;
using Newtonsoft.Json;

namespace UG
{
    public class ConversationManager
    {
        // Subscribe to this event to receive all conversation events
        public event Action<ConversationEvent> OnConversationEvent;
        public bool IsConversationRunning => _currentState != ConversationState.Idle
            && _currentState != ConversationState.Error;
        public bool IsConversationPaused => _currentState == ConversationState.Paused;

        // Access to conversation commands for external customization
        // You can set custom commands using ConversationCommands.Set method - e.g. what gets sent when conversation starts or resumes
        public ConversationCommands ConversationCommands => _conversationCommands;

        #region Dependencies
        private VoiceCaptureService _voiceCaptureService;
        private AudioStreamer _audioStreamer;
        private WebSocketService _webSocketService;
        private UGApiService _ugApiServiceV3;
        #endregion

        #region Settings and configuration
        private UGSDKSettings _settings;
        private ConversationConfiguration _conversationSettings;
        private ConversationCommands _conversationCommands = new ConversationCommands();
        private int _currentVoiceCaptureMaxSilentRetry = 0;
        #endregion

        #region State and runtime variables
        private CancellationTokenSource _cancellationTokenSource;
        private ConversationState _currentState = ConversationState.Idle;
        private bool _isStreamMicrophoneData = false;
        private string _accessToken = null;
        private List<byte> _audioResponseDebugData = new List<byte>();
        private bool _isTextComplete = false;
        #endregion

        public ConversationManager(
            WebSocketService webSocketService,
            UGApiService ugApiServiceV3,
            UGSDKSettings settings,
            AudioStreamer audioStreamer,
            VoiceCaptureService voiceCaptureService
        )
        {
            _webSocketService = webSocketService;
            _ugApiServiceV3 = ugApiServiceV3;
            _settings = settings;
            _audioStreamer = audioStreamer;
            _voiceCaptureService = voiceCaptureService;

            // Default configuration
            _conversationSettings = new ConversationConfiguration();

            // Setup voice capture events
            _voiceCaptureService.OnSpoke += OnVoiceCaptureSpoke;
            _voiceCaptureService.OnSilenced += OnVoiceCaptureSilenced;
            _voiceCaptureService.OnSilenceTimeout += OnVoiceCaptureTimeout;
            _voiceCaptureService.OnRecordingTooLong += OnVoiceCaptureSpeechTooLong;
            _voiceCaptureService.OnVADClosingTime += OnVoiceCaptureVADClosingTime;

            // Setup audio player events
            _audioStreamer.OnPlaybackTimeUpdate -= StreamerPlaybackTimeUpdate;
            _audioStreamer.OnPlaybackTimeUpdate += StreamerPlaybackTimeUpdate;
            _audioStreamer.OnPlaybackComplete += async () =>
            {
                UGLog.Log("Audio playbackcomplete; conv state: " + _currentState);
                if (!IsConversationRunning)
                {
                    return;
                }
                // SetState(ConversationState.PlayingComplete);
                OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.PlayingAudioComplete, componentId: "v3"));

                if (!_conversationSettings.IsAllowInterrupts)
                {
                    // Add a small delay to be safe - we still can't rely on unity's audio system to not lag behind even after we passed all samples
                    await Task.Delay(600);

                    // Start recording after playback is done, if interrupts are not enabled
                    StopAndClearVoiceRecording();
                    StartVoiceRecording();
                }
            };
            _audioStreamer.AudioOutputBenchmark.OnAudioPlaybackStarted += OnAudioPlaybackStarted;

            _ = Task.Run(async () => await StartAddingMicrophoneAudioData());
        }

        private void StreamerPlaybackTimeUpdate(float playbackTime)
        {
        }

        private void OnVoiceCaptureVADClosingTime(DateTime start, DateTime end)
        {
            UGLog.Log($"Set last vad time: {start} {end} => {(end - start).TotalSeconds}");
        }

        public void StartConversation()
        {
            if (_conversationSettings == null)
            {
                throw new InvalidOperationException("Conversation settings are not set, call SetConfiguration first");
            }

            StartConversation(_conversationSettings);
        }

        public async void StartConversation(ConversationConfiguration conversationConfiguration)
        {
            UGLog.Log("StartConversation: " + _currentState);
            if (_currentState != ConversationState.Idle)
            {
                ResumeConversation(_conversationCommands.PauseResumeCommand);
                return;
            }

            _conversationSettings = conversationConfiguration;

            _audioStreamer.AudioOutputBenchmark.StartBenchmark(true);
            SetState(ConversationState.Processing);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing));
            _cancellationTokenSource = new CancellationTokenSource();

            // Wait for auth token to be set before continuing
            if (string.IsNullOrEmpty(_accessToken))
            {
                while (string.IsNullOrEmpty(_accessToken))
                {
                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }
                    await Task.Delay(100);
                }
            }

            // Connect to the websocket & start sending messages
            await _webSocketService.Connect($"interact");
            var streamResponse = await _webSocketService.PostStreamingRequestStreamedAsyncV2(
                InteractSendStream(),
                _cancellationTokenSource.Token
            );

            var headers = streamResponse.headers;
            var responseStream = streamResponse.stream;

            // Process the response stream
            await foreach (var line in responseStream.WithCancellation(_cancellationTokenSource.Token))
            {
                var response = ParseResponse(line);
                if (response != null)
                {
                    OnResponseReceived(response);
                }
            }
        }

        // Resume conversation with a text command
        private void ResumeConversation(string textCommand)
        {
            if (!_webSocketService.IsConnected())
            {
                UGLog.LogError("ResumeConversation: Not connected - reconnecting...");
                // TODO: Reconnect if needed
                return;
            }

            // Clear audio because we can't send both commands and audio at the same time
            var clearAudioRequest = Messages.CreateClearAudioMessage();
            SendMessage(clearAudioRequest);

            UGLog.Log("ResumeConversation: " + _currentState);
            _audioStreamer.AudioOutputBenchmark.StartBenchmark(true);
            SetState(ConversationState.Processing);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing));

            var interactRequest = Messages.CreateInteractMessage(
                text: textCommand,
                speakers: new List<string>(),
                context: _conversationSettings.Context,
                onInput: _conversationSettings.OnInputUtilities,
                onOutput: _conversationSettings.OnOutputUtilities,
                audioOutput: true
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            SendMessage(interactRequest);
        }

        // Response stream handling
        private void OnResponseReceived(WebSocketResponseMessage response)
        {
            UGLog.Log($"V3 ConversationManager received: {response.GetType().Name}");

            // Handle different response types using type checking
            switch (response)
            {
                case TextEvent textEvent:
                    // Clear UI if this is the first text event
                    if (_isTextComplete)
                    {
                        OnConversationEvent?.Invoke(new ConversationEvent(
                            ConversationEventType.InteractionStarted,
                            componentId: "v3"
                        ));
                        _isTextComplete = false;
                    }

                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.TextReceived,
                        componentId: "",
                        new TextReceivedData(textEvent.Text)
                    ));
                    break;

                case AudioEvent audioEvent:
                    if (!string.IsNullOrEmpty(audioEvent.Audio))
                    {
                        byte[] audioData = Convert.FromBase64String(audioEvent.Audio);
#if DEBUG_SAVE_AUDIO
                        ConversationAudioDebug.SaveAudioData(audioData, DateTime.Now.Ticks.ToString(), ".byte");
#endif
                        OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.AudioReceived, componentId: "",
                            new AudioReceivedData(audioData)
                        ));
                        _audioStreamer.AddChunk(audioData);

                        // Start playing audio if not already playing
                        if (!_audioStreamer.IsStreaming())
                        {
                            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.PlayingAudio, componentId: "v3"));
                            _audioStreamer.Play();
                            SetState(ConversationState.Playing);

                            // Start recording here, when incoming audio starts playing
                            if (_conversationSettings.IsAllowInterrupts)
                            {
                                StopAndClearVoiceRecording();
                                StartVoiceRecording();
                            }
                        }
                    }
                    break;

                case InteractionStartedEvent:
                    UGLog.Log("Interaction started");

                    OnConversationEvent?.Invoke(new ConversationEvent(
                        ConversationEventType.InteractionStarted,
                        componentId: "v3"
                    ));
                    break;

                case TextCompleteEvent:
                    UGLog.Log("Text complete");
                    _isTextComplete = true;
                    break;

                case AudioCompleteEvent:
                    UGLog.Log("Audio complete");
                    // Play whatever is left without waiting for more chunks, mark stream as complete to call playback end event
                    _audioStreamer.ForcePlayChunks();
                    _audioStreamer.SetEndOfStream();
                    break;

                case InteractionCompleteEvent:
                    Debug.Log("<Interaction complete>");
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.InteractionComplete, componentId: "v3"));
                    break;

                case InteractionErrorEvent errorEvent:
                    EmitErrorEvent(errorEvent.Error);
                    SetState(ConversationState.Error);
                    break;

                case DataEvent dataEvent:
                    UGLog.Log($"Data received: {dataEvent.Data.Count} items");
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.DataReceived, componentId: "v3",
                        new DataReceivedData(dataEvent.Data)));
                    break;

                case AuthenticateResponse:
                    UGLog.Log("Authentication successful");
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.AuthenticationSuccessful, componentId: "v3"));
                    break;

                case ErrorResponse errorResponse:
                    UGLog.LogError($"Server error: {errorResponse.Error}");
                    SetState(ConversationState.Error);
                    EmitErrorEvent(errorResponse.Error);
                    break;

                case GetConfigurationResponse configResponse:
                    UGLog.Log($"Configuration received: {configResponse.Config.Prompt}");
                    UGLog.Log($"Utilities count: {configResponse.Config.Utilities?.Count ?? 0}");
                    foreach (var utility in configResponse.Config.Utilities ?? new Dictionary<string, object>())
                    {
                        UGLog.Log($"Utility '{utility.Key}': {utility.Value?.GetType().Name}");
                    }
                    UGLog.Log($"Context count: {configResponse.Config.Context?.Count ?? 0}");
                    foreach (var context in configResponse.Config.Context ?? new Dictionary<string, object>())
                    {
                        UGLog.Log($"Context '{context.Key}': {context.Value}");
                    }
                    break;

                case SetConfigurationResponse:
                    UGLog.Log("Configuration set successfully");
                    break;

                case CheckTurnResponse turnResponse:
                    UGLog.Log($"User still speaking: {turnResponse.IsUserStillSpeaking}");
                    break;

                case TranscribeResponse transcribeResponse:
                    UGLog.Log($"Transcription: {transcribeResponse.Text}");
                    break;

                case AddAudioResponse:
                    UGLog.Log("Audio added successfully");
                    break;

                case ClearAudioResponse:
                    UGLog.Log("Audio cleared successfully");
                    break;

                case InterruptResponse:
                    UGLog.Log("Interrupt successful");
                    break;

                default:
                    UGLog.Log($"Unhandled response type: {response.GetType().Name}");
                    break;
            }
        }

        private async IAsyncEnumerable<string> InteractSendStream()
        {
            // auth
            AuthenticateRequest authRequest = Messages.CreateAuthenticateMessage(_accessToken);
            yield return authRequest.ToJson();

            //set config
            yield return Messages.CreateSetConfigurationMessage(_conversationSettings).ToJson();

            // get config
            GetConfigurationRequest getConfigRequest = Messages.CreateGetConfigurationMessage();

            await _webSocketService.SendMessageAsync(getConfigRequest.ToJson());

            var interactRequest = Messages.CreateInteractMessage(
                text: _conversationCommands.StartCommand,
                speakers: new List<string>(),
                context: _conversationSettings.Context,
                onInput: _conversationSettings.OnInputUtilities,
                onOutput: _conversationSettings.OnOutputUtilities,
                audioOutput: true
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            await _webSocketService.SendMessageAsync(interactRequest.ToJson());
        }

        // Response parsing - return values are casted to the appropriate WebSocketResponseMessage type
        private WebSocketResponseMessage ParseResponse(string jsonLine)
        {
            try
            {
                var jObject = JObject.Parse(jsonLine);
                var kind = jObject["kind"]?.Value<string>();
                var type = jObject["type"]?.Value<string>();

                // Process interact stream messages
                if (type == "stream" && kind == "interact")
                {
                    var eventType = jObject["event"]?.Value<string>();
                    return eventType switch
                    {
                        "interaction_started" => jObject.ToObject<InteractionStartedEvent>(),
                        "text" => jObject.ToObject<TextEvent>(),
                        "text_complete" => jObject.ToObject<TextCompleteEvent>(),
                        "audio" => jObject.ToObject<AudioEvent>(),
                        "audio_complete" => jObject.ToObject<AudioCompleteEvent>(),
                        "interaction_complete" => jObject.ToObject<InteractionCompleteEvent>(),
                        "interaction_error" => jObject.ToObject<InteractionErrorEvent>(),
                        "data" => jObject.ToObject<DataEvent>(),
                        _ => jObject.ToObject<InteractResponse>() // fallback to base InteractResponse
                    };
                }

                // Process other stream messages - stream, non-interact
                if (type == "stream")
                {
                    // kind - close
                }

                // Parse by kind for non-interact messages
                return kind switch
                {
                    "authenticate" => jObject.ToObject<AuthenticateResponse>(),
                    "error" => jObject.ToObject<ErrorResponse>(),
                    "check_turn" => jObject.ToObject<CheckTurnResponse>(),
                    "transcribe" => jObject.ToObject<TranscribeResponse>(),
                    "add_audio" => jObject.ToObject<AddAudioResponse>(),
                    "clear_audio" => jObject.ToObject<ClearAudioResponse>(),
                    "set_configuration" => jObject.ToObject<SetConfigurationResponse>(),
                    "get_configuration" => jObject.ToObject<GetConfigurationResponse>(),
                    "interrupt" => jObject.ToObject<InterruptResponse>(),
                    _ => jObject.ToObject<WebSocketResponseMessage>()
                };
            }
            catch (Exception ex)
            {
                UGLog.LogError($"Failed to parse response: {ex.Message}");
                UGLog.LogError($"Raw line: {jsonLine}");
                return null;
            }
        }

        public void SetAccessToken(string accessToken)
        {
            _accessToken = accessToken;
        }

        public void SetConfigurationFromJson(string configurationJson)
        {
            try
            {
                _conversationSettings = JsonConvert.DeserializeObject<ConversationConfiguration>(configurationJson);
                SetConfiguration(_conversationSettings);
            }
            catch (Exception ex)
            {
                UGLog.LogError($"Failed to set configuration from JSON: {ex.Message}");
            }
        }

        // Can be applied in runtime
        public void SetConfiguration(ConversationConfiguration conversationConfiguration)
        {
            _conversationSettings = conversationConfiguration;

            UGLog.Log("[ConversationManager] SetConfiguration: " + conversationConfiguration.Prompt + " " + conversationConfiguration.Temperature + " " + conversationConfiguration.IsAllowInterrupts);
            SetConfigurationRequest setConfigRequest = Messages.CreateSetConfigurationMessage(_conversationSettings);
            _ = _webSocketService.SendMessageAsync(setConfigRequest.ToJson());
        }


        #region Voice recording and VAD functions
        private void StartVoiceRecording()
        {
            if (!IsConversationRunning)
            {
                return;
            }

            if (_currentState == ConversationState.Paused)
            {
                return;
            }

            SetState(ConversationState.RecordingStart);
            _voiceCaptureService.StartRecording();
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.RecordingMicrophone, componentId: "v3", new RecordingMicrophoneData(true)));
        }

        private void StopAndClearVoiceRecording()
        {
            _voiceCaptureService.Clear();
            _voiceCaptureService.StopRecording();
        }

        // When user speech is detected
        private void OnVoiceCaptureSpoke()
        {
            _currentVoiceCaptureMaxSilentRetry = 0;

            SetState(ConversationState.PlayerSpoke);

            // Send a message to clear audio first (in case it was not cleared from pause)
            var clearAudioRequest = Messages.CreateClearAudioMessage();
            // _ = _webSocketService.SendMessageAsync(clearAudioRequest.ToJson());
            SendMessage(clearAudioRequest);

            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.PlayerSpoke, componentId: "v3"));
            bool isInterrupt = _audioStreamer.IsStreaming(); // will this interrupt audio stream?

            if (isInterrupt)
            {
                // TODO: Handle interrupts
            }

            // Starts sending audio with AddAudio messages
            _isStreamMicrophoneData = true;
        }

        private async Task StartAddingMicrophoneAudioData()
        {
            List<byte> accumulatedBytes = new List<byte>();

            while (true)
            {
                if (!_isStreamMicrophoneData)
                {
                    await Task.Delay(50);
                    accumulatedBytes.Clear();
                    continue;
                }

                // Dequeue all data we have from VoiceCaptureService at this point
                while (_voiceCaptureService._micEncodedStreamDataQueue.TryDequeue(out byte[] buffer))
                {
                    UGLog.Log("Dequeued audio data: " + buffer.Length);
                    accumulatedBytes.AddRange(buffer);
                }

                bool isRecording = _voiceCaptureService.State == VoiceCaptureService.VoiceCaptureState.Recording;
                bool isRecordingStoppedAndHaveData = !isRecording && accumulatedBytes.Count > 0;
                bool isAllDataProcessed = _voiceCaptureService.IsAllDataProcessed();
                bool isBufferEmpty = accumulatedBytes.Count == 0;

                if (isRecordingStoppedAndHaveData || accumulatedBytes.Count >= 512)
                {
                    UGLog.Log($"[WS] Sending accumulated audio bytes: {accumulatedBytes.Count}");

                    await _webSocketService.SendMessageAsync(
                        Messages.CreateAddAudioMessage(Convert.ToBase64String(accumulatedBytes.ToArray()),
                            new AudioConfig { SampleRate = 16000, MimeType = "audio/ogg" }).ToJson()
                    );

#if UNITY_EDITOR && DEBUG_SAVE_AUDIO
                    _audioResponseDebugData.AddRange(accumulatedBytes);
#endif
                    accumulatedBytes.Clear();
                }

                if (!isRecording && isAllDataProcessed && isBufferEmpty)
                {
                    UGLog.Log("[WS] Processing complete");
                    break;
                }

                await Task.Delay(10);
            }
        }

        private void OnVoiceCaptureSilenced()
        {
            // If TurnTaker is not enabled, we just finish
            // If enabled, we just send check turn event but keep sending audio data (check_turn)

            // Handle max silent retry
            if (_currentVoiceCaptureMaxSilentRetry >= _conversationSettings.VoiceCaptureMaxSilentRetry)
            {
                UGLog.LogError("Voice capture max silent retry reached, stopping conversation");
                StopConversation();
                return;
            }
            _currentVoiceCaptureMaxSilentRetry++;

            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.MicrophoneSilenced, componentId: "v3"));

            // Start RTT benchmark
            _audioStreamer.AudioOutputBenchmark.StartBenchmark(false);

            // Stop recording voice
            // _voiceCaptureService.Flush(); // TODO: Make sure to flush the data properly
            _voiceCaptureService.Clear();
            _voiceCaptureService.StopRecording();
            _isStreamMicrophoneData = false;

            // Stop audio stream
            _audioStreamer.Stop();
            _audioStreamer.Flush();

#if UNITY_EDITOR && DEBUG_SAVE_AUDIO
            ConversationAudioDebug.SaveAudioData(_audioResponseDebugData.ToArray(), DateTime.Now.Ticks.ToString());
            _audioResponseDebugData.Clear();
#endif

            var interactRequest = Messages.CreateInteractMessage(
                text: null,
                speakers: new List<string>(),
                context: _conversationSettings.Context,
                onInput: _conversationSettings.OnInputUtilities,
                onOutput: _conversationSettings.OnOutputUtilities,
                audioOutput: true
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            SendMessage(interactRequest);
        }

        private void SendMessage(WebSocketRequestMessage requestMessage)
        {
            Task.Run(async () =>
            {
                await _webSocketService.SendMessageAsync(requestMessage.ToJson());
            });
        }

        private void OnVoiceCaptureTimeout()
        {
            UGLog.Log("Voice capture timeout - no speech detected for a while");

            // Emit timeout event
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.VoiceCaptureNoSpeechTimeout, componentId: ""));

            // Conversation is automatically paused and we send resume message
            PauseConversation();
            ResumeConversation(_conversationCommands.PauseResumeCommand);
        }

        private void OnVoiceCaptureSpeechTooLong()
        {
            UGLog.Log("Voice capture timeout - speech was too long to process");

            // Emit speech too long event - can be ignored or logged as a warning
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.VoiceCaptureSpeechTooLong, componentId: ""));

            // Consider this as audio silenced
            OnVoiceCaptureSilenced();
        }
        #endregion

        public void Dispose()
        {
            _voiceCaptureService.OnSpoke -= OnVoiceCaptureSpoke;
            _voiceCaptureService.OnSilenced -= OnVoiceCaptureSilenced;
            _voiceCaptureService.OnSilenceTimeout -= OnVoiceCaptureTimeout;
            _voiceCaptureService.OnRecordingTooLong -= OnVoiceCaptureSpeechTooLong;
            _voiceCaptureService.OnVADClosingTime -= OnVoiceCaptureVADClosingTime;

            _cancellationTokenSource?.Dispose();
        }

        public void PauseConversation()
        {
            // Kill audio playback,
            _audioStreamer.Stop();
            _audioStreamer.Flush();

            // Stop voice capture
            _voiceCaptureService.StopRecording();
            _voiceCaptureService.Clear();

            // Send a message to clear audio we already sent
            var clearAudioRequest = Messages.CreateClearAudioMessage();
            SendMessage(clearAudioRequest);

            // Set conversation state to paused - we will send [resume_conversation] text on next run
            SetState(ConversationState.Paused);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.ConversationPaused));
        }

        public void StopConversation()
        {
            _currentVoiceCaptureMaxSilentRetry = 0;
            PauseConversation();
            Task.Run(async () =>
            {
                await _webSocketService.Close();
                OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Stopped));
            });
            SetState(ConversationState.Idle);
        }

        public void ClearConversation()
        {
            SetState(ConversationState.Idle);

            _currentVoiceCaptureMaxSilentRetry = 0;

            // Cancel all pending tasks
            _cancellationTokenSource?.Cancel();

            // Stop voice capture & player
            StopAndClearVoiceRecording();
            _audioStreamer.Stop();
            _audioStreamer.Flush();

            // Clear configuartion
            _conversationSettings = new ConversationConfiguration();
            _conversationSettings.OnInputUtilities = new List<string>();
            _conversationSettings.OnOutputUtilities = new List<string>();

            // Close websocket
            Task.Run(async () =>
            {
                try
                {
                    await _webSocketService.Close();
                }
                catch (Exception ex)
                {
                    UGLog.LogWarning("[ConversationFlowService] Error closing websocket: " + ex.Message);
                }
            });
        }

        public void SetOnOutputUtilities(List<string> utilities)
        {
            _conversationSettings.OnOutputUtilities = utilities;
        }
        public void SetOnInputUtilities(List<string> utilities)
        {
            _conversationSettings.OnInputUtilities = utilities;
        }

        public void SetState(ConversationState state)
        {
            UGLog.Log("Set Conversation State: " + state);
            _currentState = state;
        }

        public void EmitErrorEvent(string error)
        {
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Error,
                componentId: "",
                new ErrorData(error))
            );
        }

        /// <summary>
        /// Interrupt audio playback/voice recording and send a text message
        /// </summary>
        /// <param name="text"></param>
        public async void SendTextMessage(string text, bool isAudioOutput)
        {
            if (!IsConversationRunning || _webSocketService == null || !_webSocketService.IsConnected())
            {
                UGLog.LogWarning("[ConversationManager] SendTextMessage: Conversation is not running or websocket is not connected");
                return;
            }

            // Interrupt audio and voice capture
            if (_audioStreamer.IsStreaming())
            {
                _audioStreamer.Stop();
                _audioStreamer.Flush();
            }
            if (_voiceCaptureService.State != VoiceCaptureService.VoiceCaptureState.Idle)
            {
                _voiceCaptureService.Clear();
                _voiceCaptureService.StopRecording();
            }

            SetState(ConversationState.Processing);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing));

            // Send a message to clear audio we already sent
            var clearAudioRequest = Messages.CreateClearAudioMessage();
            await _webSocketService.SendMessageAsync(clearAudioRequest.ToJson());

            // Update configuartion (audio/no audio flag)
            // _conversationSettings.IsAudioOutput = isAudioOutput;
            // SetConfiguration(_conversationSettings);

            var interactRequest = Messages.CreateInteractMessage(
                text: text,
                speakers: new List<string>(),
                context: _conversationSettings.Context,
                onInput: _conversationSettings.OnInputUtilities,
                onOutput: _conversationSettings.OnOutputUtilities,
                audioOutput: isAudioOutput
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            SendMessage(interactRequest);
        }

        #region Benchmark
        public Action<double, bool> BenchmarkRTTReceived;

        private void OnAudioPlaybackStarted(double timeStartedIn, bool isInitialBenchmark)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                BenchmarkRTTReceived?.Invoke(timeStartedIn, isInitialBenchmark);
            });
        }

        public ConversationConfiguration GetConfiguration()
        {
            return _conversationSettings;
        }
        #endregion
    }

    public enum ConversationState
    {
        Idle,
        Processing, // no interrupt
        RecordingStart, // no interrupt
        PlayerSpoke,
        Playing, // playing audio, can interrupt
        Error,
        Paused
    }
}