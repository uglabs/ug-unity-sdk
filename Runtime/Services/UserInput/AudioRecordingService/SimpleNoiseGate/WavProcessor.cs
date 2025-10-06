using System;
using System.IO;

namespace SimpleNoiseReduction
{
    public class WavProcessor
    {
        public class WavHeader
        {
            public int SampleRate { get; set; }
            public int BitsPerSample { get; set; }
            public int Channels { get; set; }
            public int DataSize { get; set; }
        }
        
        /// <summary>
        /// Reads a WAV file and returns the audio samples
        /// </summary>
        /// <param name="filePath">Path to the WAV file</param>
        /// <returns>Audio samples as float array</returns>
        public static float[] ReadWavFile(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // Read WAV header
                string riff = new string(reader.ReadChars(4));
                if (riff != "RIFF")
                    throw new Exception("Not a valid WAV file");
                
                int fileSize = reader.ReadInt32();
                string wave = new string(reader.ReadChars(4));
                if (wave != "WAVE")
                    throw new Exception("Not a valid WAV file");
                
                WavHeader header = new WavHeader();
                
                // Read format chunk
                string format = new string(reader.ReadChars(4));
                if (format != "fmt ")
                    throw new Exception("Invalid WAV format");
                
                int formatChunkSize = reader.ReadInt32();
                int audioFormat = reader.ReadInt16();
                header.Channels = reader.ReadInt16();
                header.SampleRate = reader.ReadInt32();
                int byteRate = reader.ReadInt32();
                int blockAlign = reader.ReadInt16();
                header.BitsPerSample = reader.ReadInt16();
                
                // Skip to data chunk
                while (true)
                {
                    string chunkId = new string(reader.ReadChars(4));
                    int chunkSize = reader.ReadInt32();
                    
                    if (chunkId == "data")
                    {
                        header.DataSize = chunkSize;
                        break;
                    }
                    else
                    {
                        reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                    }
                }
                
                // Read audio data
                int numSamples = header.DataSize / (header.BitsPerSample / 8);
                float[] samples = new float[numSamples];
                
                for (int i = 0; i < numSamples; i++)
                {
                    if (header.BitsPerSample == 16)
                    {
                        short sample = reader.ReadInt16();
                        samples[i] = sample / 32768.0f; // Normalize to [-1, 1]
                    }
                    else if (header.BitsPerSample == 24)
                    {
                        byte[] bytes = reader.ReadBytes(3);
                        int sample = (bytes[2] << 16) | (bytes[1] << 8) | bytes[0];
                        if ((sample & 0x800000) != 0)
                            sample |= ~0xFFFFFF; // Sign extend
                        samples[i] = sample / 8388608.0f; // Normalize to [-1, 1]
                    }
                    else if (header.BitsPerSample == 32)
                    {
                        int sample = reader.ReadInt32();
                        samples[i] = sample / 2147483648.0f; // Normalize to [-1, 1]
                    }
                    else
                    {
                        throw new Exception($"Unsupported bit depth: {header.BitsPerSample}");
                    }
                }
                
                return samples;
            }
        }
        
        /// <summary>
        /// Writes audio samples to a WAV file
        /// </summary>
        /// <param name="filePath">Output file path</param>
        /// <param name="samples">Audio samples</param>
        /// <param name="sampleRate">Sample rate (default: 16000)</param>
        /// <param name="channels">Number of channels (default: 1)</param>
        /// <param name="bitsPerSample">Bits per sample (default: 16)</param>
        public static void WriteWavFile(string filePath, float[] samples, int sampleRate = 16000, int channels = 1, int bitsPerSample = 16)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // Write WAV header
                writer.Write("RIFF".ToCharArray());
                int fileSize = 36 + samples.Length * (bitsPerSample / 8);
                writer.Write(fileSize);
                writer.Write("WAVE".ToCharArray());
                
                // Write format chunk
                writer.Write("fmt ".ToCharArray());
                writer.Write(16); // Format chunk size
                writer.Write((short)1); // Audio format (PCM)
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(sampleRate * channels * bitsPerSample / 8); // Byte rate
                writer.Write((short)(channels * bitsPerSample / 8)); // Block align
                writer.Write((short)bitsPerSample);
                
                // Write data chunk
                writer.Write("data".ToCharArray());
                writer.Write(samples.Length * (bitsPerSample / 8));
                
                // Write audio data
                for (int i = 0; i < samples.Length; i++)
                {
                    float sample = Math.Max(-1.0f, Math.Min(1.0f, samples[i])); // Clamp to [-1, 1]
                    
                    if (bitsPerSample == 16)
                    {
                        short value = (short)(sample * 32767.0f);
                        writer.Write(value);
                    }
                    else if (bitsPerSample == 24)
                    {
                        int value = (int)(sample * 8388607.0f);
                        writer.Write((byte)(value & 0xFF));
                        writer.Write((byte)((value >> 8) & 0xFF));
                        writer.Write((byte)((value >> 16) & 0xFF));
                    }
                    else if (bitsPerSample == 32)
                    {
                        int value = (int)(sample * 2147483647.0f);
                        writer.Write(value);
                    }
                }
            }
        }
    }
} 