namespace Ballverse.Core.Generation
{
    /// <summary>A reported candidate with its score and director verdicts (spec §2.4 step 4, §2.5).</summary>
    public sealed class CandidateEvaluation
    {
        public int Index { get; }
        public ulong Seed { get; }
        public CandidateRunSummary Summary { get; }
        public SpectacleBreakdown Breakdown { get; }

        /// <summary><see cref="SpectacleBreakdown.Total"/> plus the director-goal bonus; used to rank candidates.</summary>
        public double SelectionScore { get; }

        /// <summary>The winner equals the forced winner (always true without a forced winner).</summary>
        public bool MatchesWinner { get; }

        public bool IsComeback { get; }

        /// <summary>Not rejected and total ≥ threshold.</summary>
        public bool PassesThreshold { get; }

        public bool IsRejected => Breakdown.IsRejected;

        public CandidateEvaluation(int index, ulong seed, CandidateRunSummary summary, SpectacleBreakdown breakdown,
            double selectionScore, bool matchesWinner, bool isComeback, bool passesThreshold)
        {
            Index = index;
            Seed = seed;
            Summary = summary;
            Breakdown = breakdown;
            SelectionScore = selectionScore;
            MatchesWinner = matchesWinner;
            IsComeback = isComeback;
            PassesThreshold = passesThreshold;
        }
    }
}
