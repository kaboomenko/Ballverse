using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Candidate budget per device tier (spec §2.4 table). Immutable. Remote Config <c>generation_budget_by_tier</c> (spec §9.3)
    /// is parsed only by the Config module; <c>Ballverse.Core.Integration.RemoteConfigAdapters</c> builds this table from it with
    /// <see cref="With"/>.
    /// </summary>
    public sealed class GenerationBudgetTable
    {
        /// <summary>Spec defaults: A 24 / 1500 ms, B 16 / 2500 ms, C 8 / 3500 ms.</summary>
        public static readonly GenerationBudgetTable Default =
            new GenerationBudgetTable(new[] { GenerationBudget.TierA, GenerationBudget.TierB, GenerationBudget.TierC });

        readonly GenerationBudget[] _byTier;

        GenerationBudgetTable(GenerationBudget[] byTier)
        {
            _byTier = byTier;
        }

        public GenerationBudget Get(DeviceTier tier)
        {
            int i = (int)tier;
            if (i < 0 || i >= _byTier.Length) throw new ArgumentOutOfRangeException(nameof(tier));
            return _byTier[i];
        }

        /// <summary>Copy with one tier replaced (e.g. by the Remote Config adapter, <c>Ballverse.Core.Integration</c>).</summary>
        public GenerationBudgetTable With(DeviceTier tier, GenerationBudget budget)
        {
            int i = (int)tier;
            if (i < 0 || i >= _byTier.Length) throw new ArgumentOutOfRangeException(nameof(tier));
            if (!GenerationBudget.IsValidConfigured(budget.Candidates, budget.TimeMs))
                throw new ArgumentOutOfRangeException(nameof(budget), budget, "Budget outside the configurable range");
            var copy = (GenerationBudget[])_byTier.Clone();
            copy[i] = budget;
            return new GenerationBudgetTable(copy);
        }
    }
}
