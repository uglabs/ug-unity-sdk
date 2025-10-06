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
        private const int BufferSize = 256; // Buffer size in samples
        private const int SampleRate = 44100; // Sample rate
        private const float MinDataToPlayKb = 16; // Wait for 16Kb of data
        private AudioClip _audioClip;
        private AudioSource _audioSource;
        private readonly AudioChunkFormatter _chunkFormatter = new();
        private int _dataReadPointer = 0;
        private List<byte[]> _completeChunks = new();
        private float[] _samplesData;
        private bool _isEndOfStream = false;
        private int _totalEmptySamplesCount = 0;
        private int _firstSampleValue = -1;
        private bool _isAllSamplesPlayed = false;
        private bool _isPlayingSamples;
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

        #region Actions
        public Action<float> OnPlaybackTimeUpdate; // Playback time in seconds
        public Action OnPlaybackComplete;
        public Action OnStartedPlayingSamples;
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
            bool isPlayingMP3 = _outputAudioFormat == "audio/mpeg";

            _audioClip = AudioClip.Create("StreamedAudio", BufferSize, 1, SampleRate * (isPlayingMP3 ? 2 : 1), true, OnAudioRead);
            _audioSource.clip = _audioClip;
            _audioSource.volume = 0.9f;
            _audioSource.Play();
            UGLog.Log("Start playback: " + DateTime.Now.ToString("HH:mm:ss:fff"));
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
            _chunkFormatter.AddChunk(chunk);
        }

        public void ForcePlayChunks()
        {
            AddCompleteChunk(new byte[0], true, false);
        }

        private void AddCompleteChunk(byte[] chunk, bool isDontWaitForSecondChunk = false, bool isTrace = true)
        {
            if (chunk != null && chunk.Length != 0)
            {
                _completeChunks.Add(chunk);

                // Log size of first chunk in KB
                if (_completeChunks.Count == 1)
                {
                    float chunkSizeKB = chunk.Length / 1024f;
                    UGLog.Log($"First audio chunk size: {chunkSizeKB:F2} KB");
                }
            }

            // Calculate total size of all chunks
            int totalSize = _completeChunks.Sum(c => c?.Length ?? 0);
            float totalSizeKB = totalSize / 1024f;
            bool hasEnoughData = totalSizeKB >= (MinDataToPlayKb * _bufferingLevel);

            if (!hasEnoughData && !isDontWaitForSecondChunk)
            {
                return;
            }

            // If we don't have any chunks, don't play
            if (_completeChunks == null || _completeChunks.Count == 0)
            {
                return;
            }

            bool isStartingPlayback = !_isStreaming && !_isPlayingSamples;

            _samplesData = AudioUtils.ConvertMP3ToPCM(CombineChunks(_completeChunks));
        }

        private void OnAudioRead(float[] data)
        {
            int dataOffset = 0;
            int emptySamples = 0;
            while (dataOffset < data.Length)
            {
                if (_samplesData != null && _samplesData.Length > data.Length + _dataReadPointer)
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
                    _isAllSamplesPlayed = _dataReadPointer >= _samplesData.Length - 128;
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

        public void Flush()
        {
            if (_audioSource != null)
            {
                _audioSource.clip = null;
                _audioSource.Stop();
            }
            _dataReadPointer = 0;
            _completeChunks.Clear();
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