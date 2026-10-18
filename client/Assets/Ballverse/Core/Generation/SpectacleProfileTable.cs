using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Spectacle profile of every mode M01–M15 (spec §2.4 step 4: "weights w1…w5 are per mode and live in Remote Config", §9.3
    /// <c>generation_weights</c>). Immutable; <see cref="ApplyRemoteJson"/> returns a new table.
    /// </summary>
    public sealed class SpectacleProfileTable
    {
        /// <summary>
        /// Built-in defaults. All numbers are initial guesses to be tuned through Remote Config with the balance probe (spec §8.5).
        /// Interactive modes (M04, M05, M10–M12) have w1 = 0 because their duration is not selected (spec §2.4).
        /// Thresholds are roughly 55% of the reachable maximum w1 + w2 + w3 + w4.
        /// </summary>
        public static readonly SpectacleProfileTable Default = new SpectacleProfileTable(new[]
        {
            //       w1   w2   w3   w4   w5   dMin dMax  threshold
            P(1.0, 0.8, 0.6, 0.5, 1.5, 1.5, 8.0, 1.6),  // M01 ring_escape: ring hits and breaks
            P(1.0, 0.8, 0.4, 0.6, 1.5, 1.0, 6.0, 1.5),  // M02 grow_bounce: wall hits
            P(1.0, 0.6, 1.0, 0.4, 1.5, 2.0, 12.0, 1.6), // M03 marble_race: tension = leader gap at the finish
            P(0.0, 0.8, 0.3, 0.6, 1.2, 3.0, 25.0, 0.9), // M04 peg_drop (interactive)
            P(0.0, 0.8, 0.5, 0.4, 1.5, 1.0, 5.0, 0.9),  // M05 spiral_drop (interactive)
            P(1.0, 0.7, 1.0, 0.4, 1.5, 0.5, 4.0, 1.7),  // M06 ball_battle: weapon hits; tension = winner's HP left
            P(1.0, 0.6, 0.8, 0.4, 1.2, 0.3, 3.0, 1.5),  // M07 last_standing: eliminations and edge hits
            P(1.0, 0.6, 1.0, 0.8, 1.0, 1.0, 10.0, 1.8), // M08 color_war: tension = final percentage gap
            P(1.0, 0.8, 0.6, 0.6, 1.2, 2.0, 20.0, 1.7), // M09 multiply: gate passes
            P(0.0, 0.6, 0.4, 0.8, 1.2, 0.3, 3.0, 1.0),  // M10 maze_escape (interactive)
            P(0.0, 0.8, 0.3, 0.6, 1.5, 1.0, 10.0, 0.9), // M11 smash (interactive)
            P(0.0, 1.0, 0.3, 0.6, 2.0, 1.0, 6.0, 1.0),  // M12 chain_reaction (interactive): a broken chain is stagnation
            P(1.0, 0.8, 0.2, 0.4, 0.6, 0.5, 6.0, 1.3),  // M13 pendulum_wave: meditative, mild stagnation penalty
            P(1.0, 0.6, 0.4, 0.8, 0.8, 0.2, 3.0, 1.5),  // M14 orbit: trails matter, few collisions
            P(1.0, 0.6, 0.2, 0.8, 0.8, 0.2, 4.0, 1.4),  // M15 liquid_sand: filling and layer events
        });

        readonly SpectacleProfile[] _byMode;

        SpectacleProfileTable(SpectacleProfile[] byMode)
        {
            _byMode = byMode;
        }

        /// <summary>Profile of a mode.</summary>
        public SpectacleProfile Get(ModeCode mode)
        {
            int i = (int)mode - 1;
            if (i < 0 || i >= _byMode.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            return _byMode[i];
        }

        /// <summary>Returns a copy with one mode replaced.</summary>
        public SpectacleProfileTable With(ModeCode mode, SpectacleProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int i = (int)mode - 1;
            if (i < 0 || i >= _byMode.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            var copy = (SpectacleProfile[])_byMode.Clone();
            copy[i] = profile;
            return new SpectacleProfileTable(copy);
        }

        /// <summary>
        /// Applies the Remote Config value <c>generation_weights</c> (spec §9.3) on top of this table and returns the result.
        /// Format: an object keyed by mode id ("ring_escape") or code ("M01"), each value
        /// <c>{ "w1", "w2", "w3", "w4", "w5", "densityMin", "densityMax", "qualityThreshold" }</c>, every field optional.
        /// Remote Config must never break generation: malformed JSON keeps the whole table; unknown modes, entries of the wrong
        /// type (e.g. <c>"w1": "abc"</c>) and out-of-range values are skipped one by one (that mode keeps its current profile, the
        /// others still apply) and described in <paramref name="warnings"/>, in ordinal key order. Weights must be within
        /// 0..<see cref="SpectacleWeights.MaxWeight"/> and |qualityThreshold| ≤ <see cref="SpectacleProfile.MaxAbsThreshold"/>.
        /// </summary>
        public SpectacleProfileTable ApplyRemoteJson(string json, out IReadOnlyList<string> warnings)
        {
            var warn = new List<string>();
            warnings = warn;
            if (string.IsNullOrWhiteSpace(json)) return this;

            Dictionary<string, JToken> entries = RemoteJson.ParseObject(json, "generation_weights", warn);
            if (entries == null) return this;

            var keys = new List<string>(entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = (SpectacleProfile[])_byMode.Clone();
            foreach (string key in keys)
            {
                if (!TryResolveMode(key, out ModeCode mode))
                {
                    warn.Add("generation_weights." + key + ": unknown mode");
                    continue;
                }
                if (!RemoteJson.TryConvert(entries[key], "generation_weights." + key, warn, out SpectacleProfileDto dto)) continue;
                SpectacleProfile current = result[(int)mode - 1];
                if (TryMerge(current, dto, out SpectacleProfile merged, out string error))
                    result[(int)mode - 1] = merged;
                else
                    warn.Add("generation_weights." + key + ": " + error);
            }
            return new SpectacleProfileTable(result);
        }

        /// <summary>Serializes the full table in the <see cref="ApplyRemoteJson"/> format, keyed by mode id in ordinal order.</summary>
        public string ToJson(bool indented = true)
        {
            var map = new SortedDictionary<string, SpectacleProfileDto>(StringComparer.Ordinal);
            for (int i = 0; i < _byMode.Length; i++)
            {
                SpectacleProfile p = _byMode[i];
                map.Add(ModeCatalog.All[i].Id, new SpectacleProfileDto
                {
                    W1 = p.Weights.W1,
                    W2 = p.Weights.W2,
                    W3 = p.Weights.W3,
                    W4 = p.Weights.W4,
                    W5 = p.Weights.W5,
                    DensityMin = p.Density.MinPerSecond,
                    DensityMax = p.Density.MaxPerSecond,
                    QualityThreshold = p.QualityThreshold,
                });
            }
            return CoreJson.Serialize(map, indented);
        }

        static bool TryResolveMode(string key, out ModeCode mode)
        {
            if (ModeCatalog.TryGetById(key, out ModeInfo info))
            {
                mode = info.Code;
                return true;
            }
            return ModeCatalog.TryParseCode(key, out mode);
        }

        static bool TryMerge(SpectacleProfile current, SpectacleProfileDto dto, out SpectacleProfile merged, out string error)
        {
            merged = null;
            SpectacleWeights w = current.Weights;
            double w1 = dto.W1 ?? w.W1, w2 = dto.W2 ?? w.W2, w3 = dto.W3 ?? w.W3, w4 = dto.W4 ?? w.W4, w5 = dto.W5 ?? w.W5;
            if (!SpectacleWeights.IsValidWeight(w1) || !SpectacleWeights.IsValidWeight(w2) || !SpectacleWeights.IsValidWeight(w3)
                || !SpectacleWeights.IsValidWeight(w4) || !SpectacleWeights.IsValidWeight(w5))
            {
                error = "weights must be within 0..100";
                return false;
            }
            double min = dto.DensityMin ?? current.Density.MinPerSecond;
            double max = dto.DensityMax ?? current.Density.MaxPerSecond;
            if (!DensityRange.IsValid(min, max))
            {
                error = "density range must satisfy 0 <= densityMin <= densityMax, densityMax > 0";
                return false;
            }
            double threshold = dto.QualityThreshold ?? current.QualityThreshold;
            if (!SpectacleProfile.IsValidThreshold(threshold))
            {
                error = "qualityThreshold must be finite with |qualityThreshold| <= 1000";
                return false;
            }
            merged = new SpectacleProfile(new SpectacleWeights(w1, w2, w3, w4, w5), new DensityRange(min, max), threshold);
            error = null;
            return true;
        }

        static SpectacleProfile P(double w1, double w2, double w3, double w4, double w5, double densityMin, double densityMax, double threshold)
        {
            return new SpectacleProfile(new SpectacleWeights(w1, w2, w3, w4, w5), new DensityRange(densityMin, densityMax), threshold);
        }

        /// <summary>Debug description of one mode's profile (invariant culture).</summary>
        public string Describe(ModeCode mode)
        {
            SpectacleProfile p = Get(mode);
            SpectacleWeights w = p.Weights;
            return string.Format(CultureInfo.InvariantCulture, "{0}: w=({1},{2},{3},{4},{5}) density={6} threshold={7}",
                mode, w.W1, w.W2, w.W3, w.W4, w.W5, p.Density, p.QualityThreshold);
        }
    }
}
