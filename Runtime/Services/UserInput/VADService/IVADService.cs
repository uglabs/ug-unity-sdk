using System;

namespace UG.Services
{
    public interface IVADService
    {
        event Action OnSpoke;
        event Action<float[]> OnSpokeWithSamples;
        event Action OnSilenced;
        event Action OnHardTimeout;
        event Action<DateTime, DateTime> OnVADClosingTime;

        event Action<float, int, float> OnSpokeDebugInfo;
        event Action<float> OnSpeechProbabilityValueChanged;

        SpeechBuffer AddAudio(float[] samples);
        void SetThreshold(float minSilenceDurationMs = 750,
            float threshold = 0.55f,
            float toSilenceThreshold = 0.25f,
            float hardTimeoutMs = 3500,
            int minSpeechDetectionMs = 160);
        void Reset();
        void Dispose();
    }
}