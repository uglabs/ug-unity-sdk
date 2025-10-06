using System;
using UnityEngine;

namespace UG.Utils
{
    /// <summary>
    /// Triggers an event when audio playback starts - lives on AudioSource
    /// This happens after all the conversion/processing, as close as possible to the actual playback
    /// For more accurate benchmarking
    /// </summary>
    // [RequireComponent(typeof(AudioSource))]
    public class AudioOutputBenchmark : MonoBehaviour
    {
        public Action<double, bool> OnAudioPlaybackStarted; // Passes the timestamp when audio started

        private bool _shouldTriggerEvent = false;
        private long _benchmarkStartTime = 0;
        private long _currentTime = 0;
        private float _silenceThreshold = 0.01f; // Adjust this threshold as needed
        private bool _isInitialBenchmark = true;

        public void StartBenchmark(bool isInitial)
        {
            _shouldTriggerEvent = true;
            _benchmarkStartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _isInitialBenchmark = isInitial;
        }

        void Update()
        {
            // Keep current time updated on main thread
            _currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_shouldTriggerEvent) return;
            if (_benchmarkStartTime == 0) return;

            // Calculate RMS (Root Mean Square) to detect audio level
            float rms = CalculateRMS(data);

            if (rms > _silenceThreshold)
            {
                // Audio detected - trigger event and reset flag
                double audioStartTime = _currentTime - _benchmarkStartTime;
                OnAudioPlaybackStarted?.Invoke(audioStartTime, _isInitialBenchmark);
                _benchmarkStartTime = 0;
                _shouldTriggerEvent = false;
            }
        }

        private float CalculateRMS(float[] data)
        {
            float sum = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                sum += data[i] * data[i];
            }
            return Mathf.Sqrt(sum / data.Length);
        }
    }
}