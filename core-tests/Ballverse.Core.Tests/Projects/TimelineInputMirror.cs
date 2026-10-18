using System;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Test-only mirror of the argument checks of the Timeline module's <c>ClipTiming</c>, <c>UserVideoTiming</c> and
    /// <c>TimelineSpec</c> constructors (client/Assets/Ballverse/Core/Timeline on the main branch). The Timeline module is not in
    /// this worktree, so its limits are copied here; keep them in sync, or replace this mirror with the real types once both
    /// modules live in one tree. Each check throws <see cref="ArgumentOutOfRangeException"/> like the original.
    /// </summary>
    static class TimelineInputMirror
    {
        // ClipTiming
        public const int MinSpeedPercent = 50;
        public const int MaxSpeedPercent = 200;
        public const int MinSlowMoFactorPercent = 25;
        public const int MaxSlowMoFactorPercent = 50;
        public const int MinSlowMoTicks = 1 * SimConstants.TickRate;
        public const int MaxSlowMoTicks = 3 * SimConstants.TickRate;

        // UserVideoTiming
        public const long MaxSourceDurationUs = 600L * 1000000L;
        public const long MaxAbsOffsetUs = 600L * 1000000L;

        // TimelineSpec / StudioTimeline
        public const long MaxManualDurationUs = 600L * 1000000L;
        public const long MaxSampleTimeUs = SimConstants.MaxDurationSeconds * 1000000L;

        /// <summary>
        /// Builds the timeline input the app derives from a sanitized document (render stream of
        /// <paramref name="availableTicks"/> ticks, <paramref name="fps"/> output) and throws when any constructor would.
        /// </summary>
        public static void Validate(ProjectDocument d, int fps, int availableTicks = SimConstants.MaxDurationTicks)
        {
            var clip = d.Clip ?? throw new ArgumentNullException("clip");
            var slow = clip.FinaleSlowMo ?? throw new ArgumentNullException("clip.finaleSlowMo");
            ValidateClip(clip.TrimInTick, clip.TrimOutTick, availableTicks, Percent(clip.Speed), Ticks(slow.Seconds), Percent(slow.Factor), clip.AfterEnd);

            var v = d.UserVideo;
            if (v != null) ValidateVideo(v.TrimInUs, v.TrimOutUs, v.OffsetUs, v.AfterEnd);

            var o = d.Output ?? throw new ArgumentNullException("output");
            long manual = o.ManualDurationUs ?? 0;
            long? cover = o.Cover != null && o.Cover.PutFirst ? o.Cover.AtUs : (long?)null;
            ValidateSpec(o.DurationMode, fps, manual, cover);
        }

        public static void ValidateClip(int trimIn, int trimOut, int available, int speedPercent, int slowMoTicks, int factorPercent, SimAfterEnd afterEnd)
        {
            if (available < 1 || available > SimConstants.MaxDurationTicks) throw new ArgumentOutOfRangeException("availableTicks", available, null);
            if (trimIn < 0) throw new ArgumentOutOfRangeException("trimInTick", trimIn, null);
            if (trimOut <= trimIn || trimOut > available) throw new ArgumentOutOfRangeException("trimOutTick", trimOut, null);
            if (speedPercent < MinSpeedPercent || speedPercent > MaxSpeedPercent) throw new ArgumentOutOfRangeException("speedPercent", speedPercent, null);
            if (slowMoTicks != 0 && (slowMoTicks < MinSlowMoTicks || slowMoTicks > MaxSlowMoTicks)) throw new ArgumentOutOfRangeException("slowMoTicks", slowMoTicks, null);
            if (factorPercent < MinSlowMoFactorPercent || factorPercent > MaxSlowMoFactorPercent) throw new ArgumentOutOfRangeException("slowMoFactorPercent", factorPercent, null);
            if (!Enum.IsDefined(typeof(SimAfterEnd), afterEnd)) throw new ArgumentOutOfRangeException("afterEnd", afterEnd, null);
        }

        public static void ValidateVideo(long trimInUs, long trimOutUs, long offsetUs, VideoAfterEnd afterEnd)
        {
            if (trimInUs < 0) throw new ArgumentOutOfRangeException("trimInUs", trimInUs, null);
            if (trimOutUs <= trimInUs || trimOutUs > MaxSourceDurationUs) throw new ArgumentOutOfRangeException("trimOutUs", trimOutUs, null);
            if (offsetUs < -MaxAbsOffsetUs || offsetUs > MaxAbsOffsetUs) throw new ArgumentOutOfRangeException("offsetUs", offsetUs, null);
            if (!Enum.IsDefined(typeof(VideoAfterEnd), afterEnd)) throw new ArgumentOutOfRangeException("afterEnd", afterEnd, null);
        }

        public static void ValidateSpec(DurationMode mode, int fps, long manualDurationUs, long? coverTimeUs)
        {
            if (!Enum.IsDefined(typeof(DurationMode), mode)) throw new ArgumentOutOfRangeException("durationMode", mode, null);
            if (fps != 30 && fps != 60) throw new ArgumentOutOfRangeException("fps", fps, null);
            if (mode == DurationMode.Manual && manualDurationUs <= 0) throw new ArgumentOutOfRangeException("manualDurationUs", manualDurationUs, null);
            if (manualDurationUs < 0 || manualDurationUs > MaxManualDurationUs) throw new ArgumentOutOfRangeException("manualDurationUs", manualDurationUs, null);
            if (coverTimeUs.HasValue && (coverTimeUs.Value < 0 || coverTimeUs.Value > MaxSampleTimeUs)) throw new ArgumentOutOfRangeException("coverTimeUs", coverTimeUs, null);
        }

        /// <summary>ClipTiming.PercentFromRatio: throws on non-finite input, rounds half away from zero.</summary>
        static int Percent(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio)) throw new ArgumentOutOfRangeException(nameof(ratio));
            return (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>ClipTiming.TicksFromSeconds: throws on non-finite input, rounds half away from zero.</summary>
        static int Ticks(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            return (int)Math.Round(seconds * SimConstants.TickRate, MidpointRounding.AwayFromZero);
        }
    }
}
