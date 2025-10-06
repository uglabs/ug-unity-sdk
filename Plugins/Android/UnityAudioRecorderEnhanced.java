package com.uglabs;

import android.app.Activity;
import android.content.Context;
import android.content.res.AssetManager;
import android.util.Log;
import com.unity3d.player.UnityPlayer;
import com.uglabs.deepfilternet3.DeepFilterNet3AudioProcessor;
import java.util.ArrayList;
import java.util.List;

import java.io.IOException;
import java.io.InputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;

/**
 * Unity-friendly wrapper for AudioRecorderEnhanced
 * 
 * Provides simple static methods for Unity C# integration
 * Uses polling-based GetData() approach for audio transfer
 */
public class UnityAudioRecorderEnhanced {
    private static final String TAG = "UnityAudioRecorderEnhanced";
    
    // Static instance management
    private static AudioRecorderEnhanced recorder;
    private static List<IAudioEnhancement> enhancements;
    private static DeepFilterNet3AudioProcessor deepFilterNet3;
    

    // Audio configuration
    private static int currentSampleRate = 48000;
    private static int currentBufferLengthSec = 5;
    private static int currentChunkSizeSamples = 4800;
    
    // State tracking
    private static boolean isCreated = false;
    private static boolean isInitialized = false;
    
    /**
     * Create the enhanced audio recorder with specified parameters
     * 
     * @param sampleRate Sample rate in Hz (e.g., 48000, 16000)
     * @param bufferLengthSec Circular buffer length in seconds
     * @param chunkSizeSamples How many samples to accumulate before firing callback
     * @return true if creation successful
     */
    public static boolean create(int sampleRate, int bufferLengthSec, int chunkSizeSamples) {
        try {
            Log.d(TAG, String.format("Creating UnityAudioRecorderEnhanced - Rate: %d, Buffer: %ds, Chunk: %d", 
                   sampleRate, bufferLengthSec, chunkSizeSamples));
            
            if (isCreated) {
                Log.w(TAG, "UnityAudioRecorderEnhanced already created, disposing first...");
                dispose();
            }
            
            // Store configuration
            currentSampleRate = sampleRate;
            currentBufferLengthSec = bufferLengthSec;
            currentChunkSizeSamples = chunkSizeSamples;
            
            // Initialize enhancement list
            enhancements = new ArrayList<>();
            
            isCreated = true;
            Log.d(TAG, "UnityAudioRecorderEnhanced created successfully");
            return true;
            
        } catch (Exception e) {
            Log.e(TAG, "Error creating UnityAudioRecorderEnhanced: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }
    
    /**
     * Add DeepFilterNet3 noise reduction enhancement
     * 
     * @param modelPath Path to DeepFilterNet3 model file (can be asset path or absolute path)
     * @return true if enhancement added successfully
     */
    public static boolean addDeepFilterNet3Enhancement(String modelPath) {
        try {
            Log.d(TAG, "Adding DeepFilterNet3 enhancement...");
            
            if (!isCreated) {
                Log.e(TAG, "UnityAudioRecorderEnhanced not created yet");
                return false;
            }
            
            if (deepFilterNet3 != null) {
                Log.w(TAG, "DeepFilterNet3 already exists, disposing first...");
                deepFilterNet3.dispose();
            }
            
            // Load model from path
            Log.d(TAG, "Loading DeepFilterNet3 model from: " + modelPath);
            byte[] modelBytes = loadModelFile(modelPath);
            if (modelBytes == null) {
                Log.e(TAG, "Failed to load model file: " + modelPath);
                return false;
            }
            
            Log.d(TAG, "Model loaded successfully: " + modelBytes.length + " bytes");
            
            // Create DeepFilterNet3 processor
            deepFilterNet3 = new DeepFilterNet3AudioProcessor();
            
            // Initialize with callback (for async processing if needed)
            boolean success = deepFilterNet3.init(modelBytes, new DeepFilterNet3AudioProcessor.AudioProcessingCallback() {
                @Override
                public void onProcessedAudio(float[] processedSamples, int sampleCount, float lsnr) {
                    // Optional: could forward async results to Unity if needed
                    Log.v(TAG, "DF3 async processed: " + sampleCount + " samples, LSNR: " + lsnr);
                }
                
                @Override
                public void onProcessingError(String error) {
                    Log.e(TAG, "DF3 processing error: " + error);
                }
                
                @Override
                public void onProcessingStatus(String status, String message) {
                    Log.d(TAG, "DF3 status: " + status + " - " + message);
                }
            });
            
            if (!success) {
                Log.e(TAG, "Failed to initialize DeepFilterNet3 processor");
                return false;
            }
            
            // Configure parameters
            deepFilterNet3.setAttenuationLimit(10.0f);
            deepFilterNet3.setPostFilterBeta(0.5f);
            
            // Add to enhancement list
            enhancements.add(deepFilterNet3);
            
            Log.d(TAG, "DeepFilterNet3 enhancement added successfully");
            return true;
            
        } catch (Exception e) {
            Log.e(TAG, "Error adding DeepFilterNet3 enhancement: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }
    
    /**
     * Initialize the audio recorder
     * 
     * @return true if initialization successful
     */
    public static boolean initialize() {
        try {
            Log.d(TAG, "Initializing UnityAudioRecorderEnhanced...");
            
            if (!isCreated) {
                Log.e(TAG, "UnityAudioRecorderEnhanced not created yet");
                return false;
            }
            
            if (isInitialized) {
                Log.w(TAG, "Already initialized");
                return true;
            }
            
            // Get Unity context
            Activity context = UnityPlayer.currentActivity;
            if (context == null) {
                Log.e(TAG, "Unity context not available");
                return false;
            }
            
            // Create the enhanced recorder
            recorder = new AudioRecorderEnhanced(
                context,
                currentSampleRate,
                currentBufferLengthSec,
                currentChunkSizeSamples,
                enhancements
            );
            
            // No callback needed for GetData() approach
            
            // Initialize the recorder (provide dummy callback for GetData approach)
            boolean success = recorder.init(new IAudioRecorder.AudioRecordingCallback() {
                @Override
                public void onSamplesRecorded(float[] samples, int sampleCount) {
                    // No-op for polling approach
                }
                
                @Override
                public void onPermissionsGranted(boolean granted) {
                    // No-op for polling approach
                }
                
                @Override
                public void onRecordingError(String error) {
                    Log.e(TAG, "Recording error: " + error);
                }
                
                @Override
                public void onRecordingStatusChanged(boolean isRecording) {
                    // No-op for polling approach
                }
            }, true); // Request microphone permission
            
            if (success) {
                isInitialized = true;
                Log.d(TAG, "UnityAudioRecorderEnhanced initialized successfully");
                return true;
            } else {
                Log.e(TAG, "Failed to initialize AudioRecorderEnhanced");
                return false;
            }
            
        } catch (Exception e) {
            Log.e(TAG, "Error initializing UnityAudioRecorderEnhanced: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }
    
    /**
     * Start recording audio
     * 
     * @return true if recording started successfully
     */
    public static boolean startRecording() {
        try {
            if (!isInitialized || recorder == null) {
                Log.e(TAG, "UnityAudioRecorderEnhanced not initialized");
                return false;
            }
            
            Log.d(TAG, "Starting audio recording...");
            boolean success = recorder.startRecording();
            
            if (success) {
                Log.d(TAG, "Audio recording started successfully");
            } else {
                Log.e(TAG, "Failed to start audio recording");
            }
            
            return success;
            
        } catch (Exception e) {
            Log.e(TAG, "Error starting recording: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }
    
    /**
     * Stop recording audio
     */
    public static void stopRecording() {
        try {
            if (recorder != null) {
                Log.d(TAG, "Stopping audio recording...");
                recorder.stopRecording();
            }
        } catch (Exception e) {
            Log.e(TAG, "Error stopping recording: " + e.getMessage());
            e.printStackTrace();
        }
    }
    
    /**
     * Check if currently recording
     * 
     * @return true if recording is active
     */
    public static boolean isRecording() {
        return recorder != null && recorder.isRecording();
    }
    
    /**
     * Enable or disable audio processing
     * 
     * @param enabled true to enable processing, false to bypass all enhancements
     */
    public static void setProcessingEnabled(boolean enabled) {
        if (recorder != null) {
            recorder.setProcessingEnabled(enabled);
            Log.d(TAG, "Audio processing " + (enabled ? "enabled" : "disabled"));
        }
    }
    
    /**
     * Enable or disable a specific enhancement by name
     * 
     * @param enhancementName Name of the enhancement (e.g., "DeepFilterNet3")
     * @param enabled true to enable, false to disable
     * @return true if enhancement was found and updated
     */
    public static boolean setEnhancementEnabled(String enhancementName, boolean enabled) {
        if (recorder != null) {
            return recorder.setEnhancementEnabled(enhancementName, enabled);
        }
        return false;
    }
    
    /**
     * Enable or disable acoustic echo cancellation
     * 
     * @param enabled true to enable AEC, false to disable
     * @return true if AEC setting was applied successfully
     */
    public static boolean setAECEnabled(boolean enabled) {
        if (recorder != null) {
            return recorder.setAECEnabled(enabled);
        }
        return false;
    }
    
    /**
     * Get current status and configuration information
     * 
     * @return Status information as JSON-like string
     */
    public static String getStatusInfo() {
        if (recorder != null) {
            return recorder.getEnhancementInfo();
        } else if (isCreated) {
            return String.format("Created but not initialized - SampleRate: %d Hz, Enhancements: %d", 
                   currentSampleRate, enhancements != null ? enhancements.size() : 0);
        } else {
            return "Not created";
        }
    }
    
    /**
     * Get raw audio data at specific absolute position in the stream
     * 
     * @param absoluteStartPosition Absolute position where raw data should start
     * @param sampleCount Number of samples to retrieve
     * @return Raw audio samples as float array
     */
    public static float[] getRawData(int absoluteStartPosition, int sampleCount) {
        if (recorder == null) {
            Log.w(TAG, "Recorder not initialized for getRawData");
            return new float[0];
        }
        
        try {
            if (recorder instanceof AudioRecorderEnhanced) {
                return ((AudioRecorderEnhanced) recorder).getRawDataAtPosition(absoluteStartPosition, sampleCount);
            } else {
                // Fallback to latest data if not enhanced recorder
                return recorder.getLatestData(sampleCount);
            }
        } catch (Exception e) {
            Log.e(TAG, "Error getting raw audio data: " + e.getMessage());
            return new float[0];
        }
    }
    
    /**
     * Get enhanced audio data at specific absolute position in the stream
     * This reads pre-processed enhanced audio that was processed during recording
     * 
     * @param absoluteStartPosition Absolute position where data should start  
     * @param sampleCount Number of samples to retrieve
     * @return Enhanced audio samples as float array (pre-processed data)
     */
    public static float[] getEnhancedData(int absoluteStartPosition, int sampleCount) {
        if (recorder == null) {
            Log.w(TAG, "Recorder not initialized for getEnhancedData");
            return new float[0];
        }
        
        try {
            // Read pre-processed enhanced data from circular buffer at specific position
            if (recorder instanceof AudioRecorderEnhanced) {
                return ((AudioRecorderEnhanced) recorder).getEnhancedDataAtPosition(absoluteStartPosition, sampleCount);
            } else {
                // Fallback to raw data if not enhanced recorder
                return recorder.getLatestData(sampleCount);
            }
        } catch (Exception e) {
            Log.e(TAG, "Error getting enhanced audio data: " + e.getMessage());
            return new float[0];
        }
    }
    
    /**
     * Get total enhanced samples written (absolute position in enhanced stream)
     * 
     * @return Total enhanced samples written since recording started
     */
    public static int getEnhancedPosition() {
        if (recorder == null) {
            return 0;
        }
        
        try {
            if (recorder instanceof AudioRecorderEnhanced) {
                return ((AudioRecorderEnhanced) recorder).getTotalEnhancedSamplesWritten();
            } else {
                return 0;
            }
        } catch (Exception e) {
            Log.e(TAG, "Error getting enhanced position: " + e.getMessage());
            return 0;
        }
    }
    
    /**
     * Get current write position in the circular buffer
     * 
     * @return Current write position
     */
    public static int getPosition() {
        if (recorder == null) {
            return 0;
        }
        
        try {
            return ((AudioRecorderEnhanced) recorder).getPosition();
        } catch (Exception e) {
            Log.e(TAG, "Error getting position: " + e.getMessage());
            return 0;
        }
    }
    
    /**
     * Get total raw samples written (absolute position in raw stream)
     * 
     * @return Total raw samples written since recording started
     */
    public static int getRawPosition() {
        if (recorder == null) {
            return 0;
        }
        
        try {
            if (recorder instanceof AudioRecorderEnhanced) {
                return ((AudioRecorderEnhanced) recorder).getTotalRawSamplesWritten();
            } else {
                return 0;
            }
        } catch (Exception e) {
            Log.e(TAG, "Error getting raw position: " + e.getMessage());
            return 0;
        }
    }
    
    /**
     * Get buffer length
     * 
     * @return Buffer length in samples
     */
    public static int getBufferLength() {
        if (recorder == null) {
            return 0;
        }
        
        try {
            return ((AudioRecorderEnhanced) recorder).getBufferLength();
        } catch (Exception e) {
            Log.e(TAG, "Error getting buffer length: " + e.getMessage());
            return 0;
        }
    }
    
    /**
     * Dispose of all resources
     */
    public static void dispose() {
        try {
            Log.d(TAG, "Disposing UnityAudioRecorderEnhanced...");
            
            if (recorder != null) {
                recorder.dispose();
                recorder = null;
            }
            
            if (deepFilterNet3 != null) {
                deepFilterNet3.dispose();
                deepFilterNet3 = null;
            }
            
            if (enhancements != null) {
                enhancements.clear();
                enhancements = null;
            }
            
            isCreated = false;
            isInitialized = false;
            
            Log.d(TAG, "UnityAudioRecorderEnhanced disposed");
            
        } catch (Exception e) {
            Log.e(TAG, "Error during disposal: " + e.getMessage());
            e.printStackTrace();
        }
    }
    
    /**
     * Configure DeepFilterNet3 processing parameters
     * 
     * @param attenuationLimit Attenuation limit in dB (typically 10-100, default: 10)
     * @param postFilterBeta Post-filter beta value (typically 0.0-1.0, default: 0.5)
     * @return true if settings were applied successfully
     */
    public static boolean setDF3Settings(float attenuationLimit, float postFilterBeta) {
        try {
            Log.d(TAG, String.format("Setting DF3 parameters - AttenuationLimit: %.1f dB, PostFilterBeta: %.2f", 
                   attenuationLimit, postFilterBeta));
            
            if (deepFilterNet3 == null) {
                Log.w(TAG, "DeepFilterNet3 not initialized, cannot set parameters");
                return false;
            }
            
            // Apply settings to DeepFilterNet3 processor
            deepFilterNet3.setAttenuationLimit(attenuationLimit);
            deepFilterNet3.setPostFilterBeta(postFilterBeta);
            
            Log.d(TAG, "DF3 parameters updated successfully");
            return true;
            
        } catch (Exception e) {
            Log.e(TAG, "Error setting DF3 parameters: " + e.getMessage());
            e.printStackTrace();
            return false;
        }
    }
    
    /**
     * Get current DeepFilterNet3 processing parameters
     * 
     * @return JSON-like string with current DF3 settings, or null if not available
     */
    public static String getDF3Settings() {
        try {
            if (deepFilterNet3 == null) {
                return "DeepFilterNet3 not initialized";
            }
            
            // Get current settings (if the processor supports it)
            // Note: This would require adding getters to DeepFilterNet3AudioProcessor
            return "DF3 settings available (getters not implemented yet)";
            
        } catch (Exception e) {
            Log.e(TAG, "Error getting DF3 settings: " + e.getMessage());
            return "Error: " + e.getMessage();
        }
    }
    
    // File loading helper methods
    
    /**
     * Load model file from either assets or absolute path
     * 
     * @param modelPath Path to model file (assets: "models/model.bytes" or absolute: "/path/to/model.bytes")
     * @return Model bytes or null if loading failed
     */
    private static byte[] loadModelFile(String modelPath) {
        if (modelPath == null || modelPath.isEmpty()) {
            Log.e(TAG, "Model path is null or empty");
            return null;
        }
        
        // Try loading from assets first (common case for Unity)
        byte[] modelBytes = loadAssetFile(modelPath);
        if (modelBytes != null) {
            Log.d(TAG, "Loaded model from assets: " + modelPath);
            return modelBytes;
        }
        
        // Try loading from absolute file path
        modelBytes = loadFileFromPath(modelPath);
        if (modelBytes != null) {
            Log.d(TAG, "Loaded model from file path: " + modelPath);
            return modelBytes;
        }
        
        // Try common Unity StreamingAssets paths
        String[] commonPaths = {
            "UGTestFiles/" + modelPath,
            "Models/" + modelPath,
            modelPath.startsWith("/") ? modelPath.substring(1) : modelPath // Remove leading slash
        };
        
        for (String path : commonPaths) {
            modelBytes = loadAssetFile(path);
            if (modelBytes != null) {
                Log.d(TAG, "Loaded model from assets (common path): " + path);
                return modelBytes;
            }
        }
        
        Log.e(TAG, "Failed to load model from any path: " + modelPath);
        return null;
    }
    
    /**
     * Load file from Android assets
     * 
     * @param fileName Asset file name/path
     * @return File bytes or null if loading failed
     */
    private static byte[] loadAssetFile(String fileName) {
        try {
            Context context = UnityPlayer.currentActivity;
            if (context == null) {
                Log.e(TAG, "Unity context not available for asset loading");
                return null;
            }
            
            AssetManager assetManager = context.getAssets();
            InputStream inputStream = assetManager.open(fileName);
            
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] data = new byte[16384];
            int bytesRead;
            
            while ((bytesRead = inputStream.read(data, 0, data.length)) != -1) {
                buffer.write(data, 0, bytesRead);
            }
            
            inputStream.close();
            byte[] result = buffer.toByteArray();
            Log.v(TAG, "Loaded asset file: " + fileName + " (" + result.length + " bytes)");
            return result;
            
        } catch (IOException e) {
            Log.v(TAG, "Asset file not found: " + fileName + " - " + e.getMessage());
            return null; // Not an error, just try next option
        } catch (Exception e) {
            Log.w(TAG, "Error loading asset file: " + fileName + " - " + e.getMessage());
            return null;
        }
    }
    
    /**
     * Load file from absolute file path
     * 
     * @param filePath Absolute file path
     * @return File bytes or null if loading failed
     */
    private static byte[] loadFileFromPath(String filePath) {
        try {
            File file = new File(filePath);
            if (!file.exists() || !file.isFile()) {
                Log.v(TAG, "File does not exist: " + filePath);
                return null;
            }
            
            FileInputStream fis = new FileInputStream(file);
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] data = new byte[16384];
            int bytesRead;
            
            while ((bytesRead = fis.read(data, 0, data.length)) != -1) {
                buffer.write(data, 0, bytesRead);
            }
            
            fis.close();
            byte[] result = buffer.toByteArray();
            Log.v(TAG, "Loaded file: " + filePath + " (" + result.length + " bytes)");
            return result;
            
        } catch (IOException e) {
            Log.v(TAG, "File not found: " + filePath + " - " + e.getMessage());
            return null;
        } catch (Exception e) {
            Log.w(TAG, "Error loading file: " + filePath + " - " + e.getMessage());
            return null;
        }
    }
    
    // Debug and utility methods
    
    /**
     * Get debug information about the current state
     */
    public static String getDebugInfo() {
        StringBuilder info = new StringBuilder();
        info.append("=== UnityAudioRecorderEnhanced Debug Info ===\n");
        info.append("Created: ").append(isCreated).append("\n");
        info.append("Initialized: ").append(isInitialized).append("\n");
        info.append("Recording: ").append(isRecording()).append("\n");
        info.append("Sample Rate: ").append(currentSampleRate).append(" Hz\n");
        info.append("Buffer Length: ").append(currentBufferLengthSec).append(" sec\n");
        info.append("Chunk Size: ").append(currentChunkSizeSamples).append(" samples\n");
        info.append("Enhancements: ").append(enhancements != null ? enhancements.size() : 0).append("\n");
        
        if (recorder != null) {
            info.append("\n").append(recorder.getEnhancementInfo());
        }
        
        return info.toString();
    }
    
    /**
     * Log current debug information
     */
    public static void logDebugInfo() {
        Log.d(TAG, getDebugInfo());
    }
} 