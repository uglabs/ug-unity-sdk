package com.rikorose.deepfilternet;

import android.util.Log;

public class NativeDeepFilterNet {
    private static final String TAG = "NativeDeepFilterNet";
    private static boolean libraryLoadedSuccessfully = false;
    
    // Load the native library
    static {
        try {
            System.loadLibrary("df");
            libraryLoadedSuccessfully = true;
            Log.d(TAG, "Successfully loaded libdf library!");
        } catch (UnsatisfiedLinkError e) {
            libraryLoadedSuccessfully = false;
            Log.e(TAG, "Failed to load libdf library: " + e.getMessage());
        }
    }
    
    // Native method declarations that match the Rust JNI function signatures
    public static native long newNative(byte[] modelBytes, float attenLim);
    public static native void freeNative(long ptr);
    public static native long getFrameLengthNative(long ptr);
    public static native boolean setAttenLimNative(long ptr, float limDb);
    public static native boolean setPostFilterBetaNative(long ptr, float beta);
    public static native float processFrameNative(long ptr, java.nio.ByteBuffer buffer);
    
    // Helper methods for testing and debugging
    public static boolean isLibraryLoaded() {
        return libraryLoadedSuccessfully;
    }
    
    public static String getLibraryInfo() {
        return "DeepFilterNet3 Native Library - Package: com.rikorose.deepfilternet";
    }
    
    // Additional method to test if native functions actually work
    public static boolean testNativeFunctions() {
        if (!libraryLoadedSuccessfully) {
            return false;
        }
        
        try {
            // Test calling a native function with null pointer - should return 0 without crashing
            long result = getFrameLengthNative(0);
            Log.d(TAG, "Native function test result: " + result);
            return true;
        } catch (Exception e) {
            Log.e(TAG, "Native function test failed: " + e.getMessage());
            return false;
        }
    }
} 