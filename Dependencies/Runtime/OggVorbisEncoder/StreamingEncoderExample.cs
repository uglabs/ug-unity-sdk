
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

// Example usage
/*
// Use a thread safe queue for rawData and converted data
    public ConcurrentQueue<float[]> _micRawDataQueue = new();
    public ConcurrentQueue<byte[]> _micEncodedStreamDataQueue = new();
// Put data in the queue from the audio source as they are ready
    protected virtual void OnAudioSamplesReady(float[] samples)
    {
        _micRawDataQueue.Enqueue(samples);
    }
// Run a task to convert PCM to Vorbis and put the result in a stream
 Task.Run(() =>
        {
            Debug.Log("Start stream conversion");
            StreamingEncoder.ConvertPCMToOggVorbis(_micRawDataQueue, _audioMicEncodedOutputStream, _micEncodedStreamDataQueue, 16000, 1, StreamingEncoder.PcmSample.SixteenBit, 16000, 1, _audioConversionCancellationToken.Token);
        }).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Debug.LogError($"Task failed: {task.Exception?.Flatten().Message}");
            }
        });
// Stop stream
    private async void StopStream()
    {
        while (true)
        {
            Debug.Log($"Wait for complete stream: {_micEncodedStreamDataQueue.Count} {_micRawDataQueue.Count}");
            await Task.Delay(10);
            if (_micEncodedStreamDataQueue.Count > 0) continue;
            if (_micRawDataQueue.Count > 0) continue;
            break;
        }

        _audioConversionCancellationToken?.Cancel();
        // _audioOutStreamCancellationToken?.Cancel();
    }
*/

namespace OggVorbisEncoder.Example
{
    public class StreamingEncoder
    {
        private static readonly int WriteBufferSize = 512; //512; 
        private static readonly int[] SampleRates = { 8000, 11025, 16000, 22050, 32000, 44100 };

        public static void ConvertPCMFile()
        {
            using (Stream inputStream = File.OpenRead(Path.Combine(Application.persistentDataPath, $"raw_pcm.raw"))) //File.OpenRead("raw_pcm.raw"))
            {
                using (Stream outputStream = File.Create(Path.Combine(Application.persistentDataPath, "encoded_stream.ogg")))
                {
                    ConvertPCMToOggVorbis(inputStream, outputStream, null, 16000, 2, PcmSample.SixteenBit, 16000, 1, default);
                }
            }
        }

        public static void ConvertPCMToOggVorbis(ConcurrentQueue<float[]> inputQueue, Stream outputStream, ConcurrentQueue<byte[]> outputQueue, int outputSampleRate, int outputChannels, PcmSample pcmSampleSize, int pcmSampleRate, int pcmChannels, CancellationToken cancellationToken, int? streamSerialNumber = null)
        {
            InitOggStream(outputSampleRate, outputChannels, out OggStream oggStream, out ProcessingState processingState, streamSerialNumber);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested || inputQueue == null)
                {
                    UnityEngine.Debug.Log("Cancelled conversion");
                    break;
                }

                if (!inputQueue.TryDequeue(out float[] samples))
                {
                    Thread.Sleep(5);
                    continue;
                }

                int chunkSize = samples.Length;
                // UnityEngine.Debug.Log("Read chunk from input stream: " + chunkSize + " stream length: " + inputQueue.Count);
                if (chunkSize <= 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                int numPcmSamples = samples.Length;
                float pcmDuraton = numPcmSamples / (float)pcmSampleRate;

                int numOutputSamples = (int)(pcmDuraton * outputSampleRate);

                float[][] outSamples = new float[outputChannels][];

                for (int ch = 0; ch < outputChannels; ch++)
                {
                    outSamples[ch] = new float[numOutputSamples];
                }

                for (int sampleNumber = 0; sampleNumber < numOutputSamples; sampleNumber++)
                {
                    for (int ch = 0; ch < outputChannels; ch++)
                    {
                        int sampleIndex = sampleNumber * pcmChannels;
                        outSamples[ch][sampleNumber] = samples[sampleIndex];
                    }
                }

                FlushPages(oggStream, outputStream, outputQueue, true); //! was false, not flushing everything
                ProcessChunk(outSamples, processingState, oggStream, numOutputSamples);
            }

            FlushPages(oggStream, outputStream, outputQueue, true);
        }

        public static void ConvertPCMToOggVorbis(Stream inputStream, Stream outputStream, ConcurrentQueue<byte[]> q, int outputSampleRate, int outputChannels, PcmSample pcmSampleSize, int pcmSampleRate, int pcmChannels, CancellationToken cancellationToken, int? streamSerialNumber = null)
        {
            byte[] pcm = new byte[WriteBufferSize];

            InitOggStream(outputSampleRate, outputChannels, out OggStream oggStream, out ProcessingState processingState, streamSerialNumber);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested || !inputStream.CanRead)
                {
                    UnityEngine.Debug.Log("Cancelled conversion");
                    break;
                }

                int chunkSize = inputStream.Read(pcm, 0, WriteBufferSize);
                UnityEngine.Debug.Log("Read chunk from input stream: " + chunkSize + " stream length: " + inputStream.Length);
                if (chunkSize <= 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                int numPcmSamples = chunkSize / (int)pcmSampleSize / pcmChannels;
                float pcmDuraton = numPcmSamples / (float)pcmSampleRate;

                int numOutputSamples = (int)(pcmDuraton * outputSampleRate);

                float[][] outSamples = new float[outputChannels][];

                for (int ch = 0; ch < outputChannels; ch++)
                {
                    outSamples[ch] = new float[numOutputSamples];
                }

                for (int sampleNumber = 0; sampleNumber < numOutputSamples; sampleNumber++)
                {
                    float rawSample = 0.0f;

                    for (int ch = 0; ch < outputChannels; ch++)
                    {
                        int sampleIndex = (sampleNumber * pcmChannels) * (int)pcmSampleSize;

                        if (ch < pcmChannels)
                            sampleIndex += ch * (int)pcmSampleSize;

                        switch (pcmSampleSize)
                        {
                            case PcmSample.EightBit:
                                rawSample = ByteToSample(pcm[sampleIndex]);
                                break;
                            default:
                            case PcmSample.SixteenBit:
                                rawSample = ShortToSample((short)(pcm[sampleIndex + 1] << 8 | pcm[sampleIndex]));
                                break;
                        }

                        outSamples[ch][sampleNumber] = rawSample;
                    }
                }

                FlushPages(oggStream, outputStream, q, true); //! was false, not flushing everything
                ProcessChunk(outSamples, processingState, oggStream, numOutputSamples);
            }

            FlushPages(oggStream, outputStream, q, true);
        }

        private static void ProcessChunk(float[][] floatSamples, ProcessingState processingState, OggStream oggStream, int writeBufferSize)
        {
            processingState.WriteData(floatSamples, writeBufferSize, 0);

            while (!oggStream.Finished && processingState.PacketOut(out OggPacket packet))
            {
                oggStream.PacketIn(packet);
            }
            //Debug.Log("[Encoder] ProcessChunk size: " + floatSamples[0].Length);
        }

        private static void InitOggStream(int sampleRate, int channels, out OggStream oggStream, out ProcessingState processingState, int? streamSerialNumber = null)
        {
            Debug.Log("[Encoder] InitOggStream");
            // Stores all the static vorbis bitstream settings
            var info = VorbisInfo.InitVariableBitRate(channels, sampleRate, 0.5f);

            // set up our packet->stream encoder
            // This doesn't really matter - we should just keep the stream going and not restart it
            // We can't concatenate two streams together easily eanyways - we should always have 1 stream/stream index
            // var serial = new System.Random().Next();
            int serial = streamSerialNumber ?? new System.Random().Next();
            oggStream = new OggStream(serial);

            // =========================================================
            // HEADER
            // =========================================================
            // Vorbis streams begin with three headers; the initial header (with
            // most of the codec setup parameters) which is mandated by the Ogg
            // bitstream spec.  The second header holds any comment fields.  The
            // third header holds the bitstream codebook.
            var comments = new Comments();
            comments.AddTag("ARTIST", "TTS");

            var infoPacket = HeaderPacketBuilder.BuildInfoPacket(info);
            var commentsPacket = HeaderPacketBuilder.BuildCommentsPacket(comments);
            var booksPacket = HeaderPacketBuilder.BuildBooksPacket(info);

            oggStream.PacketIn(infoPacket);
            oggStream.PacketIn(commentsPacket);
            oggStream.PacketIn(booksPacket);

            // =========================================================
            // BODY (Audio Data)
            // =========================================================
            processingState = ProcessingState.Create(info);
        }

        static int combinedLength = 0;
        private static void FlushPages(OggStream oggStream, Stream output, ConcurrentQueue<byte[]> q, bool force)
        {
            while (oggStream.PageOut(out OggPage page, force))
            {
                // UnityEngine.Debug.Log("Flush pages: " + page.Body.Length);
                output.Write(page.Header, 0, page.Header.Length);
                output.Write(page.Body, 0, page.Body.Length);
                if (q != null)
                {
                    byte[] combined = new byte[page.Header.Length + page.Body.Length];
                    Array.Copy(page.Header, 0, combined, 0, page.Header.Length);
                    Array.Copy(page.Body, 0, combined, page.Header.Length, page.Body.Length);
                    // UnityEngine.Debug.Log("[Encoder] Flush pages total length: " + combined.Length);
                    q.Enqueue(combined);
                    combinedLength += combined.Length;
                }
            }
            // UnityEngine.Debug.Log("Output length: " + output.Length + " combiined length:" + combinedLength + " queue length:" + q.Count);
        }

        private static float ByteToSample(short pcmValue)
        {
            return pcmValue / 128f;
        }

        private static float ShortToSample(short pcmValue)
        {
            return pcmValue / 32768f;
        }

        public enum PcmSample : int
        {
            EightBit = 1,
            SixteenBit = 2
        }
    }
}