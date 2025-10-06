using System;

namespace UG.Services.UserInput.AudioRecordingService
{
    public interface IAudioRecordingService
    {
        void Init(bool isRequestMicPermissionOnInit);
        void StartRecording();
        void StopRecording();
        void RequestMicrophonePermission(Action<bool> onPermissionResult);
        event Action<float[]> OnSamplesRecorded;
        event Action<bool> OnPermissionsGranted;
        void Dispose();
    }
}