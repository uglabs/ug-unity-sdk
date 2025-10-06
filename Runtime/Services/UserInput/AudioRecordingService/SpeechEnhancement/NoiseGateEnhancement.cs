using System;
using SimpleNoiseReduction;

namespace UG.Services.UserInput.AudioRecordingService
{
    /// <summary>
    /// Noise gate enhancement that implements IAudioEnhancement interface
    /// </summary>
    public class NoiseGateEnhancement : IAudioEnhancement
    {
        private float _thresholdDb = -30.0f;
        private float _reductionDb = -20.0f;
        private float _attackMs = 5.0f;
        private float _decayMs = 50.0f;
        private float _holdMs = 0.0f;
        private float _frequencyThresholdKhz = 0.0f;
        
        // Persistent state across multiple ProcessSamples calls
        private float _gateLevel = 0.0f;
        private int _holdCounter = 0;
        private bool _isHolding = false;
        
        public string Name => "Noise Gate";
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Create a noise gate enhancement with default settings
        /// </summary>
        public NoiseGateEnhancement() { }
        
        /// <summary>
        /// Create a noise gate enhancement with custom settings
        /// </summary>
        /// <param name="thresholdDb">Gate threshold in dB (default -30.0)</param>
        /// <param name="reductionDb">Level reduction in dB (default -20.0)</param>
        /// <param name="attackMs">Attack time in milliseconds (default 5.0)</param>
        /// <param name="decayMs">Decay time in milliseconds (default 50.0)</param>
        /// <param name="holdMs">Hold time in milliseconds (default 0.0)</param>
        /// <param name="frequencyThresholdKhz">Frequency threshold in kHz (default 0.0 = no frequency filtering)</param>
        public NoiseGateEnhancement(float thresholdDb = -30.0f, float reductionDb = -20.0f, 
            float attackMs = 5.0f, float decayMs = 50.0f, float holdMs = 0.0f, float frequencyThresholdKhz = 0.0f)
        {
            _thresholdDb = thresholdDb;
            _reductionDb = reductionDb;
            _attackMs = attackMs;
            _decayMs = decayMs;
            _holdMs = holdMs;
            _frequencyThresholdKhz = frequencyThresholdKhz;
        }

        public static NoiseGateEnhancement CreateDefaults()
        {
            return new NoiseGateEnhancement(-38.0f, -15.0f, 3.0f, 50.0f, 8.0f, 0.0f); 
            // - 48, -10, 3, 20, 8, 0
        }
        
        /// <summary>
        /// Create a conservative noise gate (gentle settings)
        /// </summary>
        public static NoiseGateEnhancement CreateConservative()
        {
            return new NoiseGateEnhancement(-50.0f, -10.0f, 25.0f, 150.0f);
        }
        
        /// <summary>
        /// Create an aggressive noise gate (strong settings)
        /// </summary>
        public static NoiseGateEnhancement CreateAggressive()
        {
            return new NoiseGateEnhancement(-20.0f, -40.0f, 2.0f, 20.0f);
        }
        
        public float[] ProcessSamples(float[] inputSamples, int sampleRate = 16000)
        {
            if (!IsEnabled || inputSamples == null || inputSamples.Length == 0)
                return inputSamples;
                
            try
            {
                // Use the persistent state version to maintain gate state across chunks
                return AdvancedNoiseGate.ProcessNoiseGateWithState(
                    inputSamples, 
                    _thresholdDb, 
                    _reductionDb, 
                    _attackMs, 
                    _holdMs, 
                    _decayMs, 
                    _frequencyThresholdKhz, 
                    sampleRate,
                    ref _gateLevel,
                    ref _holdCounter,
                    ref _isHolding
                );
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error in NoiseGateEnhancement: {e.Message}");
                // Return original samples if processing fails
                return inputSamples;
            }
        }
        
        /// <summary>
        /// Update noise gate parameters
        /// </summary>
        public void UpdateParameters(float thresholdDb, float reductionDb, float attackMs, float decayMs, 
            float holdMs = 0.0f, float frequencyThresholdKhz = 0.0f)
        {
            _thresholdDb = thresholdDb;
            _reductionDb = reductionDb;
            _attackMs = attackMs;
            _decayMs = decayMs;
            _holdMs = holdMs;
            _frequencyThresholdKhz = frequencyThresholdKhz;
        }
        
        /// <summary>
        /// Reset the noise gate state (useful for testing or when audio source changes)
        /// </summary>
        public void ResetState()
        {
            _gateLevel = 0.0f;
            _holdCounter = 0;
            _isHolding = false;
        }
        
        /// <summary>
        /// Get current gate state for debugging
        /// </summary>
        public (float gateLevel, int holdCounter, bool isHolding) GetCurrentState()
        {
            return (_gateLevel, _holdCounter, _isHolding);
        }
    }
} 