using System;
using System.Collections.Generic;

namespace UG.Services
{
    public class SileroVadService : IVADService
    {
        private readonly SileroVadOnnxModel _model;
        private readonly int _samplingRate;
        private float _threshold;
        private float _minSilenceSamples;
        private readonly float _speechPadSamples;
        private float _toSilenceThreshold;
        private int _minChunksToDetectSpeech = 0;
        private readonly float _minSpeechSamples;  // Added for minimum speech duration
        private readonly List<float> _buffer;
        private bool _inSpeech = false;
        private int _lastVoiceIndex = 0;
        private int _silenceSamples = 0;
        private int _bufferStartOffset = 0;
        private DateTime _lastVoiceDateTime;
        private int _speechChunksDetected = 0;
        private int _hardTimeoutMilliseconds = 3500;
        private DateTime _lastSpeechDateTime = DateTime.MinValue;
        private bool _hasDetectedSpeechInSession = false;
        private DateTime _lastSpeechEndTime = DateTime.MinValue;
        private readonly TimeSpan _speechCooldownPeriod = TimeSpan.FromSeconds(0); // Added the cooldown to prevent speech from starting too early
        public event Action<float[]> OnSpeechDetected;
        public event Action OnSpoke;
        public event Action<float, int, float> OnSpokeDebugInfo;
        public event Action<float> OnSpeechProbabilityValueChanged;
        public event Action<float[]> OnSpokeWithSamples;
        public event Action OnSilenced;
        [Obsolete("Should be handled externally instead")]
        public event Action OnHardTimeout;
        public event Action<DateTime, DateTime> OnVADClosingTime;

        public SileroVadService(byte[] onnxModelData,
                float speechThreshold = 0.4f, // threshold for speech probability
                int minSilenceDurationMs = 750, // minimum duration of silence to trigger end of speech
                int speechPadMs = 250, // padding to add to the start and end of speech
                int samplingRate = Constants.Constants.MicrophoneSampleRate, // audio sample rate
                float to_silence_threshold = 0.25f, //  threshold for speech probability to trigger end of speech; if not provided will be set to speech_threshold - 0.15
                int minSpeechDetectionMs = 160, // Additional value - when VAD is closed,minimum duration of speech to Open VAD (helps avoid false positives from noise or echo)
                int minSpeechDurationMs = 350)  // Additional value - min speech duration in ms - we don't consider VAD closed if speech was less than this length (should still fit "Ah, "O!" sounds, but nothing too short)
        {
            this._model = new SileroVadOnnxModel(onnxModelData);
            this._samplingRate = samplingRate;
            this._threshold = speechThreshold;
            this._minSilenceSamples = samplingRate * minSilenceDurationMs / 1000f;
            this._speechPadSamples = samplingRate * speechPadMs / 1000f;
            this._toSilenceThreshold = to_silence_threshold;
            // Convert milliseconds to chunks (each chunk is 32ms at 16kHz)
            this._minChunksToDetectSpeech = (int)Math.Ceiling(minSpeechDetectionMs / 32.0);
            this._minSpeechSamples = samplingRate * minSpeechDurationMs / 1000f;
            this._buffer = new List<float>();

            Reset();
        }

        public void SetThreshold(float minSilenceDurationMs,
            float threshold = 0.55f,
            float to_silence_threshold = 0.25f,
            float hardTimeoutMs = 3500,
            int minSpeechDetectionMs = 160)
        {
            this._minSilenceSamples = this._samplingRate * minSilenceDurationMs / 1000f;
            this._threshold = threshold;
            this._toSilenceThreshold = to_silence_threshold;
            this._hardTimeoutMilliseconds = (int)hardTimeoutMs;
            // Convert milliseconds to chunks (each chunk is 32ms at 16kHz)
            this._minChunksToDetectSpeech = (int)Math.Ceiling(minSpeechDetectionMs / 32.0);
            UGLog.Log($"[VAD] Settings set: {threshold} and toSilenceThreshold: {to_silence_threshold} and timeout: {minSilenceDurationMs}(min silence samples: {_minSilenceSamples}) and hardTimeoutMs: {hardTimeoutMs}");
        }

        public SpeechBuffer AddAudio(float[] audioChunk)
        {
            _buffer.AddRange(audioChunk);

            var speechProbability = GetSpeechProbability(audioChunk);
#if UG_VAD_DEBUG
            UnityEngine.Debug.Log($"[VAD] Speech probability: {speechProbability:F4} (threshold: {_threshold:F4}, toSilence: {_toSilenceThreshold:F4}), chunks: {_speechChunksDetected}");
#endif
            OnSpeechProbabilityValueChanged?.Invoke(speechProbability);

            // Update last speech time if speech is detected
            if (speechProbability >= _threshold)
            {
                _lastSpeechDateTime = DateTime.UtcNow;
                _hasDetectedSpeechInSession = true;
            }

            // Check for hard timeout - only if we've detected speech in this session
            var noSpeechMs = (DateTime.UtcNow - _lastSpeechDateTime).TotalMilliseconds;
            if (_hasDetectedSpeechInSession && speechProbability < _threshold && noSpeechMs > _hardTimeoutMilliseconds)
            {
                UnityEngine.Debug.Log($"Hard timeout triggered after {_hardTimeoutMilliseconds}ms of no speech");
                OnHardTimeout?.Invoke();
                _inSpeech = false;
                _hasDetectedSpeechInSession = false;
                return null;
            }

            // count speech chunks detected
            if (speechProbability >= _threshold)
            {
                _speechChunksDetected++;
            }
            else
            {
                _speechChunksDetected = 0;
            }

            // no speech detected and no current speech
            if (speechProbability < _threshold && !_inSpeech)
                return null;

            if (speechProbability >= _threshold && _speechChunksDetected >= _minChunksToDetectSpeech)
            {
                var timeSinceLastSpeech = DateTime.UtcNow - _lastSpeechEndTime;
                if (!_inSpeech && timeSinceLastSpeech > _speechCooldownPeriod)
                {
                    UGLog.Log($"[VAD] Speech detected: {speechProbability} chunks: {_speechChunksDetected} (time since last speech: {timeSinceLastSpeech.TotalSeconds:F2}s)");
                    OnSpokeDebugInfo?.Invoke(speechProbability, _speechChunksDetected, (float)timeSinceLastSpeech.TotalSeconds);
                    OnSpoke?.Invoke();
                    _inSpeech = true;

                    // Remove excess silence while keeping speech padding
                    int silenceSize = _buffer.Count - audioChunk.Length;
                    int silenceToRemoveSize = Math.Max(0, silenceSize - (int)_speechPadSamples);
                    _bufferStartOffset += silenceToRemoveSize;
                    _buffer.RemoveRange(0, silenceToRemoveSize);

                    OnSpokeWithSamples?.Invoke(_buffer.ToArray());
                }

                _lastVoiceIndex = _buffer.Count;
                _lastVoiceDateTime = DateTime.UtcNow;
                _silenceSamples = 0;
                return null;
            }

            // Handle transition to silence when in speech
            if (speechProbability < _toSilenceThreshold && _inSpeech)
            {
                _silenceSamples += audioChunk.Length;
                if (_silenceSamples < _minSilenceSamples)
                {
                    UGLog.LogWarning($"[VAD] Not enough silence samples: {_silenceSamples} < {_minSilenceSamples}");
                    return null;
                }

                DateTime silenceDetectionTime = DateTime.UtcNow;
                OnVADClosingTime?.Invoke(_lastVoiceDateTime, silenceDetectionTime);

                _inSpeech = false;

                int speechLength = _lastVoiceIndex + (int)_speechPadSamples;

                // Check if speech duration meets minimum requirement
                if (speechLength < _minSpeechSamples)
                {
                    float speechDurationSeconds = SamplesToMs(speechLength) / 1000f;
                    float minSpeechDurationSeconds = SamplesToMs((int)_minSpeechSamples) / 1000f;
                    UGLog.LogWarning($"[VAD] Speech too short: {speechLength} samples ({speechDurationSeconds:F3}s) < {_minSpeechSamples} samples ({minSpeechDurationSeconds:F3}s)");
                    _buffer.RemoveRange(0, Math.Min(speechLength, _buffer.Count));
                    _bufferStartOffset += speechLength;
                    return null;
                }

                float[] speech = _buffer.GetRange(0, Math.Min(speechLength, _buffer.Count)).ToArray();

                // Calculate start and end times in milliseconds
                float speechStartMs = SamplesToMs(_bufferStartOffset);
                float speechEndMs = speechStartMs + SamplesToMs(speechLength);

                // Update buffer and offset
                _buffer.RemoveRange(0, Math.Min(speechLength, _buffer.Count));
                _bufferStartOffset += speechLength;

                OnSpeechDetected?.Invoke(speech);
                OnSilenced?.Invoke();
                _lastSpeechEndTime = DateTime.UtcNow;
                return new SpeechBuffer(speechStartMs, speechEndMs, speech);
            }

            return null;
        }

        public void Reset()
        {
            _model.ResetStates();
            this._inSpeech = false;
            this._buffer.Clear();
            this._bufferStartOffset = 0;
            _lastVoiceIndex = 0;
            _silenceSamples = 0;
            this._bufferStartOffset = 0;
            this._hasDetectedSpeechInSession = false;
            this._lastSpeechEndTime = DateTime.MinValue;
        }

        private float GetSpeechProbability(float[] audioChunk)
        {
            return _model.Call(new[] { audioChunk }, _samplingRate)[0];
        }

        private float SamplesToMs(int samples)
        {
            return (samples * 1000f) / _samplingRate;
        }

        public void Dispose()
        {
            _model.Dispose();
        }
    }
}