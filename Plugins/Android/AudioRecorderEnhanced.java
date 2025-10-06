package com.uglabs;

import android.app.Activity;
import android.util.Log;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;

/**
 * Enhanced Audio Recorder that wraps the basic AudioRecorder
 * and processes audio samples through multiple enhancement algorithms.
 * 
 * Can apply noise reduction, echo cancellation, and other audio processing effects.
 * Mirrors the functionality of C# AudioRecordingServiceEnhancedWrapper.
 */
public class AudioRecorderEnhanced implements IAudioRecorder {
    private static final String TAG = "AudioRecorderEnhanced";
    
    // Core components
    private AudioRecorder sourceAudioRecorder;
    private List<IAudioEnhancement> enhancements;
    private boolean isDisposed = false;
    private boolean isInitialized = false;
    private int sampleRate = 16000;
    
    // Callback handling
    private IAudioRecorder.AudioRecordingCallback clientCallback;
    private EnhancedAudioCallback enhancedCallback;
    
    // Audio processing
    private boolean isProcessingEnabled = true;
    
    // Enhanced audio circular buffer (separate from raw audio buffer)
    private float[] enhancedAudioBuffer;
    private int enhancedBufferWritePosition = 0;
    private int totalEnhancedSamplesWritten = 0; // Track absolute position for enhanced audio
    private final Object enhancedBufferLock = new Object();
    
    /**
     * Callback interface for enhanced audio (with both raw and processed samples)
     */
    public interface EnhancedAudioCallback {
        /**
         * Called when raw audio samples are received (before enhancement)
         * @param samples Raw audio samples
         * @param sampleCount Number of samples
         */
        void onRawSamplesRecorded(float[] samples, int sampleCount);
        
        /**
         * Called when enhanced audio samples are available (after all enhancements)
         * @param samples Enhanced audio samples
         * @param sampleCount Number of samples
         */
        void onEnhancedSamplesRecorded(float[] samples, int sampleCount);
        
        /**
         * Called when permissions are granted/denied
         * @param granted true if permissions granted
         */
        void onPermissionsGranted(boolean granted);
        
        /**
         * Called when a recording error occurs
         * @param error Error message
         */
        void onRecordingError(String error);
        
        /**
         * Called when recording status changes
         * @param isRecording true if recording started, false if stopped
         */
        void onRecordingStatusChanged(boolean isRecording);
    }
    
    /**
     * Constructor
     * 
     * @param context Activity context
     * @param sampleRate Sample rate (e.g., 48000, 16000)
     * @param bufferLengthSec Circular buffer length in seconds
     * @param chunkSizeSamples How many samples to accumulate before firing callback
     * @param enhancements Optional list of enhancement algorithms to apply
     */
    public AudioRecorderEnhanced(Activity context, int sampleRate, int bufferLengthSec, 
                                int chunkSizeSamples, List<IAudioEnhancement> enhancements) {
        this.sampleRate = sampleRate;
        this.enhancements = new CopyOnWriteArrayList<>();
        
        if (enhancements != null) {
            this.enhancements.addAll(enhancements);
        }
        
        // Create the source audio recorder
        sourceAudioRecorder = new AudioRecorder(context, sampleRate, bufferLengthSec, chunkSizeSamples);
        
        // Initialize enhanced audio circular buffer (same size as raw buffer)
        int bufferSizeSamples = sampleRate * bufferLengthSec;
        enhancedAudioBuffer = new float[bufferSizeSamples];
        
        Log.d(TAG, String.format("AudioRecorderEnhanced created - SampleRate: %d, Enhancements: %d, EnhancedBuffer: %d samples", 
               sampleRate, this.enhancements.size(), bufferSizeSamples));
    }
    
    /**
     * Constructor with default enhancement list
     */
    public AudioRecorderEnhanced(Activity context, int sampleRate, int bufferLengthSec, int chunkSizeSamples) {
        this(context, sampleRate, bufferLengthSec, chunkSizeSamples, null);
    }
    
    /**
     * Set the enhanced callback (allows access to both raw and processed audio)
     * 
     * @param enhancedCallback Callback for raw and enhanced audio events
     */
    public void setEnhancedCallback(EnhancedAudioCallback enhancedCallback) {
        this.enhancedCallback = enhancedCallback;
    }
    
    // IAudioRecorder implementation
    
    @Override
    public boolean init(IAudioRecorder.AudioRecordingCallback callback, boolean isRequestMicPermissionOnInit) {
        if (isDisposed) {
            Log.e(TAG, "AudioRecorderEnhanced is disposed");
            return false;
        }
        
        if (isInitialized) {
            Log.w(TAG, "AudioRecorderEnhanced already initialized");
            return true;
        }
        
        if (callback == null) {
            Log.e(TAG, "Callback cannot be null");
            return false;
        }
        
        this.clientCallback = callback;
        
        try {
            Log.d(TAG, "Initializing AudioRecorderEnhanced...");
            
            // Create internal callback that processes samples
            IAudioRecorder.AudioRecordingCallback internalCallback = new IAudioRecorder.AudioRecordingCallback() {
                @Override
                public void onSamplesRecorded(float[] samples, int sampleCount) {
                    onSourceSamplesReceived(samples, sampleCount);
                }
                
                @Override
                public void onPermissionsGranted(boolean granted) {
                    // Forward permission events
                    if (clientCallback != null) {
                        clientCallback.onPermissionsGranted(granted);
                    }
                    if (enhancedCallback != null) {
                        enhancedCallback.onPermissionsGranted(granted);
                    }
                }
                
                @Override
                public void onRecordingError(String error) {
                    // Forward error events
                    if (clientCallback != null) {
                        clientCallback.onRecordingError(error);
                    }
                    if (enhancedCallback != null) {
                        enhancedCallback.onRecordingError(error);
                    }
                }
                
                @Override
                public void onRecordingStatusChanged(boolean isRecording) {
                    // Forward status events
                    if (clientCallback != null) {
                        clientCallback.onRecordingStatusChanged(isRecording);
                    }
                    if (enhancedCallback != null) {
                        enhancedCallback.onRecordingStatusChanged(isRecording);
                    }
                }
            };
            
            // Initialize the source recorder
            boolean success = sourceAudioRecorder.init(internalCallback, isRequestMicPermissionOnInit);
            
            if (success) {
                isInitialized = true;
                Log.d(TAG, "AudioRecorderEnhanced initialized successfully");
                logEnhancementInfo();
            } else {
                Log.e(TAG, "Failed to initialize source AudioRecorder");
            }
            
            return success;
            
        } catch (Exception e) {
            Log.e(TAG, "Error during AudioRecorderEnhanced initialization: " + e.getMessage());
            e.printStackTrace();
            if (clientCallback != null) {
                clientCallback.onRecordingError("Enhanced recorder initialization error: " + e.getMessage());
            }
            return false;
        }
    }
    
    @Override
    public boolean startRecording() {
        if (isDisposed) {
            Log.e(TAG, "AudioRecorderEnhanced is disposed");
            return false;
        }
        
        if (!isInitialized) {
            Log.e(TAG, "AudioRecorderEnhanced not initialized");
            if (clientCallback != null) {
                clientCallback.onRecordingError("Enhanced recorder not initialized");
            }
            return false;
        }
        
        // Reset enhanced buffer position tracking
        synchronized (enhancedBufferLock) {
            enhancedBufferWritePosition = 0;
            totalEnhancedSamplesWritten = 0;
        }
        
        return sourceAudioRecorder.startRecording();
    }
    
    @Override
    public void stopRecording() {
        if (isDisposed) {
            return; // Allow stopping even if disposed
        }
        
        if (sourceAudioRecorder != null) {
            sourceAudioRecorder.stopRecording();
        }
    }
    
    @Override
    public void dispose() {
        if (isDisposed) {
            return;
        }
        
        Log.d(TAG, "Disposing AudioRecorderEnhanced...");
        
        // Stop recording if active
        if (isRecording()) {
            stopRecording();
        }
        
        // Dispose all enhancements
        for (IAudioEnhancement enhancement : enhancements) {
            try {
                enhancement.dispose();
            } catch (Exception e) {
                Log.w(TAG, "Error disposing enhancement '" + enhancement.getName() + "': " + e.getMessage());
            }
        }
        enhancements.clear();
        
        // Dispose source recorder
        if (sourceAudioRecorder != null) {
            sourceAudioRecorder.dispose();
            sourceAudioRecorder = null;
        }
        
        // Clean up enhanced buffer
        synchronized (enhancedBufferLock) {
            enhancedAudioBuffer = null;
            enhancedBufferWritePosition = 0;
            totalEnhancedSamplesWritten = 0;
        }
        
        isDisposed = true;
        isInitialized = false;
        clientCallback = null;
        enhancedCallback = null;
        
        Log.d(TAG, "AudioRecorderEnhanced disposed");
    }
    
    @Override
    public boolean isRecording() {
        return sourceAudioRecorder != null && sourceAudioRecorder.isRecording();
    }
    
    @Override
    public boolean setAECEnabled(boolean enabled) {
        return sourceAudioRecorder != null && sourceAudioRecorder.setAECEnabled(enabled);
    }
    
    @Override
    public float[] getLatestData(int sampleCount) {
        return sourceAudioRecorder != null ? sourceAudioRecorder.getLatestData(sampleCount) : new float[0];
    }
    
    /**
     * Get raw audio data at specific absolute position in the stream
     * 
     * @param absoluteStartPosition Absolute position where raw data should start
     * @param sampleCount Number of samples to read
     * @return Raw audio samples, or empty array if data not available
     */
    public float[] getRawDataAtPosition(int absoluteStartPosition, int sampleCount) {
        if (sourceAudioRecorder instanceof AudioRecorder) {
            return ((AudioRecorder) sourceAudioRecorder).getDataAtPosition(absoluteStartPosition, sampleCount);
        }
        return new float[0];
    }
    
    /**
     * Get total raw samples written (absolute position in raw stream)
     * 
     * @return Total raw samples written since recording started
     */
    public int getTotalRawSamplesWritten() {
        if (sourceAudioRecorder instanceof AudioRecorder) {
            return ((AudioRecorder) sourceAudioRecorder).getTotalSamplesWritten();
        }
        return 0;
    }
    
    // Enhancement management methods
    
    /**
     * Add an enhancement algorithm to the processing pipeline
     * 
     * @param enhancement The enhancement to add
     */
    public void addEnhancement(IAudioEnhancement enhancement) {
        if (enhancement != null && !enhancements.contains(enhancement)) {
            enhancements.add(enhancement);
            Log.d(TAG, "Added enhancement: " + enhancement.getName());
        }
    }
    
    /**
     * Remove an enhancement algorithm from the processing pipeline
     * 
     * @param enhancement The enhancement to remove
     */
    public void removeEnhancement(IAudioEnhancement enhancement) {
        if (enhancement != null && enhancements.remove(enhancement)) {
            Log.d(TAG, "Removed enhancement: " + enhancement.getName());
        }
    }
    
    /**
     * Get all enhancement algorithms in the pipeline
     * 
     * @return List of enhancement algorithms
     */
    public List<IAudioEnhancement> getEnhancements() {
        return new ArrayList<>(enhancements);
    }
    
    /**
     * Set the sample rate for processing
     * 
     * @param sampleRate Sample rate in Hz
     */
    public void setSampleRate(int sampleRate) {
        this.sampleRate = sampleRate;
        Log.d(TAG, "Sample rate set to: " + sampleRate + " Hz");
    }
    
    /**
     * Enable or disable audio processing (bypass all enhancements)
     * 
     * @param enabled true to enable processing, false to bypass
     */
    public void setProcessingEnabled(boolean enabled) {
        isProcessingEnabled = enabled;
        Log.d(TAG, "Audio processing " + (enabled ? "enabled" : "disabled"));
    }
    
    /**
     * Check if audio processing is enabled
     */
    public boolean isProcessingEnabled() {
        return isProcessingEnabled;
    }
    
    /**
     * Get enhancement by name
     * 
     * @param name Enhancement name to search for
     * @return Enhancement instance or null if not found
     */
    public IAudioEnhancement getEnhancementByName(String name) {
        for (IAudioEnhancement enhancement : enhancements) {
            if (enhancement.getName().equals(name)) {
                return enhancement;
            }
        }
        return null;
    }
    
    /**
     * Enable/disable a specific enhancement by name
     * 
     * @param enhancementName Name of the enhancement
     * @param enabled true to enable, false to disable
     * @return true if enhancement was found and updated
     */
    public boolean setEnhancementEnabled(String enhancementName, boolean enabled) {
        IAudioEnhancement enhancement = getEnhancementByName(enhancementName);
        if (enhancement != null) {
            enhancement.setEnabled(enabled);
            Log.d(TAG, "Enhancement '" + enhancementName + "' " + (enabled ? "enabled" : "disabled"));
            return true;
        } else {
            Log.w(TAG, "Enhancement '" + enhancementName + "' not found");
            return false;
        }
    }
    
    /**
     * Get information about all enhancements
     */
    public String getEnhancementInfo() {
        if (enhancements.isEmpty()) {
            return "No enhancements configured";
        }
        
        StringBuilder info = new StringBuilder();
        info.append("Audio Enhancements (").append(enhancements.size()).append("):\n");
        
        for (int i = 0; i < enhancements.size(); i++) {
            IAudioEnhancement enhancement = enhancements.get(i);
            info.append(String.format("  %d. %s - %s [%s]\n", 
                       i + 1, 
                       enhancement.getName(),
                       enhancement.getDescription(),
                       enhancement.isEnabled() ? "ENABLED" : "DISABLED"));
        }
        
        return info.toString();
    }
    
    /**
     * Get current position in the circular buffer
     * 
     * @return Current write position
     */
    public int getPosition() {
        return sourceAudioRecorder != null ? ((AudioRecorder) sourceAudioRecorder).getPosition() : 0;
    }
    
    /**
     * Get buffer length
     * 
     * @return Buffer length in samples
     */
    public int getBufferLength() {
        return sourceAudioRecorder != null ? ((AudioRecorder) sourceAudioRecorder).getBufferLength() : 0;
    }
    
    /**
     * Process samples through the enhancement pipeline
     * Public method for external access to enhancement processing
     * 
     * @param samples Input audio samples
     * @return Processed audio samples
     */
    public float[] processEnhancedSamples(float[] samples) {
        return processSamples(samples);
    }
    
    /**
     * Get latest enhanced audio data from the pre-processed circular buffer
     * 
     * @param sampleCount Number of samples to retrieve
     * @return Enhanced audio samples that were processed during recording
     */
    public float[] getLatestEnhancedData(int sampleCount) {
        if (enhancedAudioBuffer == null || sampleCount <= 0) {
            return new float[0];
        }
        
        synchronized (enhancedBufferLock) {
            int bufferLength = enhancedAudioBuffer.length;
            float[] result = new float[Math.min(sampleCount, bufferLength)];
            
            // Calculate starting position (circular buffer)
            int startPos = (enhancedBufferWritePosition - result.length + bufferLength) % bufferLength;
            
            // Copy data from circular buffer
            for (int i = 0; i < result.length; i++) {
                result[i] = enhancedAudioBuffer[(startPos + i) % bufferLength];
            }
            
            return result;
        }
    }
    
    /**
     * Get enhanced audio data at specific absolute position in the stream
     * 
     * @param absoluteStartPosition Absolute position where enhanced data should start
     * @param sampleCount Number of samples to read
     * @return Enhanced audio samples, or empty array if data not available
     */
    public float[] getEnhancedDataAtPosition(int absoluteStartPosition, int sampleCount) {
        synchronized (enhancedBufferLock) {
            if (enhancedAudioBuffer == null || sampleCount <= 0) {
                return new float[0];
            }
            
            int bufferLength = enhancedAudioBuffer.length;
            int currentAbsolutePosition = totalEnhancedSamplesWritten;
            
            // Calculate how far back in the buffer we need to go
            int absoluteEndPosition = absoluteStartPosition + sampleCount;
            int samplesFromEnd = currentAbsolutePosition - absoluteEndPosition;
            
            // Check if requested data is still available in circular buffer
            if (samplesFromEnd < 0) {
                Log.w(TAG, "Requested future enhanced data: start=" + absoluteStartPosition + 
                      ", current=" + currentAbsolutePosition);
                return new float[0];
            }
            
            if (samplesFromEnd >= bufferLength) {
                Log.w(TAG, "Requested enhanced data too old: start=" + absoluteStartPosition + 
                      ", current=" + currentAbsolutePosition + ", buffer=" + bufferLength);
                return new float[0];
            }
            
            // Calculate position in circular buffer
            int bufferEndPos = (enhancedBufferWritePosition - 1 + bufferLength) % bufferLength;
            int bufferStartPos = (bufferEndPos - samplesFromEnd - sampleCount + 1 + bufferLength) % bufferLength;
            
            float[] result = new float[sampleCount];
            
            if (bufferStartPos + sampleCount <= bufferLength) {
                // Data doesn't wrap around
                System.arraycopy(enhancedAudioBuffer, bufferStartPos, result, 0, sampleCount);
            } else {
                // Data wraps around the circular buffer
                int firstPartLength = bufferLength - bufferStartPos;
                System.arraycopy(enhancedAudioBuffer, bufferStartPos, result, 0, firstPartLength);
                System.arraycopy(enhancedAudioBuffer, 0, result, firstPartLength, sampleCount - firstPartLength);
            }
            
            return result;
        }
    }
    
    /**
     * Get total enhanced samples written (absolute position in enhanced stream)
     * 
     * @return Total enhanced samples written since recording started
     */
    public int getTotalEnhancedSamplesWritten() {
        synchronized (enhancedBufferLock) {
            return totalEnhancedSamplesWritten;
        }
    }
    
    // Private methods
    
    /**
     * Store enhanced audio samples in the circular buffer for GetData() polling
     * 
     * @param samples Enhanced audio samples to store
     */
    private void storeEnhancedSamples(float[] samples) {
        if (samples == null || samples.length == 0 || enhancedAudioBuffer == null) {
            return;
        }
        
        synchronized (enhancedBufferLock) {
            int bufferLength = enhancedAudioBuffer.length;
            
            // Write samples to circular buffer
            for (int i = 0; i < samples.length; i++) {
                enhancedAudioBuffer[enhancedBufferWritePosition] = samples[i];
                enhancedBufferWritePosition = (enhancedBufferWritePosition + 1) % bufferLength;
            }
            totalEnhancedSamplesWritten += samples.length;
        }
    }
    
    /**
     * Process audio samples from the source recorder
     * 
     * @param samples Raw audio samples from the source recorder
     * @param sampleCount Number of samples
     */
    private void onSourceSamplesReceived(float[] samples, int sampleCount) {
        if (isDisposed || samples == null || sampleCount <= 0) {
            return;
        }
        
        try {
            // Emit raw samples (both to client callback and enhanced callback)
            if (clientCallback != null) {
                clientCallback.onSamplesRecorded(samples, sampleCount);
            }
            if (enhancedCallback != null) {
                enhancedCallback.onRawSamplesRecorded(samples, sampleCount);
            }
            
            // Apply enhancements and store in enhanced buffer
            float[] processedSamples;
            if (isProcessingEnabled) {
                processedSamples = processSamples(samples);
            } else {
                // Processing disabled, use original samples
                processedSamples = samples.clone();
            }
            
            // Store enhanced samples in circular buffer for GetData() polling
            storeEnhancedSamples(processedSamples);
            
            // Emit enhanced samples via callback (for legacy compatibility)
            if (enhancedCallback != null) {
                enhancedCallback.onEnhancedSamplesRecorded(processedSamples, processedSamples.length);
            }
            
        } catch (Exception e) {
            Log.e(TAG, "Error processing audio samples in AudioRecorderEnhanced: " + e.getMessage());
            e.printStackTrace();
            
            // Emit error
            if (clientCallback != null) {
                clientCallback.onRecordingError("Audio processing error: " + e.getMessage());
            }
            if (enhancedCallback != null) {
                enhancedCallback.onRecordingError("Audio processing error: " + e.getMessage());
            }
        }
    }
    
    /**
     * Process audio samples through all enabled enhancement algorithms
     * 
     * @param samples Input audio samples
     * @return Processed audio samples
     */
    private float[] processSamples(float[] samples) {
        if (samples == null || samples.length == 0) {
            return samples;
        }
        
        float[] processedSamples = samples.clone(); // Start with a copy
        
        // Apply each enhancement in sequence
        for (IAudioEnhancement enhancement : enhancements) {
            if (enhancement.isEnabled()) {
                try {
                    processedSamples = enhancement.processSamples(processedSamples, sampleRate);
                } catch (Exception e) {
                    Log.e(TAG, "Error in enhancement '" + enhancement.getName() + "': " + e.getMessage());
                    e.printStackTrace();
                    // Continue with other enhancements if one fails
                }
            }
        }
        
        return processedSamples;
    }
    
    private void logEnhancementInfo() {
        Log.d(TAG, getEnhancementInfo());
        
        // Test sample rate support for each enhancement
        for (IAudioEnhancement enhancement : enhancements) {
            boolean supported = enhancement.supportsSampleRate(sampleRate);
            Log.d(TAG, String.format("Enhancement '%s' supports %d Hz: %s", 
                   enhancement.getName(), sampleRate, supported ? "YES" : "NO"));
        }
    }
} 