using System;
using System.Collections.Generic;
using System.IO;
using NLayer;

namespace UG.Services
{
    public class Streamingmp3Decoder
    {
        private MpegFile _mpegFile;
        private MemoryStream _continuousStream;
        private bool _isInitialized;
        private int _sampleRate;
        private int _channels;
        private int _detectedSampleRate = 0;
        private long _lastProcessedPosition = 0;
        private bool _streamCreated = false;

        public Streamingmp3Decoder()
        {
            _continuousStream = new MemoryStream();
            _isInitialized = false;
        }

        /// <summary>
        /// Process chunk by adding to continuous stream and processing new data
        /// </summary>
        /// <param name="mp3Data">Byte array containing MP3 data</param>
        /// <returns>Array of decoded PCM samples as floats</returns>
        public float[] ProcessChunk(byte[] mp3Data)
        {
            if (mp3Data == null || mp3Data.Length == 0)
                return new float[0];

            // Add new data to continuous stream
            _continuousStream.Write(mp3Data, 0, mp3Data.Length);
            _continuousStream.Flush();

            try
            {
                // Create or recreate MpegFile if needed
                if (!_streamCreated)
                {
                    CreateMpegFile();
                }
                else
                {
                    // Reset EOF flag to continue processing
                    ResetEofFlag();
                }

                // Process new data from the stream
                return ProcessNewData();
            }
            catch (Exception ex)
            {
                UGLog.LogError($"[mp3Decoder] Processing failed: {ex.Message}");
                return new float[0];
            }
        }

        /// <summary>
        /// Create MpegFile from the continuous stream
        /// </summary>
        private void CreateMpegFile()
        {
            try
            {
                // Reset stream position to beginning
                _continuousStream.Position = 0;

                // Create MpegFile from the continuous stream
                _mpegFile = new MpegFile(_continuousStream);

                // Initialize decoder properties
                if (!_isInitialized)
                {
                    _detectedSampleRate = _mpegFile.SampleRate;
                    _channels = _mpegFile.Channels;
                    _sampleRate = _detectedSampleRate;

                    UGLog.Log($"[mp3Decoder] First chunk initialized: {_detectedSampleRate}Hz, {_channels} channels");
                    _isInitialized = true;
                }

                _streamCreated = true;
                _lastProcessedPosition = 0;
            }
            catch (Exception ex)
            {
                UGLog.LogError($"[mp3Decoder] Failed to create MpegFile: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset the EOF flag to continue processing
        /// </summary>
        private void ResetEofFlag()
        {
            if (_mpegFile != null)
            {
                _mpegFile._eofFound = false;
            }
        }

        /// <summary>
        /// Process new data that has been added to the stream
        /// </summary>
        private float[] ProcessNewData()
        {
            if (_mpegFile == null || !_isInitialized)
                return new float[0];

            var decodedSamples = new List<float>();

            try
            {
                // Read samples from current position
                var sampleBuffer = new float[1152 * _channels];
                int totalSamplesRead = 0;
                int samplesRead;

                do
                {
                    samplesRead = _mpegFile.ReadSamples(sampleBuffer, 0, sampleBuffer.Length);
                    if (samplesRead > 0)
                    {
                        var actualSamples = new float[samplesRead];
                        Array.Copy(sampleBuffer, actualSamples, samplesRead);
                        decodedSamples.AddRange(actualSamples);
                        totalSamplesRead += samplesRead;
                    }
                }
                while (samplesRead > 0);

                return decodedSamples.ToArray();
            }
            catch (Exception ex)
            {
                return new float[0];
            }
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
            return _detectedSampleRate;
        }

        /// <summary>
        /// Process any remaining buffered data
        /// </summary>
        public float[] ProcessRemainingData()
        {
            if (_mpegFile == null || !_isInitialized)
                return new float[0];

            return ProcessNewData();
        }

        public void Dispose()
        {
            _mpegFile?.Dispose();
            _continuousStream?.Dispose();
        }
    }
}