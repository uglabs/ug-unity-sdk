using System.Collections.Generic;

namespace UG.Services
{
    public class SpeechBuffer
    {
        public float[] Samples;
        public float Start { get; set; }
        public float End { get; set; }

        public SpeechBuffer(float start, float end, float[] samples)
        {
            Start = start;
            End = end;
            Samples = samples;
        }
    }
}