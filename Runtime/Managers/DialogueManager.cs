using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Multi-character conversation manager - supports conversations between 2-5 characters
    /// conversation_history context value is added automatically to keep all participants up to date
    /// </summary>
    public class DialogueManager : BaseConversationFlowManager
    {
        public event Action<ConversationEvent, int> OnConversationEvent;
        public event Action OnConversationComplete; // called when the conversation is complete/reached maximum number of messages
        public bool IsConversationRunning => _currentState != DialogueState.Idle
            && _currentState != DialogueState.Error;
        public bool IsConversationPaused => _currentState == DialogueState.Paused;
        public int CharacterCount => _webSocketServices?.Count ?? 0;

        #region Dependencies
        private AudioStreamer _audioStreamer;
        private List<WebSocketService> _webSocketServices = new List<WebSocketService>();
        private UGApiService _ugApiServiceV3;
        #endregion

        #region Settings and configuration
        private UGSDKSettings _settings;
        private List<ParticipantData> _participants = new List<ParticipantData>();
        private List<string> _onOutputUtilities = new List<string>();
        private List<string> _onInputUtilities = new List<string>();
        private ConversationCommands _conversationCommands = new ConversationCommands();
        public List<ConversationHistory> ConversationHistory => _conversationHistory;
        #endregion

        #region State and runtime variables
        private CancellationTokenSource _cancellationTokenSource;
        private DialogueState _currentState = DialogueState.Idle;
        private string _accessToken = null;
        private int _turn = 0;
        private List<ConversationHistory> _conversationHistory = new List<ConversationHistory>();
        private int _maxTurns = 0; // Calculated as maxTurnsPerParticipant * participants.Count
        #endregion

        public DialogueManager(AudioStreamer audioStreamer, List<WebSocketService> webSocketServices, UGApiService ugApiServiceV3)
        {
            if (webSocketServices == null || webSocketServices.Count < 2)
            {
                throw new ArgumentException("DialogueManager requires at least 2 WebSocketServices", nameof(webSocketServices));
            }
            if (webSocketServices.Count > 5)
            {
                throw new ArgumentException("DialogueManager supports maximum 5 WebSocketServices", nameof(webSocketServices));
            }

            _audioStreamer = audioStreamer;
            _webSocketServices = webSocketServices;
            _ugApiServiceV3 = ugApiServiceV3;

            // Initialize participants for each character
            for (int i = 0; i < _webSocketServices.Count; i++)
            {
                _participants.Add(new ParticipantData
                {
                    ParticipantIndex = i,
                    ParticipantName = $"player_turn_{i}",
                    ConversationConfiguration = new ConversationConfiguration()
                });
            }

            // Setup audio player events
            _audioStreamer.OnPlaybackComplete += async () =>
            {
                UGLog.Log("Audio playbackcomplete; conv state: " + _currentState);

                if (!IsConversationRunning)
                {
                    return;
                }

                await Task.Delay(100);

                if (_maxTurns > 0 && _turn >= _maxTurns)
                {
                    Debug.LogError($"Dialogue - playback complete; turn {_turn} >= maxTurns {_maxTurns}");
                    OnConversationComplete?.Invoke();
                    return;
                }

                if (_participants.Count == 0)
                {
                    Debug.LogError("Dialogue - playback complete; no participants configured");
                    return;
                }

                int currentCharacterIndex = _turn % _participants.Count;
                string previousDialogueMessage = "";
                if (_turn > 0 && _conversationHistory.Count > 0)
                {
                    // Get the last message from conversation history
                    var lastHistory = _conversationHistory[_conversationHistory.Count - 1];
                    previousDialogueMessage = lastHistory.ParticipantName + ": " + (lastHistory?.Message ?? "");
                }
                var currentWebSocket = _webSocketServices[currentCharacterIndex];
                var currentParticipant = _participants[currentCharacterIndex];
                var currentConfig = currentParticipant.ConversationConfiguration;

                // Provide full history except for the last message
                currentConfig.Context["conversation_history"] = GetFullHistory(isSkipLast: true);

                Debug.Log($"Dialogue - playback complete; character index: {currentCharacterIndex}, connected: {currentWebSocket.IsConnected()}");
                Debug.Log("Send text message to dialogue: " + previousDialogueMessage);

                // Emit interaction started event so that we know when to clear the UI
                OnConversationEvent?.Invoke(new ConversationEvent(
                        ConversationEventType.InteractionStarted,
                        componentId: "v3"
                    ), _turn);

                if (!currentWebSocket.IsConnected())
                {
                    // Connect websocket for initial message
                    string connectionId = $"websocket{currentCharacterIndex + 1}";
                    await ConnectAndProcessWebSocket(currentWebSocket, currentConfig, connectionId);
                }
                else
                {
                    // Send response to the current websocket
                    if (!string.IsNullOrEmpty(previousDialogueMessage))
                    {
                        var interactRequest = Messages.CreateInteractMessage(
                            text: previousDialogueMessage,
                            speakers: new List<string>(),
                            context: currentConfig.Context,
                            onInput: _onInputUtilities,
                            onOutput: _onOutputUtilities,
                            audioOutput: true
                        );

                        await currentWebSocket.SendMessageAsync(interactRequest.ToJson());
                    }
                }

                // SetState(ConversationState.PlayingComplete);
                OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.PlayingAudioComplete, componentId: "dialogue_1"), _turn);
            };
        }

        // TODO: Apply in runtime?
        public void SetParticipants(List<ParticipantData> participants)
        {
            if (participants == null || participants.Count < 2 || participants.Count > 5)
            {
                throw new ArgumentException($"SetParticipants requires 2-5 participants, got {participants?.Count ?? 0}", nameof(participants));
            }
            if (participants.Count > _webSocketServices.Count)
            {
                throw new ArgumentException($"SetParticipants requires at most {_webSocketServices.Count} participants (matching available WebSocket services), got {participants.Count}", nameof(participants));
            }

            _participants.Clear();
            _participants.AddRange(participants);

            if (participants.Count > 0)
            {
                UGLog.Log("[DialogueManager] SetParticipants: " + participants[0].ParticipantName + " " + participants[0].ConversationConfiguration.Prompt);
            }
            // SetConfigurationRequest setConfigRequest = Messages.CreateSetConfigurationMessage(_participants[0].ConversationConfiguration);
            // _ = _webSocketServices[0].SendMessageAsync(setConfigRequest.ToJson());
        }

        // Backward compatibility method for 2-character dialogue
        public void SetConfiguration(ConversationConfiguration conversationConfiguration, ConversationConfiguration conversationConfigurationDialogue)
        {
            var participants = new List<ParticipantData>
            {
                new ParticipantData
                {
                    ParticipantIndex = 0,
                    ParticipantName = "player_turn_0",
                    ConversationConfiguration = conversationConfiguration
                },
                new ParticipantData
                {
                    ParticipantIndex = 1,
                    ParticipantName = "player_turn_1",
                    ConversationConfiguration = conversationConfigurationDialogue
                }
            };
            SetParticipants(participants);
        }

        public void SetConfigurationFromJson(List<string> configurationJsons)
        {
            if (configurationJsons == null || configurationJsons.Count < 2 || configurationJsons.Count > 5)
            {
                throw new ArgumentException($"SetConfigurationFromJson requires 2-5 JSON configurations, got {configurationJsons?.Count ?? 0}", nameof(configurationJsons));
            }
            if (configurationJsons.Count > _webSocketServices.Count)
            {
                throw new ArgumentException($"SetConfigurationFromJson requires at most {_webSocketServices.Count} JSON configurations (matching available WebSocket services), got {configurationJsons.Count}", nameof(configurationJsons));
            }

            var participants = new List<ParticipantData>();
            for (int i = 0; i < configurationJsons.Count; i++)
            {
                var config = JsonConvert.DeserializeObject<ConversationConfiguration>(configurationJsons[i]);
                participants.Add(new ParticipantData
                {
                    ParticipantIndex = i,
                    ParticipantName = $"player_turn_{i}",
                    ConversationConfiguration = config
                });
            }

            SetParticipants(participants);
            if (_participants.Count > 0)
            {
                UGLog.Log("[DialogueManager] SetConfigurationFromJson: " + _participants[0].ParticipantName + " " + _participants[0].ConversationConfiguration.Prompt);
            }
        }

        public List<ParticipantData> GetParticipants()
        {
            return _participants;
        }

        public void StartConversation(int maxTurnsPerParticipant = 0)
        {
            if (_participants == null || _participants.Count < 2 || _participants.Count > 5)
            {
                throw new InvalidOperationException($"Participants count ({_participants?.Count ?? 0}) must be between 2-5, call SetParticipants first");
            }
            if (_participants.Count > _webSocketServices.Count)
            {
                throw new InvalidOperationException($"Participants count ({_participants.Count}) exceeds available WebSocket services ({_webSocketServices.Count})");
            }

            foreach (var participant in _participants)
            {
                if (participant == null || participant.ConversationConfiguration == null)
                {
                    throw new InvalidOperationException("Participants are not properly configured, call SetParticipants first");
                }
            }

            var configurations = _participants.Select(p => p.ConversationConfiguration).ToList();
            StartConversation(configurations, maxTurnsPerParticipant);
        }

        public async void StartConversation(List<ConversationConfiguration> configurations, int maxTurnsPerParticipant = 1)
        {
            if (configurations == null || configurations.Count < 2 || configurations.Count > 5)
            {
                throw new ArgumentException($"StartConversation requires 2-5 configurations, got {configurations?.Count ?? 0}", nameof(configurations));
            }
            if (configurations.Count > _webSocketServices.Count)
            {
                throw new ArgumentException($"StartConversation requires at most {_webSocketServices.Count} configurations (matching available WebSocket services), got {configurations.Count}", nameof(configurations));
            }

            UGLog.Log("StartConversation: " + _currentState);

            // TODO: Check pause dialogue logic
            if (_currentState != DialogueState.Idle)
            {
                ResumeConversation(_conversationCommands.PauseResumeCommand);
                return;
            }

            // Update participants with new configurations if needed
            if (_participants.Count == 0 || _participants.Count != configurations.Count)
            {
                _participants.Clear();
                for (int i = 0; i < configurations.Count; i++)
                {
                    _participants.Add(new ParticipantData
                    {
                        ParticipantIndex = i,
                        ParticipantName = $"player_turn_{i}",
                        ConversationConfiguration = configurations[i]
                    });
                }
            }
            else
            {
                // Update existing participants' configurations
                for (int i = 0; i < configurations.Count; i++)
                {
                    _participants[i].ConversationConfiguration = configurations[i];
                }
            }

            // Reset conversation state
            _turn = 0;
            _conversationHistory.Clear();
            _maxTurns = maxTurnsPerParticipant > 0 ? maxTurnsPerParticipant * _participants.Count : 0; // 0 means unlimited

            SetState(DialogueState.Processing);
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing), _turn);
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

            // Start the main websocket (primary character) 
            Debug.Log("Dialogue websocket1 connecting");
            Debug.Log("Available participants: " + _participants.Count + " available websockets: " + _webSocketServices.Count);
            _participants[0].ConversationConfiguration.Context["conversation_history"] = ""; // empty conversation history at start
            await ConnectAndProcessWebSocket(_webSocketServices[0], _participants[0].ConversationConfiguration, "websocket1");
        }

        // Backward compatibility method for 2-character dialogue
        public void StartConversation(
            ConversationConfiguration conversationConfiguration1,
            ConversationConfiguration conversationConfiguration2,
            int maxTurnsPerParticipant = 0)
        {
            StartConversation(new List<ConversationConfiguration> { conversationConfiguration1, conversationConfiguration2 }, maxTurnsPerParticipant);
        }

        private async Task ConnectAndProcessWebSocket(
            WebSocketService webSocketService,
            ConversationConfiguration conversationConfiguration,
            string connectionId)
        {
            Debug.Log("Dialogue websocket " + connectionId + " connecting");
            // Connect to the websocket & start sending messages
            await webSocketService.Connect($"interact");
            var streamResponse = await webSocketService.PostStreamingRequestStreamedAsyncV2(
                InteractSendStream(_conversationCommands.StartCommand, webSocketService, conversationConfiguration),
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

        private void ResumeConversation(string textCommand)
        {
            if (_participants.Count == 0)
            {
                UGLog.LogError("ResumeConversation: No participants configured");
                return;
            }
            int currentCharacterIndex = _turn % _participants.Count;
            var currentWebSocket = _webSocketServices[currentCharacterIndex];

            if (!currentWebSocket.IsConnected())
            {
                UGLog.LogError($"ResumeConversation: Character {currentCharacterIndex} not connected - reconnecting...");
                // TODO: Reconnect if needed
                return;
            }

            // TODO: Add reconnect logic
            // UGLog.Log("ResumeConversation: " + _currentState);
            // _audioStreamer.AudioOutputBenchmark.StartBenchmark(true);
            // SetState(ConversationState.Processing);
            // OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Processing));

            // var interactRequest = Messages.CreateInteractMessage(
            //     text: textCommand,
            //     speakers: new List<string>(),
            //     context: _conversationSettings.Context,
            //     onInput: _onInputUtilities,
            //     onOutput: _onOutputUtilities,
            //     audioOutput: true
            // );
            // UGLog.Log("Interact message: " + interactRequest.ToJson());
            // SendMessage(interactRequest);
        }

        public void SetState(DialogueState state)
        {
            UGLog.Log("Set Dialogue State: " + state);
            _currentState = state;
        }

        private async IAsyncEnumerable<string> InteractSendStream(
            string message,
            WebSocketService webSocketService,
            ConversationConfiguration conversationConfiguration)
        {
            // auth
            AuthenticateRequest authRequest = Messages.CreateAuthenticateMessage(_accessToken);
            yield return authRequest.ToJson();

            //set config
            yield return Messages.CreateSetConfigurationMessage(conversationConfiguration).ToJson();

            var interactRequest = Messages.CreateInteractMessage(
                text: message,
                speakers: new List<string>(),
                context: conversationConfiguration.Context,
                onInput: _onInputUtilities,
                onOutput: _onOutputUtilities,
                audioOutput: true
            );
            UGLog.Log("Interact message: " + interactRequest.ToJson());
            await webSocketService.SendMessageAsync(interactRequest.ToJson());
        }

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
                    ), _turn);

                    // Find or create conversation history entry for this turn
                    int currentCharacterIndex = _turn % _participants.Count;
                    var currentParticipant = _participants[currentCharacterIndex];

                    var historyEntry = _conversationHistory.FirstOrDefault(h => h.Turn == _turn);
                    if (historyEntry == null)
                    {
                        historyEntry = new ConversationHistory
                        {
                            ParticipantName = currentParticipant.ParticipantName,
                            Message = textEvent.Text,
                            Turn = _turn
                        };
                        _conversationHistory.Add(historyEntry);
                    }
                    else
                    {
                        // Append to existing message
                        historyEntry.Message += textEvent.Text;
                    }
                    break;

                case AudioEvent audioEvent:
                    if (!string.IsNullOrEmpty(audioEvent.Audio))
                    {
                        byte[] audioData = Convert.FromBase64String(audioEvent.Audio);
                        OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.AudioReceived, componentId: "",
                            new AudioReceivedData(audioData)
                        ), _turn);
#if DEBUG_SAVE_AUDIO
                        ConversationAudioDebug.SaveAudioData(audioData, DateTime.Now.Ticks.ToString(), ".byte");
#endif
                        _audioStreamer.AddChunk(audioData);

                        // Start playing audio if not already playing
                        if (!_audioStreamer.IsStreaming())
                        {
                            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.PlayingAudio, componentId: "v3"), _turn);

                            _audioStreamer.Play();
                            SetState(DialogueState.Playing);
                        }
                    }
                    break;

                case InteractionStartedEvent:
                    OnConversationEvent?.Invoke(new ConversationEvent(
                        ConversationEventType.InteractionStarted,
                        componentId: "v3"
                    ), _turn);
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
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.InteractionComplete, componentId: "v3"), _turn);
                    _turn++;
                    break;

                case InteractionErrorEvent errorEvent:
                    EmitErrorEvent(errorEvent.Error);
                    SetState(DialogueState.Error);
                    break;

                case DataEvent dataEvent:
                    UGLog.Log($"Data received: {dataEvent.Data.Count} items");
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.DataReceived, componentId: "v3",
                        new DataReceivedData(dataEvent.Data)), _turn);
                    break;

                case AuthenticateResponse:
                    UGLog.Log("Authentication successful");
                    OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.AuthenticationSuccessful, componentId: "v3"), _turn);
                    break;

                case ErrorResponse errorResponse:
                    UGLog.LogError($"Server error: {errorResponse.Error}");
                    SetState(DialogueState.Error);
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

        public string GetFullHistory(bool isSkipLast = false)
        {
            if (_conversationHistory == null || _conversationHistory.Count == 0)
            {
                return string.Empty;
            }

            var historyLines = new List<string>();
            int count = isSkipLast ? _conversationHistory.Count - 1 : _conversationHistory.Count;

            // If skipping last and only one entry exists, return empty string
            if (count <= 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < count; i++)
            {
                var entry = _conversationHistory[i];
                historyLines.Add($"{entry.ParticipantName}: {entry.Message}");
            }

            return string.Join("\n", historyLines);
        }

        public void EmitErrorEvent(string error)
        {
            OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Error,
                componentId: "",
                new ErrorData(error))
            , _turn);
        }

        public void SetAccessToken(string accessToken)
        {
            _accessToken = accessToken;
        }

        public void StopConversation()
        {
            Task.Run(async () =>
            {
                // Close all WebSocket connections
                foreach (var webSocket in _webSocketServices)
                {
                    try
                    {
                        await webSocket.Close();
                    }
                    catch (Exception ex)
                    {
                        UGLog.LogWarning($"[DialogueManager] Error closing websocket: {ex.Message}");
                    }
                }
                OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.Stopped), _turn);
            });
            SetState(DialogueState.Idle);
        }

        public void ClearConversation()
        {
            SetState(DialogueState.Idle);

            // Cancel all pending tasks
            _cancellationTokenSource?.Cancel();

            // Stop audio player
            _audioStreamer.Stop();
            _audioStreamer.Flush();

            // Close all websockets
            Task.Run(async () =>
            {
                foreach (var webSocket in _webSocketServices)
                {
                    try
                    {
                        await webSocket.Close();
                    }
                    catch (Exception ex)
                    {
                        UGLog.LogWarning("[DialogueManager] Error closing websocket: " + ex.Message);
                    }
                }
            });

            // Clear conversation history
            _conversationHistory.Clear();
            _turn = 0;
        }

        private void PauseConversation()
        {
            // TODO: Add pause logic
            //     // Stop audio playback
            //     _audioStreamer.Stop();
            //     _audioStreamer.Flush();

            //     // Set conversation state to paused
            //     SetState(DialogueState.Paused);
            //     OnConversationEvent?.Invoke(new ConversationEvent(ConversationEventType.ConversationPaused), _turn);
        }
    }

    public class ParticipantData
    {
        public int ParticipantIndex { get; set; }
        public string ParticipantName { get; set; }
        public ConversationConfiguration ConversationConfiguration { get; set; }
    }

    public class ConversationHistory
    {
        public string ParticipantName { get; set; }
        public string Message { get; set; }
        public int Turn { get; set; }
    }
}

public enum DialogueState
{
    Idle,
    Processing, // no interrupt
    Playing, // playing audio, can interrupt
    Error,
    Paused
}