using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Settings of one press of "🎲 Generate" (spec §2.4, §2.5). A mutable bag; <see cref="GenerationSession"/> validates and
    /// copies it on construction, so later changes do not affect a running session.
    /// </summary>
    public sealed class GenerationSessionOptions
    {
        /// <summary>No forced winner (the "any" choice of <c>forcedWinner</c>, spec §2.2).</summary>
        public const int NoForcedWinner = -1;

        /// <summary>
        /// Largest accepted <see cref="Budget"/> candidate count. Far above any tier budget (Remote Config allows ≤ 64); it only keeps
        /// the forced-winner multiplier (≤ ×4) and the limit updates far from <see cref="int"/> overflow.
        /// </summary>
        public const int MaxBudgetCandidates = 65536;

        /// <summary>Largest accepted <see cref="Budget"/> time and <see cref="ForcedWinnerTimeMs"/>, 1 h (same purpose as <see cref="MaxBudgetCandidates"/>).</summary>
        public const long MaxBudgetTimeMs = 3600000;

        /// <summary>Shortest target duration, 10 s (spec §2.2 <c>targetDuration</c> 10–180 s).</summary>
        public const int MinTargetDurationTicks = 10 * SimConstants.TickRate;

        /// <summary>Seed of the session; candidate seeds derive from it (<see cref="CandidateSeeds"/>).</summary>
        public ulong SessionSeed { get; set; }

        /// <summary>
        /// Optional mode. When set, the mode drives validation: an interactive-only mode (catalog <see cref="ModeInfo.Interactive"/>
        /// without <see cref="ModeInfo.HasAutoControl"/>) makes the session interactive even if <see cref="Interactive"/> is false
        /// (spec §2.4); forced winner needs a mode with a winner; director goals are limited to race and battle
        /// (<see cref="SupportsDirectorGoals"/>, spec §2.5).
        /// </summary>
        public ModeCode? Mode { get; set; }

        /// <summary>Base budget before the forced-winner multiplier and the one-time doubling (spec §2.4 table).</summary>
        public GenerationBudget Budget { get; set; } = GenerationBudget.TierB;

        /// <summary>Minimum total score for a candidate to pass (spec §2.4 step 5).</summary>
        public double QualityThreshold { get; set; }

        /// <summary>Target duration in ticks, <see cref="MinTargetDurationTicks"/>..MaxDurationTicks (spec §2.2 <c>targetDuration</c>, 10–180 s), or null for "unlimited".</summary>
        public int? TargetDurationTicks { get; set; }

        /// <summary>Participant that must win, or <see cref="NoForcedWinner"/> (spec §2.5).</summary>
        public int ForcedWinner { get; set; } = NoForcedWinner;

        /// <summary>Number of participants; required (≥ 2) with a forced winner, sets the budget multiplier (spec §2.5).</summary>
        public int ParticipantCount { get; set; }

        public DirectorGoal Goal { get; set; }

        /// <summary>
        /// Interactive session (a player controls the run): only the arena is generated, duration and winner are not selected
        /// (spec §2.4). Implied by an interactive-only <see cref="Mode"/>; for a mode with both controls (M05) the caller sets it
        /// when the player control is chosen. Setting it for a mode without an interactive control is rejected.
        /// </summary>
        public bool Interactive { get; set; }

        public SpectacleWeights Weights { get; set; } = SpectacleWeights.Default;

        public DensityRange Density { get; set; } = new DensityRange(1.0, 8.0);

        /// <summary>
        /// Time budget of one phase with a forced winner, in ms. Null (default) keeps <see cref="Budget"/>'s time: spec §2.5 only
        /// multiplies the candidate budget (×N, <see cref="GenerationSession.BudgetMultiplier"/>), and the tier time budget
        /// (spec §2.4 table, acceptance "≤ 2,5 с on tier B") stays, so the extra candidates come from parallel runs. Set it to
        /// opt in to a longer forced-winner phase (e.g. <c>Budget.TimeMs × BudgetMultiplier</c>). Open question for the owner:
        /// whether <c>generation_budget_by_tier</c> (spec §9.3) gets a forced-winner time column; until then the spec default applies.
        /// </summary>
        public long? ForcedWinnerTimeMs { get; set; }

        /// <summary>
        /// Options for a mode from the profile and budget tables (spec §2.4, §9.3): weights, density, threshold, interactive flag
        /// and the tier budget. Forced winner, participants and goal are left for the caller.
        /// </summary>
        public static GenerationSessionOptions ForMode(ModeCode mode, DeviceTier tier, ulong sessionSeed, int? targetDurationTicks,
            SpectacleProfileTable profiles = null, GenerationBudgetTable budgets = null)
        {
            ModeInfo info = ModeCatalog.Get(mode);
            SpectacleProfile profile = (profiles ?? SpectacleProfileTable.Default).Get(mode);
            return new GenerationSessionOptions
            {
                SessionSeed = sessionSeed,
                Mode = mode,
                Budget = (budgets ?? GenerationBudgetTable.Default).Get(tier),
                QualityThreshold = profile.QualityThreshold,
                TargetDurationTicks = targetDurationTicks,
                Interactive = info.Interactive && !info.HasAutoControl,
                Weights = profile.Weights,
                Density = profile.Density,
            };
        }

        /// <summary>Director goals (last-second win, comeback) apply to race and battle only: M03, M06 (spec §2.5).</summary>
        public static bool SupportsDirectorGoals(ModeCode mode)
        {
            return mode == ModeCode.M03 || mode == ModeCode.M06;
        }

        /// <summary>The session is interactive: <see cref="Interactive"/> or an interactive-only <see cref="Mode"/> (spec §2.4).</summary>
        internal bool EffectiveInteractive => Interactive || (Mode.HasValue && !ModeCatalog.Get(Mode.Value).HasAutoControl);

        internal GenerationSessionOptions Clone()
        {
            return (GenerationSessionOptions)MemberwiseClone();
        }

        internal void Validate()
        {
            if (Budget.Candidates < 1 || Budget.TimeMs < 1) throw new ArgumentException("Budget must be at least 1 candidate and 1 ms", nameof(Budget));
            // Upper bounds keep every limit update (doubling, forced-winner multiplier, "Search longer") far from overflow.
            if (Budget.Candidates > MaxBudgetCandidates || Budget.TimeMs > MaxBudgetTimeMs)
                throw new ArgumentException("Budget must be at most " + MaxBudgetCandidates + " candidates and " + MaxBudgetTimeMs + " ms", nameof(Budget));
            if (double.IsNaN(QualityThreshold) || double.IsInfinity(QualityThreshold))
                throw new ArgumentException("Quality threshold must be finite", nameof(QualityThreshold));
            if (TargetDurationTicks.HasValue
                && (TargetDurationTicks.Value < MinTargetDurationTicks || TargetDurationTicks.Value > SimConstants.MaxDurationTicks))
                throw new ArgumentException("Target duration must be within 10..180 s (spec §2.2)", nameof(TargetDurationTicks));
            if (Weights == null) throw new ArgumentException("Weights are required", nameof(Weights));
            if (!DensityRange.IsValid(Density.MinPerSecond, Density.MaxPerSecond))
                throw new ArgumentException("Density range is invalid", nameof(Density));
            if (ForcedWinnerTimeMs.HasValue && (ForcedWinnerTimeMs.Value < 1 || ForcedWinnerTimeMs.Value > MaxBudgetTimeMs))
                throw new ArgumentException("Forced-winner time budget must be 1.." + MaxBudgetTimeMs + " ms", nameof(ForcedWinnerTimeMs));
            if (Goal != DirectorGoal.None && Goal != DirectorGoal.LastSecond && Goal != DirectorGoal.Comeback)
                throw new ArgumentException("Unknown director goal", nameof(Goal));
            if (ParticipantCount < 0) throw new ArgumentException("Participant count must be non-negative", nameof(ParticipantCount));

            if (Mode.HasValue && Interactive && !ModeCatalog.Get(Mode.Value).Interactive)
                throw new ArgumentException("Interactive is set for a non-interactive mode", nameof(Interactive));
            bool interactive = EffectiveInteractive;
            bool hasWinner = Mode == null || ModeCatalog.Get(Mode.Value).HasWinner;
            if (ForcedWinner != NoForcedWinner)
            {
                if (interactive) throw new ArgumentException("Interactive modes cannot select a winner (spec §2.4)", nameof(ForcedWinner));
                if (!hasWinner) throw new ArgumentException("The mode has no winner (spec §2.5)", nameof(ForcedWinner));
                if (ParticipantCount < 2) throw new ArgumentException("A forced winner needs at least 2 participants", nameof(ParticipantCount));
                if (ForcedWinner < 0 || ForcedWinner >= ParticipantCount)
                    throw new ArgumentException("Forced winner must be a participant index", nameof(ForcedWinner));
            }
            if (Goal != DirectorGoal.None && interactive)
                throw new ArgumentException("Interactive modes have no director goals (spec §2.4)", nameof(Goal));
            if (Goal != DirectorGoal.None && Mode.HasValue && !SupportsDirectorGoals(Mode.Value))
                throw new ArgumentException("Director goals apply to race and battle only (spec §2.5)", nameof(Goal));
            if (Goal == DirectorGoal.Comeback && !hasWinner)
                throw new ArgumentException("Comeback needs a mode with a winner (spec §2.5)", nameof(Goal));
        }
    }
}
