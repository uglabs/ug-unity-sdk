package com.uglabs;

/**
 * Interface for Audio Recording functionality
 * 
 * Defines the contract for audio recording implementations
 * Provides a clean abstraction for different audio recording strategies
 */
public interface IAudioRecorder {
    
    /**
     * Audio recording callback interface
     * Implementations should use this to notify about audio events
     */
    interface AudioRecordingCallback {
        /**
         * Called when new audio samples are recorded
         * 
         * @param samples Audio samples as float array (-1.0 to 1.0)
         * @param sampleCount Number of valid samples in the array
         */
        void onSamplesRecorded(float[] samples, int sampleCount);
        
        /**
         * Called when permission status changes
         * 
         * @param granted true if microphone permission is granted
         */
        void onPermissionsGranted(boolean granted);
        
        /**
         * Called when an error occurs during recording
         * 
         * @param error Description of the error
         */
        void onRecordingError(String error);
        
        /**
         * Called when recording status changes
         * 
         * @param isRecording true if recording is active, false if stopped
         */
        void onRecordingStatusChanged(boolean isRecording);
    }
    
    /**
     * Initialize the audio recorder
     * 
     * @param callback Callback interface for receiving audio data and events
     * @param isRequestMicPermissionOnInit Whether to check permissions during initialization
     * @return true if initialization successful, false otherwise
     */
    boolean init(AudioRecordingCallback callback, boolean isRequestMicPermissionOnInit);
    
    /**
     * Start audio recording
     * 
     * @return true if recording started successfully, false otherwise
     */
    boolean startRecording();
    
    /**
     * Stop audio recording
     * Stops the recording and clears buffers
     */
    void stopRecording();
    
    /**
     * Dispose of the audio recorder and clean up all resources
     * Should be called when the recorder is no longer needed
     */
    void dispose();
    
    /**
     * Check if recording is currently active
     * 
     * @return true if recording is in progress, false otherwise
     */
    boolean isRecording();
    
    /**
     * Enable or disable acoustic echo cancellation (if supported)
     * 
     * @param enabled true to enable AEC, false to disable
     * @return true if AEC state was changed successfully, false if not supported or failed
     */
    boolean setAECEnabled(boolean enabled);
    
    /**
     * Get the latest audio data from the internal buffer
     * 
     * @param sampleCount Number of samples to retrieve
     * @return Array of audio samples (most recent first), empty array if unavailable
     */
    float[] getLatestData(int sampleCount);
} 