namespace Ballverse.Core.Generation
{
    /// <summary>A reported candidate with its score and director verdicts (spec §2.4 step 4, §2.5).</summary>
    public sealed class CandidateEvaluation
    {
        public int Index { get; }
        public ulong Seed { get; }
        public CandidateRunSummary Summary { get; }
        public SpectacleBreakdown Breakdown { get; }

        /// <summary>The winner equals the forced winner (always true without a forced winner).</summary>
        public bool MatchesWinner { get; }

        /// <summary><see cref="DirectorAnalysis.IsComeback"/> of the run.</summary>
        public bool IsComeback { get; }

        /// <summary>
        /// The session's director goal is met by this candidate: always true for <see cref="DirectorGoal.None"/>; finishTension ≥
        /// <see cref="GenerationSession.LastSecondTensionGoal"/> for LastSecond; <see cref="IsComeback"/> for Comeback (spec §2.5).
        /// </summary>
        public bool GoalMet { get; }

        /// <summary>Not rejected and total ≥ threshold.</summary>
        public bool PassesThreshold { get; }

        public bool IsRejected => Breakdown.IsRejected;

        public CandidateEvaluation(int index, ulong seed, CandidateRunSummary summary, SpectacleBreakdown breakdown,
            bool matchesWinner, bool isComeback, bool goalMet, bool passesThreshold)
        {
            Index = index;
            Seed = seed;
            Summary = summary;
            Breakdown = breakdown;
            MatchesWinner = matchesWinner;
            IsComeback = isComeback;
            GoalMet = goalMet;
            PassesThreshold = passesThreshold;
        }
    }
}
