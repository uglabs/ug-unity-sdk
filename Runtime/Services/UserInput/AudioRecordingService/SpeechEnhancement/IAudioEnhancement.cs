using System;

namespace UG.Services.UserInput.AudioRecordingService
{
    /// <summary>
    /// Interface for audio enhancement algorithms that can process audio samples
    /// </summary>
    public interface IAudioEnhancement
    {
        /// <summary>
        /// Process audio samples and return enhanced samples
        /// </summary>
        /// <param name="inputSamples">Input audio samples</param>
        /// <param name="sampleRate">Sample rate in Hz (default 16000)</param>
        /// <returns>Enhanced audio samples</returns>
        float[] ProcessSamples(float[] inputSamples, int sampleRate = 16000);
        
        /// <summary>
        /// Name of the enhancement algorithm
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Whether this enhancement is currently enabled
        /// </summary>
        bool IsEnabled { get; set; }
    }
} 