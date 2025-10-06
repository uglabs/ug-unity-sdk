using System;
using System.Numerics;
#if FREQUENCY_FILTER
using FftSharp;
#endif
using UnityEngine;

namespace SimpleNoiseReduction
{
    public class AdvancedNoiseGate
    {
        /// <summary>
        /// Advanced noise gate with parameters matching professional audio software
        /// </summary>
        /// <param name="inputSignal">Input audio samples</param>
        /// <param name="gateThresholdDb">Gate threshold in dB (default -34.0)</param>
        /// <param name="levelReductionDb">Level reduction in dB (default -24.0)</param>
        /// <param name="attackMs">Attack time in milliseconds (default 10.0)</param>
        /// <param name="holdMs">Hold time in milliseconds (default 50.0)</param>
        /// <param name="decayMs">Decay time in milliseconds (default 100.0)</param>
        /// <param name="frequencyThresholdKhz">Frequency threshold in kHz (default 0.0 = no frequency filtering)</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <returns>Filtered audio samples</returns>
        public static float[] ProcessNoiseGate(float[] inputSignal, 
            float gateThresholdDb = -34.0f, 
            float levelReductionDb = -24.0f,
            float attackMs = 10.0f, 
            float holdMs = 50.0f, 
            float decayMs = 100.0f,
            float frequencyThresholdKhz = 0.0f,
            int sampleRate = 16000)
        {
            // Convert dB values to linear
            float gateThreshold = (float)Math.Pow(10, gateThresholdDb / 20.0);
            float levelReduction = (float)Math.Pow(10, levelReductionDb / 20.0);
            
            // Convert time values to samples
            int attackSamples = (int)(attackMs * sampleRate / 1000.0);
            int holdSamples = (int)(holdMs * sampleRate / 1000.0);
            int decaySamples = (int)(decayMs * sampleRate / 1000.0);
            
            float[] outputSignal = new float[inputSignal.Length];
            
            // State variables for the gate
            float gateLevel = 0.0f; // Current gate level (0 = closed, 1 = open)
            int holdCounter = 0; // Counter for hold time
            bool isHolding = false; // Whether we're in hold state
            
            // Process each sample
            for (int i = 0; i < inputSignal.Length; i++)
            {
                float inputSample = inputSignal[i];
                float inputMagnitude = Math.Abs(inputSample);
                
                // Check if input exceeds threshold
                if (inputMagnitude > gateThreshold)
                {
                    // Gate should open
                    if (gateLevel < 1.0f)
                    {
                        // Attack phase - gradually open the gate
                        gateLevel += 1.0f / attackSamples;
                        if (gateLevel > 1.0f) gateLevel = 1.0f;
                    }
                    
                    // Reset hold counter
                    holdCounter = 0;
                    isHolding = false;
                }
                else
                {
                    // Input below threshold
                    if (gateLevel > 0.0f)
                    {
                        if (!isHolding)
                        {
                            // Start hold phase
                            holdCounter++;
                            if (holdCounter >= holdSamples)
                            {
                                isHolding = true;
                            }
                        }
                        else
                        {
                            // Decay phase - gradually close the gate
                            gateLevel -= 1.0f / decaySamples;
                            if (gateLevel < 0.0f) gateLevel = 0.0f;
                        }
                    }
                }
                
                // Apply gate level to output
                float outputSample = inputSample * (gateLevel + (1.0f - gateLevel) * levelReduction);
                outputSignal[i] = outputSample;
            }
            
            // Apply frequency filtering if specified
            if (frequencyThresholdKhz > 0.0f)
            {
                outputSignal = ApplyFrequencyFilter(outputSignal, frequencyThresholdKhz * 1000.0f, sampleRate);
            }
            
            return outputSignal;
        }

        /// <summary>
        /// Advanced noise gate with persistent state across multiple calls
        /// This method maintains gate state between audio chunks of varying sizes
        /// </summary>
        /// <param name="inputSignal">Input audio samples</param>
        /// <param name="gateThresholdDb">Gate threshold in dB</param>
        /// <param name="levelReductionDb">Level reduction in dB</param>
        /// <param name="attackMs">Attack time in milliseconds</param>
        /// <param name="holdMs">Hold time in milliseconds</param>
        /// <param name="decayMs">Decay time in milliseconds</param>
        /// <param name="frequencyThresholdKhz">Frequency threshold in kHz</param>
        /// <param name="sampleRate">Sample rate in Hz</param>
        /// <param name="gateLevel">Current gate level (0 = closed, 1 = open) - passed by reference</param>
        /// <param name="holdCounter">Current hold counter - passed by reference</param>
        /// <param name="isHolding">Current hold state - passed by reference</param>
        /// <returns>Filtered audio samples</returns>
        public static float[] ProcessNoiseGateWithState(float[] inputSignal, 
            float gateThresholdDb, 
            float levelReductionDb,
            float attackMs, 
            float holdMs, 
            float decayMs,
            float frequencyThresholdKhz,
            int sampleRate,
            ref float gateLevel,
            ref int holdCounter,
            ref bool isHolding)
        {
            // Convert dB values to linear
            float gateThreshold = (float)Math.Pow(10, gateThresholdDb / 20.0);
            float levelReduction = (float)Math.Pow(10, levelReductionDb / 20.0);
            
            // Convert time values to samples
            int attackSamples = (int)(attackMs * sampleRate / 1000.0);
            int holdSamples = (int)(holdMs * sampleRate / 1000.0);
            int decaySamples = (int)(decayMs * sampleRate / 1000.0);
            
            float[] outputSignal = new float[inputSignal.Length];
            
            // Process each sample using the persistent state
            for (int i = 0; i < inputSignal.Length; i++)
            {
                float inputSample = inputSignal[i];
                float inputMagnitude = Math.Abs(inputSample);
                
                // Check if input exceeds threshold
                if (inputMagnitude > gateThreshold)
                {
                    // Gate should open
                    if (gateLevel < 1.0f)
                    {
                        // Attack phase - gradually open the gate
                        gateLevel += 1.0f / attackSamples;
                        if (gateLevel > 1.0f) gateLevel = 1.0f;
                    }
                    
                    // Reset hold counter
                    holdCounter = 0;
                    isHolding = false;
                }
                else
                {
                    // Input below threshold
                    if (gateLevel > 0.0f)
                    {
                        if (!isHolding)
                        {
                            // Start hold phase
                            holdCounter++;
                            if (holdCounter >= holdSamples)
                            {
                                isHolding = true;
                            }
                        }
                        else
                        {
                            // Decay phase - gradually close the gate
                            gateLevel -= 1.0f / decaySamples;
                            if (gateLevel < 0.0f) gateLevel = 0.0f;
                        }
                    }
                }
                
                // Apply gate level to output
                float outputSample = inputSample * (gateLevel + (1.0f - gateLevel) * levelReduction);
                outputSignal[i] = outputSample;
            }
            
            // Apply frequency filtering if specified
            if (frequencyThresholdKhz > 0.0f)
            {
                outputSignal = ApplyFrequencyFilter(outputSignal, frequencyThresholdKhz * 1000.0f, sampleRate);
            }
            
            return outputSignal;
        }
        
        /// <summary>
        /// Ultra-conservative noise gate with very gentle settings
        /// </summary>
        /// <param name="inputSignal">Input audio samples</param>
        /// <param name="thresholdDb">Gate threshold in dB (default -50.0 = very sensitive)</param>
        /// <param name="reductionDb">Level reduction in dB (default -10.0 = very gentle)</param>
        /// <param name="attackMs">Attack time in milliseconds (default 25.0 = very slow)</param>
        /// <param name="decayMs">Decay time in milliseconds (default 150.0 = very slow)</param>
        /// <returns>Filtered audio samples</returns>
        public static float[] UltraConservativeNoiseGate(float[] inputSignal, 
            float thresholdDb = -50.0f, 
            float reductionDb = -10.0f,
            float attackMs = 25.0f, 
            float decayMs = 150.0f)
        {
            return ProcessNoiseGate(inputSignal, thresholdDb, reductionDb, attackMs, 0.0f, decayMs, 0.0f);
        }
        
        /// <summary>
        /// Apply frequency filtering to remove frequencies above threshold
        /// </summary>
        private static float[] ApplyFrequencyFilter(float[] inputSignal, float frequencyThreshold, int sampleRate)
        {
#if FREQUENCY_FILTER
            int frameSize = 512;
            int hopSize = frameSize / 2;
            int numFrames = (inputSignal.Length - frameSize) / hopSize + 1;
            
            float[] outputSignal = new float[inputSignal.Length];
            Array.Clear(outputSignal, 0, outputSignal.Length);
            
            // Create Hanning window
            var window = new FftSharp.Windows.Hanning();
            
            // Calculate frequency bins
            double[] frequencies = FftSharp.FFT.FrequencyScale(frameSize, sampleRate);
            
            // Apply frequency filter frame by frame
            for (int frame = 0; frame < numFrames; frame++)
            {
                int startSample = frame * hopSize;
                double[] frameData = new double[frameSize];
                
                // Extract frame and convert to double
                for (int i = 0; i < frameSize; i++)
                {
                    if (startSample + i < inputSignal.Length)
                        frameData[i] = inputSignal[startSample + i];
                }
                
                // Apply window
                window.ApplyInPlace(frameData);
                
                // FFT using FftSharp
                Complex[] fft = FftSharp.FFT.Forward(frameData);
                
                // Apply frequency filter
                for (int i = 0; i < fft.Length; i++)
                {
                    if (i < frequencies.Length)
                    {
                        // Low-pass filter: attenuate frequencies above threshold
                        if (frequencies[i] > frequencyThreshold)
                        {
                            // Gradually reduce magnitude for frequencies above threshold
                            double attenuation = frequencyThreshold / frequencies[i];
                            fft[i] *= attenuation;
                        }
                    }
                }
                
                // IFFT
                FftSharp.FFT.Inverse(fft);
                double[] filteredFrame = new double[fft.Length];
                for (int i = 0; i < fft.Length; i++)
                {
                    filteredFrame[i] = fft[i].Real;
                }
                
                // Apply window and overlap-add
                window.ApplyInPlace(filteredFrame);
                for (int i = 0; i < frameSize; i++)
                {
                    if (startSample + i < outputSignal.Length)
                    {
                        outputSignal[startSample + i] += (float)filteredFrame[i];
                    }
                }
            }
            return outputSignal;
#else
            return inputSignal;
#endif
        }
        
        /// <summary>
        /// Simple noise gate with basic parameters (easier to use)
        /// </summary>
        /// <param name="inputSignal">Input audio samples</param>
        /// <param name="thresholdDb">Gate threshold in dB</param>
        /// <param name="reductionDb">Level reduction in dB</param>
        /// <param name="attackMs">Attack time in milliseconds</param>
        /// <param name="decayMs">Decay time in milliseconds</param>
        /// <returns>Filtered audio samples</returns>
        public static float[] SimpleNoiseGate(float[] inputSignal, 
            float thresholdDb = -30.0f, 
            float reductionDb = -20.0f,
            float attackMs = 5.0f, 
            float decayMs = 50.0f)
        {
            return ProcessNoiseGate(inputSignal, thresholdDb, reductionDb, attackMs, 0.0f, decayMs, 0.0f);
        }
    }
} 