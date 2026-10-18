using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Timing of the user video V on the studio timeline (spec §3.4). Immutable.
    /// <para>
    /// The trimmed video is the half-open source range [<see cref="TrimInUs"/>, <see cref="TrimOutUs"/>) in source
    /// presentation time (µs). <see cref="OffsetUs"/> is where the trimmed video starts on the output timeline, relative
    /// to the clip start (output time 0). Negative = the video started earlier, so its first |offset| µs are not shown;
    /// positive = the video starts later. At output time t the video shows source pts <c>TrimInUs + (t - OffsetUs)</c>
    /// (spec §7.5 step b).
    /// </para>
    /// </summary>
    public sealed class UserVideoTiming
    {
        /// <summary>Largest accepted |offset|: one maximal video length (offsets beyond it can never show a frame).</summary>
        public const long MaxAbsOffsetUs = 3600L * 1000000L;

        public long TrimInUs { get; }
        /// <summary>End of the trimmed video (exclusive).</summary>
        public long TrimOutUs { get; }
        public long OffsetUs { get; }
        public VideoAfterEnd AfterEnd { get; }

        /// <summary>Trimmed video length, µs (always ≥ 1).</summary>
        public long LengthUs => TrimOutUs - TrimInUs;

        /// <summary>Output time where the trimmed video ends: <c>OffsetUs + LengthUs</c> (may be ≤ 0).</summary>
        public long EndOnOutputUs => OffsetUs + LengthUs;

        /// <summary>Source pts shown as the video's last frame (<c>TrimOutUs - 1</c>, the end is exclusive).</summary>
        public long LastPtsUs => TrimOutUs - 1;

        public UserVideoTiming(long trimInUs, long trimOutUs, long offsetUs = 0, VideoAfterEnd afterEnd = VideoAfterEnd.Freeze)
        {
            if (trimInUs < 0)
                throw new ArgumentOutOfRangeException(nameof(trimInUs), trimInUs, "trimInUs must be >= 0.");
            if (trimOutUs <= trimInUs || trimOutUs > MaxAbsOffsetUs)
                throw new ArgumentOutOfRangeException(nameof(trimOutUs), trimOutUs, "trimOutUs must be in (trimInUs, 1 h].");
            if (offsetUs < -MaxAbsOffsetUs || offsetUs > MaxAbsOffsetUs)
                throw new ArgumentOutOfRangeException(nameof(offsetUs), offsetUs, "offsetUs out of range.");
            if (!Enum.IsDefined(typeof(VideoAfterEnd), afterEnd))
                throw new ArgumentOutOfRangeException(nameof(afterEnd), afterEnd, "Unknown VideoAfterEnd.");

            TrimInUs = trimInUs;
            TrimOutUs = trimOutUs;
            OffsetUs = offsetUs;
            AfterEnd = afterEnd;
        }
    }
}
