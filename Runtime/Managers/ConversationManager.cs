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

namespace UG
{
    public class ConversationManager
    {
        // Subscribe to this event to receive all conversation events
        public event Action<ConversationEvent> OnConversationEvent;
        public bool IsConversationRunning => _currentState != ConversationState.Idle
            && _currentState != ConversationState.Error;
        public bool IsConversationPaused => _currentState == ConversationState.Paused;

        #region Dependencies
        private VoiceCaptureService _voiceCaptureService;
        private AudioStreamer _audioStreamer;
        private WebSocketService _webSocketService;
        private UGApiServiceV3 _ugApiServiceV3;
        #endregion

        #region Settings and configuration
        private UGSDKSettings _settings;
        private ConversationConfiguration _conversationSettings;
        private List<string> _onOutputUtilities = new List<string>();
        private List<string> _onInputUtilities = new List<string>();
        #endregion

        #region State and runtime variables
        private CancellationTokenSource _cancellationTokenSource;
        private ConversationState _currentState = ConversationState.Idle;
        private bool _isStreamMicrophoneData = false;
        private string _accessToken = null;
        private List<byte> _audioResponseDebugData = new List<byte>();
        #endregion

        public ConversationManager(
            WebSocketService webSocketService,
            UGApiServiceV3 ugApiServiceV3,
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
            _voiceCaptureService.OnTimeout += OnVoiceCaptureTimeout;
            _voiceCaptureService.OnHardTimeout += OnVoiceCaptureHardTimeout;
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

            _ = StartAddingMicrophoneAudioData();
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
                ResumeConversation();
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

        private void ResumeConversation()
        {
            UGLog.Log("ResumeConversation: " + _currentState);
            _audioStreamer.AudioOutputBenchmark.StartBenchmark(true);
            SetState(ConversationState.Processing);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing));

            var interactRequest = Messages.CreateInteractMessage(
                text: "[resume_conversation]",
                speakers: new List<string>(),
                context: new Dictionary<string, object> { },
                onInput: _onInputUtilities,
                onOutput: _onOutputUtilities,
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
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.TextReceived,
                        componentId: "",
                        new TextReceivedData(textEvent.Text)
                    ));
                    break;

                case AudioEvent audioEvent:
                    if (!string.IsNullOrEmpty(audioEvent.Audio))
                    {
                        byte[] audioData = Convert.FromBase64String(audioEvent.Audio);
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
                    OnConversationEvent?.Invoke(new ConversationEvent(
                        ConversationEventType.InteractionStarted,
                        componentId: "v3"
                    ));
                    break;

                case TextCompleteEvent:
                    UGLog.Log("Text complete");
                    break;

                case AudioCompleteEvent:
                    UGLog.Log("Audio complete");
                    // Play whatever is left without waiting for more chunks, mark stream as complete to call playback end event
                    _audioStreamer.ForcePlayChunks();
                    _audioStreamer.SetEndOfStream();
                    break;

                case InteractionCompleteEvent:
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.InteractionComplete, componentId: "v3"));
                    break;

                case InteractionErrorEvent errorEvent:
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Error, componentId: "v3", new ErrorData(errorEvent.Error)));
                    SetState(ConversationState.Error);
                    break;

                case DataEvent dataEvent:
                    UGLog.Log($"Data received: {dataEvent.Data}");
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
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Error, componentId: "v3", new ErrorData(errorResponse.Error)));
                    break;

                case GetConfigurationResponse configResponse:
                    UGLog.Log($"Configuration received: {configResponse.Config.Prompt}");
                    UGLog.Log($"Utilities count: {configResponse.Config.Utilities?.Count ?? 0}");
                    foreach (var utility in configResponse.Config.Utilities ?? new Dictionary<string, object>())
                    {
                        UGLog.Log($"Utility '{utility.Key}': {utility.Value?.GetType().Name}");
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
            yield return GetSetConfigurationMessage().ToJson();

            // get config
            GetConfigurationRequest getConfigRequest = Messages.CreateGetConfigurationMessage();

            await _webSocketService.SendMessageAsync(getConfigRequest.ToJson());

            var interactRequest = Messages.CreateInteractMessage(
                text: "hello", // Should be replaced with [start_conversation]
                speakers: new List<string>(),
                context: new Dictionary<string, object> { },
                onInput: _onInputUtilities,
                onOutput: _onOutputUtilities,
                audioOutput: true
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            await _webSocketService.SendMessageAsync(interactRequest.ToJson());
        }

        // Response parsing - return values are casted to the appropriate type
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

        // Can be applied in runtime
        public void SetConfiguration(ConversationConfiguration conversationConfiguration)
        {
            _conversationSettings = conversationConfiguration;

            UGLog.Log("[ConversationManager] SetConfiguration: " + conversationConfiguration.Prompt + " " + conversationConfiguration.Temperature + " " + conversationConfiguration.IsAllowInterrupts);
            SetConfigurationRequest setConfigRequest = GetSetConfigurationMessage();
            _ = _webSocketService.SendMessageAsync(setConfigRequest.ToJson());
        }

        private SetConfigurationRequest GetSetConfigurationMessage()
        {
            SetConfigurationRequest setConfigRequest = Messages.CreateSetConfigurationMessage(
                prompt: _conversationSettings.Prompt,
                temperature: _conversationSettings.Temperature,
                utilities: _conversationSettings.Utilities
            );
            UGLog.Log("Set configuration message: " + setConfigRequest.ToJson());
            return setConfigRequest;
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
            SetState(ConversationState.PlayerSpoke);

            // Send a message to clear audio first (in case it was not cleared from pause)
            var clearAudioRequest = Messages.CreateClearAudioMessage();
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

        private async Awaitable StartAddingMicrophoneAudioData()
        {
            // Switch to the backgroudn thread - we just keep this task running and waiting for when we have data
            await Awaitable.BackgroundThreadAsync();

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
            ConversationAudioUGLog.SaveAudioData(_audioResponseDebugData.ToArray(), DateTime.Now.Ticks.ToString());
            _audioResponseDebugData.Clear();
#endif

            var interactRequest = Messages.CreateInteractMessage(
                text: null,
                speakers: new List<string>(),
                context: new Dictionary<string, object> { },
                onInput: _onInputUtilities,
                onOutput: _onOutputUtilities,
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
            UGLog.Log("Voice capture timeout");
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.LongTimeoutTriggered, componentId: "v3"));
        }

        private void OnVoiceCaptureHardTimeout()
        {
            UGLog.Log("OnVoiceCaptureHardTimeout");
        }
        #endregion

        public void Dispose()
        {
            _voiceCaptureService.OnSpoke -= OnVoiceCaptureSpoke;
            _voiceCaptureService.OnSilenced -= OnVoiceCaptureSilenced;
            _voiceCaptureService.OnTimeout -= OnVoiceCaptureTimeout;
            _voiceCaptureService.OnHardTimeout -= OnVoiceCaptureHardTimeout;
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
            PauseConversation();
            Task.Run(async () =>
            {
                await _webSocketService.Close();
            });
            SetState(ConversationState.Idle);
        }

        public void ClearConversation()
        {
            SetState(ConversationState.Idle);

            // Cancel all pending tasks
            _cancellationTokenSource?.Cancel();

            // Stop voice capture & player
            StopAndClearVoiceRecording();
            _audioStreamer.Stop();
            _audioStreamer.Flush();

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
            _onOutputUtilities = utilities;
        }
        public void SetOnInputUtilities(List<string> utilities)
        {
            _onInputUtilities = utilities;
        }

        public void SetState(ConversationState state)
        {
            UGLog.Log("Set Conversation State: " + state);
            _currentState = state;
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