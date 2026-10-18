using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Replay ↔ Params glue (spec §7.4 .bvr header <c>paramsJson</c> / <c>paramsHash</c> and PARM live changes, §2.2 panel rules).
    /// <para><b>Decision:</b> the header's <c>paramsJson</c> is exactly Params' compact canonical JSON
    /// (<see cref="ParamJson.ToCanonicalJson"/>). Replay defines paramsHash as XXH64 of the UTF-8 bytes of paramsJson, and Params
    /// defines <see cref="ParamJson.ParamsHash"/> as XXH64 of the same canonical UTF-8 bytes, so the two hashes are identical by
    /// construction: <c>header.ParamsHash == ParamJson.ParamsHash(set, schema)</c>. Equal hashes therefore mean "same parameters
    /// under the same schema" (cache keys, render-stream regeneration, duplicate detection).</para>
    /// <para>PARM values use Params' single-value encoding (<see cref="ParamJson.WriteValue"/>, compact canonical JSON of one
    /// value), which the replay writer accepts as strict compact JSON.</para>
    /// <para>Tier caps: the canonical form is written without tier caps (values are already sanitized at the user's tier
    /// when the run starts), and reading never re-applies caps, so a replay reproduces the same values on any device.</para>
    /// </summary>
    public static class ReplayParams
    {
        /// <summary>Header for a run of <paramref name="schema"/>'s mode with sanitized parameters (see class remarks).</summary>
        /// <exception cref="ArgumentException">The set is not sanitized (see <see cref="ParamJson.ToCanonicalJson"/>) or a header field is invalid.</exception>
        public static ReplayHeader CreateHeader(string modeId, ushort modeSimVersion, string appVersion, PhysicsBackend physicsBackend,
            ulong seed, ParamSet sanitized, ParamSchema schema)
        {
            return new ReplayHeader(modeId, modeSimVersion, appVersion, physicsBackend, seed, ParamJson.ToCanonicalJson(sanitized, schema));
        }

        /// <summary>
        /// True when <paramref name="header"/> stores exactly the canonical form of <paramref name="set"/> under
        /// <paramref name="schema"/> (hash comparison; no JSON parsing).
        /// </summary>
        public static bool Matches(ReplayHeader header, ParamSet set, ParamSchema schema)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            return header.ParamsHash == ParamJson.ParamsHash(set, schema);
        }

        /// <summary>
        /// Parameters of a replay header for <paramref name="schema"/>, never throwing on the stored data: values are sanitized
        /// without tier caps, ids missing from the file get defaults (reported as <see cref="ParamIssueCode.Missing"/>), unknown
        /// ids are dropped. <see cref="ReplayParamsResult.IsExact"/> tells whether re-canonicalizing gives the stored hash
        /// (false after a schema change or for a hand-made file: the run may then not reproduce).
        /// </summary>
        public static ReplayParamsResult ReadParams(ReplayHeader header, ParamSchema schema)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (!ParamJson.TryFromJson(header.ParamsJson, out ParamSet raw, out string error))
            {
                var defaults = schema.CreateDefaults(DeviceTier.A);
                var issue = new ParamIssue("", ParamIssueCode.InvalidValue, "paramsJson unreadable: " + error);
                return new ReplayParamsResult(defaults, new[] { issue }, false);
            }
            SanitizeResult partial = raw.SanitizePartial(schema);
            var issues = new List<ParamIssue>(partial.Issues);
            foreach (var def in schema.Params)
            {
                if (!partial.Values.Contains(def.Id)) issues.Add(new ParamIssue(def.Id, ParamIssueCode.Missing, "missing; using default"));
            }
            ParamSet values = schema.CreateDefaults(DeviceTier.A).Overlay(partial.Values);
            return new ReplayParamsResult(values, issues, header.ParamsHash == ParamJson.ParamsHash(values, schema));
        }

        /// <summary>
        /// PARM record for a live parameter change at <paramref name="tick"/> (spec §2.2: only live parameters change while
        /// running). <paramref name="value"/> must be the value as applied (sanitized at the user's tier).
        /// </summary>
        /// <exception cref="ArgumentException">Unknown or structural (not live) parameter, or a value that is not sanitized.</exception>
        public static ReplayParamChange ParamChange(uint tick, string paramId, ParamValue value, ParamSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (paramId == null || !schema.TryGet(paramId, out ParamDef def)) throw new ArgumentException("Unknown parameter " + paramId, nameof(paramId));
            if (!def.Live) throw new ArgumentException("Parameter " + paramId + " is structural; a change while running restarts the run (spec §2.2)", nameof(paramId));
            var issues = new List<ParamIssue>();
            ParamValue normalized = schema.SanitizeValue(paramId, value, DeviceTier.A, issues);
            if (issues.Count > 0) throw new ArgumentException("Value is not sanitized: " + issues[0], nameof(value));
            return new ReplayParamChange(tick, paramId, ParamJson.WriteValue(normalized));
        }

        /// <summary>
        /// Decodes a PARM record for <paramref name="schema"/>; never throws on stored data. False when the parameter is unknown
        /// or not live, or the value is not JSON; otherwise the value is sanitized without tier caps (issues report fixes).
        /// </summary>
        public static bool TryReadParamChange(ReplayParamChange change, ParamSchema schema, out ParamValue value, out IReadOnlyList<ParamIssue> issues)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            value = null;
            var list = new List<ParamIssue>();
            issues = list;
            if (!schema.TryGet(change.ParamId, out ParamDef def) || !def.Live) return false;
            ParamValue raw;
            try
            {
                raw = ParamJson.ReadValue(change.ValueJson);
            }
            catch (FormatException)
            {
                return false;
            }
            value = schema.SanitizeValue(change.ParamId, raw, DeviceTier.A, list);
            return true;
        }

        /// <summary>
        /// Parameters in effect during <paramref name="tick"/>: <paramref name="initial"/> with every decodable change whose tick
        /// is ≤ <paramref name="tick"/> applied in record order (a change applies at its tick's boundary, spec §7.4).
        /// </summary>
        public static ParamSet ParamsAtTick(ParamSet initial, IReadOnlyList<ReplayParamChange> changes, uint tick, ParamSchema schema)
        {
            if (initial == null) throw new ArgumentNullException(nameof(initial));
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            ParamSet values = initial;
            for (int i = 0; i < changes.Count; i++)
            {
                ReplayParamChange c = changes[i];
                if (c.Tick > tick) break;
                if (TryReadParamChange(c, schema, out ParamValue v, out _)) values = values.With(c.ParamId, v);
            }
            return values;
        }
    }

    /// <summary>Result of <see cref="ReplayParams.ReadParams"/>.</summary>
    public sealed class ReplayParamsResult
    {
        /// <summary>Complete parameter set for the schema.</summary>
        public ParamSet Values { get; }
        public IReadOnlyList<ParamIssue> Issues { get; }
        /// <summary>The stored paramsHash equals <see cref="ParamJson.ParamsHash"/> of <see cref="Values"/>: the replay was written from exactly these parameters.</summary>
        public bool IsExact { get; }

        public ReplayParamsResult(ParamSet values, IReadOnlyList<ParamIssue> issues, bool isExact)
        {
            Values = values;
            Issues = issues;
            IsExact = isExact;
        }
    }
}
