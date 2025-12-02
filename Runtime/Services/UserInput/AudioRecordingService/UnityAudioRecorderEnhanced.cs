using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UG.Utils;
using System.Threading.Tasks;

namespace UG.Services.UserInput.AudioRecordingService
{
    /// <summary>
    /// Unity C# wrapper for the enhanced audio recorder with noise reduction capabilities.
    /// Provides a clean interface to the native Android audio recording system with NoiseSuppression integration.
    /// </summary>
    public class UnityAudioRecorderEnhanced : MonoBehaviour, IDisposable
    {
        [Header("Audio Configuration")]
        [SerializeField] private int sampleRate = 48000;
        [SerializeField] private int bufferLengthSec = 5;
        [SerializeField] private int chunkSizeSamples = 480; // 10ms at 48kHz (exact model frame size)
        
        [Header("Enhancement Settings")]
        [SerializeField] private bool enableNoiseReduction = true;
        [SerializeField] private bool enableAEC = false;
        [SerializeField] private bool enableProcessing = true;
        
        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private bool logAudioRMS = false;
        
        // Events
        public event Action<float[]> OnRawAudioReceived;
        public event Action<float[]> OnEnhancedAudioReceived;
        public event Action<bool> OnPermissionsChanged;
        public event Action<bool> OnRecordingStatusChanged;
        public event Action<string> OnRecordingError;
        public event Action<string> OnStatusUpdate;
        
        // Properties
        public bool IsInitialized { get; private set; }
        public bool IsRecording { get; private set; }
        public int SampleRate => sampleRate;
        public int ChunkSizeSamples => chunkSizeSamples;
        
        // Native interface
        private AndroidJavaClass nativeClass;
        private bool isDisposed = false;
        
        // Unity Editor recording support (similar to AudioRecordingService)
#if UNITY_EDITOR
        private AudioClip microphoneClip;
        private string selectedDevice;
        private int lastSample = 0;
#endif
        
        // Audio data handling (similar to AudioRecordingServiceAEC)
        private readonly Queue<float[]> rawAudioQueue = new Queue<float[]>();
        private readonly Queue<float[]> enhancedAudioQueue = new Queue<float[]>();
        private readonly object audioQueueLock = new object();
        
        // Polling-based recording with absolute positioning
        private int lastRawSample = 0;
        private int lastEnhancedSample = 0;
        private System.Threading.CancellationTokenSource cancellationTokenSource;
        
        /// <summary>
        /// Initialize the enhanced audio recorder
        /// </summary>
        /// <param name="modelPath">DeepFilterNet3 model file path (StreamingAssets path or absolute path)</param>
        /// <returns>True if initialization successful</returns>
        public bool Initialize(string modelPath = null, int sampleRate = 16000)
        {
            if (IsInitialized)
            {
                LogWarning("Already initialized");
                return true;
            }

            this.sampleRate = sampleRate;
            
            try
            {
                LogDebug("Initializing UnityAudioRecorderEnhanced...");
                
#if UNITY_ANDROID && !UNITY_EDITOR
                // Get native class
                nativeClass = new AndroidJavaClass("com.uglabs.UnityAudioRecorderEnhanced");
                if (nativeClass == null)
                {
                    LogError("Failed to get UnityAudioRecorderEnhanced native class");
                    return false;
                }
                
                // Create the recorder
                bool createSuccess = nativeClass.CallStatic<bool>("create", sampleRate, bufferLengthSec, chunkSizeSamples);
                if (!createSuccess)
                {
                    LogError("Failed to create native audio recorder");
                    return false;
                }
                
                // Add DeepFilterNet3 enhancement if model path provided
                if (!string.IsNullOrEmpty(modelPath))
                {
                    LogDebug($"Adding DeepFilterNet3 enhancement with model path: {modelPath}");
                    bool enhancementSuccess = nativeClass.CallStatic<bool>("addDeepFilterNet3Enhancement", modelPath);
                    if (!enhancementSuccess)
                    {
                        LogError("Failed to add DeepFilterNet3 enhancement");
                        return false;
                    }
                }
                
                // Initialize (no callback needed for polling approach)
                bool initSuccess = nativeClass.CallStatic<bool>("initialize");
                if (!initSuccess)
                {
                    LogError("Failed to initialize native audio recorder");
                    return false;
                }
                
                // Apply initial settings
                nativeClass.CallStatic("setProcessingEnabled", enableProcessing);
                nativeClass.CallStatic<bool>("setEnhancementEnabled", "DeepFilterNet3", enableNoiseReduction);
                nativeClass.CallStatic<bool>("setAECEnabled", enableAEC);
                
                IsInitialized = true;
                LogDebug("UnityAudioRecorderEnhanced initialized successfully");
                
                // Log status
                if (enableDebugLogs)
                {
                    string statusInfo = nativeClass.CallStatic<string>("getStatusInfo");
                    LogDebug($"Status: {statusInfo}");
                }
                
                return true;
                
#elif UNITY_EDITOR
                // Unity Editor mode - use Unity's built-in microphone recording
                LogDebug("Initializing UnityAudioRecorderEnhanced for Unity Editor...");
                
                // Get the default microphone device
                if (Microphone.devices.Length > 0)
                {
                    selectedDevice = Microphone.devices[0];
                    LogDebug($"Selected microphone device: {selectedDevice}");
                }
                else
                {
                    LogError("No microphone device found!");
                    return false;
                }
                
                if (selectedDevice == null)
                {
                    LogError("No microphone device available!");
                    return false;
                }
                
                IsInitialized = true;
                LogDebug("UnityAudioRecorderEnhanced initialized successfully for Unity Editor");
                return true;
                
#else
                LogWarning("UnityAudioRecorderEnhanced only works on Android devices and Unity Editor");
                return false;
#endif
                
            }
            catch (Exception e)
            {
                LogError($"Error initializing UnityAudioRecorderEnhanced: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Start audio recording
        /// </summary>
        /// <returns>True if recording started successfully</returns>
        public bool StartRecording()
        {
            if (!IsInitialized)
            {
                LogError("Not initialized. Call Initialize() first.");
                return false;
            }
            
            if (IsRecording)
            {
                LogWarning("Already recording");
                return true;
            }
            
            try
            {
                LogDebug("Starting audio recording...");
                
#if UNITY_ANDROID && !UNITY_EDITOR
                bool success = nativeClass.CallStatic<bool>("startRecording");
                if (success)
                {
                    IsRecording = true; // Set recording state
                    LogDebug("Audio recording started successfully");
                    
                    // Start polling for audio data
                    lastRawSample = 0;
                    lastEnhancedSample = 0;
                    cancellationTokenSource = new System.Threading.CancellationTokenSource();
                    
                    // Start async reading task
                    _ = ReadSamplesContinuouslyAsync(cancellationTokenSource.Token);
                }
                else
                {
                    LogError("Failed to start audio recording");
                }
                return success;
                
#elif UNITY_EDITOR
                // Unity Editor mode - start Unity microphone recording
                LogDebug("Starting Unity Editor audio recording...");
                
                if (string.IsNullOrEmpty(selectedDevice))
                {
                    LogError("No microphone device selected!");
                    return false;
                }
                
                try
                {
                    microphoneClip = Microphone.Start(selectedDevice, true, bufferLengthSec, sampleRate);
                    if (microphoneClip == null)
                    {
                        LogError("Failed to start microphone recording.");
                        return false;
                    }
                    
                    IsRecording = true;
                    lastSample = 0;
                    
                    // Start the sample reading task
                    cancellationTokenSource = new System.Threading.CancellationTokenSource();
                    _ = ReadSamplesContinuouslyAsync(cancellationTokenSource.Token);
                    
                    LogDebug("Unity Editor audio recording started successfully");
                    return true;
                }
                catch (Exception e)
                {
                    LogError($"Failed to start Unity Editor recording: {e.Message}");
                    return false;
                }
                
#else
                LogWarning("Audio recording only works on Android devices and Unity Editor");
                return false;
#endif
                
            }
            catch (Exception e)
            {
                LogError($"Error starting recording: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Stop audio recording
        /// </summary>
        public void StopRecording()
        {
            if (!IsRecording)
            {
                LogDebug("Not currently recording");
                return;
            }
            
            try
            {
                LogDebug("Stopping audio recording...");
                
                IsRecording = false; // Set recording state
                
                // Stop the polling task
                cancellationTokenSource?.Cancel();
                
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeClass?.CallStatic("stopRecording");
#elif UNITY_EDITOR
                // Unity Editor mode - stop Unity microphone recording
                if (!string.IsNullOrEmpty(selectedDevice))
                {
                    Microphone.End(selectedDevice);
                }
                
                if (microphoneClip != null)
                {
                    UnityEngine.Object.Destroy(microphoneClip);
                    microphoneClip = null;
                }
#endif
                
            }
            catch (Exception e)
            {
                LogError($"Error stopping recording: {e.Message}");
            }
        }
        
        /// <summary>
        /// Enable or disable audio processing
        /// </summary>
        /// <param name="enabled">True to enable processing, false to bypass all enhancements</param>
        public void SetProcessingEnabled(bool enabled)
        {
            enableProcessing = enabled;
            
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeClass?.CallStatic("setProcessingEnabled", enabled);
#endif
                LogDebug($"Audio processing {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception e)
            {
                LogError($"Error setting processing enabled: {e.Message}");
            }
        }
        
        /// <summary>
        /// Enable or disable noise reduction
        /// </summary>
        /// <param name="enabled">True to enable noise reduction</param>
        public void SetNoiseReductionEnabled(bool enabled)
        {
            enableNoiseReduction = enabled;
            
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                bool success = nativeClass?.CallStatic<bool>("setEnhancementEnabled", "DeepFilterNet3", enabled) ?? false;
                if (!success)
                {
                    LogWarning("Failed to set noise reduction state");
                }
#endif
                LogDebug($"Noise reduction {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception e)
            {
                LogError($"Error setting noise reduction: {e.Message}");
            }
        }
        
        /// <summary>
        /// Enable or disable acoustic echo cancellation
        /// </summary>
        /// <param name="enabled">True to enable AEC</param>
        public void SetAECEnabled(bool enabled)
        {
            enableAEC = enabled;
            
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                bool success = nativeClass?.CallStatic<bool>("setAECEnabled", enabled) ?? false;
                if (!success)
                {
                    LogWarning("Failed to set AEC state");
                }
#endif
                LogDebug($"AEC {(enabled ? "enabled" : "disabled")}");
            }
            catch (Exception e)
            {
                LogError($"Error setting AEC: {e.Message}");
            }
        }
        
        /// <summary>
        /// Configure DeepFilterNet3 processing parameters
        /// </summary>
        /// <param name="attenuationLimit">Attenuation limit in dB (typically 10-100, default: 10)</param>
        /// <param name="postFilterBeta">Post-filter beta value (typically 0.0-1.0, default: 0.5)</param>
        /// <returns>True if settings were applied successfully</returns>
        public bool SetDF3Settings(float attenuationLimit, float postFilterBeta)
        {
            try
            {
                LogDebug($"Setting DF3 parameters - AttenuationLimit: {attenuationLimit:F1} dB, PostFilterBeta: {postFilterBeta:F2}");
                
#if UNITY_ANDROID && !UNITY_EDITOR
                bool success = nativeClass?.CallStatic<bool>("setDF3Settings", attenuationLimit, postFilterBeta) ?? false;
                if (success)
                {
                    LogDebug("DF3 parameters updated successfully");
                }
                else
                {
                    LogWarning("Failed to update DF3 parameters");
                }
                return success;
#else
                LogWarning("DF3 settings only work on Android devices");
                return false;
#endif
                
            }
            catch (Exception e)
            {
                LogError($"Error setting DF3 parameters: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Get current DeepFilterNet3 processing parameters
        /// </summary>
        /// <returns>String with current DF3 settings information</returns>
        public string GetDF3Settings()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return nativeClass?.CallStatic<string>("getDF3Settings") ?? "Not initialized";
#else
                return "Editor mode - no DF3 functionality";
#endif
            }
            catch (Exception e)
            {
                LogError($"Error getting DF3 settings: {e.Message}");
                return "Error retrieving DF3 settings";
            }
        }
        
        /// <summary>
        /// Get latest raw audio data from the circular buffer
        /// </summary>
        /// <param name="sampleCount">Number of samples to retrieve</param>
        /// <returns>Audio samples as float array</returns>
        public float[] GetLatestRawAudio(int sampleCount = 4800)
        {
            try
            {
                // Use the queued audio data instead of native call
                lock (audioQueueLock)
                {
                    if (rawAudioQueue.Count > 0)
                    {
                        // Get the most recent audio chunk
                        float[][] allChunks = rawAudioQueue.ToArray();
                        return allChunks.LastOrDefault() ?? new float[0];
                    }
                }
                return new float[0];
            }
            catch (Exception e)
            {
                LogError($"Error getting latest raw audio: {e.Message}");
                return new float[0];
            }
        }
        
        /// <summary>
        /// Get queued raw audio samples (non-blocking)
        /// </summary>
        /// <returns>Array of audio sample arrays, or empty if none available</returns>
        public float[][] GetQueuedRawAudio()
        {
            lock (audioQueueLock)
            {
                if (rawAudioQueue.Count == 0)
                    return new float[0][];
                
                float[][] result = new float[rawAudioQueue.Count][];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = rawAudioQueue.Dequeue();
                }
                return result;
            }
        }
        
        /// <summary>
        /// Get queued enhanced audio samples (non-blocking)
        /// </summary>
        /// <returns>Array of audio sample arrays, or empty if none available</returns>
        public float[][] GetQueuedEnhancedAudio()
        {
            lock (audioQueueLock)
            {
                if (enhancedAudioQueue.Count == 0)
                    return new float[0][];
                
                float[][] result = new float[enhancedAudioQueue.Count][];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = enhancedAudioQueue.Dequeue();
                }
                return result;
            }
        }
        
        /// <summary>
        /// Get current status information
        /// </summary>
        /// <returns>Status information string</returns>
        public string GetStatusInfo()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return nativeClass?.CallStatic<string>("getStatusInfo") ?? "Not initialized";
#elif UNITY_EDITOR
                return $"Unity Editor mode - Device: {selectedDevice ?? "None"}, Recording: {IsRecording}, Initialized: {IsInitialized}";
#else
                return "Unsupported platform";
#endif
            }
            catch (Exception e)
            {
                LogError($"Error getting status info: {e.Message}");
                return "Error retrieving status";
            }
        }
        
        /// <summary>
        /// Log debug information about the current state
        /// </summary>
        public void LogDebugInfo()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeClass?.CallStatic("logDebugInfo");
#endif
                LogDebug($"Unity State - Initialized: {IsInitialized}, Recording: {IsRecording}");
            }
            catch (Exception e)
            {
                LogError($"Error logging debug info: {e.Message}");
            }
        }
        
        // Unity lifecycle
        
        private void Start()
        {
            // Auto-initialize if not done manually
            // You can call Initialize() manually with model bytes if needed
        }
        
        private void OnDestroy()
        {
            Dispose();
        }
        
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && IsRecording)
            {
                LogDebug("Application paused, stopping recording");
                StopRecording();
            }
        }
        
        public void Dispose()
        {
            if (isDisposed) return;
            
            try
            {
                LogDebug("Disposing UnityAudioRecorderEnhanced...");
                
                StopRecording();
                
                // Clean up cancellation token
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                
#if UNITY_ANDROID && !UNITY_EDITOR
                nativeClass?.CallStatic("dispose");
#elif UNITY_EDITOR
                // Unity Editor cleanup
                if (!string.IsNullOrEmpty(selectedDevice))
                {
                    Microphone.End(selectedDevice);
                }
                
                if (microphoneClip != null)
                {
                    UnityEngine.Object.Destroy(microphoneClip);
                    microphoneClip = null;
                }
#endif
                
                // Clear queues
                lock (audioQueueLock)
                {
                    rawAudioQueue.Clear();
                    enhancedAudioQueue.Clear();
                }
                
                IsInitialized = false;
                IsRecording = false;
                isDisposed = true;
                
                LogDebug("UnityAudioRecorderEnhanced disposed");
            }
            catch (Exception e)
            {
                LogError($"Error during disposal: {e.Message}");
            }
        }
        
        // Continuous audio reading (similar to AudioRecordingServiceAEC)
        
        private async Task ReadSamplesContinuouslyAsync(System.Threading.CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && IsRecording)
            {
                try
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    // Get current absolute positions (both raw and enhanced use absolute positioning)
                    int currentRawPosition = nativeClass.CallStatic<int>("getRawPosition");
                    int currentEnhancedPosition = nativeClass.CallStatic<int>("getEnhancedPosition");
                    
                    bool hasRawData = currentRawPosition > lastRawSample;
                    bool hasEnhancedData = currentEnhancedPosition > lastEnhancedSample;
                    
                    // Process raw audio if available
                    if (hasRawData)
                    {
                        int rawSampleCount = currentRawPosition - lastRawSample;
                        if (rawSampleCount > 0)
                        {
                            float[] rawSamples = nativeClass.CallStatic<float[]>("getRawData", lastRawSample, rawSampleCount);
                            if (rawSamples != null && rawSamples.Length > 0)
                            {
                                ProcessAudioSamples(rawSamples, true);
                            }
                            lastRawSample = currentRawPosition;
                        }
                    }
                    
                    // Process enhanced audio if available (independently)
                    if (hasEnhancedData)
                    {
                        int enhancedSampleCount = currentEnhancedPosition - lastEnhancedSample;
                        if (enhancedSampleCount > 0)
                        {
                            float[] enhancedSamples = nativeClass.CallStatic<float[]>("getEnhancedData", lastEnhancedSample, enhancedSampleCount);
                            if (enhancedSamples != null && enhancedSamples.Length > 0)
                            {
                                ProcessAudioSamples(enhancedSamples, false);
                            }
                            lastEnhancedSample = currentEnhancedPosition;
                        }
                    }
                    
                    // If no new data available, wait before next check
                    if (!hasRawData && !hasEnhancedData)
                    {
                        await UG.Utils.Awaitable.NextFrameAsync();
                        continue;
                    }
                    
#elif UNITY_EDITOR
                    // Unity Editor mode - read from Unity microphone
                    if (string.IsNullOrEmpty(selectedDevice) || microphoneClip == null)
                    {
                        await UG.Utils.Awaitable.NextFrameAsync();
                        continue;
                    }

                    int currentPosition = Microphone.GetPosition(selectedDevice);
                    if (currentPosition < 0 || lastSample == currentPosition)
                    {
                        await UG.Utils.Awaitable.NextFrameAsync();
                        continue;
                    }

                    int sampleCount = GetSampleCount(currentPosition);
                    if (sampleCount > 0)
                    {
                        float[] samples = new float[sampleCount];
                        microphoneClip.GetData(samples, lastSample);
                        
                        // In Unity Editor mode, we only have raw audio (no enhancement)
                        ProcessAudioSamples(samples, true);
                        
                        // For Unity Editor, we simulate enhanced audio as the same as raw
                        // (since we don't have DeepFilterNet3 in editor)
                        if (enableNoiseReduction)
                        {
                            ProcessAudioSamples(samples, false);
                        }
                    }

                    lastSample = currentPosition;
#endif
                }
                catch (Exception e)
                {
                    LogError($"Error reading samples: {e.Message}");
                }
                
                await UG.Utils.Awaitable.NextFrameAsync();
            }
        }
        
        private void ProcessAudioSamples(float[] samples, bool isRawAudio)
        {
            if (samples == null || samples.Length == 0)
                return;
            
            // Queue audio and invoke events
            lock (audioQueueLock)
            {
                if (isRawAudio)
                {
                    rawAudioQueue.Enqueue(samples);
                    
                    // Keep queue size manageable
                    while (rawAudioQueue.Count > 10)
                        rawAudioQueue.Dequeue();
                }
                else
                {
                    enhancedAudioQueue.Enqueue(samples);
                    
                    // Keep queue size manageable
                    while (enhancedAudioQueue.Count > 10)
                        enhancedAudioQueue.Dequeue();
                }
            }
            
            // Log RMS if enabled
            if (logAudioRMS)
            {
                float rms = CalculateRMS(samples);
                string audioType = isRawAudio ? "Raw" : "Enhanced";
                LogDebug($"{audioType} audio: {samples.Length} samples, RMS: {rms:F4}");
            }
            
            // Invoke events on main thread
            if (isRawAudio)
            {
                this.OnRawAudioReceived?.Invoke(samples);
            }
            else
            {
                this.OnEnhancedAudioReceived?.Invoke(samples);
            }
        }
        
        // Private helper methods
        
#if UNITY_EDITOR
        private int GetSampleCount(int currentPosition)
        {
            if (microphoneClip == null) return 0;

            if (currentPosition > lastSample)
            {
                return currentPosition - lastSample;
            }
            else if (currentPosition < lastSample) // Buffer wrapped
            {
                return (microphoneClip.samples - lastSample) + currentPosition;
            }
            return 0;
        }
#endif
        
        private float CalculateRMS(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return 0f;
            
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            
            return Mathf.Sqrt(sum / samples.Length);
        }
        
        // Logging helpers
        
        private void LogDebug(string message)
        {
            if (enableDebugLogs)
                UGLog.Log($"[UnityAudioRecorderEnhanced] {message}");
        }
        
        private void LogWarning(string message)
        {
            UGLog.LogWarning($"[UnityAudioRecorderEnhanced] {message}");
        }
        
        private void LogError(string message)
        {
            UGLog.LogError($"[UnityAudioRecorderEnhanced] {message}");
        }

        internal void SetSampleRate(int targetSampleRate)
        {
            sampleRate = targetSampleRate;
        }
    }
} 