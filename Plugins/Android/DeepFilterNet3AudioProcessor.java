package com.uglabs.deepfilternet3;

import android.util.Log;
import com.rikorose.deepfilternet.NativeDeepFilterNet;
import com.uglabs.IAudioEnhancement;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;


/**
 * Decoupled DeepFilterNet3 Audio Processor
 * 
 * Can process audio samples from any source (files, real-time recording, etc.)
 * Works independently of Unity or any specific framework
 * Uses callback interface for processed audio output
 */
public class DeepFilterNet3AudioProcessor implements IAudioEnhancement {
    private static final String TAG = "DF3AudioProcessor";
    
    // Callback interface for processed audio
    public interface AudioProcessingCallback {
        void onProcessedAudio(float[] processedSamples, int sampleCount, float lsnr);
        void onProcessingError(String error);
        void onProcessingStatus(String status, String message);
    }
    
    // Instance variables
    private long nativePtr = 0;
    private int frameSamples = 0;
    private boolean isInitialized = false;
    private AudioProcessingCallback callback;
    
    // Synchronous processing 
    private ByteBuffer frameBuffer;
    
    // Processing parameters
    private float attenuationLimit = 10.0f;
    private float postFilterBeta = 0.5f;
    private boolean isEnabled = true;
    
    /**
     * Initialize the DeepFilterNet3 processor with model data
     * 
     * @param modelBytes The model file bytes
     * @param callback Callback interface for receiving processed audio
     * @return true if initialization successful, false otherwise
     */
    public boolean init(byte[] modelBytes, AudioProcessingCallback callback) {
        if (isInitialized) {
            Log.w(TAG, "Processor already initialized");
            return true;
        }
        
        if (modelBytes == null || modelBytes.length == 0) {
            Log.e(TAG, "Invalid model data provided");
            return false;
        }
        
        if (callback == null) {
            Log.e(TAG, "Callback cannot be null");
            return false;
        }
        
        try {
            Log.d(TAG, "Initializing DeepFilterNet3 processor...");
            this.callback = callback;
            
            // Initialize native instance
            nativePtr = NativeDeepFilterNet.newNative(modelBytes, attenuationLimit);
            if (nativePtr == 0) {
                Log.e(TAG, "Failed to create native DeepFilterNet3 instance");
                callback.onProcessingError("Failed to initialize DeepFilterNet3 native instance");
                return false;
            }
            
            // Get frame size
            long frameBytes = NativeDeepFilterNet.getFrameLengthNative(nativePtr);
            frameSamples = (int)(frameBytes / 2); // 16-bit = 2 bytes per sample
            
            if (frameSamples <= 0) {
                Log.e(TAG, "Invalid frame size: " + frameSamples);
                callback.onProcessingError("Invalid frame size: " + frameSamples);
                cleanup();
                return false;
            }
            
            Log.d(TAG, "Frame size: " + frameSamples + " samples (" + frameBytes + " bytes)");
            
            // Set initial parameters
            boolean attenResult = NativeDeepFilterNet.setAttenLimNative(nativePtr, attenuationLimit);
            boolean betaResult = NativeDeepFilterNet.setPostFilterBetaNative(nativePtr, postFilterBeta);
            
            if (!attenResult || !betaResult) {
                Log.w(TAG, "Warning: Failed to set some parameters (atten: " + attenResult + ", beta: " + betaResult + ")");
            }
            
            // Initialize processing infrastructure
            frameBuffer = ByteBuffer.allocateDirect(frameSamples * 2);
            frameBuffer.order(ByteOrder.LITTLE_ENDIAN);
            
            // Note: Using synchronous processing only, no async thread needed
            
            isInitialized = true;
            Log.d(TAG, "DeepFilterNet3 processor initialized successfully");
            callback.onProcessingStatus("INITIALIZED", "DeepFilterNet3 processor ready");
            
            return true;
            
        } catch (Exception e) {
            Log.e(TAG, "Error during initialization: " + e.getMessage());
            e.printStackTrace();
            callback.onProcessingError("Initialization error: " + e.getMessage());
            cleanup();
            return false;
        }
    }
    
    /**
     * Set attenuation limit in dB
     * 
     * @param limitDb Attenuation limit (typically 6-20 dB)
     * @return true if successful
     */
    public boolean setAttenuationLimit(float limitDb) {
        if (!isInitialized) {
            Log.w(TAG, "Processor not initialized");
            return false;
        }
        
        this.attenuationLimit = limitDb;
        boolean result = NativeDeepFilterNet.setAttenLimNative(nativePtr, limitDb);
        
        if (result) {
            Log.d(TAG, "Attenuation limit set to: " + limitDb + " dB");
        } else {
            Log.e(TAG, "Failed to set attenuation limit to: " + limitDb + " dB");
        }
        
        return result;
    }
    
    /**
     * Set post-filter beta value
     * 
     * @param beta Post-filter beta (typically 0.0-1.0)
     * @return true if successful
     */
    public boolean setPostFilterBeta(float beta) {
        if (!isInitialized) {
            Log.w(TAG, "Processor not initialized");
            return false;
        }
        
        this.postFilterBeta = beta;
        boolean result = NativeDeepFilterNet.setPostFilterBetaNative(nativePtr, beta);
        
        if (result) {
            Log.d(TAG, "Post-filter beta set to: " + beta);
        } else {
            Log.e(TAG, "Failed to set post-filter beta to: " + beta);
        }
        
        return result;
    }    
    
    /**
     * Get frame size in samples
     */
    public int getFrameSamples() {
        return frameSamples;
    }
    
    /**
     * Dispose of the processor and clean up resources
     */
    public void dispose() {
        Log.d(TAG, "Disposing DeepFilterNet3 processor...");
        
        // No async processing to shut down in synchronous mode
        
        cleanup();
        
        Log.d(TAG, "DeepFilterNet3 processor disposed");
        if (callback != null) {
            callback.onProcessingStatus("DISPOSED", "Processor cleanup completed");
        }
    }
    
    // IAudioEnhancement interface implementation
    
    @Override
    public float[] processSamples(float[] inputSamples, int sampleRate) {
        if (!isEnabled || !isInitialized) {
            Log.w(TAG, "Processor not enabled or not initialized, returning original samples");
            return inputSamples.clone();
        }
        
        if (inputSamples == null || inputSamples.length == 0) {
            Log.w(TAG, "Empty or null input samples");
            return new float[0];
        }
        
        try {
            // Convert float samples to 16-bit PCM
            short[] pcmSamples = new short[inputSamples.length];
            for (int i = 0; i < inputSamples.length; i++) {
                float clamped = Math.max(-1.0f, Math.min(1.0f, inputSamples[i]));
                pcmSamples[i] = (short)(clamped * 32767f);
            }
            
            // Process synchronously for interface compatibility
            return processSamplesSync(pcmSamples);
            
        } catch (Exception e) {
            Log.e(TAG, "Error in processSamples: " + e.getMessage());
            return inputSamples.clone(); // Return original on error
        }
    }
    
    @Override
    public String getName() {
        return "DeepFilterNet3";
    }
    
    @Override
    public boolean isEnabled() {
        return isEnabled;
    }
    
    @Override
    public void setEnabled(boolean enabled) {
        this.isEnabled = enabled;
        Log.d(TAG, "DeepFilterNet3 enhancement " + (enabled ? "enabled" : "disabled"));
    }
    
    @Override
    public String getDescription() {
        return "DeepFilterNet3 - Real-time noise suppression using deep learning";
    }
    
    @Override
    public boolean supportsSampleRate(int sampleRate) {
        // DeepFilterNet3 typically works best with 48kHz but can handle others
        return sampleRate >= 16000 && sampleRate <= 48000;
    }
    
    /**
     * Synchronous processing method for interface compatibility
     * Processes samples immediately without using the async queue
     */
    private float[] processSamplesSync(short[] pcmSamples) {
        if (!isInitialized || nativePtr == 0) {
            Log.w(TAG, "Processor not initialized for sync processing");
            // Convert back to float and return original
            float[] result = new float[pcmSamples.length];
            for (int i = 0; i < pcmSamples.length; i++) {
                result[i] = pcmSamples[i] / 32768.0f;
            }
            return result;
        }
        
        try {
            // Process samples in frames, similar to async method but synchronous
            int totalFrames = (pcmSamples.length + frameSamples - 1) / frameSamples;
            short[] processedData = new short[pcmSamples.length];
            
            // Reuse the instance frame buffer (important for model state continuity)
            // This ensures the DeepFilterNet3 model maintains internal state across frames
            synchronized (this) {
                for (int frame = 0; frame < totalFrames; frame++) {
                    int startSample = frame * frameSamples;
                    int endSample = Math.min(startSample + frameSamples, pcmSamples.length);
                    int frameLength = endSample - startSample;
                    
                    // Fill frame buffer
                    frameBuffer.clear();
                    for (int i = 0; i < frameSamples; i++) {
                        short sample = (i < frameLength) ? pcmSamples[startSample + i] : 0;
                        frameBuffer.putShort(sample);
                    }
                    frameBuffer.flip();
                    
                    // Process frame
                    float lsnr = NativeDeepFilterNet.processFrameNative(nativePtr, frameBuffer);
                    
                    // Get processed data
                    frameBuffer.rewind();
                    for (int i = 0; i < frameLength; i++) {
                        processedData[startSample + i] = frameBuffer.getShort();
                    }
                }
            }
            
            // Convert back to float
            float[] result = new float[processedData.length];
            for (int i = 0; i < processedData.length; i++) {
                result[i] = processedData[i] / 32768.0f;
            }
            
            return result;
            
        } catch (Exception e) {
            Log.e(TAG, "Error in sync processing: " + e.getMessage());
            // Return original samples converted to float
            float[] result = new float[pcmSamples.length];
            for (int i = 0; i < pcmSamples.length; i++) {
                result[i] = pcmSamples[i] / 32768.0f;
            }
            return result;
        }
    }
    

    
    private void cleanup() {
        if (nativePtr != 0) {
            try {
                NativeDeepFilterNet.freeNative(nativePtr);
                Log.d(TAG, "Native instance freed");
            } catch (Exception e) {
                Log.e(TAG, "Error freeing native instance: " + e.getMessage());
            }
            nativePtr = 0;
        }
        
        isInitialized = false;
    }
} 