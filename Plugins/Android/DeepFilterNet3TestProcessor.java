package com.uglabs.deepfilternet3;

import android.content.Context;
import android.content.res.AssetManager;
import android.util.Log;
import com.unity3d.player.UnityPlayer;
import com.rikorose.deepfilternet.NativeDeepFilterNet;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;

public class DeepFilterNet3TestProcessor {
    private static final String TAG = "DF3TestProcessor";
    
    // Unity callback object and method names
    private static String unityGameObjectName = "";
    private static String unityCallbackMethod = "";
    
    public static void processAudioFileWithCallback(String modelPath, String audioPath, String gameObjectName, String callbackMethod) {
        unityGameObjectName = gameObjectName;
        unityCallbackMethod = callbackMethod;
        
        try {
            Log.d(TAG, "Starting DeepFilterNet3 test processing with callbacks...");
            
            // Send status update
            sendStatusToUnity("STARTING", "DeepFilterNet3 processing started");
            
            // Step 1: Load model from Android assets
            Log.d(TAG, "Loading model from: " + modelPath);
            sendStatusToUnity("LOADING_MODEL", "Loading model: " + modelPath);
            
            byte[] modelBytes = loadAssetFile(modelPath);
            if (modelBytes == null) {
                sendErrorToUnity("Failed to load model file: " + modelPath);
                return;
            }
            Log.d(TAG, "Model loaded: " + modelBytes.length + " bytes");
            sendStatusToUnity("MODEL_LOADED", "Model loaded: " + modelBytes.length + " bytes");
            
            // Step 2: Initialize DeepFilterNet3
            Log.d(TAG, "Initializing DeepFilterNet3...");
            sendStatusToUnity("INITIALIZING", "Initializing DeepFilterNet3...");
            
            long nativePtr = NativeDeepFilterNet.newNative(modelBytes, 18.0f);
            if (nativePtr == 0) {
                sendErrorToUnity("Failed to initialize DeepFilterNet3");
                return;
            }
            Log.d(TAG, "DeepFilterNet3 initialized with pointer: " + nativePtr);
            sendStatusToUnity("INITIALIZED", "DeepFilterNet3 initialized successfully");
            
            try {
                // Step 3: Set parameters
                Log.d(TAG, "Setting processing parameters...");
                boolean attenResult = NativeDeepFilterNet.setAttenLimNative(nativePtr, 50.0f);
                boolean betaResult = NativeDeepFilterNet.setPostFilterBetaNative(nativePtr, 0.01f);
                Log.d(TAG, "Parameters set - Attenuation: " + attenResult + ", Beta: " + betaResult);
                
                // Step 4: Get frame info
                long frameBytes = NativeDeepFilterNet.getFrameLengthNative(nativePtr);
                int frameSamples = (int)(frameBytes / 2);
                Log.d(TAG, "Frame info - Bytes: " + frameBytes + ", Samples: " + frameSamples);
                
                if (frameSamples <= 0) {
                    sendErrorToUnity("Invalid frame size: " + frameSamples);
                    return;
                }
                
                sendStatusToUnity("FRAME_INFO", "Frame size: " + frameSamples + " samples");
                
                // Step 5: Load and process audio file
                Log.d(TAG, "Loading audio from: " + audioPath);
                sendStatusToUnity("LOADING_AUDIO", "Loading audio: " + audioPath);
                
                short[] audioData = loadWavFile(audioPath);
                if (audioData == null) {
                    sendErrorToUnity("Failed to load audio file: " + audioPath);
                    return;
                }
                Log.d(TAG, "Audio loaded: " + audioData.length + " samples");
                sendStatusToUnity("AUDIO_LOADED", "Audio loaded: " + audioData.length + " samples");
                
                // Step 6: Process audio frame by frame with callbacks
                Log.d(TAG, "Processing audio with real-time callbacks...");
                sendStatusToUnity("PROCESSING_START", "Starting audio processing...");
                
                processAudioWithCallbacks(nativePtr, audioData, frameSamples);
                
                sendStatusToUnity("COMPLETED", "Audio processing completed successfully");
                Log.d(TAG, "Processing completed successfully");
                
            } finally {
                // Step 7: Cleanup
                Log.d(TAG, "Cleaning up native instance...");
                NativeDeepFilterNet.freeNative(nativePtr);
                Log.d(TAG, "Cleanup complete");
                sendStatusToUnity("CLEANUP", "Native cleanup completed");
            }
            
        } catch (Exception e) {
            Log.e(TAG, "Error in processAudioFileWithCallback: " + e.getMessage());
            e.printStackTrace();
            sendErrorToUnity("Processing error: " + e.getMessage());
        }
    }
    
    private static void processAudioWithCallbacks(long nativePtr, short[] audioData, int frameSamples) {
        try {
            int totalFrames = (audioData.length + frameSamples - 1) / frameSamples;
            Log.d(TAG, "Processing " + totalFrames + " frames with callbacks...");
            
            ByteBuffer byteBuffer = ByteBuffer.allocateDirect(frameSamples * 2);
            byteBuffer.order(ByteOrder.LITTLE_ENDIAN);
            
            // We'll send processed audio in chunks instead of individual frames
            List<Short> processedChunk = new ArrayList<>();
            int chunkSize = frameSamples * 10; // Send every 10 frames as a chunk
            
            for (int frame = 0; frame < totalFrames; frame++) {
                int startSample = frame * frameSamples;
                int endSample = Math.min(startSample + frameSamples, audioData.length);
                int frameLength = endSample - startSample;
                
                // Fill ByteBuffer with frame data
                byteBuffer.clear();
                for (int i = 0; i < frameSamples; i++) {
                    short sample = (i < frameLength) ? audioData[startSample + i] : 0;
                    byteBuffer.putShort(sample);
                }
                byteBuffer.flip();
                
                // Process frame
                float lsnr = NativeDeepFilterNet.processFrameNative(nativePtr, byteBuffer);
                
                // Get processed data back and add to chunk
                byteBuffer.rewind();
                for (int i = 0; i < frameLength; i++) {
                    short processedSample = byteBuffer.getShort();
                    processedChunk.add(processedSample);
                }
                
                // Send chunk when it's ready or at the end
                if (processedChunk.size() >= chunkSize || frame == totalFrames - 1) {
                    sendAudioChunkToUnity(processedChunk, frame, totalFrames);
                    processedChunk.clear();
                }
                
                // Log progress
                if (frame % 100 == 0) {
                    float progress = (float) frame / totalFrames;
                    Log.d(TAG, String.format("Processing: %.1f%% (Frame %d/%d, LSNR: %.2f dB)", 
                           progress * 100, frame, totalFrames, lsnr));
                    
                    sendStatusToUnity("PROGRESS", String.format("%.1f%% complete", progress * 100));
                }
            }
            
            Log.d(TAG, "Audio processing with callbacks completed successfully");
            
        } catch (Exception e) {
            Log.e(TAG, "Error processing audio frames: " + e.getMessage());
            e.printStackTrace();
            sendErrorToUnity("Audio processing error: " + e.getMessage());
        }
    }
    
    private static void sendAudioChunkToUnity(List<Short> audioChunk, int currentFrame, int totalFrames) {
        try {
            // Convert List<Short> to float array
            float[] floatSamples = new float[audioChunk.size()];
            for (int i = 0; i < audioChunk.size(); i++) {
                floatSamples[i] = audioChunk.get(i) / 32768.0f;
            }
            
            // Create a string with chunk info and samples
            StringBuilder chunkData = new StringBuilder();
            chunkData.append("AUDIO_CHUNK:");
            chunkData.append(floatSamples.length).append(":");
            chunkData.append(currentFrame).append(":");
            chunkData.append(totalFrames).append(":");
            
            for (int i = 0; i < floatSamples.length; i++) {
                if (i > 0) chunkData.append(",");
                chunkData.append(floatSamples[i]);
            }
            
            // Send to Unity
            UnityPlayer.UnitySendMessage(unityGameObjectName, unityCallbackMethod, chunkData.toString());
            
        } catch (Exception e) {
            Log.e(TAG, "Error sending audio chunk to Unity: " + e.getMessage());
        }
    }
    
    private static void sendStatusToUnity(String status, String message) {
        try {
            String statusMessage = "STATUS:" + status + ":" + message;
            UnityPlayer.UnitySendMessage(unityGameObjectName, unityCallbackMethod, statusMessage);
        } catch (Exception e) {
            Log.e(TAG, "Error sending status to Unity: " + e.getMessage());
        }
    }
    
    private static void sendErrorToUnity(String error) {
        try {
            String errorMessage = "ERROR:" + error;
            UnityPlayer.UnitySendMessage(unityGameObjectName, unityCallbackMethod, errorMessage);
        } catch (Exception e) {
            Log.e(TAG, "Error sending error to Unity: " + e.getMessage());
        }
    }
    
    private static byte[] loadAssetFile(String fileName) {
        try {
            Context context = UnityPlayer.currentActivity;
            AssetManager assetManager = context.getAssets();
            InputStream inputStream = assetManager.open(fileName);
            
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] data = new byte[16384];
            int bytesRead;
            
            while ((bytesRead = inputStream.read(data, 0, data.length)) != -1) {
                buffer.write(data, 0, bytesRead);
            }
            
            inputStream.close();
            return buffer.toByteArray();
            
        } catch (IOException e) {
            Log.e(TAG, "Failed to load asset file: " + fileName, e);
            return null;
        }
    }
    
    private static short[] loadWavFile(String fileName) {
        try {
            Context context = UnityPlayer.currentActivity;
            AssetManager assetManager = context.getAssets();
            InputStream inputStream = assetManager.open(fileName);
            
            // Skip WAV header (44 bytes for standard WAV)
            byte[] header = new byte[44];
            inputStream.read(header);
            
            // Read audio data
            ByteArrayOutputStream buffer = new ByteArrayOutputStream();
            byte[] data = new byte[16384];
            int bytesRead;
            
            while ((bytesRead = inputStream.read(data, 0, data.length)) != -1) {
                buffer.write(data, 0, bytesRead);
            }
            
            inputStream.close();
            byte[] audioBytes = buffer.toByteArray();
            
            // Convert bytes to shorts (16-bit PCM, little-endian)
            short[] audioData = new short[audioBytes.length / 2];
            ByteBuffer byteBuffer = ByteBuffer.wrap(audioBytes);
            byteBuffer.order(ByteOrder.LITTLE_ENDIAN);
            
            for (int i = 0; i < audioData.length; i++) {
                audioData[i] = byteBuffer.getShort();
            }
            
            Log.d(TAG, "Loaded WAV file: " + audioData.length + " samples");
            return audioData;
            
        } catch (IOException e) {
            Log.e(TAG, "Failed to load WAV file: " + fileName, e);
            return null;
        }
    }
} 