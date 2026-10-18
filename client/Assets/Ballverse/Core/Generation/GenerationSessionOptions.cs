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

        /// <summary>Seed of the session; candidate seeds derive from it (<see cref="CandidateSeeds"/>).</summary>
        public ulong SessionSeed { get; set; }

        /// <summary>Optional mode; when set, the session checks that forced winner / comeback are only used by modes with a winner.</summary>
        public ModeCode? Mode { get; set; }

        /// <summary>Base budget before the forced-winner multiplier and the one-time doubling (spec §2.4 table).</summary>
        public GenerationBudget Budget { get; set; } = GenerationBudget.TierB;

        /// <summary>Minimum total score for a candidate to pass (spec §2.4 step 5).</summary>
        public double QualityThreshold { get; set; }

        /// <summary>Target duration in ticks (spec §2.2 <c>targetDuration</c>, 10–180 s), or null for "unlimited".</summary>
        public int? TargetDurationTicks { get; set; }

        /// <summary>Participant that must win, or <see cref="NoForcedWinner"/> (spec §2.5).</summary>
        public int ForcedWinner { get; set; } = NoForcedWinner;

        /// <summary>Number of participants; required (≥ 2) with a forced winner, sets the budget multiplier (spec §2.5).</summary>
        public int ParticipantCount { get; set; }

        public DirectorGoal Goal { get; set; }

        /// <summary>Interactive mode: only the arena is generated, duration and winner are not selected (spec §2.4).</summary>
        public bool Interactive { get; set; }

        public SpectacleWeights Weights { get; set; } = SpectacleWeights.Default;

        public DensityRange Density { get; set; } = new DensityRange(1.0, 8.0);

        /// <summary>
        /// Weight of the director-goal bonus added to the selection score (not to the quality check): LastSecond adds
        /// weight × finishTension, Comeback adds weight when the candidate is a comeback. Default 1.
        /// </summary>
        public double GoalBonusWeight { get; set; } = 1.0;

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
                Interactive = info.Interactive,
                Weights = profile.Weights,
                Density = profile.Density,
            };
        }

        internal GenerationSessionOptions Clone()
        {
            return (GenerationSessionOptions)MemberwiseClone();
        }

        internal void Validate()
        {
            if (Budget.Candidates < 1 || Budget.TimeMs < 1) throw new ArgumentException("Budget must be at least 1 candidate and 1 ms", nameof(Budget));
            if (double.IsNaN(QualityThreshold) || double.IsInfinity(QualityThreshold))
                throw new ArgumentException("Quality threshold must be finite", nameof(QualityThreshold));
            if (TargetDurationTicks.HasValue && (TargetDurationTicks.Value <= 0 || TargetDurationTicks.Value > SimConstants.MaxDurationTicks))
                throw new ArgumentException("Target duration must be within 1..MaxDurationTicks (spec §2.1)", nameof(TargetDurationTicks));
            if (Weights == null) throw new ArgumentException("Weights are required", nameof(Weights));
            if (!DensityRange.IsValid(Density.MinPerSecond, Density.MaxPerSecond))
                throw new ArgumentException("Density range is invalid", nameof(Density));
            if (!SpectacleWeights.IsValidWeight(GoalBonusWeight))
                throw new ArgumentException("Goal bonus weight must be finite and non-negative", nameof(GoalBonusWeight));
            if (Goal != DirectorGoal.None && Goal != DirectorGoal.LastSecond && Goal != DirectorGoal.Comeback)
                throw new ArgumentException("Unknown director goal", nameof(Goal));
            if (ParticipantCount < 0) throw new ArgumentException("Participant count must be non-negative", nameof(ParticipantCount));

            bool hasWinner = Mode == null || ModeCatalog.Get(Mode.Value).HasWinner;
            if (ForcedWinner != NoForcedWinner)
            {
                if (Interactive) throw new ArgumentException("Interactive modes cannot select a winner (spec §2.4)", nameof(ForcedWinner));
                if (!hasWinner) throw new ArgumentException("The mode has no winner (spec §2.5)", nameof(ForcedWinner));
                if (ParticipantCount < 2) throw new ArgumentException("A forced winner needs at least 2 participants", nameof(ParticipantCount));
                if (ForcedWinner < 0 || ForcedWinner >= ParticipantCount)
                    throw new ArgumentException("Forced winner must be a participant index", nameof(ForcedWinner));
            }
            if (Goal != DirectorGoal.None && Interactive)
                throw new ArgumentException("Interactive modes have no director goals (spec §2.4)", nameof(Goal));
            if (Goal == DirectorGoal.Comeback && !hasWinner)
                throw new ArgumentException("Comeback needs a mode with a winner (spec §2.5)", nameof(Goal));
        }
    }
}
