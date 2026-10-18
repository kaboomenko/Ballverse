using System;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Duration estimates used to enforce the 180 s cap (spec §3.4). The clip plays at <c>speed</c>; the last
    /// <c>finaleSlowMo.seconds</c> of source clip time additionally play at <c>factor</c>:
    /// <c>out = (L - s) / speed + s / (speed * factor)</c>, with <c>s = min(seconds, L)</c>.
    /// The timeline module owns exact frame math; this is the validator's model of it.
    /// </summary>
    public static class ProjectTiming
    {
        /// <summary>Microseconds of <paramref name="ticks"/> at 60 ticks/s, rounded to nearest.</summary>
        public static long TicksToUs(long ticks)
        {
            return (long)Math.Round(ticks * (double)ProjectLimits.MicrosPerSecond / SimConstants.TickRate, MidpointRounding.AwayFromZero);
        }

        /// <summary>Output (reel) duration of the trimmed clip in microseconds; 0 for an empty or inverted trim.</summary>
        public static long ClipOutputUs(int trimInTick, int trimOutTick, double speed, double slowMoSeconds, double slowMoFactor)
        {
            long lengthTicks = (long)trimOutTick - trimInTick;
            if (lengthTicks <= 0 || !(speed > 0)) return 0;
            double lengthS = lengthTicks / (double)SimConstants.TickRate;
            double s = slowMoSeconds > 0 && slowMoFactor > 0 ? Math.Min(slowMoSeconds, lengthS) : 0.0;
            double outS = (lengthS - s) / speed + (s > 0 ? s / (speed * slowMoFactor) : 0.0);
            return (long)Math.Round(outS * ProjectLimits.MicrosPerSecond, MidpointRounding.AwayFromZero);
        }

        /// <summary>Output duration of the clip of <paramref name="clip"/>.</summary>
        public static long ClipOutputUs(ClipSettings clip)
        {
            var slow = clip.FinaleSlowMo ?? new FinaleSlowMo();
            return ClipOutputUs(clip.TrimInTick, clip.TrimOutTick, clip.Speed, slow.Seconds, slow.Factor);
        }

        /// <summary>Trimmed length of the user video, microseconds (0 when absent or inverted).</summary>
        public static long VideoLengthUs(UserVideoSettings video)
        {
            if (video == null) return 0;
            return Math.Max(0, video.TrimOutUs - video.TrimInUs);
        }

        /// <summary>Reel duration implied by <c>output.durationMode</c> (spec §3.4: Auto = clip, = V, Manual).</summary>
        public static long ReelDurationUs(ProjectDocument doc)
        {
            var output = doc.Output ?? new OutputSettings();
            switch (output.DurationMode)
            {
                case DurationMode.MatchVideo:
                    return VideoLengthUs(doc.UserVideo);
                case DurationMode.Manual:
                    return output.ManualDurationUs ?? 0;
                default:
                    return doc.Clip == null ? 0 : ClipOutputUs(doc.Clip);
            }
        }
    }
}
