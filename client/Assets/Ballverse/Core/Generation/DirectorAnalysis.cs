using System;

namespace Ballverse.Core.Generation
{
    /// <summary>Director predicates over a candidate run (spec §2.5). Pure functions.</summary>
    public static class DirectorAnalysis
    {
        /// <summary>
        /// Comeback (spec §2.5 "the winner was last in the first half"). The summary only carries per-second samples, so:
        /// there is a winner, at least 2 participants and at least one sample in the first half (sample i is at tick (i + 1) × 60,
        /// first half = ticks ≤ duration / 2); the winner never led in any first-half sample; and, when the mode reports
        /// <see cref="CandidateRunSummary.LastPlaceBySecond"/>, the winner was in last place in at least one first-half sample.
        /// With 2 participants "never led" already means "was last".
        /// </summary>
        public static bool IsComeback(CandidateRunSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            int winner = summary.WinnerIndex;
            if (winner < 0 || summary.ParticipantCount < 2) return false;

            int firstHalfSamples = FirstHalfSampleCount(summary.DurationTicks);
            int leaderSamples = Math.Min(firstHalfSamples, summary.LeaderBySecond.Count);
            if (leaderSamples == 0) return false;
            for (int i = 0; i < leaderSamples; i++)
            {
                if (summary.LeaderBySecond[i] == winner) return false;
            }

            if (summary.LastPlaceBySecond.Count == 0) return true;
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
