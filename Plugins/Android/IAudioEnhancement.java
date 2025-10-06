package com.uglabs;

/**
 * Interface for audio enhancement algorithms that can process audio samples
 * 
 * Defines the contract for audio enhancement implementations such as:
 * - Noise reduction (DeepFilterNet3)
 * - Echo cancellation
 * - Gain control
 * - Speech enhancement
 */
public interface IAudioEnhancement {
    
    /**
     * Process audio samples and return enhanced samples
     * 
     * @param inputSamples Input audio samples as float array (-1.0 to 1.0)
     * @param sampleRate Sample rate in Hz (e.g., 16000, 48000)
     * @return Enhanced audio samples as float array
     */
    float[] processSamples(float[] inputSamples, int sampleRate);
    
    /**
     * Process audio samples with default sample rate (16000 Hz)
     * 
     * @param inputSamples Input audio samples as float array (-1.0 to 1.0)
     * @return Enhanced audio samples as float array
     */
    default float[] processSamples(float[] inputSamples) {
        return processSamples(inputSamples, 16000);
    }
    
    /**
     * Get the name of the enhancement algorithm
     * 
     * @return Human-readable name of the enhancement (e.g., "DeepFilterNet3", "Echo Canceller")
     */
    String getName();
    
    /**
     * Check if this enhancement is currently enabled
     * 
     * @return true if enhancement is active, false if disabled
     */
    boolean isEnabled();
    
    /**
     * Enable or disable this enhancement
     * 
     * @param enabled true to enable enhancement, false to disable
     */
    void setEnabled(boolean enabled);
    
    /**
     * Initialize the enhancement with configuration parameters
     * Optional method for enhancements that require setup
     * 
     * @param config Configuration parameters (implementation-specific)
     * @return true if initialization successful, false otherwise
     */
    default boolean initialize(Object config) {
        return true; // Default implementation does nothing
    }
    
    /**
     * Clean up resources used by the enhancement
     * Should be called when the enhancement is no longer needed
     */
    default void dispose() {
        // Default implementation does nothing
    }
    
    /**
     * Get information about the enhancement capabilities
     * 
     * @return Description of what this enhancement does
     */
    default String getDescription() {
        return "Audio enhancement: " + getName();
    }
    
    /**
     * Check if this enhancement supports the given sample rate
     * 
     * @param sampleRate Sample rate to check
     * @return true if supported, false otherwise
     */
    default boolean supportsSampleRate(int sampleRate) {
        return true; // Default: support all sample rates
    }
} 