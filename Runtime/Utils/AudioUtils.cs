using System;
using System.Collections.Generic;
using System.IO;
using MP3Sharp;
using OggVorbisEncoder.Example;
using UnityEngine;

namespace UG.Utils
{
    public class AudioUtils
    {
        private const float MaxSampleValue = 32768f; // Max value for 16-bit audio, used for normalization.
        private const int BufferSize = 4096; // Size of the buffer for reading MP3 data.

        public static AudioClip GetAudioClip(byte[] audioData, int channels = 1, int sampleRate = 44100)
        {
            float[] samples = ConvertMP3ToPCM(audioData);

            var audioClip = AudioClip.Create("clip", samples.Length, channels, sampleRate * 2, false);
            audioClip.SetData(samples, 0);

            return audioClip;
        }

        public static float[] ConvertMP3ToPCM(byte[] mp3Data)
        {
            // Initialize MP3Stream with the byte array
            using MP3Stream mp3Stream = new(new MemoryStream(mp3Data));

            // Buffer for reading the decoded PCM data
            byte[] buffer = new byte[BufferSize];
            int bytesRead;
            List<float> samplesList = new List<float>();

            while ((bytesRead = mp3Stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                // Convert bytes to float samples (assuming 16-bit PCM data)
                for (int i = 0; i < bytesRead; i += 2)
                {
                    // Convert 2 bytes into a 16-bit signed sample
                    short sample = BitConverter.ToInt16(buffer, i);

                    // Normalize the sample to the range -1.0f to 1.0f
                    float normalizedSample = sample / MaxSampleValue;
                    samplesList.Add(normalizedSample);
                }
            }

            return samplesList.ToArray();
        }

        public static byte[] ConvertRawPCMToOgg(
            byte[] audioData,
            int pcmChannels = 1,
            int outputChannels = 1,
            int sampleRate = 16000
        )
        {
            byte[] oggBytes = Encoder.ConvertRawPCMFile(
                sampleRate,
                outputChannels,
                audioData,
                Encoder.PcmSample.SixteenBit,
                sampleRate,
                pcmChannels
            );

            return oggBytes;
        }
    }
}