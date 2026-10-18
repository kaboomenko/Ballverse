using System;
using System.Collections.Generic;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Acceptance metrics of Requirement 3 for tests, the balance probe and telemetry (spec §2.4 "Acceptance", §8.5).
    /// </summary>
    public static class GenerationAcceptance
    {
        /// <summary>Allowed deviation from the target: ±3 s.</summary>
        public const int DurationToleranceTicks = 3 * SimConstants.TickRate;

        /// <summary>Required share of generations finishing within target ± 3 s (automatic modes with a finale, target 30 s).</summary>
        public const double RequiredDurationHitRate = 0.8;

        /// <summary>Required share of generations ready within the tier time budget (95% on tier B).</summary>
        public const double RequiredTimeHitRate = 0.95;

        /// <summary>
        /// True when the run reached its finale within target ± 3 s. A run stopped by the target × 1.3 limit never counts; a run
        /// that reached the global 180 s cap has a forced finale there (spec §2.1, <see cref="CandidateRunSummary.ReachedFinale"/>).
        /// </summary>
        public static bool FinishedWithinTarget(CandidateRunSummary summary, int targetDurationTicks)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (targetDurationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(targetDurationTicks));
            return summary.ReachedFinale && Math.Abs(summary.DurationTicks - targetDurationTicks) <= DurationToleranceTicks;
        }

        /// <summary>
        /// Fraction of generations (their selected candidates' summaries) whose finale lies within target ± 3 s (spec §2.4: ≥ 80%).
        /// Null entries (generations that chose nothing) count as misses. Empty input → 0.
        /// </summary>
        public static double FractionFinishedWithinTarget(IReadOnlyList<CandidateRunSummary> selected, int targetDurationTicks)
        {
            if (selected == null) throw new ArgumentNullException(nameof(selected));
            if (targetDurationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(targetDurationTicks));
            if (selected.Count == 0) return 0.0;
            int hits = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] != null && FinishedWithinTarget(selected[i], targetDurationTicks)) hits++;
            }
            return hits / (double)selected.Count;
        }

        /// <summary>Fraction of completed generations whose selection finished within target ± 3 s; null results count as misses.</summary>
        public static double FractionFinishedWithinTarget(IReadOnlyList<GenerationResult> results, int targetDurationTicks)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var summaries = new CandidateRunSummary[results.Count];
            for (int i = 0; i < summaries.Length; i++) summaries[i] = results[i]?.Selected?.Summary;
            return FractionFinishedWithinTarget(summaries, targetDurationTicks);
        }

        /// <summary>Fraction of generations whose total time is within <paramref name="budgetMs"/> (spec §2.4: 95% within 2.5 s on tier B). Empty → 0.</summary>
        public static double FractionWithinTime(IReadOnlyList<long> elapsedMs, long budgetMs)
        {
            if (elapsedMs == null) throw new ArgumentNullException(nameof(elapsedMs));
            if (budgetMs <= 0) throw new ArgumentOutOfRangeException(nameof(budgetMs));
            if (elapsedMs.Count == 0) return 0.0;
            int hits = 0;
            for (int i = 0; i < elapsedMs.Count; i++)
            {
                if (elapsedMs[i] <= budgetMs) hits++;
            }
            return hits / (double)elapsedMs.Count;
        }
    }
}
