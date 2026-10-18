namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Outcome of a generation (spec §2.4 step 5, §2.5) plus the <c>sim_generate</c> telemetry fields
    /// (<c>candidates</c>, <c>ms</c>, <c>quality_ok</c>, <c>has_forced_winner</c>; spec §9.2).
    /// </summary>
    public sealed class GenerationResult
    {
        public GenerationOutcome Outcome { get; }

        /// <summary>The chosen candidate; null for WinnerNotFound, NoViableCandidate and Cancelled.</summary>
        public CandidateEvaluation Selected { get; }

        /// <summary>For WinnerNotFound: the candidate "Take nearest" would choose, or -1 when every candidate was rejected.</summary>
        public int NearestCandidateIndex { get; }

        public int CandidatesEvaluated { get; }
        public long ElapsedMs { get; }
        public bool BudgetDoubled { get; }
        public int SearchLongerCount { get; }
        public bool HasForcedWinner { get; }

        /// <summary>The director goal was achieved by the selected candidate (always true for <see cref="DirectorGoal.None"/>).</summary>
        public bool GoalMet { get; }

        public GenerationResult(GenerationOutcome outcome, CandidateEvaluation selected, int nearestCandidateIndex, int candidatesEvaluated,
            long elapsedMs, bool budgetDoubled, int searchLongerCount, bool hasForcedWinner, bool goalMet)
        {
            Outcome = outcome;
            Selected = selected;
            NearestCandidateIndex = nearestCandidateIndex;
            CandidatesEvaluated = candidatesEvaluated;
            ElapsedMs = elapsedMs;
            BudgetDoubled = budgetDoubled;
            SearchLongerCount = searchLongerCount;
            HasForcedWinner = hasForcedWinner;
            GoalMet = goalMet;
        }

        public int SelectedIndex => Selected?.Index ?? -1;

        public ulong SelectedSeed => Selected?.Seed ?? 0UL;

        /// <summary>A candidate was chosen and it passes the quality threshold (<c>quality_ok</c> in <c>sim_generate</c>).</summary>
        public bool QualityOk => Selected != null && Selected.PassesThreshold;

        /// <summary>
        /// UI hint "try a longer duration" (spec §2.4 step 5): the generation is over and nothing that passes the threshold
        /// was chosen (BelowThreshold, NoViableCandidate, or a NearestWinner below the threshold).
        /// </summary>
        public bool QualityBelowThreshold =>
            (Outcome == GenerationOutcome.BelowThreshold || Outcome == GenerationOutcome.NoViableCandidate
                || Outcome == GenerationOutcome.NearestWinner) && !QualityOk;

        /// <summary>The selected candidate's winner is the forced winner (true when no winner was forced and something was chosen).</summary>
        public bool WinnerMatched => Selected != null && Selected.MatchesWinner;
    }
}
