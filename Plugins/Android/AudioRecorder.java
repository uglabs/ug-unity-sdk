package com.uglabs;

import android.content.Context;
import android.app.Activity;
import android.media.AudioManager;
import android.media.AudioRecordingConfiguration;
import android.media.audiofx.AcousticEchoCanceler;
import android.util.Log;
import android.media.AudioRecord;
import android.media.AudioFormat;
import android.media.MediaRecorder;
import android.media.AudioDeviceInfo;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.List;
// Removed AndroidX dependencies - using native Android APIs instead
import android.content.pm.PackageManager;

/**
 * Flexible Audio Recorder for Android
 * 
 * Provides a clean interface for audio recording with configurable parameters
 * Matches IAudioRecordingService interface for Unity integration
 */
public class AudioRecorder implements IAudioRecorder {
    private static final String TAG = "AudioRecorder";
    
    // Audio configuration
    private static final int CHANNEL_CONFIG = AudioFormat.CHANNEL_IN_MONO;
    private static final int AUDIO_FORMAT = AudioFormat.ENCODING_PCM_FLOAT;
    private static final int BYTES_PER_FLOAT = 4;
    
    // Audio gain configuration
    private static final float DEFAULT_GAIN_MULTIPLIER = 6f; // +50% gain
    private float gainMultiplier = DEFAULT_GAIN_MULTIPLIER;
    
    // Instance variables
    private Activity context;
    private IAudioRecorder.AudioRecordingCallback callback;
    private int sampleRate;
    private int bufferLengthSec;
    private int minBufferSize;
    private int chunkSizeSamples; // How often to fire callback
    
    // Audio recording components
    private AudioRecord audioRecord;
    private AudioManager audioManager;
    private AcousticEchoCanceler aec;
    
    // Recording state
    private boolean isInitialized = false;
    private boolean isRecording = false;
    private Thread recordingThread;
    
    // Audio buffers
    private float[] circularAudioBuffer;
    private float[] tempBuffer;
    private float[] callbackBuffer;
    private int writePosition = 0;
    private int totalSamplesWritten = 0; // Track absolute position for raw audio
    private final Object bufferLock = new Object();
    
    // AEC and audio routing
    private boolean aecEnabled = false;
    private AudioDeviceInfo currentCommunicationDevice;
    
    /**
     * Constructor
     * 
     * @param context Activity context
     * @param sampleRate Sample rate (e.g., 48000, 44100, 16000)
     * @param bufferLengthSec Circular buffer length in seconds
     * @param chunkSizeSamples How many samples to accumulate before firing callback
     */
    public AudioRecorder(Activity context, int sampleRate, int bufferLengthSec, int chunkSizeSamples) {
        this.context = context;
        this.sampleRate = sampleRate;
        this.bufferLengthSec = bufferLengthSec;
        this.chunkSizeSamples = chunkSizeSamples;
        
        // Calculate buffer sizes
        this.minBufferSize = AudioRecord.getMinBufferSize(sampleRate, CHANNEL_CONFIG, AUDIO_FORMAT);
        
        Log.d(TAG, String.format("AudioRecorder created - SampleRate: %d, BufferLength: %ds, ChunkSize: %d samples", 
               sampleRate, bufferLengthSec, chunkSizeSamples));
    }
    
    /**
     * Initialize the audio recorder
     * 
     * @param callback Callback interface for receiving audio data and events
     * @param isRequestMicPermissionOnInit Whether to check permissions during init
     * @return true if initialization successful
     */
    public boolean init(IAudioRecorder.AudioRecordingCallback callback, boolean isRequestMicPermissionOnInit) {
        if (isInitialized) {
            Log.w(TAG, "AudioRecorder already initialized");
            return true;
        }
        
        if (callback == null) {
            Log.e(TAG, "Callback cannot be null");
            return false;
        }
        
        this.callback = callback;
        
        try {
            Log.d(TAG, "Initializing AudioRecorder...");
            
            // Check permissions if requested
            if (isRequestMicPermissionOnInit) {
                boolean permissionGranted = isRecordPermissionGranted();
                callback.onPermissionsGranted(permissionGranted);
                
                if (!permissionGranted) {
                    Log.e(TAG, "Microphone permission not granted");
                    return false;
                }
            }
            
            // Initialize buffers
            circularAudioBuffer = new float[sampleRate * bufferLengthSec];
            tempBuffer = new float[minBufferSize / BYTES_PER_FLOAT];
            callbackBuffer = new float[chunkSizeSamples];
            
            // Initialize AudioRecord
            // VOICE_COMMUNICATION might or might not enable hardware AEC
            // We use UNPROCESSED/MIC because we don't want to use native AEC (which is unreliable)
            // MIC seems better for our use-case, still not AEC - UNPROCESSED doesn't apply AGC - so we'd have to "guess" gain values
            audioRecord = new AudioRecord(
                MediaRecorder.AudioSource.MIC, //! VOICE_COMMUNICATION
                sampleRate, 
                CHANNEL_CONFIG, 
                AUDIO_FORMAT, 
                minBufferSize
            );

            Log.d(TAG, "AudioRecord initialized with state(!): " + audioRecord.getState() + " and sample rate: " + sampleRate + " and mode: " + MediaRecorder.AudioSource.UNPROCESSED);
            
            if (audioRecord.getState() != AudioRecord.STATE_INITIALIZED) {
                Log.e(TAG, "Failed to initialize AudioRecord");
                callback.onRecordingError("Failed to initialize AudioRecord");
                return false;
            }
            
            // Initialize AudioManager
            audioManager = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            if (audioManager == null) {
                Log.e(TAG, "AudioManager is not available");
                callback.onRecordingError("AudioManager is not available");
                return false;
            }
            
            // Experiment: Use normal mode to preserve media audio (background music)
            // This is because setting MODE_IN_COMMUNICATION will break background audio if it's playing
            // while still enabling AEC for echo cancellation
            // Overall, AudioManager.MODE_NORMAL is the only one that doesn't interfere with media audio
            //! MODE_NORMAL/MODE_IN_COMMUNICATION
            audioManager.setMode(AudioManager.MODE_NORMAL); // audioManager.setMode(AudioManager.MODE_IN_COMMUNICATION);
            
            // Don't override volume control - preserve media volume controls (only if using MODE_IN_COMMUNICATION)
            context.setVolumeControlStream(AudioManager.STREAM_VOICE_CALL);
            
            // Initialize AEC
            // We should not be using this, because this is not reliable - works fine on one devide but not the other
            // initializeAEC();
            
            isInitialized = true;
            Log.d(TAG, "AudioRecorder initialized successfully");
            Log.d(TAG, "EXPERIMENT: Using MODE_NORMAL with AEC enabled to preserve media audio");
            
            return true;
        } catch (Exception e) {
            Log.e(TAG, "Error during AudioRecorder initialization: " + e.getMessage());
            e.printStackTrace();
            if (callback != null) {
                callback.onRecordingError("Initialization error: " + e.getMessage());
            }
            return false;
        }
    }
    
    /**
     * Start audio recording
     * 
     * @return true if recording started successfully
     */
    public boolean startRecording() {
        if (!isInitialized) {
            Log.e(TAG, "AudioRecorder not initialized");
            if (callback != null) {
                callback.onRecordingError("AudioRecorder not initialized");
            }
            return false;
        }
        
        if (isRecording) {
            Log.w(TAG, "Recording already in progress");
            return true;
        }
        
        // Check permissions again
        if (!isRecordPermissionGranted()) {
            Log.e(TAG, "Microphone permission not granted");
            if (callback != null) {
                callback.onRecordingError("Microphone permission not granted");
                callback.onPermissionsGranted(false);
            }
            return false;
        }
        
        try {
            // Clear buffers
            clearBuffers();
            
            // Configure audio routing and AEC
            configureAudioRouting();
            
            // Start AudioRecord
            audioRecord.startRecording();
            
            if (audioRecord.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING) {
                Log.e(TAG, "Failed to start AudioRecord");
                if (callback != null) {
                    callback.onRecordingError("Failed to start AudioRecord");
                }
                return false;
            }
            
            // Start recording thread
            isRecording = true;
            startRecordingThread();
            
            Log.d(TAG, "Audio recording started");
            if (callback != null) {
                callback.onRecordingStatusChanged(true);
            }
            
            return true;
            
        } catch (Exception e) {
            Log.e(TAG, "Error starting recording: " + e.getMessage());
            e.printStackTrace();
            if (callback != null) {
                callback.onRecordingError("Failed to start recording: " + e.getMessage());
            }
            return false;
        }
    }
    
    /**
     * Stop audio recording
     */
    public void stopRecording() {
        if (!isRecording) {
            Log.w(TAG, "Recording not in progress");
            return;
        }
        
        try {
            Log.d(TAG, "Stopping audio recording...");
            
            // Stop recording thread
            isRecording = false;
            
            // Wait for recording thread to finish
            if (recordingThread != null && recordingThread.isAlive()) {
                recordingThread.interrupt();
                try {
                    recordingThread.join(1000); // Wait up to 1 second
                } catch (InterruptedException e) {
                    Log.w(TAG, "Interrupted while waiting for recording thread to finish");
                }
                recordingThread = null;
            }
            
            // Stop AudioRecord
            if (audioRecord != null && audioRecord.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING) {
                audioRecord.stop();
            }
            
            // Clear buffers
            clearBuffers();
            
            Log.d(TAG, "Audio recording stopped");
            if (callback != null) {
                callback.onRecordingStatusChanged(false);
            }
            
        } catch (Exception e) {
            Log.e(TAG, "Error stopping recording: " + e.getMessage());
            e.printStackTrace();
            if (callback != null) {
                callback.onRecordingError("Error stopping recording: " + e.getMessage());
            }
        }
    }
    
    /**
     * Dispose of the audio recorder and clean up resources
     */
    public void dispose() {
        Log.d(TAG, "Disposing AudioRecorder...");
        
        // Stop recording if active
        if (isRecording) {
            stopRecording();
        }
        
        // Clean up AEC
        if (aec != null) {
            try {
                aec.setEnabled(false);
                aec.release();
                aec = null;
            } catch (Exception e) {
                Log.w(TAG, "Error releasing AEC: " + e.getMessage());
            }
        }
        
        // Clean up AudioRecord
        if (audioRecord != null) {
            try {
                if (audioRecord.getState() == AudioRecord.STATE_INITIALIZED) {
                    audioRecord.release();
                }
                audioRecord = null;
            } catch (Exception e) {
                Log.w(TAG, "Error releasing AudioRecord: " + e.getMessage());
            }
        }
        
        // Audio manager cleanup (already in normal mode from experiment)
        if (audioManager != null) {
            try {
                // Ensure we're in normal mode (should already be from init)
                audioManager.setMode(AudioManager.MODE_NORMAL);
                audioManager = null;
            } catch (Exception e) {
                Log.w(TAG, "Error resetting AudioManager: " + e.getMessage());
            }
        }
        
        // Clear buffers
        circularAudioBuffer = null;
        tempBuffer = null;
        callbackBuffer = null;
        
        isInitialized = false;
        callback = null;
        
        Log.d(TAG, "AudioRecorder disposed");
    }
    
    /**
     * Get current recording status
     */
    public boolean isRecording() {
        return isRecording && audioRecord != null && 
               audioRecord.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING;
    }
    
    /**
     * Enable/disable acoustic echo cancellation
     */
    public boolean setAECEnabled(boolean enabled) {
        if (aec == null) {
            Log.w(TAG, "AEC not available");
            return false;
        }
        
        try {
            aec.setEnabled(enabled);
            aecEnabled = aec.getEnabled();
            Log.d(TAG, "AEC " + (aecEnabled ? "enabled" : "disabled"));
            return aecEnabled == enabled;
        } catch (Exception e) {
            Log.e(TAG, "Error setting AEC: " + e.getMessage());
            return false;
        }
    }
    
    /**
     * Get latest audio data from circular buffer
     */
    public float[] getLatestData(int sampleCount) {
        synchronized (bufferLock) {
            if (circularAudioBuffer == null || sampleCount <= 0) {
                return new float[0];
            }
            
            float[] latestData = new float[Math.min(sampleCount, circularAudioBuffer.length)];
            int bufferSize = circularAudioBuffer.length;
            int start = (writePosition - latestData.length + bufferSize) % bufferSize;
            
            if (start + latestData.length <= bufferSize) {
                System.arraycopy(circularAudioBuffer, start, latestData, 0, latestData.length);
            } else {
                int firstPartLength = bufferSize - start;
                System.arraycopy(circularAudioBuffer, start, latestData, 0, firstPartLength);
                System.arraycopy(circularAudioBuffer, 0, latestData, firstPartLength, latestData.length - firstPartLength);
            }
            
            return latestData;
        }
    }
    
    /**
     * Get raw audio data at specific absolute position in the stream
     * 
     * @param absoluteStartPosition Absolute position where data should start
     * @param sampleCount Number of samples to read
     * @return Audio samples, or empty array if data not available
     */
    public float[] getDataAtPosition(int absoluteStartPosition, int sampleCount) {
        synchronized (bufferLock) {
            if (circularAudioBuffer == null || sampleCount <= 0) {
                return new float[0];
            }
            
            int bufferLength = circularAudioBuffer.length;
            int currentAbsolutePosition = totalSamplesWritten;
            
            // Calculate how far back in the buffer we need to go
            int absoluteEndPosition = absoluteStartPosition + sampleCount;
            int samplesFromEnd = currentAbsolutePosition - absoluteEndPosition;
            
            // Check if requested data is still available in circular buffer
            if (samplesFromEnd < 0) {
                Log.w(TAG, "Requested future raw data: start=" + absoluteStartPosition + 
                      ", current=" + currentAbsolutePosition);
                return new float[0];
            }
            
            if (samplesFromEnd >= bufferLength) {
                Log.w(TAG, "Requested raw data too old: start=" + absoluteStartPosition + 
                      ", current=" + currentAbsolutePosition + ", buffer=" + bufferLength);
                return new float[0];
            }
            
            // Calculate position in circular buffer
            int bufferEndPos = (writePosition - 1 + bufferLength) % bufferLength;
            int bufferStartPos = (bufferEndPos - samplesFromEnd - sampleCount + 1 + bufferLength) % bufferLength;
            
            float[] result = new float[sampleCount];
            
            if (bufferStartPos + sampleCount <= bufferLength) {
                // Data doesn't wrap around
                System.arraycopy(circularAudioBuffer, bufferStartPos, result, 0, sampleCount);
            } else {
                // Data wraps around the circular buffer
                int firstPartLength = bufferLength - bufferStartPos;
                System.arraycopy(circularAudioBuffer, bufferStartPos, result, 0, firstPartLength);
                System.arraycopy(circularAudioBuffer, 0, result, firstPartLength, sampleCount - firstPartLength);
            }
            
            return result;
        }
    }
    
    /**
     * Get total samples written (absolute position in raw stream)
     * 
     * @return Total samples written since recording started
     */
    public int getTotalSamplesWritten() {
        synchronized (bufferLock) {
            return totalSamplesWritten;
        }
    }
    
    // Private helper methods
    
    private boolean isRecordPermissionGranted() {
        return context.checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) 
               == PackageManager.PERMISSION_GRANTED;
    }
    
    private void initializeAEC() {
        Log.d(TAG, "Initializing AEC");
        if (!AcousticEchoCanceler.isAvailable()) {
            Log.d(TAG, "AcousticEchoCanceler is not available on this device");
            return;
        }
        
        try {
            aec = AcousticEchoCanceler.create(audioRecord.getAudioSessionId());
            if (aec == null) {
                Log.w(TAG, "Failed to create AcousticEchoCanceler");
                return;
            }
            
            aec.setEnabled(false); // Will be enabled in configureAudioRouting //!
            Log.d(TAG, "AcousticEchoCanceler initialized (will be enabled in normal mode)");
        } catch (Exception e) {
            Log.w(TAG, "Error initializing AEC: " + e.getMessage());
        }
    }
    
    private void configureAudioRouting() {
        if (audioManager == null) {
            return;
        }
        
        try {
            // In normal mode, be less aggressive with audio routing
            // Let the system handle routing naturally to preserve media audio
            
            // Always try to enable AEC in normal mode (experiment)
            if (aec != null) {
                Log.d(TAG, "Enabling AEC in normal mode (experimental)");
                setAECEnabled(false); //!  true
            }
            
            // Optional: Still try to set communication device but don't force it
            currentCommunicationDevice = setCommunicationDevice();
            
        } catch (Exception e) {
            Log.w(TAG, "Error configuring audio routing: " + e.getMessage());
        }
    }
    
    private AudioDeviceInfo setCommunicationDevice() {
        if (audioManager == null) {
            return null;
        }
        
        try {
            AudioDeviceInfo communicationDevice = null;
            AudioDeviceInfo[] devices = audioManager.getDevices(AudioManager.GET_DEVICES_OUTPUTS);
            
            // Prefer Bluetooth SCO, fallback to built-in speaker
            for (AudioDeviceInfo device : devices) {
                if (device.getType() == AudioDeviceInfo.TYPE_BLUETOOTH_SCO) {
                    communicationDevice = device;
                    break;
                } else if (device.getType() == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER && communicationDevice == null) {
                    communicationDevice = device;
                }
            }
            
            if (communicationDevice != null) {
                boolean result = audioManager.setCommunicationDevice(communicationDevice);
                if (result) {
                    Log.d(TAG, "Communication device set: " + communicationDevice.getProductName());
                    return communicationDevice;
                } else {
                    Log.w(TAG, "Failed to set communication device");
                }
            } else {
                Log.w(TAG, "No suitable communication device found");
            }
            
        } catch (Exception e) {
            Log.w(TAG, "Error setting communication device: " + e.getMessage());
        }
        
        return null;
    }
    
    private void clearBuffers() {
        synchronized (bufferLock) {
            if (circularAudioBuffer != null) {
                java.util.Arrays.fill(circularAudioBuffer, 0.0f);
            }
            if (tempBuffer != null) {
                java.util.Arrays.fill(tempBuffer, 0.0f);
            }
            if (callbackBuffer != null) {
                java.util.Arrays.fill(callbackBuffer, 0.0f);
            }
            writePosition = 0;
            totalSamplesWritten = 0;
        }
    }
    
    private void startRecordingThread() {
        recordingThread = new Thread(() -> {
            Log.d(TAG, "Recording thread started");
            
            int samplesCollected = 0;
            
            while (isRecording) {
                try {
                    // Read audio data
                    int floatsRead = audioRecord.read(tempBuffer, 0, tempBuffer.length, AudioRecord.READ_NON_BLOCKING);
                    
                    if (floatsRead > 0) {
                        // Debug: Log sample values occasionally to verify gain
                        if (Math.random() < 0.01) { // Log 1% of the time
                            float maxBefore = 0.0f;
                            for (int i = 0; i < Math.min(floatsRead, 100); i++) {
                                maxBefore = Math.max(maxBefore, Math.abs(tempBuffer[i]));
                            }
                            Log.d(TAG, "Before gain - Max sample: " + maxBefore + ", Gain: " + gainMultiplier);
                        }
                        
                        // Apply gain to the audio samples
                        // applyGain(tempBuffer, floatsRead, gainMultiplier);
                        
                        // Debug: Log after gain values
                        if (Math.random() < 0.01) { // Log 1% of the time
                            float maxAfter = 0.0f;
                            for (int i = 0; i < Math.min(floatsRead, 100); i++) {
                                maxAfter = Math.max(maxAfter, Math.abs(tempBuffer[i]));
                            }
                            Log.d(TAG, "After gain - Max sample: " + maxAfter);
                        }
                        
                        // Store in circular buffer
                        storeInCircularBuffer(tempBuffer, floatsRead);
                        
                        // Collect samples for callback
                        for (int i = 0; i < floatsRead; i++) {
                            callbackBuffer[samplesCollected] = tempBuffer[i];
                            samplesCollected++;
                            
                            // Fire callback when chunk is full
                            if (samplesCollected >= chunkSizeSamples) {
                                if (callback != null) {
                                    // Create copy for callback
                                    float[] callbackData = new float[samplesCollected];
                                    System.arraycopy(callbackBuffer, 0, callbackData, 0, samplesCollected);
                                    callback.onSamplesRecorded(callbackData, samplesCollected);
                                }
                                samplesCollected = 0;
                            }
                        }
                    } else if (floatsRead == AudioRecord.ERROR_INVALID_OPERATION) {
                        Log.e(TAG, "AudioRecord invalid operation");
                        break;
                    } else if (floatsRead == AudioRecord.ERROR_BAD_VALUE) {
                        Log.e(TAG, "AudioRecord bad value");
                        break;
                    }
                    
                    // Small sleep to prevent busy waiting
                    Thread.sleep(1);
                    
                } catch (InterruptedException e) {
                    Log.d(TAG, "Recording thread interrupted");
                    break;
                } catch (Exception e) {
                    Log.e(TAG, "Error in recording thread: " + e.getMessage());
                    if (callback != null) {
                        callback.onRecordingError("Recording thread error: " + e.getMessage());
                    }
                    break;
                }
            }
            
            Log.d(TAG, "Recording thread stopped");
        }, "AudioRecorder Thread");
        
        recordingThread.start();
    }
    
    private void storeInCircularBuffer(float[] data, int length) {
        synchronized (bufferLock) {
            if (circularAudioBuffer == null || length <= 0) {
                return;
            }
            
            int bufferSize = circularAudioBuffer.length;
            
            if (writePosition + length <= bufferSize) {
                // Simple case: no wraparound
                System.arraycopy(data, 0, circularAudioBuffer, writePosition, length);
                writePosition = (writePosition + length) % bufferSize;
            } else {
                // Wraparound case
                int firstPartLength = bufferSize - writePosition;
                System.arraycopy(data, 0, circularAudioBuffer, writePosition, firstPartLength);
                
                int secondPartLength = length - firstPartLength;
                System.arraycopy(data, firstPartLength, circularAudioBuffer, 0, secondPartLength);
                
                writePosition = secondPartLength;
            }
            
            // Update absolute position tracking
            totalSamplesWritten += length;
        }
    }
    
    /**
     * Apply gain to audio samples with clipping protection
     * 
     * @param samples Audio samples to process
     * @param length Number of samples to process
     * @param gainMultiplier Gain multiplier (1.0f = no change, 1.5f = +50%)
     */
    private void applyGain(float[] samples, int length, float gainMultiplier) {
        if (samples == null || length <= 0 || gainMultiplier == 1.0f) {
            return;
        }
        
        for (int i = 0; i < length; i++) {
            float sample = samples[i] * gainMultiplier;
            // Clamp to prevent clipping
            samples[i] = Math.max(-1.0f, Math.min(1.0f, sample));
        }
    }
    
    /**
     * Get current write position in the circular buffer
     * 
     * @return Current write position
     */
    public int getPosition() {
        synchronized (bufferLock) {
            return writePosition;
        }
    }
    
    /**
     * Get buffer length
     * 
     * @return Buffer length in samples
     */
    public int getBufferLength() {
        synchronized (bufferLock) {
            return circularAudioBuffer != null ? circularAudioBuffer.length : 0;
        }
    }
    
    /**
     * Set audio gain multiplier
     * 
     * @param gainMultiplier Gain multiplier (1.0f = no change, 1.5f = +50%, 2.0f = +100%)
     */
    public void setGainMultiplier(float gainMultiplier) {
        this.gainMultiplier = Math.max(0.1f, Math.min(10.0f, gainMultiplier)); // Clamp between 0.1x and 10x
        Log.d(TAG, "Gain multiplier set to: " + this.gainMultiplier);
    }
    
    /**
     * Get current gain multiplier
     * 
     * @return Current gain multiplier
     */
    public float getGainMultiplier() {
        return gainMultiplier;
    }
} 