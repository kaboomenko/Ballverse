using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Timing of the user video V on the studio timeline (spec §3.4). Immutable.
    /// <para>
    /// The trimmed video is the half-open source range [<see cref="TrimInUs"/>, <see cref="TrimOutUs"/>) in source
    /// presentation time (µs). <see cref="OffsetUs"/> is where the trimmed video starts relative to the start of the
    /// trimmed clip S on the shared timeline: positive = V starts later than S, negative = V starts earlier. The output
    /// starts at the earlier of the two track starts, so neither track's head is ever dropped (see
    /// <see cref="StudioTimeline"/>).
    /// </para>
    /// <para>
    /// Sign convention (decision): V local time = timeline time − offset, i.e. at clip-relative time t the video shows
    /// source pts <c>TrimInUs + (t − OffsetUs)</c>. This is the opposite sign of the pseudo-formula
    /// <c>ReadFrameAt(t + offset)</c> in spec §7.5 step b; project.json <c>userVideo.offsetUs</c> (spec §7.4) uses the
    /// convention of this class.
    /// </para>
    /// </summary>
    public sealed class UserVideoTiming
    {
        /// <summary>Longest accepted source video, µs: 10 min (spec §3.2 "до 10 мин на входе").</summary>
        public const long MaxSourceDurationUs = 600L * 1000000L;

        /// <summary>
        /// Largest accepted |offset|, µs (10 min). Only guards against garbage: any |offset| above 180 s already keeps one
        /// track off the output entirely.
        /// </summary>
        public const long MaxAbsOffsetUs = 600L * 1000000L;

        public long TrimInUs { get; }
        /// <summary>End of the trimmed video (exclusive), at most <see cref="MaxSourceDurationUs"/>.</summary>
        public long TrimOutUs { get; }
        public long OffsetUs { get; }
        public VideoAfterEnd AfterEnd { get; }

        /// <summary>
        /// Trimmed video length, µs (always ≥ 1). A length above 180 s is accepted; the 180 s output clamp keeps the
        /// fragment that reaches the file ≤ 180 s (spec §3.2).
        /// </summary>
        public long LengthUs => TrimOutUs - TrimInUs;

        /// <summary>Source pts shown as the video's last frame (<c>TrimOutUs - 1</c>, the end is exclusive).</summary>
        public long LastPtsUs => TrimOutUs - 1;

        public UserVideoTiming(long trimInUs, long trimOutUs, long offsetUs = 0, VideoAfterEnd afterEnd = VideoAfterEnd.Freeze)
        {
            if (trimInUs < 0)
                throw new ArgumentOutOfRangeException(nameof(trimInUs), trimInUs, "trimInUs must be >= 0.");
            if (trimOutUs <= trimInUs || trimOutUs > MaxSourceDurationUs)
                throw new ArgumentOutOfRangeException(nameof(trimOutUs), trimOutUs, "trimOutUs must be in (trimInUs, 10 min] (spec §3.2).");
            if (offsetUs < -MaxAbsOffsetUs || offsetUs > MaxAbsOffsetUs)
                throw new ArgumentOutOfRangeException(nameof(offsetUs), offsetUs, "offsetUs out of range.");
            if (!Enum.IsDefined(typeof(VideoAfterEnd), afterEnd))
                throw new ArgumentOutOfRangeException(nameof(afterEnd), afterEnd, "Unknown VideoAfterEnd.");

            TrimInUs = trimInUs;
            TrimOutUs = trimOutUs;
            OffsetUs = offsetUs;
            AfterEnd = afterEnd;
        }

        /// <summary>
        /// Moves an offset by whole output frames for the ±1 frame buttons (spec §3.4 "точная подстройка ±1 кадр").
        /// The offset is first snapped to the nearest frame of the <paramref name="fps"/> grid, then moved by
        /// <paramref name="frames"/>, and the result is that grid time rounded to the nearest µs (halves away from zero),
        /// so repeated nudges never accumulate rounding (60 nudges at 60 fps = exactly 1 s). Clamped to ±<see cref="MaxAbsOffsetUs"/>.
        /// </summary>
        public static long NudgeOffsetUs(long offsetUs, int frames, int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            if (offsetUs < -MaxAbsOffsetUs || offsetUs > MaxAbsOffsetUs)
                throw new ArgumentOutOfRangeException(nameof(offsetUs), offsetUs, "offsetUs out of range.");
            long k = RoundDiv(offsetUs * fps, StudioTimeline.MicrosPerSecond) + frames;
            long result = RoundDiv(k * StudioTimeline.MicrosPerSecond, fps);
            if (result > MaxAbsOffsetUs) return MaxAbsOffsetUs;
            if (result < -MaxAbsOffsetUs) return -MaxAbsOffsetUs;
            return result;
        }

        static long RoundDiv(long a, long b)
        {
            // b > 0; rounds half away from zero.
            return a >= 0 ? (a + b / 2) / b : -((-a + b / 2) / b);
        }
    }
}
