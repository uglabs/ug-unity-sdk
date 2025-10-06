using System;
using System.Collections.Generic;

namespace UG.Services
{
    public delegate void SpeechDetectedCallback(float speechProbability, int? startOffset, int? endOffset);

    // Default speech detector implementation - this version is meant for dealing with files - not streaming (has Reset()) at the start 
    public class SileroVadDetector
    {
        private readonly SileroVadOnnxModel _model;
        private float _threshold;
        private float _negThreshold;
        private readonly int _samplingRate;
        private readonly int _windowSizeSample;
        private readonly float _minSpeechSamples;
        private readonly float _speechPadSamples;
        private readonly float _maxSpeechSamples;
        private float _minSilenceSamples;
        private readonly float _minSilenceSamplesAtMaxSpeech;
        private int _audioLengthSamples;
        private const float THRESHOLD_GAP = 0.15f;
        // ReSharper disable once InconsistentNaming
        private const int SAMPLING_RATE_8K = 8000;
        // ReSharper disable once InconsistentNaming
        private const int SAMPLING_RATE_16K = 16000;

        private readonly SpeechDetectedCallback _onSpeechDetected;
        public SileroVadDetector(byte[] onnxModelData, float threshold, int samplingRate,
            int minSpeechDurationMs, float maxSpeechDurationSeconds,
            int minSilenceDurationMs, int speechPadMs,
            SpeechDetectedCallback onSpeechDetected = null)
        {
            _onSpeechDetected = onSpeechDetected;
            // We prefer 16K over 8K for other operations on the server
            if (samplingRate != SAMPLING_RATE_8K && samplingRate != SAMPLING_RATE_16K)
            {
                throw new ArgumentException("Sampling rate not support, only available for [8000, 16000]");
            }

            this._model = new SileroVadOnnxModel(onnxModelData);
            this._samplingRate = samplingRate;
            this._threshold = threshold;
            this._negThreshold = threshold - THRESHOLD_GAP;
            this._windowSizeSample = samplingRate == SAMPLING_RATE_16K ? 512 : 256;
            this._minSpeechSamples = samplingRate * minSpeechDurationMs / 1000f;
            this._speechPadSamples = samplingRate * speechPadMs / 1000f;
            this._maxSpeechSamples = samplingRate * maxSpeechDurationSeconds - _windowSizeSample - 2 * _speechPadSamples;
            this._minSilenceSamples = samplingRate * minSilenceDurationMs / 1000f;
            this._minSilenceSamplesAtMaxSpeech = samplingRate * 98 / 1000f;
            this.Reset();

            UnityEngine.Debug.Log($"[VAD] TEST Init: {threshold} {samplingRate} {minSpeechDurationMs} {maxSpeechDurationSeconds} {minSilenceDurationMs} {speechPadMs}");
        }

        public void SetThreshold(float minSilenceDurationMs, float threshold, float to_silence_threshold, float hardTimeoutMs, int minChunksForSpeech)
        {
            this._minSilenceSamples = this._samplingRate * minSilenceDurationMs / 1000f;
            this._threshold = threshold;
            this._negThreshold = to_silence_threshold;
        }

        public void Reset()
        {
            _model.ResetStates();
        }

        public List<SileroSpeechSegment> GetSpeechSegmentList(float[] samples)
        {
            Reset();

            List<float> speechProbList = new List<float>();
            this._audioLengthSamples = samples.Length;

            // Buffer for processing windows
            float[] buffer = new float[_windowSizeSample];
            // UnityEngine.Debug.Log("[VAD] TEST Samples: " + samples.Length + " window:" + _windowSizeSample);

            // Process the samples in chunks (windows)
            int index = 0;
            while (index < samples.Length)
            {
                // Fill the buffer with the next window of samples
                int samplesToRead = Math.Min(_windowSizeSample, samples.Length - index);
                Array.Copy(samples, index, buffer, 0, samplesToRead);

                // Clear any remaining buffer space if we didn't fill the whole window
                if (samplesToRead < _windowSizeSample)
                {
                    Array.Clear(buffer, samplesToRead, _windowSizeSample - samplesToRead);
                }

                float speechProb = _model.Call(new[] { buffer }, _samplingRate)[0];
                speechProbList.Add(speechProb);

                index += samplesToRead;
            }

            return CalculateProb(speechProbList);
        }

        public List<SileroSpeechSegment> GetSpeechSegmentList(byte[] pcmData)//FileInfo wavFile)
        {
            Reset();

            List<float> speechProbList = new List<float>();

            // Calculate the number of samples in the PCM data (assuming 16-bit PCM)
            int totalSamples = pcmData.Length / 2; // 2 bytes per 16-bit sample

            this._audioLengthSamples = totalSamples;

            // Buffer for the samples (assuming a window size for processing)
            float[] buffer = new float[_windowSizeSample];

            // Process the PCM data in chunks (windows)
            int index = 0;
            while (index < totalSamples)
            {
                // Fill the buffer with the next window of PCM samples
                int samplesToRead = Math.Min(_windowSizeSample, totalSamples - index);
                for (int i = 0; i < samplesToRead; i++)
                {
                    // Convert the 16-bit PCM sample (2 bytes) to a float between -1.0f and 1.0f
                    short pcmSample = BitConverter.ToInt16(pcmData, index * 2);
                    buffer[i] = pcmSample / 32768.0f; // Convert from 16-bit PCM to float
                    index++;
                }

                // Assuming _model.Call expects a 2D array, so we wrap the buffer in an array
                float speechProb = _model.Call(new[] { buffer }, _samplingRate)[0];
                speechProbList.Add(speechProb);
            }

            // Assuming CalculateProb is a method that processes the list of speech probabilities
            return CalculateProb(speechProbList);
        }

        private List<SileroSpeechSegment> CalculateProb(List<float> speechProbList)
        {
            List<SileroSpeechSegment> result = new List<SileroSpeechSegment>();
            bool triggered = false;
            int tempEnd = 0, prevEnd = 0, nextStart = 0;
            SileroSpeechSegment segment = new SileroSpeechSegment();

            for (int i = 0; i < speechProbList.Count; i++)
            {
                float speechProb = speechProbList[i];
                UnityEngine.Debug.Log("[VAD] TEST Speech prob: " + speechProb + " " + segment.StartSecond);
                if (speechProb >= _threshold)
                {
                    _onSpeechDetected?.Invoke(speechProb, segment.StartOffset, segment.EndOffset);
                    UnityEngine.Debug.Log("[VAD] TEST Speech detected:: " + speechProb + " " + segment.StartSecond);
                }
                if (speechProb >= _threshold && (tempEnd != 0))
                {
                    tempEnd = 0;
                    if (nextStart < prevEnd)
                    {
                        nextStart = _windowSizeSample * i;
                    }
                }

                if (speechProb >= _threshold && !triggered)
                {
                    triggered = true;
                    segment.StartOffset = _windowSizeSample * i;
                    continue;
                }

                if (triggered && (_windowSizeSample * i) - segment.StartOffset > _maxSpeechSamples)
                {
                    if (prevEnd != 0)
                    {
                        segment.EndOffset = prevEnd;
                        result.Add(segment);
                        segment = new SileroSpeechSegment();
                        if (nextStart < prevEnd)
                        {
                            triggered = false;
                        }
                        else
                        {
                            segment.StartOffset = nextStart;
                        }

                        prevEnd = 0;
                        nextStart = 0;
                        tempEnd = 0;
                    }
                    else
                    {
                        segment.EndOffset = _windowSizeSample * i;
                        result.Add(segment);
                        segment = new SileroSpeechSegment();
                        prevEnd = 0;
                        nextStart = 0;
                        tempEnd = 0;
                        triggered = false;
                        continue;
                    }
                }

                if (speechProb < _negThreshold && triggered)
                {
                    if (tempEnd == 0)
                    {
                        tempEnd = _windowSizeSample * i;
                    }

                    if (((_windowSizeSample * i) - tempEnd) > _minSilenceSamplesAtMaxSpeech)
                    {
                        prevEnd = tempEnd;
                    }

                    if ((_windowSizeSample * i) - tempEnd < _minSilenceSamples)
                    {
                        continue;
                    }
                    else
                    {
                        segment.EndOffset = tempEnd;
                        if ((segment.EndOffset - segment.StartOffset) > _minSpeechSamples)
                        {
                            result.Add(segment);
                        }

                        segment = new SileroSpeechSegment();
                        prevEnd = 0;
                        nextStart = 0;
                        tempEnd = 0;
                        triggered = false;
                        continue;
                    }
                }
            }

            if (segment.StartOffset != null && (_audioLengthSamples - segment.StartOffset) > _minSpeechSamples)
            {
                segment.EndOffset = _audioLengthSamples;
                result.Add(segment);
            }

            for (int i = 0; i < result.Count; i++)
            {
                SileroSpeechSegment item = result[i];
                if (i == 0)
                {
                    item.StartOffset = (int)Math.Max(0, item.StartOffset.Value - _speechPadSamples);
                }

                if (i != result.Count - 1)
                {
                    SileroSpeechSegment nextItem = result[i + 1];
                    int silenceDuration = nextItem.StartOffset.Value - item.EndOffset.Value;
                    if (silenceDuration < 2 * _speechPadSamples)
                    {
                        item.EndOffset = item.EndOffset + (silenceDuration / 2);
                        nextItem.StartOffset = Math.Max(0, nextItem.StartOffset.Value - (silenceDuration / 2));
                    }
                    else
                    {
                        item.EndOffset = (int)Math.Min(_audioLengthSamples, item.EndOffset.Value + _speechPadSamples);
                        nextItem.StartOffset = (int)Math.Max(0, nextItem.StartOffset.Value - _speechPadSamples);
                    }
                }
                else
                {
                    item.EndOffset = (int)Math.Min(_audioLengthSamples, item.EndOffset.Value + _speechPadSamples);
                }
            }

            return MergeListAndCalculateSecond(result, _samplingRate);
        }

        private List<SileroSpeechSegment> MergeListAndCalculateSecond(List<SileroSpeechSegment> original, int samplingRate)
        {
            List<SileroSpeechSegment> result = new List<SileroSpeechSegment>();
            if (original == null || original.Count == 0)
            {
                return result;
            }

            int left = original[0].StartOffset.Value;
            int right = original[0].EndOffset.Value;
            if (original.Count > 1)
            {
                original.Sort((a, b) => a.StartOffset.Value.CompareTo(b.StartOffset.Value));
                for (int i = 1; i < original.Count; i++)
                {
                    SileroSpeechSegment segment = original[i];

                    if (segment.StartOffset > right)
                    {
                        result.Add(new SileroSpeechSegment(left, right,
                            CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
                        left = segment.StartOffset.Value;
                        right = segment.EndOffset.Value;
                    }
                    else
                    {
                        right = Math.Max(right, segment.EndOffset.Value);
                    }
                }

                result.Add(new SileroSpeechSegment(left, right,
                    CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
            }
            else
            {
                result.Add(new SileroSpeechSegment(left, right,
                    CalculateSecondByOffset(left, samplingRate), CalculateSecondByOffset(right, samplingRate)));
            }

            return result;
        }

        private float CalculateSecondByOffset(int offset, int samplingRate)
        {
            float secondValue = offset * 1.0f / samplingRate;
            return (float)Math.Floor(secondValue * 1000.0f) / 1000.0f;
        }

        public void Dispose()
        {
            _model.Dispose();
        }
    }
}