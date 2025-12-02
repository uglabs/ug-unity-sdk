using UnityEngine;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace UG.Services.UserInput.AudioRecordingService
{
    // Handles the recording of audio from the microphone - no AEC, native unity recording
    public class AudioRecordingService : AudioRecordingServiceBase, IDisposable
    {
        private AudioClip microphoneClip;
        private string selectedDevice;
        private float[] samples;
        private int lastSample = 0;
        private bool isRecording = false;
        private CancellationTokenSource cancellationTokenSource;
        private bool isDisposed = false;

        public override void Init(bool isRequestMicPermissionOnInit)
        {
            if (isRequestMicPermissionOnInit)
            {
                RequestMicrophonePermission((isGranted) =>
                {
                    UGLog.Log("Microphone permission granted on init: " + isGranted);
                    RaiseOnPermissionsGranted(isGranted);
                });
            }
        }

        public override void StartRecording()
        {
            UGLog.Log("[Recording] Start, mic already recording: " + Microphone.IsRecording(selectedDevice));
            RequestMicrophonePermission((isGranted) =>
            {
                RaiseOnPermissionsGranted(isGranted);
                if (!isGranted)
                {
                    UGLog.LogError("Microphone permission not granted. Cannot start recording.");
                    return;
                }

                StartRecordingInternal();
            });
        }

        private void StartRecordingInternal()
        {
            // Get the default microphone device
            if (Microphone.devices.Length > 0)
            {
                selectedDevice = Microphone.devices[0];
                UGLog.Log($"Selected microphone device: {selectedDevice}");
            }
            else
            {
                UGLog.LogError("No microphone device found!");
                return;
            }

            if (selectedDevice == null)
            {
                UGLog.LogError("No microphone device available!");
                return;
            }

            if (isRecording)
            {
                UGLog.LogWarning("Already recording!");
                return;
            }

            try
            {
                microphoneClip = Microphone.Start(selectedDevice, true, BUFFER_LENGTH_SECONDS, SAMPLE_RATE);
                if (microphoneClip == null)
                {
                    UGLog.LogError("Failed to start microphone recording.");
                    return;
                }

                isRecording = true;
                lastSample = 0;

                // Start the sample reading task
                cancellationTokenSource = new CancellationTokenSource();
                _ = ReadSamplesContinuouslyAsync(cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                UGLog.LogError($"Failed to start recording: {e.Message}");
            }
        }

        public override void StopRecording()
        {
            UGLog.Log("[Recording] StopRecording: isRecording:" + isRecording);
            if (!isRecording) return;

            isRecording = false;
            cancellationTokenSource?.Cancel();

            if (!string.IsNullOrEmpty(selectedDevice))
            {
                Microphone.End(selectedDevice);
            }

            microphoneClip = null;
        }

        private async Task ReadSamplesContinuouslyAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (string.IsNullOrEmpty(selectedDevice) || microphoneClip == null)
                    {
                        await Task.Yield();
                        continue;
                    }

                    int currentPosition = Microphone.GetPosition(selectedDevice);
                    if (currentPosition < 0 || lastSample == currentPosition)
                    {
                        await Task.Yield();
                        continue;
                    }

                    if (cancellationToken.IsCancellationRequested || microphoneClip == null)
                    {
                        await Task.Yield();
                        continue;
                    }

                    int sampleCount = GetSampleCount(currentPosition);
                    if (sampleCount > 0)
                    {
                        try
                        {
                            samples = new float[sampleCount];
                            microphoneClip.GetData(samples, lastSample);
                            RaiseOnSamplesRecorded(samples);
                        }
                        catch
                        {
                            // Ignore bounds errors - they happen during cleanup and don't affect functionality
                            UGLog.Log($"[Recording] Out of bounds");
                        }
                    }

                    lastSample = currentPosition;
                }
            }
            catch (OperationCanceledException)
            {
                UGLog.Log("[Recording] Recording cancelled");
            }
            catch (Exception e)
            {
                UGLog.LogError($"Error in audio recording loop: {e.Message}");
            }
        }

        private int GetSampleCount(int currentPosition)
        {
            if (microphoneClip == null) return 0;

            if (currentPosition > lastSample)
            {
                return currentPosition - lastSample;
            }
            else if (currentPosition < lastSample) // Buffer wrapped
            {
                return (microphoneClip.samples - lastSample) + currentPosition;
            }
            return 0;
        }

        public override void Dispose()
        {
            if (!isDisposed)
            {
                StopRecording();
                cancellationTokenSource?.Dispose();

                if (microphoneClip != null)
                {
                    UnityEngine.Object.Destroy(microphoneClip);
                    microphoneClip = null;
                }

                isDisposed = true;
            }
        }
    }
}
