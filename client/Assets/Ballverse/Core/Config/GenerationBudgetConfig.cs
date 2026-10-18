using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Auto-generation budget of one tier: up to K candidates within a time budget (spec §2.4 "Бюджет и ограничения").
    /// JSON form: <c>{"candidates":24,"ms":1500}</c>.
    /// Named <c>…Config</c> so it does not clash with the Generation module's runtime budget type when both namespaces are imported.
    /// </summary>
    public sealed class GenerationBudgetConfig
    {
        /// <summary>Maximum number of candidates K (1–64).</summary>
        public int Candidates { get; }
        /// <summary>Time budget in milliseconds (100–10000).</summary>
        public int Milliseconds { get; }

        /// <exception cref="ArgumentOutOfRangeException">A value is outside its range.</exception>
        public GenerationBudgetConfig(int candidates, int milliseconds)
        {
            if (candidates < RemoteConfigLimits.BudgetCandidatesMin || candidates > RemoteConfigLimits.BudgetCandidatesMax)
                throw new ArgumentOutOfRangeException(nameof(candidates), candidates, "candidates must be 1–64");
            if (milliseconds < RemoteConfigLimits.BudgetMsMin || milliseconds > RemoteConfigLimits.BudgetMsMax)
                throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "ms must be 100–10000");
            Candidates = candidates;
            Milliseconds = milliseconds;
        }
    }
}
