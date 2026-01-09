using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;
using AudioUtils = UG.Utils.AudioUtils;
using UG.Utils;

namespace UG.Services.AudioStreamingService
{
    // Handles buffering/streaming of audio from the server (mp3)
    public class AudioStreamer : MonoBehaviour
    {
        #region Constants
        private const int BufferSize = 256; // Buffer size in samples
        private const int SampleRate = 44100; // Sample rate
        private const float MinDataToPlayKb = 16; // Wait for 16Kb of data
        private float _audioOutputLatency = 0.200f; // Time to wait after samples are read before signaling complete
        #endregion


        private AudioClip _audioClip;
        private AudioSource _audioSource;
        private readonly AudioChunkFormatter _chunkFormatter = new();
        private MP3Decoder _mp3Decoder;
        private int _dataReadPointer = 0;
        private List<byte[]> _completeChunks = new();
        private float[] _samplesData;
        private int _totalSamplesDataAdded;
        private bool _isEndOfStream = false;
        private int _totalEmptySamplesCount = 0;
        private int _firstSampleValue = -1;
        private bool _isAllSamplesPlayed = false;
        private bool _isPlayingSamples;
        private double _samplesReadFinishedTime = -1;
        private float _totalPlaybackTime = 0f;
        private bool _isStreaming = false;
        private float playbackTime = 0f;
        private double playbackTimeDSP = 0f;
        private int lastSamplePosition;
        private double streamStartTime;
        private string _outputAudioFormat = "audio/mpeg"; //audio/pcm
        private int _bufferingLevel = 1;
        private AudioOutputBenchmark _audioOutputBenchmark;
        public AudioOutputBenchmark AudioOutputBenchmark => _audioOutputBenchmark;
        private bool _initialBufferedChunksDecoded = false;
        private int _lastDecodedChunkIndex = 0;

        #region Actions
        public Action<float> OnPlaybackTimeUpdate; // Playback time in seconds
        public Action OnPlaybackComplete;
        public Action OnStartedPlayingSamples;
        private int _detectedSampleRate;
        #endregion

        public void Init(string audioFormat, int bufferingLevel)
        {
            _outputAudioFormat = audioFormat;
            _bufferingLevel = bufferingLevel;

            if (_audioSource == null)
            {
                AudioSource audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.loop = true;
                audioSource.playOnAwake = false;
                _audioSource = audioSource;
            }
            if (_audioOutputBenchmark == null)
            {
                _audioOutputBenchmark = this.gameObject.AddComponent<AudioOutputBenchmark>();
            }
        }

        void Start()
        {
            _chunkFormatter.OnCompleteChunk += (completeChunk) =>
            {
                AddCompleteChunk(completeChunk);
            };

#if DEBUG_AUDIO
            CreateDebugger();
#endif
        }

        public void Play()
        {
            _audioClip = AudioClip.Create("StreamedAudio", BufferSize, 1, SampleRate, true, OnAudioRead);
            _audioSource.clip = _audioClip;
            _audioSource.volume = 0.9f;
            _audioSource.Play();
        }

        public void Stop()
        {
            _audioSource.Pause();
        }

        public bool IsStreaming()
        {
            if (_audioSource == null || !_audioSource.isPlaying)
            {
                return false;
            }

            // Check if we have any data to play
            if (_samplesData == null || _samplesData.Length == 0)
            {
                return false;
            }

            if (_isEndOfStream && _isAllSamplesPlayed)
            {
                return false;
            }

            return true;
        }

        // Add chunk to pass through the formatter (might not be complete)
        public void AddChunk(byte[] chunk)
        {
            if (_mp3Decoder == null)
            {
                _mp3Decoder = new MP3Decoder();
            }
            _chunkFormatter.AddChunk(chunk);
        }

        public void ForcePlayChunks()
        {
            AddCompleteChunk(new byte[0], true);
        }

        private void AddCompleteChunk(byte[] chunk, bool isDontWaitForSecondChunk = false)
        {
            if (chunk != null && chunk.Length != 0)
            {
                _completeChunks.Add(chunk);
            }

            // Calculate total size of all chunks
            int totalSize = _completeChunks.Sum(c => c?.Length ?? 0);
            float totalSizeKB = totalSize / 1024f;
            bool hasEnoughData = totalSizeKB >= (MinDataToPlayKb * _bufferingLevel);

            if (!hasEnoughData && !isDontWaitForSecondChunk)
            {
                UGLog.Log("[AudioStreamer] Not enough data to play, waiting for more");
                return;
            }

            // If we don't have any chunks, don't play
            if (_completeChunks == null || _completeChunks.Count == 0)
            {
                UGLog.Log("[AudioStreamer] No chunks to play");
                return;
            }

            // Decode chunks - on first playback, decode all buffered chunks that haven't been decoded yet
            // This fixes audio cutoff at the beginning when chunks were buffered but not decoded
            float[] newSamples;
            int chunksToDecodeFrom = _lastDecodedChunkIndex;
            int chunksToDecodeTo = _completeChunks.Count;
            
            if (!_initialBufferedChunksDecoded && chunksToDecodeTo > chunksToDecodeFrom)
            {
                // First time decoding - decode all buffered chunks at once
                var chunksToProcess = _completeChunks.Skip(chunksToDecodeFrom).Take(chunksToDecodeTo - chunksToDecodeFrom).ToList();
                byte[] allChunks = CombineChunks(chunksToProcess);
                newSamples = DecodeChunk(allChunks);
                _lastDecodedChunkIndex = chunksToDecodeTo;
                _initialBufferedChunksDecoded = true;
                UGLog.Log($"[AudioStreamer] Starting playback - decoded {chunksToProcess.Count} buffered chunks ({allChunks.Length} bytes)");
            }
            else if (chunk != null && chunk.Length > 0)
            {
                // Subsequent chunks - decode only the new chunk
                newSamples = DecodeChunk(chunk);
                _lastDecodedChunkIndex = _completeChunks.Count;
            }
            else
            {
                newSamples = new float[0];
            }

            // Combine new samples with existing samples
            if (_samplesData == null || _samplesData.Length == 0)
            {
                _samplesData = newSamples;
                _totalSamplesDataAdded = newSamples.Length;
            }
            else if (newSamples.Length > 0)
            {
                // Combine existing samples with new samples
                float[] combinedSamples = new float[_samplesData.Length + newSamples.Length];
                Array.Copy(_samplesData, 0, combinedSamples, 0, _samplesData.Length);
                Array.Copy(newSamples, 0, combinedSamples, _samplesData.Length, newSamples.Length);
                _samplesData = combinedSamples;
                _totalSamplesDataAdded = _samplesData.Length;
            }
        }

        private float[] DecodeChunk(byte[] chunk)
        {
            if (chunk == null || chunk.Length == 0)
                return new float[0];

            try
            {
                float[] decodedSamples = _mp3Decoder.ProcessChunk(chunk);

                // Log audio format on first successful decode
                if (decodedSamples.Length > 0 && !_isStreaming)
                {
                    (int sampleRate, int channels) = _mp3Decoder.GetAudioFormat();
                    int detectedSampleRate = _mp3Decoder.GetDetectedSampleRate();
                    _detectedSampleRate = detectedSampleRate;
                    UGLog.Log($"Decoded audio format: {detectedSampleRate}Hz, {channels} channels");
                }

                return decodedSamples;
            }
            catch (Exception ex)
            {
                UGLog.LogError($"Error processing chunk with NLayer: {ex.Message}");
                return new float[0];
            }
        }

        private void OnAudioRead(float[] data)
        {
            int dataOffset = 0;
            int emptySamples = 0;
            while (dataOffset < data.Length)
            {
                if (_samplesData != null && _dataReadPointer + data.Length <= _samplesData.Length)
                {
                    Array.Copy(_samplesData, _dataReadPointer, data, 0, data.Length);
                    _dataReadPointer += data.Length;
                    dataOffset += data.Length;
                }
                else if (_samplesData != null && _dataReadPointer < _samplesData.Length)
                {
                    // We have some samples but not a full buffer
                    int remainingSamples = _samplesData.Length - _dataReadPointer;
                    Array.Copy(_samplesData, _dataReadPointer, data, 0, remainingSamples);
                    _dataReadPointer += remainingSamples;
                    dataOffset += remainingSamples;

                    // Fill the rest with silence
                    while (dataOffset < data.Length)
                    {
                        if (_isPlayingSamples)
                        {
                            emptySamples++;
                        }
                        data[dataOffset++] = 0f;
                    }
                }
                else
                {
                    // No samples left, fill entire buffer with silence
                    while (dataOffset < data.Length)
                    {
                        if (_isPlayingSamples)
                        {
                            emptySamples++;
                        }

                        data[dataOffset++] = 0f;
                    }
                }
            }

            if (dataOffset < data.Length)
            {
                for (int i = dataOffset; i < data.Length; i++)
                {
                    data[i] = 0;
                    if (_isPlayingSamples)
                    {
                        emptySamples++;
                    }
                }
            }

            if (!_isEndOfStream)
            {
                _totalEmptySamplesCount += emptySamples;
            }

            if (_isStreaming && _isPlayingSamples)
            {
                // Calculate the total time based on the non-empty samples
                float secondsPerFrame = (float)data.Length / (float)(SampleRate * 2);
                float actualTimePlayed = secondsPerFrame * (data.Length - emptySamples) / data.Length;

                _totalPlaybackTime += actualTimePlayed;
            }
        }

        public void Update()
        {
            bool isStreaming = IsStreaming();

            // Check for playback complete and notify listeners
            if (_isStreaming && !isStreaming)
            {
#if DEBUG_SAVE_AUDIO
                string filePath = $"{Application.persistentDataPath}/audio_file_new{DateTime.Now.Ticks}_.wav";

                UGLog.Log($"Saving audio to file: {filePath}");

                // Append the bytes to the file
                if (!File.Exists(filePath))
                {
                    File.WriteAllBytes(filePath, CombineChunks(_completeChunks));
                }
#endif
                // Calculate time corresponding to empty samples and subtract from DSP time
                float emptySamplesTime = (float)_totalEmptySamplesCount / (SampleRate * 2);
                float actualPlaybackTime = (float)playbackTimeDSP - emptySamplesTime + 0.5f;

                OnPlaybackTimeUpdate?.Invoke(actualPlaybackTime);
                OnPlaybackComplete?.Invoke();
                UGLog.Log("[Player] Playback complete");
            }
            _isStreaming = isStreaming;

            // Track when the all samples actually passed through the audio system
            if (_audioClip != null && _samplesData != null)
            {
                if (!_isPlayingSamples)
                {
                    streamStartTime = AudioSettings.dspTime;
                    // Notify listeners that samples actually started playing
                    UGLog.Log("OnStartedPlayingSamples");
                    OnStartedPlayingSamples?.Invoke();
                }

                _isPlayingSamples = true;
                int samplePlayedValue = Mathf.RoundToInt((float)AudioSettings.dspTime * _audioClip.frequency);
                if (_firstSampleValue == -1)
                {
                    _firstSampleValue = samplePlayedValue;
                }
                else if (_isEndOfStream)
                {
                    // Once we added all of the samples available - we can start checking for _isAllSamplesPlayed
                    int totalDataLength = _samplesData.Length + _totalEmptySamplesCount;
                    bool allSamplesRead = _dataReadPointer >= _samplesData.Length - 128;
                    
                    // Record time when reading finished, then wait for audio output latency
                    if (allSamplesRead && _samplesReadFinishedTime < 0)
                    {
                        _samplesReadFinishedTime = AudioSettings.dspTime;
                    }
                    _isAllSamplesPlayed = _samplesReadFinishedTime > 0 && 
                                          AudioSettings.dspTime >= _samplesReadFinishedTime + _audioOutputLatency;
                }
            }

            if (_isStreaming && _totalPlaybackTime > 0 && _audioSource.isPlaying)
            {
                // Data was added, but not played through yet - wait for an actual playback before reporting
                int currentSamplePosition = _audioSource.timeSamples;
                // Calculate the difference in samples since the last frame
                int samplesPlayedThisFrame = currentSamplePosition - lastSamplePosition;

                // Ensure that we handle wraparound correctly (looping)
                if (samplesPlayedThisFrame < 0)
                {
                    samplesPlayedThisFrame += _audioSource.clip.samples;
                }

                // Update the playback time based on the sample rate
                playbackTime += (float)samplesPlayedThisFrame / SampleRate;

                // Update DSP time
                if (_totalPlaybackTime > playbackTimeDSP)
                {
                    playbackTimeDSP += AudioSettings.dspTime - streamStartTime;
                }
                else
                {
                    streamStartTime = AudioSettings.dspTime;
                }

                // Update last sample position for the next frame
                lastSamplePosition = currentSamplePosition;

                // Calculate time corresponding to empty samples and subtract from DSP time
                float emptySamplesTime = (float)_totalEmptySamplesCount / (SampleRate * 2);
                float actualPlaybackTime = (float)playbackTimeDSP - emptySamplesTime;

                OnPlaybackTimeUpdate?.Invoke(actualPlaybackTime); // used to be just playback time
            }
        }

        private byte[] CombineChunks(List<byte[]> chunks)
        {
            using MemoryStream memoryStream = new();
            foreach (byte[] chunk in chunks)
            {
                memoryStream.Write(chunk, 0, chunk.Length);
            }

            return memoryStream.ToArray();
        }

        public void SetEndOfStream()
        {
            UGLog.Log("SetEndOfStream; samples data length: " + _samplesData?.Length);
            _isEndOfStream = true;
        }
        public bool IsEndOfStream() => _isEndOfStream;

        /// <summary>
        /// Sets the audio output latency (in seconds) to wait after samples are read before signaling playback complete.
        /// Default is 0.5 seconds. Increase if audio is being cut off at the end.
        /// </summary>
        public void SetAudioOutputLatency(float latencySeconds)
        {
            _audioOutputLatency = latencySeconds;
        }

        public void Flush()
        {
            if (_audioSource != null)
            {
                _audioSource.clip = null;
                _audioSource.Stop();
            }
            if (_mp3Decoder != null)
            {
                _mp3Decoder.Dispose();
                _mp3Decoder = null;
            }
            _dataReadPointer = 0;
            _completeChunks.Clear();
            _chunkFormatter.Clear();
            _samplesData = null;
            _isEndOfStream = false;
            _isAllSamplesPlayed = false;
            _firstSampleValue = -1;
            _totalEmptySamplesCount = 0;
            _totalPlaybackTime = 0;
            playbackTime = 0;
            playbackTimeDSP = 0;
            lastSamplePosition = 0;
            _isPlayingSamples = false;
            _totalSamplesDataAdded = 0;
            _detectedSampleRate = 0;
            _samplesReadFinishedTime = -1;
            _initialBufferedChunksDecoded = false;
            _lastDecodedChunkIndex = 0;
        }

#if DEBUG_AUDIO
        private void CreateDebugger()
        {
            // Check if debugger already exists
            var existingDebugger = FindObjectOfType<AudioStreamerDebugger>();
            if (existingDebugger != null && existingDebugger.targetStreamer == this)
            {
                return; // Debugger already exists for this streamer
            }

            // Create debugger GameObject
            GameObject debuggerObject = new GameObject($"AudioStreamer_Debugger_{gameObject.name}");
            debuggerObject.transform.SetParent(transform);

            // Add debugger component and set reference to this streamer
            var debugger = debuggerObject.AddComponent<AudioStreamerDebugger>();
            debugger.targetStreamer = this;

            UGLog.Log("[DEBUG] Audio Streamer Debugger created");
        }
#endif
    }
}