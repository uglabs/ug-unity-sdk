using System;
using System.Collections.Generic;
using System.IO;
using NLayer.NAudioSupport;
using NAudio.Wave;

namespace UG.Services
{
    public class MP3Decoder
    {
        private Mp3FrameDecompressor _decompressor;
        private bool _isInitialized;
        private int _sampleRate;
        private int _channels;
        private List<byte> _accumulatedData = new List<byte>();

        public MP3Decoder()
        {
            _isInitialized = false;
        }

        /// <summary>
        /// Process chunk using NAudio's Mp3Frame + NLayer's decoder
        /// </summary>
        /// <param name="mp3Data">Byte array containing MP3 data</param>
        /// <returns>Array of decoded PCM samples as floats</returns>
        public float[] ProcessChunk(byte[] mp3Data)
        {
            if (mp3Data == null || mp3Data.Length == 0)
                return new float[0];

            UGLog.Log($"[MP3Decoder] Processing chunk of {mp3Data.Length} bytes");

            // Add new data to accumulated buffer
            _accumulatedData.AddRange(mp3Data);

            var decodedSamples = new List<float>();

            try
            {
                // Simple frame detection like the Python script
                int offset = 0;
                int processedBytes = 0;

                while (offset < _accumulatedData.Count - 4)
                {
                    // Look for MP3 sync word (0xFF followed by 0xE0)
                    if (_accumulatedData[offset] == 0xFF && (_accumulatedData[offset + 1] & 0xE0) == 0xE0)
                    {
                        // Parse the 4-byte header to get frame size
                        var frameSize = ParseMp3FrameHeader(offset);
                        if (frameSize > 0 && offset + frameSize <= _accumulatedData.Count)
                        {
                            // We have a complete frame
                            var frameData = new byte[frameSize];
                            for (int i = 0; i < frameSize; i++)
                            {
                                frameData[i] = _accumulatedData[offset + i];
                            }

                            // Decode the frame using NAudio
                            var frameSamples = DecodeFrameFromBytes(frameData);
                            if (frameSamples != null && frameSamples.Length > 0)
                            {
                                decodedSamples.AddRange(frameSamples);
                                offset += frameSize;
                                processedBytes = offset;
                            }
                            else
                            {
                                offset++;
                            }
                        }
                        else
                        {
                            offset++;
                        }
                    }
                    else
                    {
                        offset++;
                    }
                }

                // Remove processed data from buffer
                if (processedBytes > 0)
                {
                    _accumulatedData.RemoveRange(0, processedBytes);
                }
            }
            catch (Exception ex)
            {
                UGLog.LogWarning($"[MP3Decoder] Frame processing failed: {ex.Message}");
                return new float[0];
            }

            return decodedSamples.ToArray();
        }

        private int ParseMp3FrameHeader(int offset)
        {
            if (offset + 4 > _accumulatedData.Count)
                return 0;

            // Parse MP3 frame header (same logic as Python script)
            uint header = (uint)((_accumulatedData[offset] << 24) |
                                (_accumulatedData[offset + 1] << 16) |
                                (_accumulatedData[offset + 2] << 8) |
                                _accumulatedData[offset + 3]);

            // Check sync word (first 11 bits should be all 1s)
            uint syncWord = (header >> 21) & 0x7FF;
            if (syncWord != 0x7FF)
                return 0;

            // Extract MPEG version and layer
            uint mpegVersion = (header >> 19) & 0x3;
            uint layer = (header >> 17) & 0x3;

            // Extract bitrate and sampling rate
            uint bitrateIndex = (header >> 12) & 0xF;
            uint samplingRateIndex = (header >> 10) & 0x3;

            // Extract padding
            uint padding = (header >> 9) & 0x1;

            // Calculate frame size for MPEG-1 Layer III
            if (mpegVersion == 3 && layer == 1)
            {
                int[] bitrates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
                int[] samplingRates = { 44100, 48000, 32000, 0 };

                int bitrate = bitrates[bitrateIndex] * 1000;
                int samplingRate = samplingRates[samplingRateIndex];

                if (bitrate > 0 && samplingRate > 0)
                {
                    // Frame size calculation for MPEG-1 Layer III (including padding)
                    int frameSize = (int)((144 * bitrate) / samplingRate) + (int)padding;
                    return frameSize;
                }
            }

            return 0;
        }

        private float[] DecodeFrameFromBytes(byte[] frameData)
        {
            try
            {
                using (var stream = new MemoryStream(frameData))
                {
                    var frame = Mp3Frame.LoadFromStream(stream);
                    if (frame != null)
                    {
                        // Initialize decompressor on first successful frame
                        if (!_isInitialized)
                        {
                            InitializeDecompressor(frame);
                        }

                        return DecodeFrame(frame);
                    }
                }
            }
            catch (Exception ex)
            {
                UGLog.LogWarning($"[MP3Decoder] Frame decode failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Initialize the decompressor with the first frame
        /// </summary>
        private void InitializeDecompressor(Mp3Frame frame)
        {
            // Create WaveFormat from the frame
            var waveFormat = new WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2);

            // Initialize the decompressor
            _decompressor = new Mp3FrameDecompressor(waveFormat);

            // Store format info
            _sampleRate = frame.SampleRate;
            _channels = frame.ChannelMode == ChannelMode.Mono ? 1 : 2;

            _isInitialized = true;
        }

        /// <summary>
        /// Decode a frame using NLayer's decoder
        /// </summary>
        private float[] DecodeFrame(Mp3Frame frame)
        {
            try
            {
                if (_decompressor == null) return null;

                // Create output buffer large enough for the frame's entire output (up to 9,216 bytes as per error message)
                // Use a generous buffer size to avoid the "Buffer not large enough" error
                var outputBuffer = new byte[9216]; // Maximum size mentioned in error

                // Decompress the frame
                int bytesDecoded = _decompressor.DecompressFrame(frame, outputBuffer, 0);

                if (bytesDecoded > 0)
                {
                    // Convert bytes to float samples
                    var samples = new float[bytesDecoded / sizeof(float)];
                    Buffer.BlockCopy(outputBuffer, 0, samples, 0, bytesDecoded);
                    return samples;
                }
            }
            catch (Exception ex)
            {
                UGLog.LogWarning($"[MP3Decoder] Frame decode failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get current audio format information
        /// </summary>
        public (int sampleRate, int channels) GetAudioFormat()
        {
            return (_sampleRate, _channels);
        }

        /// <summary>
        /// Get the original detected sample rate from the MP3 file
        /// </summary>
        public int GetDetectedSampleRate()
        {
            return _sampleRate;
        }

        /// <summary>
        /// Process any remaining buffered data
        /// </summary>
        public float[] ProcessRemainingData()
        {
            if (_accumulatedData.Count == 0)
                return new float[0];

            return ProcessChunk(new byte[0]); // Process remaining buffer
        }

        public void Dispose()
        {
            _decompressor?.Dispose();
            _accumulatedData.Clear();
        }
    }
}