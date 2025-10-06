using UnityEngine;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Android;
using System.Runtime.InteropServices;
using System.Collections;
using UG;
using UG.Services;

namespace UG.Services.UserInput.AudioRecordingService
{
    public class AudioRecordingServiceAEC : AudioRecordingServiceBase, IDisposable
    {
        private AudioClip microphoneClip;
        private string selectedDevice;
        private float[] samples;
        private int lastSample = 0;
        private bool isRecording = false;
        private CancellationTokenSource cancellationTokenSource;
        private bool isDisposed = false;
        private const int MODEL_INPUT_SIZE = 512;

        #region Native methods
#if UNITY_ANDROID && !UNITY_EDITOR
        private const string NATIVE_MICROPHONE_JAVA_CLASS_NAME = "com.uglabs.NativeMicrophoneManager";
        private AndroidJavaObject context;
        private AndroidJavaObject nativeMicrophoneManager;
        private int bufferLength;
#elif UNITY_IPHONE && !UNITY_EDITOR
        [DllImport("__Internal")]
        public static extern void _Initialize(double sampleRate, int bufferLengthSeconds, int microphoneBufferSize);

        [DllImport ("__Internal")] 
        private static extern bool _InitializeNativeAEC();

        [DllImport("__Internal")]
        public static extern void _StartRecording(bool enableAEC);

        [DllImport("__Internal")]
        public static extern void _StopRecording();

        [DllImport("__Internal")]
        public static extern bool _IsRecording();

        [DllImport("__Internal")]
        public static extern System.IntPtr _GetAudioData(int offsetSamples, int sampleCount);

        [DllImport("__Internal")]
        public static extern int _GetWritePosition();

        [DllImport("__Internal")]
        public static extern int _GetBufferLength();

        [DllImport("__Internal")]
        private static extern void _RegisterAudioRouteChangeListener();

        [DllImport("__Internal")]
        private static extern void _UnregisterAudioRouteChangeListener();

        private int bufferLength;
#endif
        #endregion

        public override void Init(bool isRequestMicPermissionOnInit)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Initialize the native Android AECManager
            using(var unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer")){
                context = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");
            }
            nativeMicrophoneManager = new AndroidJavaObject(NATIVE_MICROPHONE_JAVA_CLASS_NAME, context, SAMPLE_RATE, BUFFER_LENGTH_SECONDS);
            bufferLength = nativeMicrophoneManager.Call<int>("GetBufferLength");
#elif UNITY_IPHONE && !UNITY_EDITOR
            _Initialize((double) SAMPLE_RATE, BUFFER_LENGTH_SECONDS, MODEL_INPUT_SIZE);

            // Initialize native AEC
            bool aecInitialzed = _InitializeNativeAEC();
            UGLog.Log($"Acoustic Echo Cancellation initialized: {aecInitialzed}");

            bufferLength = _GetBufferLength();
            _RegisterAudioRouteChangeListener();
            UGLog.Log($"Buffer length: {bufferLength}");
#endif

            if (isRequestMicPermissionOnInit)
            {
                RequestMicrophonePermission((isGranted) =>
                {
                    UGLog.Log("Microphone permission granted on init: " + isGranted);
                    RaiseOnPermissionsGranted(isGranted);
                });
            }
        }

#if UNITY_IPHONE && !UNITY_EDITOR
        private float[] GetAudioData(int offsetSamples, int sampleCount)
        {
            // Call the native function
            IntPtr dataPtr = _GetAudioData(offsetSamples, sampleCount);

            // Convert the native pointer to a managed float array
            float[] data = new float[sampleCount];
            Marshal.Copy(dataPtr, data, 0, sampleCount);

            // Free the allocated memory in the native plugin
            Marshal.FreeHGlobal(dataPtr);

            return data;
        }
#endif

        public override void StartRecording()
        {
            RequestMicrophonePermission((isGranted) =>
            {
                RaiseOnPermissionsGranted(isGranted);
                if (!isGranted)
                {
                    UGLog.LogError("Microphone permission not granted. Cannot start recording.");
                    return;
                }

                // Get the default microphone device
                if (Microphone.devices.Length > 0)
                {
                    selectedDevice = Microphone.devices[0];
                    UGLog.Log($"Selected microphone device: {selectedDevice}");
                }
                else
                {
                    UGLog.LogError("No microphone device found!");
                }

                _ = Record();
            });
        }

        private async Task Record()
        {
            UGLog.Log("Starting recording...");
            if (isRecording)
            {
                UGLog.LogWarning("Already recording!");
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            nativeMicrophoneManager.Call("Start");
            if (!nativeMicrophoneManager.Call<bool>("IsRecording"))
            {
                UGLog.LogError("Failed to start recording.");
                return;
            }
            bool aecEnabled = nativeMicrophoneManager.Call<bool>("EnableAEC");
            UGLog.Log($"AEC enabled: {aecEnabled}");
#elif UNITY_IPHONE && !UNITY_EDITOR
            _StartRecording(true);
            if (!_IsRecording())
            {
                UGLog.LogError("Failed to start recording.");
                return;
            }
#else
            if (selectedDevice == null)
            {
                UGLog.LogError("No microphone device available!");
                return;
            }

            microphoneClip = Microphone.Start(selectedDevice, true, BUFFER_LENGTH_SECONDS, SAMPLE_RATE);
#endif
            isRecording = true;
            lastSample = 0;

            // Start the sample reading task
            cancellationTokenSource = new CancellationTokenSource();
            try
            {
                await ReadSamplesContinuouslyAsync(cancellationTokenSource.Token);
            }
            catch (Exception e)
            {
                UGLog.LogError("Error reading samples: " + e.Message);
            }
        }

        public override void StopRecording()
        {
            if (!isRecording) return;

            isRecording = false;
            cancellationTokenSource?.Cancel();
#if UNITY_ANDROID && !UNITY_EDITOR
            nativeMicrophoneManager.Call("End");
            if (nativeMicrophoneManager.Call<bool>("IsRecording"))
            {
                UGLog.LogError("Failed to stop recording.");
            }
#elif UNITY_IPHONE && !UNITY_EDITOR
            _StopRecording();
            if (_IsRecording())
            {
                UGLog.LogError("Failed to stop recording.");
            }
#else
            Microphone.End(selectedDevice);
            microphoneClip = null;
#endif
        }

        private async Awaitable ReadSamplesContinuouslyAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && isRecording)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                int currentPosition = nativeMicrophoneManager.Call<int>("GetPosition");
#elif UNITY_IPHONE && !UNITY_EDITOR
                int currentPosition = _GetWritePosition();
#else
                int currentPosition = Microphone.GetPosition(selectedDevice);
                int diff = currentPosition - lastSample;
                //                UGLog.Log($"Current differcne: {diff}");
#endif
                if (currentPosition < 0 || lastSample == currentPosition)
                {
                    await Task.Yield();
                    continue;
                }

                int sampleCount = GetSampleCount(currentPosition);
                if (sampleCount > 0)
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    samples = nativeMicrophoneManager.Call<float[]>("GetData", lastSample, sampleCount);
#elif UNITY_IPHONE && !UNITY_EDITOR
                    samples = GetAudioData(lastSample, sampleCount);
#else
                    samples = new float[sampleCount];
                    microphoneClip.GetData(samples, lastSample);
#endif
                    RaiseOnSamplesRecorded(samples);
                }

                lastSample = currentPosition;
            }
        }

        private int GetSampleCount(int currentPosition)
        {
            if (currentPosition > lastSample)
            {
                return currentPosition - lastSample;
            }
            else if (currentPosition < lastSample) // Buffer wrapped
            {
#if (UNITY_ANDROID || UNITY_IPHONE) && !UNITY_EDITOR
                return (bufferLength - lastSample) + currentPosition;
#else
                return (microphoneClip.samples - lastSample) + currentPosition;
#endif
            }
            return 0;
        }

        public override void Dispose()
        {
            if (!isDisposed)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
            nativeMicrophoneManager.Call("Stop");
#elif UNITY_IPHONE && !UNITY_EDITOR
            _StopRecording();
            _UnregisterAudioRouteChangeListener();
#else
                StopRecording();
#endif
                cancellationTokenSource?.Dispose();
                isDisposed = true;
            }
        }
    }
}