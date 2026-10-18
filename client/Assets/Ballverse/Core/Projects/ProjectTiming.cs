using System;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Duration model used to enforce the 1-180 s limits (spec §3.4, §3.8) and to show a project's length (spec §3.9).
    /// It mirrors the timeline's exact integer model so that the project duration matches the export within one frame
    /// (spec §3.8 acceptance "длительность = длительность проекта ± 1 кадр"):
    /// <list type="bullet">
    /// <item>speed p and slow-mo factor f are taken in hundredths (<see cref="PercentFromRatio"/>), the slow-mo length in
    /// whole ticks (<see cref="TicksFromSeconds"/>);</item>
    /// <item>with L trimmed ticks, S = min(slow-mo ticks, L) and N = L − S, the clip lasts
    /// N / (60 · p/100) + S / (60 · p·f/10⁴) seconds, rounded up to whole µs;</item>
    /// <item>the reel is the content (Auto = clip, "= V" = trimmed video, Manual = the given length) plus the 0.1 s
    /// cover lead-in when <c>output.cover.putFirst</c> is on (<see cref="CoverLeadInUs"/>; the timeline prepends it).</item>
    /// </list>
    /// </summary>
    public static class ProjectTiming
    {
        /// <summary>Length of the cover lead-in, µs (spec §3.4 "Поставить выбранный кадр первым на 0,1 с").</summary>
        public const long CoverLeadInUs = 100000L;

        /// <summary>Microseconds of <paramref name="ticks"/> at 60 ticks/s, rounded to nearest.</summary>
        public static long TicksToUs(long ticks)
        {
            return (long)Math.Round(ticks * (double)ProjectLimits.MicrosPerSecond / SimConstants.TickRate, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// A ratio such as 1.25 (<c>clip.speed</c>, <c>finaleSlowMo.factor</c>) in hundredths, rounded half away from zero,
        /// as the timeline applies it. Non-finite values give 0.
        /// </summary>
        public static int PercentFromRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || Math.Abs(ratio) > int.MaxValue / 200.0) return 0;
            return (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Seconds (<c>finaleSlowMo.seconds</c>) in whole ticks, rounded half away from zero. Non-finite values give 0.</summary>
        public static int TicksFromSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || Math.Abs(seconds) > int.MaxValue / 120.0) return 0;
            return (int)Math.Round(seconds * SimConstants.TickRate, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Output duration of the trimmed clip in microseconds (rounded up); 0 for an empty or inverted trim or a
        /// non-positive speed. A non-positive slow-mo length or factor disables the slow-mo.
        /// </summary>
        public static long ClipOutputUs(int trimInTick, int trimOutTick, double speed, double slowMoSeconds, double slowMoFactor)
        {
            long length = (long)trimOutTick - trimInTick;
            long p = PercentFromRatio(speed);
            if (length <= 0 || p <= 0) return 0;
            long f = PercentFromRatio(slowMoFactor);
            long slowTicks = TicksFromSeconds(slowMoSeconds);
            if (f <= 0 || slowTicks <= 0)
            {
                f = 100;
                slowTicks = 0;
            }
            long s = Math.Min(slowTicks, length);
            long n = length - s;
            // Units of 1 / (60 · p · f) s: a normal tick lasts 100 · f units, a slowed one 10⁴ units.
            try
            {
                checked
                {
                    long units = n * 100L * f + s * 10000L;
                    long unitsPerSecond = SimConstants.TickRate * p * f;
                    return CeilDiv(units * ProjectLimits.MicrosPerSecond, unitsPerSecond);
                }
            }
            catch (OverflowException)
            {
                // Only reachable with absurd, unsanitized values; an estimate is enough there.
                double outS = n / (SimConstants.TickRate * p / 100.0) + s / (SimConstants.TickRate * p * f / 10000.0);
                return outS * ProjectLimits.MicrosPerSecond >= long.MaxValue ? long.MaxValue : (long)Math.Ceiling(outS * ProjectLimits.MicrosPerSecond);
            }
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

        /// <summary>
        /// Content duration implied by <c>output.durationMode</c> (spec §3.4: Auto = clip, = V, Manual), without the
        /// cover lead-in. <c>output.cover.atUs</c> lies on this timeline.
        /// </summary>
        public static long ContentDurationUs(ProjectDocument doc)
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

        /// <summary>Length of the cover lead-in of <paramref name="doc"/>: <see cref="CoverLeadInUs"/> with <c>putFirst</c>, else 0.</summary>
        public static long CoverUs(ProjectDocument doc)
        {
            var cover = doc.Output?.Cover;
            return cover != null && cover.PutFirst ? CoverLeadInUs : 0;
        }

        /// <summary>
        /// Longest content allowed by the 180 s cap (spec §3.4): 180 s, or 179.9 s when the cover lead-in is prepended.
        /// </summary>
        public static long MaxContentUs(ProjectDocument doc)
        {
            return ProjectLimits.MaxDurationUs - CoverUs(doc);
        }

        /// <summary>Reel (export) duration: <see cref="ContentDurationUs"/> plus the cover lead-in (spec §3.4, §3.8).</summary>
        public static long ReelDurationUs(ProjectDocument doc)
        {
            return ContentDurationUs(doc) + CoverUs(doc);
        }

        static long CeilDiv(long a, long b)
        {
            return (a + b - 1) / b;
        }
    }
}
