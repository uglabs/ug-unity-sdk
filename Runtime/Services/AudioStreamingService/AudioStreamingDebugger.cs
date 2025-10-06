using UnityEngine;

namespace UG.Services.AudioStreamingService
{
    public class AudioStreamerDebugger : MonoBehaviour
    {
        [SerializeField] public AudioStreamer targetStreamer;

        [Header("Streaming Status")]
        [SerializeField] public bool isStreaming;
        [SerializeField] public bool isPlayingSamples;
        [SerializeField] public bool isEndOfStream;
        [SerializeField] public bool isAllSamplesPlayed;

        [Header("Data Stats")]
        [SerializeField] public int completeChunksCount;
        [SerializeField] public int dataReadPointer;
        [SerializeField] public int totalEmptySamplesCount;
        [SerializeField] public int firstSampleValue;

        [Header("Playback Timing")]
        [SerializeField] public float totalPlaybackTime;
        [SerializeField] public float playbackTime;
        [SerializeField] public double playbackTimeDSP;
        [SerializeField] public int lastSamplePosition;

        [Header("Audio Source")]
        [SerializeField] public bool isAudioSourcePlaying;
        [SerializeField] public int audioSourceTimeSamples;
        [SerializeField] public float audioSourceTime;

        private void Update()
        {
            if (targetStreamer == null)
                return;

            // Access public properties
            isStreaming = targetStreamer.IsStreaming();

            // Access private fields through reflection
            var streamerType = typeof(AudioStreamer);
            isPlayingSamples = (bool)streamerType.GetField("_isPlayingSamples", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            isEndOfStream = (bool)streamerType.GetField("_isEndOfStream", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            isAllSamplesPlayed = (bool)streamerType.GetField("_isAllSamplesPlayed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);

            var completeChunks = (System.Collections.Generic.List<byte[]>)streamerType.GetField("_completeChunks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            completeChunksCount = completeChunks?.Count ?? 0;

            dataReadPointer = (int)streamerType.GetField("_dataReadPointer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            totalEmptySamplesCount = (int)streamerType.GetField("_totalEmptySamplesCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            firstSampleValue = (int)streamerType.GetField("_firstSampleValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);

            totalPlaybackTime = (float)streamerType.GetField("_totalPlaybackTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            playbackTime = (float)streamerType.GetField("playbackTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            playbackTimeDSP = (double)streamerType.GetField("playbackTimeDSP", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            lastSamplePosition = (int)streamerType.GetField("lastSamplePosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);

            // Get audio source info
            var audioSource = (AudioSource)streamerType.GetField("_audioSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(targetStreamer);
            if (audioSource != null && audioSource.clip != null)
            {
                isAudioSourcePlaying = audioSource.isPlaying;
                audioSourceTimeSamples = audioSource.timeSamples;
                audioSourceTime = audioSource.time;
            }
        }
    }
}