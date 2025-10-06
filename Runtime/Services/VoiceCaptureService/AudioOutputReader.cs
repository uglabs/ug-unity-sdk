using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;

namespace UG.Services
{
    /// <summary>
    /// Must be attached to a game object with an AudioListener component.
    /// Avoid GC pressure as much as possible
    /// </summary>
    public unsafe class AudioOutputReader : MonoBehaviour
    {
        // Simple event to emit original Unity audio samples
        public System.Action<float[]> EmitMonoSamples;

        // Flag to control audio processing (set from main thread)
        private volatile bool shouldProcessAudio = false;

        private void Awake()
        {
            // Initialize zero-GC mono conversion buffers
            monoConversionBuffer = new NativeArray<float>(maxExpectedSamples, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            cachedMonoArrays = new Dictionary<int, float[]>();
        }

        private void Start()
        {
            // Set the flag to start processing audio (main thread)
            shouldProcessAudio = true;
        }

        // Zero-GC pre-allocated buffers for mono conversion
        private NativeArray<float> monoConversionBuffer;
        private Dictionary<int, float[]> cachedMonoArrays; // Cache arrays by size to avoid allocations
        private int maxExpectedSamples = 2048; // Max samples we expect per OnAudioFilterRead call

        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Check if we should process audio (thread-safe flag)
            if (!shouldProcessAudio)
                return;

            // Convert to mono and emit immediately - no extra processing
            if (EmitMonoSamples != null)
            {
                ConvertToMonoZeroGC(data, channels);
            }

            return;
        }

        private void ConvertToMonoZeroGC(float[] data, int channels)
        {
            if (data.Length > maxExpectedSamples)
            {
                Debug.LogWarning($"[AudioOutputReader] Input data ({data.Length}) exceeds max expected samples ({maxExpectedSamples})");
                return;
            }

            int outputLength;

            if (channels == 1)
            {
                // Already mono - pass the original array directly (no copy needed!)
                EmitMonoSamples?.Invoke(data);
                return;
            }
            else
            {
                // Convert multi-channel to mono by averaging
                outputLength = data.Length / channels;
            }

            // Get or create cached array for this output length
            if (!cachedMonoArrays.TryGetValue(outputLength, out float[] outputArray))
            {
                outputArray = new float[outputLength];
                cachedMonoArrays[outputLength] = outputArray;
                Debug.Log($"[AudioOutputReader] Created new cached mono array for {outputLength} samples");
            }

            // Convert to mono using the cached array (zero allocation after first use)
            for (int i = 0; i < outputLength; i++)
            {
                float sample = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    sample += data[i * channels + ch];
                }
                outputArray[i] = sample / channels;
            }

            // Emit the cached array (zero allocation!)
            EmitMonoSamples?.Invoke(outputArray);
        }

        /// <summary>
        /// Simple mono conversion - no buffering, just direct conversion
        /// </summary>
        private float[] ConvertToMonoSimple(float[] data, int channels)
        {
            if (channels == 1)
            {
                // Already mono, return copy
                float[] mono = new float[data.Length];
                System.Array.Copy(data, mono, data.Length);
                return mono;
            }
            else
            {
                // Convert multi-channel to mono by averaging
                int monoLength = data.Length / channels;
                float[] mono = new float[monoLength];

                for (int i = 0; i < monoLength; i++)
                {
                    float sample = 0f;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        sample += data[i * channels + ch];
                    }
                    mono[i] = sample / channels;
                }

                return mono;
            }
        }

        private void OnDestroy()
        {
            
        }
    }
}