using System;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Decoded sample: float PCM at 48 kHz, mono or interleaved stereo (spec §7.8 "samples are decoded to 48 kHz PCM in
    /// memory"). The data array is taken by reference (banks can be up to 24 MB) and must not be modified afterwards.
    /// </summary>
    public sealed class AudioSample
    {
        readonly float[] _data;

        /// <summary>Wraps <paramref name="data"/>; throws on null, a channel count other than 1/2, a partial frame or non-finite values.</summary>
        public AudioSample(float[] data, int channels)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (channels != 1 && channels != 2) throw new ArgumentOutOfRangeException(nameof(channels), "channels must be 1 or 2");
            if (data.Length % channels != 0) throw new ArgumentException("data length is not a whole number of frames", nameof(data));
            for (int i = 0; i < data.Length; i++)
            {
                if (float.IsNaN(data[i]) || float.IsInfinity(data[i])) throw new ArgumentException("sample data contains NaN or infinity", nameof(data));
            }
            _data = data;
            Channels = channels;
            FrameCount = data.Length / channels;
        }

        public int Channels { get; }
        public int FrameCount { get; }
        public ReadOnlySpan<float> Data => _data;

        internal float[] RawData => _data;
    }
}
