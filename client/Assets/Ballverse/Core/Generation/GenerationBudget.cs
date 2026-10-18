using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Candidate budget of one generation: at most <see cref="Candidates"/> headless runs and at most <see cref="TimeMs"/> of
    /// generation time, whichever runs out first (spec §2.4 "Budget and limits"; Remote Config <c>generation_budget_by_tier</c>, §9.3).
    /// </summary>
    public readonly struct GenerationBudget : IEquatable<GenerationBudget>
    {
        /// <summary>Upper bound accepted from Remote Config for <see cref="Candidates"/> (guards against a broken remote value).</summary>
        public const int MaxConfiguredCandidates = 1024;
        /// <summary>Upper bound accepted from Remote Config for <see cref="TimeMs"/>, 60 s.</summary>
        public const long MaxConfiguredTimeMs = 60000;

        /// <summary>Tier A default: 24 candidates, 1.5 s.</summary>
        public static readonly GenerationBudget TierA = new GenerationBudget(24, 1500);
        /// <summary>Tier B default: 16 candidates, 2.5 s.</summary>
        public static readonly GenerationBudget TierB = new GenerationBudget(16, 2500);
        /// <summary>Tier C default: 8 candidates, 3.5 s.</summary>
        public static readonly GenerationBudget TierC = new GenerationBudget(8, 3500);

        public int Candidates { get; }
        public long TimeMs { get; }

        /// <exception cref="ArgumentOutOfRangeException">candidates or timeMs is less than 1.</exception>
        public GenerationBudget(int candidates, long timeMs)
        {
            if (candidates < 1) throw new ArgumentOutOfRangeException(nameof(candidates));
            if (timeMs < 1) throw new ArgumentOutOfRangeException(nameof(timeMs));
            Candidates = candidates;
            TimeMs = timeMs;
        }

        /// <summary>Range check applied to Remote Config values: 1..MaxConfiguredCandidates and 1..MaxConfiguredTimeMs.</summary>
        public static bool IsValidConfigured(int candidates, long timeMs)
        {
            return candidates >= 1 && candidates <= MaxConfiguredCandidates && timeMs >= 1 && timeMs <= MaxConfiguredTimeMs;
        }

        /// <summary>Spec defaults per tier (spec §2.4 table).</summary>
        public static GenerationBudget ForTier(DeviceTier tier)
        {
            switch (tier)
            {
                case DeviceTier.A: return TierA;
                case DeviceTier.B: return TierB;
                case DeviceTier.C: return TierC;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        /// <summary>
        /// Budget multiplied by <paramref name="factor"/> on both limits (e.g. an opt-in forced-winner time budget; the session
        /// itself scales only the candidate count, spec §2.5).
        /// </summary>
        public GenerationBudget Scale(int factor)
        {
            if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor));
            return new GenerationBudget(checked(Candidates * factor), checked(TimeMs * factor));
        }

        public bool Equals(GenerationBudget other)
        {
            return Candidates == other.Candidates && TimeMs == other.TimeMs;
        }

        public override bool Equals(object obj)
        {
            return obj is GenerationBudget other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Candidates * 397 ^ TimeMs.GetHashCode();
        }

        public override string ToString()
        {
            return Candidates.ToString(System.Globalization.CultureInfo.InvariantCulture) + " candidates / "
                + TimeMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms";
        }
    }
}
