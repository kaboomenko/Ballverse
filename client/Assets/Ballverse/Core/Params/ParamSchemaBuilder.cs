using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Collects parameter definitions and builds an immutable <see cref="ParamSchema"/> (spec §7.6).
    /// <see cref="Build"/> validates everything and throws <see cref="ParamSchemaException"/> on: invalid or duplicate ids,
    /// min &gt; max, step &lt;= 0, default outside the range / off the step grid / not an option, tier caps outside the range,
    /// <c>visibleIf</c> referencing an unknown parameter or forming a cycle, or a condition value the target can never take.
    /// </summary>
    public sealed class ParamSchemaBuilder
    {
        readonly List<ParamDef> _defs = new List<ParamDef>();
        readonly List<string> _excluded = new List<string>();

        public ParamSchemaBuilder Add(ParamDef def)
        {
            _defs.Add(def ?? throw new ArgumentNullException(nameof(def)));
            return this;
        }

        public ParamSchemaBuilder AddRange(IEnumerable<ParamDef> defs)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            foreach (var d in defs) Add(d);
            return this;
        }

        /// <summary>Layer only: removes a common parameter from the merged schema ("если не отмечено иное", spec §2.2).</summary>
        public ParamSchemaBuilder Exclude(string commonParamId)
        {
            if (string.IsNullOrEmpty(commonParamId)) throw new ArgumentException("Id is required", nameof(commonParamId));
            _excluded.Add(commonParamId);
            return this;
        }

        /// <summary>Builds a complete schema; every <c>visibleIf</c> target must be in it.</summary>
        public ParamSchema Build()
        {
            if (_excluded.Count > 0)
                throw new ParamSchemaException(null, "Exclude() is only allowed in a mode layer (BuildLayer)");
            return BuildCore(false);
        }

        /// <summary>
        /// Builds a mode layer for <see cref="ParamSchema.Merge"/>: definitions whose id matches a common parameter
        /// override its range / default / tier caps / options; <c>visibleIf</c> may reference common parameters,
        /// which is checked at merge time. A layer cannot be used to sanitize values.
        /// </summary>
        public ParamSchema BuildLayer()
        {
            return BuildCore(true);
        }

        ParamSchema BuildCore(bool isLayer)
        {
            var defs = new List<ParamDef>(_defs.Count);
            var index = new Dictionary<string, ParamDef>(StringComparer.Ordinal);
            foreach (var source in _defs)
            {
                var def = source.Clone();
                def.Validate();
                if (index.ContainsKey(def.Id)) throw new ParamSchemaException(def.Id, "duplicate id");
                index.Add(def.Id, def);
                defs.Add(def);
            }

            foreach (var def in defs)
            {
                var cond = def.VisibleIf;
                if (cond == null) continue;
                if (cond.TargetId == def.Id) throw new ParamSchemaException(def.Id, "visibleIf references itself");
                if (!index.TryGetValue(cond.TargetId, out ParamDef target))
                {
                    if (isLayer) continue;
                    throw new ParamSchemaException(def.Id, "visibleIf references unknown parameter '" + cond.TargetId + "'");
                }
                def.SetVisibleIf(ValidateCondition(def, cond, target));
            }

            foreach (var def in defs) CheckCycle(def, index);

            var excluded = new List<string>();
            var seenExcluded = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in _excluded)
            {
                if (index.ContainsKey(id)) throw new ParamSchemaException(id, "is both defined and excluded");
                if (seenExcluded.Add(id)) excluded.Add(id);
            }

            var order = new int[defs.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = defs[a].Group.CompareTo(defs[b].Group);
                if (c != 0) return c;
                c = defs[a].Order.CompareTo(defs[b].Order);
                return c != 0 ? c : a.CompareTo(b);
            });
            var sorted = new ParamDef[defs.Count];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = defs[order[i]];
            return new ParamSchema(sorted, excluded.ToArray(), isLayer);
        }

        /// <summary>
        /// Checks a condition against its target and returns it with values in their sanitized form, so e.g.
        /// <c>VisibleIf.Is("g", ParamValue.Float(0.15))</c> on a float target matches the stored <c>0.15f</c>.
        /// </summary>
        internal static VisibleIf ValidateCondition(ParamDef def, VisibleIf cond, ParamDef target)
        {
            if (cond.Kind == VisibleIfKind.IsTrue)
            {
                if (target.Type != ParamType.Bool)
                    throw new ParamSchemaException(def.Id, "visibleIf IsTrue needs a Bool target, '" + target.Id + "' is " + target.Type);
                return cond;
            }
            var values = cond.Values;
            var normalized = new ParamValue[values.Count];
            for (int i = 0; i < normalized.Length; i++)
            {
                if (!target.TryNormalizeConditionValue(values[i], out normalized[i]))
                    throw new ParamSchemaException(def.Id, "visibleIf value " + values[i] + " is never a valid value of '" + target.Id
                        + "' (it must lie in the range and on the step grid, or be one of the options)");
            }
            return cond.WithValues(normalized);
        }

        internal static void CheckCycle(ParamDef start, Dictionary<string, ParamDef> index)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
            var path = new List<string> { start.Id };
            var current = start;
            while (current.VisibleIf != null && index.TryGetValue(current.VisibleIf.TargetId, out ParamDef next))
            {
                path.Add(next.Id);
                if (!visited.Add(next.Id))
                    throw new ParamSchemaException(start.Id, "visibleIf cycle: " + string.Join(" -> ", path));
                current = next;
            }
        }
    }
}
