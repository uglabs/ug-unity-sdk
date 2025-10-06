using System;
using System.Collections.Generic;
using UnityEngine;
using UG.Services.UserInput.AudioRecordingService;

namespace UG.Services.UserInput
{
    /// <summary>
    /// Audio enhancement wrapper that processes audio samples through multiple enhancement algorithms.
    /// Can apply noise reduction, echo cancellation, and other audio processing effects.
    /// </summary>
    public class AudioRecordingServiceEnhancedWrapper : IAudioRecordingService, IDisposable
    {
        private IAudioRecordingService _sourceAudioService;
        private List<IAudioEnhancement> _enhancements;
        private bool _isDisposed = false;
        private bool _isInitialized = false;
        private int _sampleRate = 16000;

        // Emits RAW Samples - no enhancement
        public event Action<float[]> OnSamplesRecorded;

        // Emits Enhanced Samples with all enhancements applied
        public event Action<float[]> OnEnhancedSamplesRecorded;

        public event Action<bool> OnPermissionsGranted;

        /// <summary>
        /// Creates a new audio enhancement wrapper that wraps the source audio service
        /// </summary>
        /// <param name="sourceAudioService">The audio service to enhance samples from</param>
        /// <param name="enhancements">Optional list of enhancement algorithms to apply</param>
        public AudioRecordingServiceEnhancedWrapper(IAudioRecordingService sourceAudioService, List<IAudioEnhancement> enhancements = null)
        {
            _sourceAudioService = sourceAudioService ?? throw new ArgumentNullException(nameof(sourceAudioService));
            _enhancements = enhancements ?? new List<IAudioEnhancement>();
            
            // Subscribe to the source service events
            _sourceAudioService.OnSamplesRecorded += OnSourceSamplesReceived;
            
            // Note: Permission events are handled by individual service implementations
        }

        /// <summary>
        /// Initialize the speech enhancement service
        /// </summary>
        /// <param name="isRequestMicPermissionOnInit">Whether to request microphone permission on initialization</param>
        public void Init(bool isRequestMicPermissionOnInit)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioRecordingServiceEnhancedWrapper));
            }

            _sourceAudioService.Init(isRequestMicPermissionOnInit);
            _isInitialized = true;
        }

        /// <summary>
        /// Start recording and processing audio
        /// </summary>
        public void StartRecording()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioRecordingServiceEnhancedWrapper));
            }

            if (!_isInitialized)
            {
                throw new InvalidOperationException("SpeechEnhancement must be initialized before starting recording");
            }

            _sourceAudioService.StartRecording();
        }

        /// <summary>
        /// Stop recording and processing audio
        /// </summary>
        public void StopRecording()
        {
            if (_isDisposed)
            {
                return; // Allow stopping even if disposed
            }

            _sourceAudioService.StopRecording();
        }

        /// <summary>
        /// Process audio samples from the source service
        /// </summary>
        /// <param name="samples">Raw audio samples from the source service</param>
        private void OnSourceSamplesReceived(float[] samples)
        {
            if (_isDisposed || samples == null)
                return;

            try
            {
                // Emit the raw samples
                OnSamplesRecorded?.Invoke(samples);

                // Apply all enabled enhancement algorithms
                var processedSamples = ProcessSamples(samples);
                
                // Raise the event with processed samples
                OnEnhancedSamplesRecorded?.Invoke(processedSamples);
            }
            catch (Exception e)
            {
                UGLog.LogError($"Error processing audio samples in AudioEnhancementWrapper: {e.Message}");
                // Fallback: pass through original samples if processing fails
                OnSamplesRecorded?.Invoke(samples);
            }
        }

        /// <summary>
        /// Process audio samples through all enabled enhancement algorithms
        /// </summary>
        /// <param name="samples">Input audio samples</param>
        /// <returns>Processed audio samples</returns>
        private float[] ProcessSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return samples;

            var processedSamples = samples;
            
            // Apply each enhancement in sequence
            foreach (var enhancement in _enhancements)
            {
                if (enhancement.IsEnabled)
                {
                    try
                    {
                        processedSamples = enhancement.ProcessSamples(processedSamples, _sampleRate);
                    }
                    catch (Exception e)
                    {
                        UGLog.LogError($"Error in enhancement '{enhancement.Name}': {e.Message}");
                        // Continue with other enhancements if one fails
                    }
                }
            }

            return processedSamples;
        }

        /// <summary>
        /// Add an enhancement algorithm to the processing pipeline
        /// </summary>
        /// <param name="enhancement">The enhancement to add</param>
        public void AddEnhancement(IAudioEnhancement enhancement)
        {
            if (enhancement != null && !_enhancements.Contains(enhancement))
            {
                _enhancements.Add(enhancement);
            }
        }

        /// <summary>
        /// Remove an enhancement algorithm from the processing pipeline
        /// </summary>
        /// <param name="enhancement">The enhancement to remove</param>
        public void RemoveEnhancement(IAudioEnhancement enhancement)
        {
            if (enhancement != null)
            {
                _enhancements.Remove(enhancement);
            }
        }

        /// <summary>
        /// Get all enhancement algorithms in the pipeline
        /// </summary>
        /// <returns>List of enhancement algorithms</returns>
        public List<IAudioEnhancement> GetEnhancements()
        {
            return new List<IAudioEnhancement>(_enhancements);
        }

        /// <summary>
        /// Set the sample rate for processing
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz</param>
        public void SetSampleRate(int sampleRate)
        {
            _sampleRate = sampleRate;
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            // Unsubscribe from source service events
            if (_sourceAudioService != null)
            {
                _sourceAudioService.OnSamplesRecorded -= OnSourceSamplesReceived;
            }

            // Stop recording if active
            StopRecording();

            // Dispose the source service if it implements IDisposable
            if (_sourceAudioService is IDisposable disposableService)
            {
                disposableService.Dispose();
            }

            _isDisposed = true;
        }

        public void RequestMicrophonePermission(Action<bool> onPermissionResult)
        {
            _sourceAudioService.RequestMicrophonePermission(onPermissionResult);
        }
    }
}
