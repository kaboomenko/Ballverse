using System;
using System.Collections.Generic;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Spectacle score of a candidate run (spec §2.4 step 4). Pure functions of their inputs; no state.
    /// </summary>
    public static class SpectacleScorer
    {
        /// <summary>Gaps strictly longer than this (3 s) count as stagnation (spec §2.4, checklist §8.6).</summary>
        public const int StagnationGapTicks = 3 * SimConstants.TickRate;

        /// <summary>fitDuration reaches 0 at ±30% of the target (spec §2.4).</summary>
        public const double FitTolerance = 0.3;

        static readonly double s_ln2 = Math.Log(2.0);

        /// <summary>
        /// Scores a candidate (spec §2.4 step 4). <paramref name="targetDurationTicks"/> null means "unlimited" (or an interactive
        /// mode, whose duration is not selected): fitDuration is then 1 for every candidate. Stuck or aborted candidates
        /// get their metrics computed but are marked <see cref="SpectacleBreakdown.Rejected"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">summary or weights is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">target is not positive.</exception>
        public static SpectacleBreakdown Score(CandidateRunSummary summary, int? targetDurationTicks, SpectacleWeights weights, DensityRange densityRange)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (weights == null) throw new ArgumentNullException(nameof(weights));

            double fit = FitDuration(summary, targetDurationTicks);
            double density = EventDensity(summary.SignificantEventTicks.Count, summary.DurationTicks, densityRange);
            double tension = summary.FinishTension;
            double coverage = summary.Coverage;
            double stagnation = Stagnation(summary.SignificantEventTicks, summary.DurationTicks);
            double total = weights.W1 * fit + weights.W2 * density + weights.W3 * tension + weights.W4 * coverage - weights.W5 * stagnation;

            RejectReason rejected = RejectReason.None;
            if (summary.Stuck) rejected = RejectReason.Stuck;
            else if (!summary.Finished && !summary.EndedByLimit) rejected = RejectReason.Incomplete;

            return new SpectacleBreakdown(fit, density, tension, coverage, stagnation, total, rejected);
        }

        /// <summary>
        /// fitDuration of a run. A run that hit the headless limit without a finale has no finale to fit, so it gets 0 when a
        /// target is set (spec §2.4: "how close the finale is to the target"). No target → 1.
        /// </summary>
        public static double FitDuration(CandidateRunSummary summary, int? targetDurationTicks)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (targetDurationTicks == null) return 1.0;
            if (summary.EndedByLimit)
            {
                ValidateTarget(targetDurationTicks.Value);
                return 0.0;
            }
            return FitDuration(summary.DurationTicks, targetDurationTicks);
        }

        /// <summary>
        /// 1 at exactly the target, linear to 0 at ±30%, 0 beyond; null target → 1 (spec §2.4).
        /// Computed with an exact integer numerator so the ±30% edge is exactly 0.
        /// </summary>
        public static double FitDuration(int durationTicks, int? targetDurationTicks)
        {
            if (targetDurationTicks == null) return 1.0;
            int target = targetDurationTicks.Value;
            ValidateTarget(target);
            // 1 - |d - t| / (0.3 t) == (3t - 10|d - t|) / 3t
            long deviation = Math.Abs((long)durationTicks - target);
            long numerator = 3L * target - 10L * deviation;
            if (numerator <= 0) return 0.0;
            return numerator / (3.0 * target);
        }

        /// <summary>Events per second over the run; 0 for an empty run.</summary>
        public static double EventsPerSecond(int eventCount, int durationTicks)
        {
            if (eventCount < 0) throw new ArgumentOutOfRangeException(nameof(eventCount));
            if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            if (durationTicks == 0) return 0.0;
            return eventCount * (double)SimConstants.TickRate / durationTicks;
        }

        /// <summary>eventDensity metric for a run with <paramref name="eventCount"/> events over <paramref name="durationTicks"/>.</summary>
        public static double EventDensity(int eventCount, int durationTicks, DensityRange range)
        {
            return DensityScore(EventsPerSecond(eventCount, durationTicks), range);
        }

        /// <summary>
        /// 1 inside [min, max]; outside, a smooth log-scale falloff <c>2^(−log2(r)²)</c> where r is the factor by which the rate
        /// misses the nearest bound: ×2 off → 0.5, ×4 off → 0.0625, rate 0 → 0. Continuous with zero slope at the bounds.
        /// </summary>
        public static double DensityScore(double eventsPerSecond, DensityRange range)
        {
            if (double.IsNaN(eventsPerSecond) || eventsPerSecond < 0.0) throw new ArgumentOutOfRangeException(nameof(eventsPerSecond));
            double ratio;
            if (eventsPerSecond < range.MinPerSecond)
            {
                if (eventsPerSecond <= 0.0) return 0.0;
                ratio = range.MinPerSecond / eventsPerSecond;
            }
            else if (eventsPerSecond > range.MaxPerSecond)
            {
                if (double.IsPositiveInfinity(eventsPerSecond)) return 0.0;
                ratio = eventsPerSecond / range.MaxPerSecond;
            }
            else
            {
                return 1.0;
            }
            double ln = Math.Log(ratio);
            return Math.Exp(-ln * ln / s_ln2);
        }

        /// <summary>
        /// Fraction of the run covered by gaps longer than 3 s without significant events (spec §2.4). Gaps are measured from tick 0
        /// to the first event, between events and from the last event to the end; a qualifying gap counts with its full length.
        /// Empty run → 0. Event ticks must be sorted and within 0..durationTicks.
        /// </summary>
        public static double Stagnation(IReadOnlyList<int> sortedEventTicks, int durationTicks)
        {
            if (sortedEventTicks == null) throw new ArgumentNullException(nameof(sortedEventTicks));
            if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            if (durationTicks == 0) return 0.0;
            long stagnant = 0;
            int prev = 0;
            for (int i = 0; i < sortedEventTicks.Count; i++)
            {
                int t = sortedEventTicks[i];
                if (t < prev || t > durationTicks)
                    throw new ArgumentException("Event ticks must be sorted and within 0..durationTicks", nameof(sortedEventTicks));
                int gap = t - prev;
                if (gap > StagnationGapTicks) stagnant += gap;
                prev = t;
            }
            int tail = durationTicks - prev;
            if (tail > StagnationGapTicks) stagnant += tail;
            return stagnant / (double)durationTicks;
        }

        static void ValidateTarget(int target)
        {
            if (target <= 0) throw new ArgumentOutOfRangeException("targetDurationTicks", "Target duration must be positive");
        }
    }
}
