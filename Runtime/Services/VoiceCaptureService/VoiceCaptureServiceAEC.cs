using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OggVorbisEncoder.Example;
using UG.Services.UserInput.AudioRecordingService;
using UnityEngine;

namespace UG.Services
{
    public class VoiceCaptureServiceAEC
    {
        private static int BUFFER_SIZE = 512; // only 512 is supported for 16000Hz audio
        private static int MAX_SPEECH_DURATION_SECONDS = 13; // above 14 we might have an issue
        private static int MAX_WAIT_FOR_SPEECH_DURATION_SECONDS = 28;
        private IAudioRecordingService _audioRecordingService;
        private IVADService _vadService;
        private List<float> _sampleBuffer = new();
        private List<float> _recordingSamples = new();
        private DateTime _recordingStartTime;
        public Action OnSpoke;
        public Action OnSilenced;
        public Action OnTimeout;
        public Action OnHardTimeout;
        public Action OnRecordingTooLong;
        public Action<DateTime, DateTime> OnVADClosingTime;
        private int _targetSampleRate = 16000;
        private int _sourceSampleRate = 0;
        private bool isGotFirstMicSample = false;
        private readonly object _audioLock = new object();
        private bool _isProcessing = false;
        private int WEBRTC_FRAME_SIZE = 160;
        private int STREAM_DELAY_MS = 30;

        private List<float> vadBuffer = new List<float>(); // Buffer for VAD (needs 512 samples)
        private List<float> processedAECSamples = new List<float>();
        private List<float> sourceMicSamples = new List<float>();
        private List<float> sourceSpeakerSamples = new List<float>();

        public VoiceCaptureState State { get; private set; } = VoiceCaptureState.Idle;
        public enum VoiceCaptureState
        {
            Idle,
            Recording,
            Interrupted,
            RecordingFinished,
            RecordingTooLong,
            RecordingWaitForSpeech
        }

        #region Reltime voice conversion
        protected MemoryStream _audioMicEncodedOutputStream = new();
        public ConcurrentQueue<float[]> _micRawDataQueue = new();
        public ConcurrentQueue<byte[]> _micEncodedStreamDataQueue = new();
        private CancellationTokenSource _audioConversionCancellationToken = new();
        private Task _streamConversionTask;
        private bool _isKeepOpenOnSilenced = true;
        #endregion

        public VoiceCaptureServiceAEC(IAudioRecordingService audioRecordingService,
            IVADService vadService)
        {
            AudioConfiguration config = AudioSettings.GetConfiguration();
            _sourceSampleRate = config.sampleRate; // 48000 on Unity, 24000 on Android, 48000 on iOS
            WEBRTC_FRAME_SIZE = _targetSampleRate / 100;

            InitializeWebRTC();

            _audioRecordingService = audioRecordingService;
            _vadService = vadService;
            _vadService.OnSpokeWithSamples += (samples) =>
            {
                OnSpoke?.Invoke();

                _recordingStartTime = DateTime.Now;

                // Keep only last 1 second of data in the queue (16000 samples at 16000Hz)
                const int oneSecondSamples = 16000;
                int totalSamples = 0;
                foreach (var queueItem in _micRawDataQueue)
                {
                    totalSamples += queueItem.Length;
                }

                // Remove oldest entries until we're under the 1-second limit
                while (totalSamples > oneSecondSamples && _micRawDataQueue.TryDequeue(out var oldestSamples))
                {
                    totalSamples -= oldestSamples.Length;
                }

                SetState(VoiceCaptureState.Recording);

                // Check if stream conversion is still running - if it already is, we don't start a new one
                if (IsConversionTaskRunning())
                {
                    return; // Task is still running, don't start a new one
                }

                _audioMicEncodedOutputStream = new();
                _audioConversionCancellationToken = new(); // we don't cancel this in-between sending audio (turn-taker)

                _streamConversionTask = Task.Run(() =>
                {
                    StreamingEncoder.ConvertPCMToOggVorbis(_micRawDataQueue, _audioMicEncodedOutputStream, _micEncodedStreamDataQueue, 16000, 1,
                         StreamingEncoder.PcmSample.SixteenBit, 16000, 1, _audioConversionCancellationToken.Token);
                }).ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        UGLog.LogError($"Task failed: {task.Exception?.Flatten().Message}");
                    }
                });
            };
            _vadService.OnSilenced += () =>
            {
                OnSilenced?.Invoke();

                if (_isKeepOpenOnSilenced)
                {
                    return;
                }
                else
                {
                    SetState(VoiceCaptureState.RecordingFinished);
                    FinishConversionStream();
                }
            };
            _vadService.OnHardTimeout += () =>
            {
                OnHardTimeout?.Invoke();
            };

            _vadService.OnVADClosingTime += (start, end) => OnVADClosingTime?.Invoke(start, end);

            _audioRecordingService.OnSamplesRecorded += (samples) =>
            {
                float[] samplesCopy = new float[samples.Length];
                Array.Copy(samples, samplesCopy, samples.Length);

                // Add new samples to buffer for VAD
                _sampleBuffer.AddRange(samples);
                // data for all samples (returned on recording finished) - raw data
                _recordingSamples.AddRange(samplesCopy);
                // data for realtime voice conversion - to be converted to ogg
                _micRawDataQueue.Enqueue(samplesCopy);

                // Process buffer when it reaches or exceeds BUFFER_SIZE
                while (_sampleBuffer.Count >= BUFFER_SIZE)
                {
                    float[] bufferedSamples = _sampleBuffer.GetRange(0, BUFFER_SIZE).ToArray();
                    CheckForSpeech(bufferedSamples);
                    _sampleBuffer.RemoveRange(0, BUFFER_SIZE);
                }
            };
        }

        private void InitializeWebRTC()
        {
            UGLog.Log("[CleanRealtimeTest] Initializing WebRTC...");
            int initResult = UGLibInterface.InitWebRTC();
            if (initResult == 0)
            {
               UGLog.Log("[CleanRealtimeTest] WebRTC initialization successful");
                UGLog.Log($"[CleanRealtimeTest] WebRTC minor version: {UGLibInterface.GetTestMinorVersion()}");
            }
            else
            {
                UGLog.LogError($"[CleanRealtimeTest] WebRTC initialization failed with error: {initResult}");
            }

            UGLog.Log($"[CleanRealtimeTest] Setting stream delay to 10ms");

            UGLibInterface.SetEchoCancellation(true, false);
            UGLibInterface.SetGainController1(true, 1); // 0 - analog 1 - digital 2 - fixed digital
            UGLibInterface.SetGainController2(true);
            UGLibInterface.SetHighPassFilter(true);
            UGLibInterface.SetPreGainFactor(1.4f);
             // UGLibInterface.SetNoiseSuppression(true, 0);
        }

        public void OnMicrophoneSamplesReceived(float[] samples)
        {
            UGLog.Log($"[OnMicrophoneSamplesReceived] Received {samples.Length} samples");

            lock (_audioLock)
            {
                sourceMicSamples.AddRange(samples);

                if (!isGotFirstMicSample)
                {
                    isGotFirstMicSample = true;

                    // Calculate how many Unity samples correspond to the mic samples we have
                    // Mic is at 16000 Hz, Unity audio is at _sourceSampleRate
                    float ratio = (float)_sourceSampleRate / _targetSampleRate; // e.g., 48000/16000 = 3
                    int correspondingUnitySamples = Mathf.RoundToInt(sourceMicSamples.Count * ratio);

                    // Keep only the last N Unity samples that correspond to our mic data
                    if (sourceSpeakerSamples.Count > correspondingUnitySamples)
                    {
                        int samplesToRemove = sourceSpeakerSamples.Count - correspondingUnitySamples;
                        sourceSpeakerSamples.RemoveRange(0, samplesToRemove);
                        UGLog.Log($"[OnMicrophoneSamplesReceived] Trimmed {samplesToRemove} Unity samples, kept {sourceSpeakerSamples.Count} samples to match {sourceMicSamples.Count} mic samples");
                    }
                }
            }

            // Only trigger processing if we have enough samples for at least one complete frame
            if (sourceMicSamples.Count >= WEBRTC_FRAME_SIZE)
            {
                _ = RunProcessing();
            }
        }

        private async Task RunProcessing()
        {
            // Prevent multiple processing threads from running simultaneously
            if (_isProcessing) return;
            _isProcessing = true;

            try
            {
                await Task.Run(async () =>
                {
                    await Task.Delay(1);

                    // Process all available frames
                    while (true)
                    {
                        List<float> micChunk = new List<float>();
                        List<float> unityChunk = new List<float>();
                        bool hasFrame = false;

                        lock (_audioLock)
                        {
                            // Check if we have enough samples for processing
                            // For Unity audio, we need more samples if resampling is required
                            int requiredUnitySamples = _sourceSampleRate != _targetSampleRate ?
                                Mathf.RoundToInt(WEBRTC_FRAME_SIZE * (float)_sourceSampleRate / _targetSampleRate) :
                                WEBRTC_FRAME_SIZE;

                            if (sourceMicSamples.Count >= WEBRTC_FRAME_SIZE && sourceSpeakerSamples.Count >= requiredUnitySamples)
                            {
                                hasFrame = true;

                                // Extract exactly one frame from each stream
                                for (int i = 0; i < WEBRTC_FRAME_SIZE; i++)
                                {
                                    micChunk.Add(sourceMicSamples[i]);
                                }

                                // Extract Unity samples (more if resampling needed)
                                for (int i = 0; i < requiredUnitySamples; i++)
                                {
                                    unityChunk.Add(sourceSpeakerSamples[i]);
                                }

                                // Remove processed samples from the beginning
                                sourceMicSamples.RemoveRange(0, WEBRTC_FRAME_SIZE);
                                sourceSpeakerSamples.RemoveRange(0, requiredUnitySamples);

                                UGLog.Log($"[ProcessAudioInRealTime] Processed frame - Remaining: Mic: {sourceMicSamples.Count}, Unity: {sourceSpeakerSamples.Count}");
                            }
                        }

                        if (!hasFrame)
                            break;

                        // Resample Unity audio if needed
                        float[] resampledUnityChunk = unityChunk.ToArray();
                        if (_sourceSampleRate != _targetSampleRate)
                        {
                            resampledUnityChunk = DownsampleFrame(unityChunk.ToArray(), _sourceSampleRate, _targetSampleRate);
                            UGLog.Log($"[ProcessAudioInRealTime] Resampled Unity chunk: {unityChunk.Count} -> {resampledUnityChunk.Length} samples");
                        }

                        UGLog.Log($"[ProcessAudioInRealTime] Processing frame - Unity: {resampledUnityChunk.Length}, Mic: {micChunk.Count} samples");

                        // Process through WebRTC on background thread
                        float[] processedChunk = ProcessFrameThroughWebRTC(resampledUnityChunk, micChunk.ToArray());

                        if (processedChunk != null)
                        {
                            // Add to processed samples (thread-safe)
                            lock (_audioLock)
                            {
                                processedAECSamples.AddRange(processedChunk);
                                vadBuffer.AddRange(processedChunk);
                            }

                            // Send to VAD service when we have enough samples (512 samples)
                            lock (_audioLock)
                            {
                                if (vadBuffer.Count >= 512)
                                {
                                    float[] vadChunk = vadBuffer.GetRange(0, 512).ToArray();
                                    vadBuffer.RemoveRange(0, 512);
                                    _vadService.AddAudio(vadChunk);
                                    UGLog.Log($"[ProcessAudioInRealTime] Sent {vadChunk.Length} samples to VAD");
                                }
                            }

                            UGLog.Log($"[ProcessAudioInRealTime] Processed frame: {processedChunk.Length} samples, VAD buffer: {vadBuffer.Count}");
                        }
                    }
                });
            }
            catch (Exception e)
            {
                UGLog.LogError($"[ProcessAudioInRealTime] Error processing audio: {e.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private float[] ProcessFrameThroughWebRTC(float[] unityFrame, float[] micFrame)
        {
            try
            {
                // Set stream delay before processing
                UGLibInterface.SetStreamDelayMs(STREAM_DELAY_MS);

                // Create output buffer for processed audio
                float[] processedFrame = new float[WEBRTC_FRAME_SIZE];

                // Process through WebRTC using UnsafeAddrOfPinnedArrayElement
                // Note: Now using target sample rate since we've resampled the data
                int result = UGLibInterface.PushAudioOutMicInSamples(
                    GetFloatArrayPointer(unityFrame),
                    GetFloatArrayPointer(micFrame),
                    WEBRTC_FRAME_SIZE,
                    _targetSampleRate, // Use target sample rate since data is now resampled
                    GetFloatArrayPointer(processedFrame)
                );

                if (result == 0)
                {
                    return processedFrame;
                }
                else
                {
                    UGLog.LogWarning($"[CleanRealtimeTest] WebRTC processing failed with result: {result}");
                    return null;
                }
            }
            catch (Exception e)
            {
                UGLog.LogError($"[CleanRealtimeTest] Error processing frame through WebRTC: {e.Message}");
                return null;
            }
        }

        private IntPtr GetFloatArrayPointer(float[] array)
        {
            return Marshal.UnsafeAddrOfPinnedArrayElement(array, 0);
        }

        /// <summary>
        /// Simple downsampling using linear interpolation for a single frame
        /// Ensures output is exactly WEBRTC_FRAME_SIZE samples
        /// </summary>
        private float[] DownsampleFrame(float[] inputSamples, int sourceSampleRate, int targetSampleRate)
        {
            if (sourceSampleRate == targetSampleRate)
                return inputSamples;

            // Always output exactly WEBRTC_FRAME_SIZE samples
            float[] outputSamples = new float[WEBRTC_FRAME_SIZE];
            float ratio = (float)sourceSampleRate / targetSampleRate;

            for (int i = 0; i < WEBRTC_FRAME_SIZE; i++)
            {
                float sourceIndex = i * ratio;
                int index = Mathf.FloorToInt(sourceIndex);
                float fraction = sourceIndex - index;

                if (index >= inputSamples.Length - 1)
                {
                    // Handle edge case - use last sample
                    outputSamples[i] = inputSamples[inputSamples.Length - 1];
                }
                else
                {
                    // Linear interpolation between two samples
                    float sample1 = inputSamples[index];
                    float sample2 = inputSamples[index + 1];
                    float interpolatedSample = sample1 + fraction * (sample2 - sample1);
                    outputSamples[i] = interpolatedSample;
                }
            }

            return outputSamples;
        }

        private void SetState(VoiceCaptureState state)
        {
            State = state;
            UGLog.Log("Voice capture service State: " + state);
        }

        // This will make sure conversion task is finished after all the data is processed
        // TODO: We could just force-convert the remaining data
        private async void FinishConversionStream()
        {
            DateTime timeoutCheckStartTime = DateTime.MaxValue;
            while (true)
            {
                UGLog.Log($"Wait for complete stream: {_micEncodedStreamDataQueue.Count} {_micRawDataQueue.Count} task running: {IsConversionTaskRunning()}");
                await Task.Delay(10);
                if (!IsConversionTaskRunning())
                {
                    break;
                }

                if ((DateTime.Now - timeoutCheckStartTime).TotalSeconds > 2)
                {
                    UGLog.Log("Finish conversion stream by timeout");
                    break;
                }

                if (_micEncodedStreamDataQueue.Count > 0 && _micRawDataQueue.Count == 0)
                {
                    if (timeoutCheckStartTime == DateTime.MaxValue)
                    {
                        timeoutCheckStartTime = DateTime.Now;
                    }
                }
                else
                {
                    timeoutCheckStartTime = DateTime.MaxValue;
                }

                if (_micEncodedStreamDataQueue.Count > 0) continue;
                if (_micRawDataQueue.Count > 0) continue;
                break;
            }

            _audioConversionCancellationToken?.Cancel();
        }

        public void StartRecording()
        {
            UGLog.Log("Start recording");
            Clear(); // there might be some unconverted data left from previous recording
            _recordingSamples = new();
            _recordingStartTime = DateTime.Now;
            _audioRecordingService.StartRecording();
            SetState(VoiceCaptureState.RecordingWaitForSpeech);
        }

        private void CheckForSpeech(float[] samples)
        {
            try
            {
                if ((DateTime.Now - _recordingStartTime).TotalSeconds >= MAX_SPEECH_DURATION_SECONDS)
                {
                    if (State == VoiceCaptureState.Recording)
                    {
                        UGLog.Log("Recording for too long - background noise");
                        // Been recording for too long - background noise
                        StopRecording();
                        _micEncodedStreamDataQueue.Clear();
                        _micRawDataQueue.Clear();
                        FinishConversionStream();
                        OnRecordingTooLong?.Invoke();
                        SetState(VoiceCaptureState.RecordingTooLong);
                        return;
                    }
                }

                if ((DateTime.Now - _recordingStartTime).TotalSeconds >= MAX_WAIT_FOR_SPEECH_DURATION_SECONDS)
                {
                    if (State == VoiceCaptureState.RecordingWaitForSpeech)
                    {
                        // User didn't say anything - stop recording, clear everything and invoke timeout
                        StopRecording();
                        _micEncodedStreamDataQueue.Clear();
                        _micRawDataQueue.Clear();
                        FinishConversionStream();
                        OnTimeout?.Invoke();
                        return;
                    }
                }

                if (_vadService == null)
                {
                    UGLog.LogError("Detector is null");
                    return;
                }

                var speechBuffer = _vadService.AddAudio(samples);
                if (speechBuffer == null)
                {
                    return;
                }
                else
                {
                    UGLog.Log("Speech buffer: " + speechBuffer.Samples.Count());
                    UGLog.Log("Speech start: " + speechBuffer.Start + " end: " + speechBuffer.End);
                }
            }
            catch (Exception e)
            {
                UGLog.LogError("Exception: " + e);
            }
        }

        public void StopRecording()
        {
            _audioRecordingService.StopRecording();
            _vadService.Reset();
            _sampleBuffer.Clear();

            FinishConversionStream();

            SetState(VoiceCaptureState.RecordingFinished);
        }

        public float[] GetSamples()
        {
            return _recordingSamples.ToArray();
        }

        public byte[] GetBytes()
        {
            MemoryStream memoryStream = new MemoryStream();
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            float[] samplesData = _recordingSamples.ToArray();
            for (int i = 0; i < samplesData.Length; i++)
            {
                short value = (short)(samplesData[i] * 32767f);
                binaryWriter.Write(value);
            }

            return memoryStream.ToArray();
        }

        public void Interrupt()
        {
            // Stop recording was already called - but we need to clean up conversion
            // Discard any audio data accumulated so far (raw & converted)
            Clear();
            // Finish conversion stream
            FinishConversionStream();
            // Set state to interrupted
            SetState(VoiceCaptureState.Interrupted);
        }

        public void Clear()
        {
            _micEncodedStreamDataQueue.Clear();
            _micRawDataQueue.Clear();
            _audioMicEncodedOutputStream = new();
        }

        internal bool IsAllDataProcessed()
        {
            if (_audioConversionCancellationToken == null || _audioConversionCancellationToken.IsCancellationRequested)
            {
                return true;
            }
            return _micEncodedStreamDataQueue.Count == 0 && _micRawDataQueue.Count == 0;
        }

        public void Dispose()
        {
            Clear();
            _audioRecordingService?.Dispose();
            _vadService?.Dispose();
        }

        public bool IsConversionTaskRunning()
        {
            return _streamConversionTask != null &&
                   !_streamConversionTask.IsCompleted &&
                   !_streamConversionTask.IsFaulted &&
                   !_streamConversionTask.IsCanceled;
        }
    }
}