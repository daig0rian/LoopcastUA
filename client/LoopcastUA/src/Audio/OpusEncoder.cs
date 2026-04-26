using System;
using Concentus;
using Concentus.Enums;

namespace LoopcastUA.Audio
{
    internal sealed class OpusEncoder : IDisposable
    {
        private const int SampleRate = 48000;
        private const int Channels = 1;
        private const int FrameMs = 20;
        public const int FrameSamples = SampleRate * FrameMs / 1000; // 960

        private readonly IOpusEncoder _encoder;
        private readonly byte[] _outputBuffer = new byte[4000];

        public OpusEncoder(int bitrate = 48000, int complexity = 5)
        {
            _encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
            _encoder.Bitrate = bitrate;
            _encoder.Complexity = complexity;
        }

        public ArraySegment<byte> Encode(float[] monoSamples)
        {
            if (monoSamples.Length != FrameSamples)
                throw new ArgumentException($"Expected {FrameSamples} samples, got {monoSamples.Length}");

            int encoded = _encoder.Encode(
                new ReadOnlySpan<float>(monoSamples),
                FrameSamples,
                new Span<byte>(_outputBuffer),
                _outputBuffer.Length);

            return new ArraySegment<byte>(_outputBuffer, 0, encoded);
        }

        public void Dispose() { }
    }
}
