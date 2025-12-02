using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OggVorbisEncoder.Example;
using UG.Services.UserInput.AudioRecordingService;
using UnityEngine;

namespace UG.Services
{
    // Using AudioService and VAD
    // Buffer at least 512 samples before sending to VAD
    public class VoiceCaptureService
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
        public Action OnSilenceTimeout;
        public Action OnRecordingTooLong;
        public Action<DateTime, DateTime> OnVADClosingTime;

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

        public VoiceCaptureService(IAudioRecordingService audioRecordingService, IVADService vadService)
        {
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
                UGLog.Log("Voice capture speech length timeout");
                // OnHardTimeout?.Invoke();
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
                        OnSilenceTimeout?.Invoke();
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