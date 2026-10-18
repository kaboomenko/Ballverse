using System;

namespace Ballverse.Core.Generation
{
    /// <summary>Director predicates over a candidate run (spec §2.5). Pure functions.</summary>
    public static class DirectorAnalysis
    {
        /// <summary>
        /// Comeback (spec §2.5 "the winner was last in the first half"). The summary only carries per-second samples (sample i is
        /// at tick (i + 1) × 60, first half = ticks ≤ duration / 2), so a comeback needs a winner, at least 2 participants, the
        /// winner never leading in a first-half sample, and positive evidence that the winner was last in a first-half sample:
        /// <list type="bullet">
        /// <item>with <see cref="CandidateRunSummary.LastPlaceBySecond"/> reported: the winner is the last-place sample at least once;</item>
        /// <item>without it and exactly 2 participants: the other participant led at least once (a -1 "no clear leader"
        /// sample is a tie, not evidence);</item>
        /// <item>without it and more than 2 participants: never a comeback (leading samples cannot tell 2nd place from last),
        /// so race and battle modes with more than 2 participants must report LastPlaceBySecond.</item>
        /// </list>
        /// </summary>
        public static bool IsComeback(CandidateRunSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            int winner = summary.WinnerIndex;
            if (winner < 0 || summary.ParticipantCount < 2) return false;

            int firstHalfSamples = FirstHalfSampleCount(summary.DurationTicks);
            int leaderSamples = Math.Min(firstHalfSamples, summary.LeaderBySecond.Count);
            bool otherLed = false;
            for (int i = 0; i < leaderSamples; i++)
            {
                int leader = summary.LeaderBySecond[i];
                if (leader == winner) return false;
                if (leader >= 0) otherLed = true;
            }

            if (summary.LastPlaceBySecond.Count == 0)
                return summary.ParticipantCount == 2 && otherLed;
            int lastSamples = Math.Min(firstHalfSamples, summary.LastPlaceBySecond.Count);
            for (int i = 0; i < lastSamples; i++)
            {
                if (summary.LastPlaceBySecond[i] == winner) return true;
            }
            return false;
        }

        /// <summary>Number of once-per-second samples that fall into the first half of a run: (i + 1) × 60 ≤ duration / 2.</summary>
        public static int FirstHalfSampleCount(int durationTicks)
        {
            if (durationTicks < 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            return durationTicks / 2 / SimConstants.TickRate;
        }

        /// <summary>True when the run's winner is <paramref name="forcedWinner"/> (spec §2.5); a run without a winner never matches.</summary>
        public static bool MatchesWinner(CandidateRunSummary summary, int forcedWinner)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            return forcedWinner >= 0 && summary.WinnerIndex == forcedWinner;
        }
    }
}
