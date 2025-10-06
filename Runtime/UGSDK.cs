
using System;
using UG.Exceptions;
using UG.Services;
using UG.Services.UserInput.AudioRecordingService;
using UG.Services.AudioStreamingService;
using UG.Services.WebSocket;
using UG.Services.UGApiService;
using UG.Services.HttpService;
using UG.Managers;
using UG.Settings;
using UG.Utils;
using UnityEngine;

namespace UG
{
    public class UGSDK
    {
        #region V3 Services 
        private static IHttpService _httpService;
        private static WebSocketService _webSocketServiceV3;
        private static UGSDKSettings _settings;
        private static string _playerId;
        private static UGApiServiceV3 _ugApiServiceV3;
        private static AudioStreamer _audioStreamer;
        private static VoiceCaptureService _voiceCaptureService;
        private static IAudioRecordingService _audioRecordingService;
        private static IVADService _vadService;
        #endregion

        #region V3 Managers
        private static ConversationManager _conversationManager;
        public static ConversationManager ConversationManager { get => _conversationManager; }
        private static AuthenticationManager _authenticationManager;
        #endregion

        #region Runtime variables
        private static double _lastVADTime = 0f;
        public static double LastVADTime { get => _lastVADTime; }
        #endregion

        public static bool IsInitialized { get; private set; }

        public static UGSDK Initialize(string playerId = null) //, AudioRecordingConsent consentToAudioRecording = AudioRecordingConsent.None
        {
            UGSDKSettings sdkSettings = Resources.Load<UGSDKSettings>(Constants.Constants.SettingFilename);
            return Initialize(sdkSettings, playerId);
        }

        public static UGSDK Initialize(UGSDKSettings sdkSettings, string playerId = null)
        {
            if (IsInitialized)
            {
                throw new UGSDKException(ExceptionConsts.AlreadyInitialized);
            }

            ValidateSettings(sdkSettings);
            _settings = sdkSettings;
            _playerId = playerId;
            if (string.IsNullOrEmpty(_playerId))
            {
                _playerId = GetOrCreateUserId();
            }

            // Set DSP buffer size
            SetDSPBufferSize(Constants.Constants.DSPBufferSize);

            // Initialize UnityMainThreadDispatcher
            UnityMainThreadDispatcher.Initialize();
            UGLog.SetLogLevel(sdkSettings.logLevel);

            // Create audio streamer (MonoBehavior)
            GameObject ugAudioStreamerObject = new("UGSDKAudioStreamer");
            _audioStreamer = ugAudioStreamerObject.AddComponent<AudioStreamer>();
            GameObject.DontDestroyOnLoad(ugAudioStreamerObject);
            _audioStreamer.Init("audio/mpeg", 1);

            // Create voice capture services
            // Audio recording, VAD and voice capture

            _audioRecordingService = new AudioRecordingService();
            _audioRecordingService.Init(false);

            // Create VAD service
            _vadService = new SileroVadService(VADModelLoader.LoadModel());
            _vadService.SetThreshold();
            _vadService.OnVADClosingTime += (start, end) =>
            {
                UGLog.Log($"Set last vad time: {start} {end} => {(end - start).TotalMilliseconds}");
                _lastVADTime = (end - start).TotalMilliseconds;
            };

            // Create voice capture service
            _voiceCaptureService = new VoiceCaptureService(_audioRecordingService, _vadService);

            // Web services
            _httpService = new HttpClientService(sdkSettings.host);
            _ugApiServiceV3 = new UGApiServiceV3(_settings, _httpService);
            _webSocketServiceV3 = new WebSocketService(_settings.host, null);

            // Create authentication manager & authenticate right away
            _authenticationManager = new AuthenticationManager(
                settings: _settings,
                ugApiService: _ugApiServiceV3);
            _authenticationManager.OnAuthenticated += OnAuthenticated;
            _authenticationManager.OnAuthenticationFailed += OnAuthenticationFailed;
            _authenticationManager.Authenticate(_playerId);

            // Create conversation manager
            _conversationManager = new ConversationManager(
                webSocketService: _webSocketServiceV3,
                ugApiServiceV3: _ugApiServiceV3,
                settings: _settings,
                audioStreamer: _audioStreamer,
                voiceCaptureService: _voiceCaptureService);

            UGLog.Log("UGSDK Initialized");
            return new UGSDK();
        }

        public static void OnAuthenticated(string accessToken)
        {
            _conversationManager.SetAccessToken(accessToken);
        }

        public static void OnAuthenticationFailed()
        {
            UGLog.LogError("Authentication failed - please try again or check your credentials");
        }

        public static void SetDSPBufferSize(int bufferSize)
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            if (config.dspBufferSize != bufferSize)
            {
                config.dspBufferSize = bufferSize;
                bool isReset = AudioSettings.Reset(config);
            }
        }

        public static void SetVADThreshold(float minSilenceDurationMs = 650,
            float threshold = 0.75f,
            float toSilenceThreshold = 0.25f,
            float hardTimeoutMs = 3500,
            int minSpeechDetectionMs = 160)
        {
            _vadService.SetThreshold(minSilenceDurationMs: minSilenceDurationMs,
                threshold: threshold,
                toSilenceThreshold: toSilenceThreshold,
                 hardTimeoutMs: hardTimeoutMs,
                 minSpeechDetectionMs: minSpeechDetectionMs);
        }

        /// <summary>
        /// Call to explicitly request microphone permission
        /// If not called explicitly and not granted previously - permission request will be made when audio recording starts
        /// </summary>
        /// <param name="onPermissionResult"></param>
        public static void RequestMicPermission(Action<bool> onPermissionResult)
        {
            _audioRecordingService.RequestMicrophonePermission((isGranted) =>
            {
                UGLog.Log("Microphone permission granted: " + isGranted);
                onPermissionResult?.Invoke(isGranted);
            });
        }

        private static string GetOrCreateUserId()
        {
            string userId = PlayerPrefs.GetString(Constants.Constants.UGSDKPlayerPrefs);
            if (string.IsNullOrEmpty(userId))
            {
                string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                userId = $"{Guid.NewGuid()}_{timestamp}";
                PlayerPrefs.SetString(Constants.Constants.UGSDKPlayerPrefs, userId);
                PlayerPrefs.Save();
            }
            return userId;
        }

        private static void ValidateSettings(UGSDKSettings sdkSettings)
        {
            if (sdkSettings == null)
            {
                throw new UGSDKException("[UGSDK] Init error - settings file is missing. In Unity. Click on Tools -> UG Labs -> Settings -> Create Settings");
            }
            if (string.IsNullOrEmpty(sdkSettings.host))
            {
                throw new UGSDKException("[UGSDK] Init error - settings file is missing Host. In Unity. Click on Tools -> UG Labs -> Settings -> Open Settings and set Host");
            }
            if (string.IsNullOrEmpty(sdkSettings.apiKey))
            {
                throw new UGSDKException("[UGSDK] Init error - settings file is missing API key value. In Unity. Click on Tools -> UG Labs -> Settings -> Open Settings and set your API Key");
            }
        }

        public void Dispose()
        {
            _authenticationManager.OnAuthenticated -= OnAuthenticated;
            _authenticationManager.OnAuthenticationFailed -= OnAuthenticationFailed;
            _conversationManager.Dispose();
        }
    }
}