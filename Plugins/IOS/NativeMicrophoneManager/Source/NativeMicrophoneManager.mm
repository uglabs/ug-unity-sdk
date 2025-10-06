#import <Foundation/Foundation.h>
#import <UnityFramework/UnityFramework-Swift.h>
#import <AVFoundation/AVFoundation.h>

extern "C" {
    void _Initialize(double sampleRate, int bufferLengthSeconds) {
        [NativeMicrophoneManager createSharedInstanceWithSampleRate:sampleRate
                                    bufferLengthSeconds:bufferLengthSeconds];
    }

    bool _InitializeNativeAEC() {
        return [[NativeMicrophoneManager shared] initializeAEC];
    }

    // Start recording using NativeMicrophoneManager
    void _StartRecording(bool enableAEC) {
        [[NativeMicrophoneManager shared] startWithEnableAEC:enableAEC];
    }

    // Stop recording using NativeMicrophoneManager
    void _StopRecording() {
        [[NativeMicrophoneManager shared] stop];
    }

    // Check if recording is active
    bool _IsRecording() {
        return [[NativeMicrophoneManager shared] isMicRecording];
    }

    // Get audio data from the circular buffer
    float* _GetAudioData(int offsetSamples, int sampleCount) {
        NSArray<NSNumber *> *data = [[NativeMicrophoneManager shared] getDataWithOffsetSamples:offsetSamples sampleCount:sampleCount];
        float *buffer = (float *)malloc(sizeof(float) * sampleCount);
        for (int i = 0; i < sampleCount; i++) {
            buffer[i] = [data[i] floatValue];
        }
        return buffer;
    }

    // Get the current write position in the circular buffer
    int _GetWritePosition() {
        return [[NativeMicrophoneManager shared] getPosition];
    }

    // Get the total buffer length
    int _GetBufferLength() {
        return [[NativeMicrophoneManager shared] getBufferLength];
    }


    void _RegisterAudioRouteChangeListener() {
        [[NativeMicrophoneManager shared] registerAudioRouteChangeListener];
    }

    void _UnregisterAudioRouteChangeListener() {
        [[NativeMicrophoneManager shared] unregisterAudioRouteChangeListener];
    }

    void _RequestPermissionThenStart() {
        [[NativeMicrophoneManager shared] requestPermissionThenStart];
    }

    bool _IsRecordPermissionGranted() {
        return [[NativeMicrophoneManager shared] isRecordPermissionGranted];
    }

    void _CheckAudioSessionConfiguration() {
        [[NativeMicrophoneManager shared] checkAudioSessionConfiguration];
    }

    void _ForceAECConfiguration() {
        [[NativeMicrophoneManager shared] forceAECConfiguration];
    }

    void _StartAudioPlayback(double sampleRate) {
        [NativeAudioPlayer createSharedInstance];
        [[NativeAudioPlayer shared] startWithSampleRate:sampleRate];
    }
    
    void _PlayAudioBuffer(float* buffer, int bufferSize) {
        NSMutableArray<NSNumber *> *floatArray = [NSMutableArray arrayWithCapacity:bufferSize];
        for (int i = 0; i < bufferSize; i++) {
            [floatArray addObject:@(buffer[i])];
        }
        [[NativeAudioPlayer shared] playBufferWithBuffer:floatArray];
    }
    
    void _StopAudioPlayback() {
        [[NativeAudioPlayer shared] stop];
    }
}
