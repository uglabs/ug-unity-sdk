using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UG.Services.UserInput.AudioRecordingService
{
    public abstract class AudioRecordingServiceBase : IAudioRecordingService, IDisposable
    {
        protected const int SAMPLE_RATE = 16000;
        protected const int BUFFER_LENGTH_SECONDS = 10;

        public abstract void Init(bool isRequestMicPermissionOnInit);
        public abstract void StartRecording();
        public abstract void StopRecording();
        public abstract void Dispose();

        public event Action<float[]> OnSamplesRecorded;
        public event Action<bool> OnPermissionsGranted;

        protected virtual void RaiseOnSamplesRecorded(float[] samples)
        {
            OnSamplesRecorded?.Invoke(samples);
        }

        protected virtual void RaiseOnPermissionsGranted(bool granted)
        {
            OnPermissionsGranted?.Invoke(granted);
        }

        public virtual void RequestMicrophonePermission(Action<bool> onPermissionResult)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
            {
                UGLog.Log("Permission already granted for microphone access.");
                onPermissionResult?.Invoke(true);
            }
            else
            {
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += permission => {
                    UGLog.Log("Microphone permission granted. Proceeding with recording.");
                    onPermissionResult?.Invoke(true);
                };
                callbacks.PermissionDenied += permission => {
                    UGLog.LogWarning("Microphone permission not granted. Cannot proceed.");
                    onPermissionResult?.Invoke(false);
                };
                callbacks.PermissionDeniedAndDontAskAgain += permission => {
                    UGLog.LogWarning("Microphone permission denied and 'Don't ask again' selected.");
                    onPermissionResult?.Invoke(false);
                };

                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            }
#elif UNITY_IPHONE && !UNITY_EDITOR
            if (Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                UGLog.Log("Microphone permission already granted.");
                onPermissionResult?.Invoke(true);
                return;
            }

            UGLog.Log("Requesting microphone permission...");
            
            // Request permission
            Application.RequestUserAuthorization(UserAuthorization.Microphone);

            // Start a coroutine to wait for the result
            WaitForMicrophonePermission((isGranted) => {
                onPermissionResult?.Invoke(isGranted);
            });
#else
            onPermissionResult?.Invoke(true);
#endif
        }

        private async void WaitForMicrophonePermission(Action<bool> onPermissionResult)
        {
            try
            {
                // Wait until the user responds
                while (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                {
                    await Task.Yield();
                }

                // Return whether the permission was granted
                bool granted = Application.HasUserAuthorization(UserAuthorization.Microphone);
                onPermissionResult?.Invoke(granted);
            }
            catch (OperationCanceledException)
            {
                UGLog.Log("[Recording] Permission request cancelled");
                onPermissionResult?.Invoke(false);
            }
        }
    }
}