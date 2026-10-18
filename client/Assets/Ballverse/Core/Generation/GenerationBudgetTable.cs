using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Candidate budget per device tier (spec §2.4 table; Remote Config <c>generation_budget_by_tier</c>, spec §9.3). Immutable.
    /// </summary>
    public sealed class GenerationBudgetTable
    {
        /// <summary>Spec defaults: A 24 / 1500 ms, B 16 / 2500 ms, C 8 / 3500 ms.</summary>
        public static readonly GenerationBudgetTable Default =
            new GenerationBudgetTable(new[] { GenerationBudget.TierA, GenerationBudget.TierB, GenerationBudget.TierC });

        static readonly DeviceTier[] s_tiers = { DeviceTier.A, DeviceTier.B, DeviceTier.C };

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

        /// <summary>
        /// Applies Remote Config <c>generation_budget_by_tier</c> (spec §9.3): <c>{ "A": { "candidates": 24, "timeMs": 1500 }, … }</c>,
        /// fields optional. Malformed JSON keeps the whole table; unknown tiers, entries of the wrong type and out-of-range values
        /// (<see cref="GenerationBudget.IsValidConfigured"/>) are skipped one by one and reported in <paramref name="warnings"/> in
        /// ordinal key order.
        /// </summary>
        public GenerationBudgetTable ApplyRemoteJson(string json, out IReadOnlyList<string> warnings)
        {
            var warn = new List<string>();
            warnings = warn;
            if (string.IsNullOrWhiteSpace(json)) return this;

            Dictionary<string, JToken> entries = RemoteJson.ParseObject(json, "generation_budget_by_tier", warn);
            if (entries == null) return this;

            var keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = (GenerationBudget[])_byTier.Clone();
            foreach (string key in keys)
            {
                int tierIndex = Array.FindIndex(s_tiers, t => t.ToString() == key);
                if (tierIndex < 0)
                {
                    warn.Add("generation_budget_by_tier." + key + ": unknown tier");
                    continue;
                }
                if (!RemoteJson.TryConvert(entries[key], "generation_budget_by_tier." + key, warn, out GenerationBudgetDto dto)) continue;
                GenerationBudget current = result[tierIndex];
                int candidates = dto.Candidates ?? current.Candidates;
                long timeMs = dto.TimeMs ?? current.TimeMs;
                if (!GenerationBudget.IsValidConfigured(candidates, timeMs))
                {
                    warn.Add("generation_budget_by_tier." + key + ": out of range");
                    continue;
                }
                result[tierIndex] = new GenerationBudget(candidates, timeMs);
            }
            return new GenerationBudgetTable(result);
        }

        /// <summary>Serializes the table in the <see cref="ApplyRemoteJson"/> format (keys A, B, C).</summary>
        public string ToJson(bool indented = true)
        {
            var map = new SortedDictionary<string, GenerationBudgetDto>(StringComparer.Ordinal);
            for (int i = 0; i < s_tiers.Length; i++)
                map.Add(s_tiers[i].ToString(), new GenerationBudgetDto { Candidates = _byTier[i].Candidates, TimeMs = _byTier[i].TimeMs });
            return CoreJson.Serialize(map, indented);
        }
    }
}
