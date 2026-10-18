using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Ordered, immutable parameter schema (spec §7.6 "Схема параметров"). Order is (group, order, insertion) and is
    /// the order of the settings panel and of canonical JSON. Built with <see cref="ParamSchemaBuilder"/>; a mode's
    /// schema is <see cref="Merge"/> of the common schema (spec §2.2, <see cref="CommonParams"/>) and the mode layer.
    /// </summary>
    public sealed class ParamSchema
    {
        readonly ParamDef[] _params;
        readonly Dictionary<string, ParamDef> _index;
        readonly string[] _excluded;
        readonly ReadOnlyCollection<ParamDef> _paramsView;
        readonly ReadOnlyCollection<string> _excludedView;

        /// <summary>Definitions in schema order (read-only view).</summary>
        public IReadOnlyList<ParamDef> Params => _paramsView;
        public int Count => _params.Length;
        /// <summary>True for a mode layer (see <see cref="ParamSchemaBuilder.BuildLayer"/>).</summary>
        public bool IsLayer { get; }
        /// <summary>Layer only: common parameter ids removed by the mode.</summary>
        public IReadOnlyList<string> Excluded => _excludedView;

        internal ParamSchema(ParamDef[] defs, string[] excluded, bool isLayer)
        {
            _params = defs;
            _excluded = excluded;
            IsLayer = isLayer;
            _paramsView = Array.AsReadOnly(defs);
            _excludedView = Array.AsReadOnly(excluded);
            _index = new Dictionary<string, ParamDef>(StringComparer.Ordinal);
            foreach (var d in defs) _index.Add(d.Id, d);
        }

        public bool Contains(string id)
        {
            return id != null && _index.ContainsKey(id);
        }

        public bool TryGet(string id, out ParamDef def)
        {
            if (id == null)
            {
                def = null;
                return false;
            }
            return _index.TryGetValue(id, out def);
        }

        public ParamDef Get(string id)
        {
            if (!TryGet(id, out ParamDef def)) throw new KeyNotFoundException("Unknown parameter '" + id + "'");
            return def;
        }

        /// <summary>
        /// Whether a change to <paramref name="id"/> may be applied while <c>Running</c> (recorded as a replay event);
        /// otherwise the panel offers a restart (spec §2.2 panel rules).
        /// </summary>
        public bool IsLiveEditable(string id)
        {
            return Get(id).Live;
        }

        /// <summary>
        /// Sanitizes one value of <paramref name="id"/> on <paramref name="tier"/> (tier caps applied), e.g. a live edit
        /// from the panel or a replay PARM event (spec §2.2 panel rules, §7.4). Issues go to <paramref name="issues"/>
        /// when given. Throws <see cref="KeyNotFoundException"/> for an unknown id; never throws for any value.
        /// </summary>
        public ParamValue SanitizeValue(string id, ParamValue value, DeviceTier tier, List<ParamIssue> issues = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            RequireComplete();
            return Get(id).Sanitize(value, tier, true, issues ?? new List<ParamIssue>());
        }

        /// <summary>
        /// Whether the panel shows <paramref name="id"/> for the given values: its <c>visibleIf</c> holds and its target
        /// is visible too (recursively). Targets missing from <paramref name="values"/> use their default.
        /// Pass sanitized values; in a layer, conditions on parameters outside the layer count as satisfied.
        /// </summary>
        public bool IsVisible(string id, ParamSet values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var def = Get(id);
            while (def.VisibleIf != null)
            {
                if (!TryGet(def.VisibleIf.TargetId, out ParamDef target)) return true;
                if (!values.TryGet(target.Id, out ParamValue v)) v = target.Default;
                if (!def.VisibleIf.Matches(v)) return false;
                def = target;
            }
            return true;
        }

        /// <summary>Visible definitions in schema order (spec §7.6: the panel is built from the schema).</summary>
        public IReadOnlyList<ParamDef> GetVisible(ParamSet values)
        {
            var result = new List<ParamDef>();
            foreach (var d in _params)
                if (IsVisible(d.Id, values)) result.Add(d);
            return result;
        }

        /// <summary>All defaults, clamped to <paramref name="tier"/> caps.</summary>
        public ParamSet CreateDefaults(DeviceTier tier = DeviceTier.A)
        {
            RequireComplete();
            var pairs = new List<KeyValuePair<string, ParamValue>>(_params.Length);
            foreach (var d in _params) pairs.Add(new KeyValuePair<string, ParamValue>(d.Id, d.DefaultFor(tier, true)));
            return new ParamSet(pairs);
        }

        /// <summary>
        /// Mode schema = common parameters plus the mode layer (spec §7.6 "параметры режима поверх общих").
        /// A mode definition with a common id overrides range, step, default, tier caps and options but must keep the
        /// type; group, order, <c>live</c>, <c>visibleIf</c> and label stay those of the common parameter. The unit is
        /// inherited when the mode gives none, and an int parameter that accepts "unlimited" keeps accepting it.
        /// Excluded common ids are removed. The result is validated like <see cref="ParamSchemaBuilder.Build"/>.
        /// </summary>
        public static ParamSchema Merge(ParamSchema common, ParamSchema mode)
        {
            if (common == null) throw new ArgumentNullException(nameof(common));
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (common.IsLayer) throw new ArgumentException("The common schema must be complete, not a layer", nameof(common));

            var excluded = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in mode.Excluded)
            {
                if (!common.Contains(id)) throw new ParamSchemaException(id, "excluded but not a common parameter");
                excluded.Add(id);
            }

            var builder = new ParamSchemaBuilder();
            foreach (var c in common._params)
            {
                if (excluded.Contains(c.Id)) continue;
                if (mode.TryGet(c.Id, out ParamDef m))
                {
                    if (m.Type != c.Type)
                        throw new ParamSchemaException(c.Id, "mode override changes type " + c.Type + " to " + m.Type);
                    builder.Add(c.ApplyOverride(m));
                }
                else
                {
                    builder.Add(c);
                }
            }
            foreach (var m in mode._params)
                if (!common.Contains(m.Id)) builder.Add(m);
            return builder.Build();
        }

        internal void RequireComplete()
        {
            if (IsLayer) throw new InvalidOperationException("A mode layer cannot be used for values; merge it first");
        }
    }
}
