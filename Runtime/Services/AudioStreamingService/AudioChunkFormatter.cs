using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UG.Services.AudioStreamingService
{
    public class AudioChunkFormatter
    {
        /// <summary>
        /// Handle incomplete chunks - only return chunks with complete frames
        /// Leave incomplete frame in the buffer for the next chunk
        /// </summary>
        private List<byte> _buffer = new();
        private static readonly byte[] FrameSyncWord = { 0xFF, 0xFB }; //fffb92c4
        private static readonly int[] BitrateTable = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 }; // in kbps
        private static readonly int[] SamplingRateTable = { 44100, 48000, 32000 }; // Sampling rates in Hz
        public Action<byte[]> OnCompleteChunk;

        public void AddChunk(byte[] chunk)
        {
            _buffer.AddRange(chunk);
            ProcessFrames();
        }

        private void ProcessFrames()
        {
            int offset = 0;
            List<byte[]> completeFrames = new();
            while (offset <= _buffer.Count - FrameSyncWord.Length)
            {
                // Search for frame sync word
                int frameStartIndex = IndexOfFrameSyncWord(_buffer, offset);
                if (frameStartIndex == -1)
                    break; // No more frames in this buffer

                // Frame header length is typically 4 bytes
                if (frameStartIndex + 4 > _buffer.Count)
                    break; // Incomplete frame header, need more data

                // Extract frame header
                byte[] header = _buffer.Skip(frameStartIndex).Take(4).ToArray();

                // Determine the frame length (simplified for this example)
                int frameLength = GetFrameLength(header);
                if (frameLength <= 0 || frameStartIndex + frameLength > _buffer.Count)
                {
                    break; // Incomplete frame
                }

                // Extract and process the complete frame
                byte[] frameData = _buffer.Skip(frameStartIndex).Take(frameLength).ToArray();
                completeFrames.Add(frameData);

                // Remove the processed frame from the buffer
                _buffer.RemoveRange(0, frameStartIndex + frameLength);
            }

            using (var stream = new MemoryStream())
            {
                foreach (var array in completeFrames)
                {
                    stream.Write(array, 0, array.Length);
                }

                OnCompleteChunk?.Invoke(stream.ToArray());
            }
        }

        private int IndexOfFrameSyncWord(List<byte> buffer, int startOffset)
        {
            for (int i = startOffset; i <= buffer.Count - FrameSyncWord.Length; i++)
            {
                if (buffer.Skip(i).Take(FrameSyncWord.Length).SequenceEqual(FrameSyncWord))
                {
                    return i;
                }
            }
            return -1;
        }

        public static int GetFrameLength(byte[] header)
        {
            if (header.Length != 4)
                throw new ArgumentException("Invalid frame header length.");

            // Extract bits from the header
            int bitrateIndex = (header[2] >> 4) & 0x0F;
            int samplingRateIndex = (header[2] >> 2) & 0x03;
            int paddingBit = (header[2] >> 1) & 0x01;

            if (bitrateIndex == 0x0F || samplingRateIndex == 0x03)
                throw new InvalidOperationException("Invalid bitrate index or sampling rate index.");

            int bitrate = BitrateTable[bitrateIndex] * 1000; // Convert to bps
            int samplingRate = SamplingRateTable[samplingRateIndex];

            // Calculate frame length
            int frameLength = (int)((144 * bitrate) / (samplingRate + (paddingBit == 1 ? 1 : 0)));

            return frameLength + (paddingBit == 1 ? 1 : 0); // Add 1 byte for padding if needed
        }

        public void Clear()
        {
            _buffer.Clear();
        }
    }
}