using System;

namespace Ballverse.Media
{
    /// <summary>Interleaved float PCM in memory (from <see cref="IMediaBridge.DecodeAudioAsync"/>: 48 kHz stereo).</summary>
    public sealed class PcmBuffer
    {
        /// <summary>Interleaved samples, length = FrameCount × Channels, nominal range −1..1.</summary>
        public float[] Samples { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public int FrameCount { get; }

        public PcmBuffer(float[] samples, int sampleRate, int channels)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (samples.Length % channels != 0) throw new ArgumentException("sample count must be a multiple of the channel count", nameof(samples));
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
            FrameCount = samples.Length / channels;
        }

        public long DurationUs => (long)FrameCount * 1000000L / SampleRate;

        public static PcmBuffer Empty48kStereo() => new PcmBuffer(Array.Empty<float>(), PcmFile.SampleRate, PcmFile.Channels);
    }
}
