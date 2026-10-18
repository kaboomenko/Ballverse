using System;

namespace Ballverse.Media
{
    /// <summary>Time range of the source to decode. Output always starts at the range start (silence-padded if the audio track starts later).</summary>
    public readonly struct AudioDecodeRange
    {
        public long StartUs { get; }
        /// <summary>Length to decode, µs; 0 = until the end of the track.</summary>
        public long DurationUs { get; }

        public AudioDecodeRange(long startUs, long durationUs)
        {
            if (startUs < 0) throw new ArgumentOutOfRangeException(nameof(startUs));
            if (durationUs < 0) throw new ArgumentOutOfRangeException(nameof(durationUs));
            StartUs = startUs;
            DurationUs = durationUs;
        }

        public static AudioDecodeRange Whole => new AudioDecodeRange(0, 0);
    }
}
