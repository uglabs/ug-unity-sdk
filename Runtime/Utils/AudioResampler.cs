using System;
using UG.Services;
using UnityEngine;

namespace UG.Utils
{
    /// <summary>
    /// Zero-allocation audio resampler for converting between different sample rates.
    /// Uses linear interpolation for upsampling and decimation for downsampling.
    /// Optimized for real-time audio processing with minimal GC pressure.
    /// </summary>
    public class AudioResampler
    {
        private readonly int _sourceSampleRate;
        private readonly int _targetSampleRate;
        private readonly float _resampleRatio;
        private readonly bool _needsResampling;

        // Reusable buffers to avoid allocations
        private float[] _tempBuffer;
        private int _tempBufferSize;

        // State for continuous resampling (to handle fractional sample positions)
        private float _samplePosition;
        private float _lastSample;

        public AudioResampler(int sourceSampleRate, int targetSampleRate, int maxFrameSize = 2048)
        {
            _sourceSampleRate = sourceSampleRate;
            _targetSampleRate = targetSampleRate;
            _resampleRatio = (float)targetSampleRate / sourceSampleRate;
            _needsResampling = sourceSampleRate != targetSampleRate;

            // Pre-allocate temp buffer for worst-case scenario (upsampling)
            _tempBufferSize = Mathf.CeilToInt(maxFrameSize * _resampleRatio) + 2; // +2 for safety margin
            _tempBuffer = new float[_tempBufferSize];

            _samplePosition = 0f;
            _lastSample = 0f;
        }

        /// <summary>
        /// Resample input samples to target sample rate.
        /// Returns the number of output samples written to the output buffer.
        /// </summary>
        /// <param name="inputSamples">Input audio samples</param>
        /// <param name="inputLength">Number of input samples to process</param>
        /// <param name="outputSamples">Pre-allocated output buffer</param>
        /// <param name="maxOutputLength">Maximum number of samples that can be written to output</param>
        /// <returns>Number of samples written to output buffer</returns>
        public int Resample(float[] inputSamples, int inputLength, float[] outputSamples, int maxOutputLength)
        {
            if (!_needsResampling)
            {
                // No resampling needed - direct copy
                int copyLength = Mathf.Min(inputLength, maxOutputLength);
                Array.Copy(inputSamples, 0, outputSamples, 0, copyLength);
                return copyLength;
            }

            if (_resampleRatio > 1f)
            {
                // Upsampling
                return UpsampleLinear(inputSamples, inputLength, outputSamples, maxOutputLength);
            }
            else
            {
                // Downsampling
                return DownsampleDecimate(inputSamples, inputLength, outputSamples, maxOutputLength);
            }
        }

        /// <summary>
        /// Calculate the expected output length for a given input length
        /// </summary>
        public int GetExpectedOutputLength(int inputLength)
        {
            if (!_needsResampling) return inputLength;
            return Mathf.RoundToInt(inputLength * _resampleRatio);
        }

        /// <summary>
        /// Reset the resampler state (useful when starting a new audio stream)
        /// </summary>
        public void Reset()
        {
            _samplePosition = 0f;
            _lastSample = 0f;
        }

        private int UpsampleLinear(float[] input, int inputLength, float[] output, int maxOutputLength)
        {
            int outputLength = 0;
            float step = 1f / _resampleRatio;

            for (int i = 0; i < inputLength && outputLength < maxOutputLength; i++)
            {
                float currentSample = input[i];

                // Linear interpolation between _lastSample and currentSample
                while (_samplePosition < 1f && outputLength < maxOutputLength)
                {
                    output[outputLength] = Mathf.Lerp(_lastSample, currentSample, _samplePosition);
                    outputLength++;
                    _samplePosition += step;
                }

                _samplePosition -= 1f;
                _lastSample = currentSample;
            }

            return outputLength;
        }

        private int DownsampleDecimate(float[] input, int inputLength, float[] output, int maxOutputLength)
        {
            int outputLength = 0;

            for (int i = 0; i < inputLength && outputLength < maxOutputLength; i++)
            {
                _samplePosition += _resampleRatio;

                if (_samplePosition >= 1f)
                {
                    // Take this sample
                    output[outputLength] = input[i];
                    outputLength++;
                    _samplePosition -= 1f;
                }
            }

            return outputLength;
        }

        /// <summary>
        /// Convenience method that allocates and returns a new array with resampled data.
        /// Note: This method allocates memory and should be used sparingly in real-time scenarios.
        /// </summary>
        public float[] ResampleAllocating(float[] inputSamples)
        {
            if (!_needsResampling) return inputSamples;

            int expectedOutputLength = GetExpectedOutputLength(inputSamples.Length);
            float[] output = new float[expectedOutputLength];
            int actualOutputLength = Resample(inputSamples, inputSamples.Length, output, expectedOutputLength);

            if (actualOutputLength != expectedOutputLength)
            {
                // Resize array if needed
                float[] resizedOutput = new float[actualOutputLength];
                Array.Copy(output, resizedOutput, actualOutputLength);
                return resizedOutput;
            }

            return output;
        }

        public bool NeedsResampling => _needsResampling;
        public float ResampleRatio => _resampleRatio;
        public int SourceSampleRate => _sourceSampleRate;
        public int TargetSampleRate => _targetSampleRate;
    }
}