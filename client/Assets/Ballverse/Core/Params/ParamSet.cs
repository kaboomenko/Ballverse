using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Immutable parameter values keyed by id (spec §7.6 <c>ParamSet</c>). May be partial or hold invalid / unknown
    /// values (presets, JSON input); <see cref="Sanitize"/> turns it into a complete valid set for a schema and tier.
    /// <see cref="Ids"/> are sorted ordinally so iteration never depends on hash order.
    /// </summary>
    public sealed class ParamSet : IEquatable<ParamSet>
    {
        public static readonly ParamSet Empty = new ParamSet(new KeyValuePair<string, ParamValue>[0]);

        readonly Dictionary<string, ParamValue> _values;
        readonly string[] _ids;

        /// <summary>Builds a set; throws on null ids / values or duplicate ids.</summary>
        public ParamSet(IEnumerable<KeyValuePair<string, ParamValue>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            _values = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var kv in values)
            {
                if (kv.Key == null) throw new ArgumentException("Null parameter id", nameof(values));
                if (kv.Value == null) throw new ArgumentException("Null value for '" + kv.Key + "'", nameof(values));
                if (_values.ContainsKey(kv.Key)) throw new ArgumentException("Duplicate parameter id '" + kv.Key + "'", nameof(values));
                _values.Add(kv.Key, kv.Value);
            }
            _ids = new string[_values.Count];
            _values.Keys.CopyTo(_ids, 0);
            Array.Sort(_ids, StringComparer.Ordinal);
        }

        ParamSet(Dictionary<string, ParamValue> values)
        {
            _values = values;
            _ids = new string[_values.Count];
            _values.Keys.CopyTo(_ids, 0);
            Array.Sort(_ids, StringComparer.Ordinal);
        }

        public int Count => _ids.Length;
        /// <summary>Ids in ordinal order.</summary>
        public IReadOnlyList<string> Ids => _ids;

        public bool Contains(string id)
        {
            return id != null && _values.ContainsKey(id);
        }

        public bool TryGet(string id, out ParamValue value)
        {
            if (id == null)
            {
                value = null;
                return false;
            }
            return _values.TryGetValue(id, out value);
        }

        public ParamValue Get(string id)
        {
            if (!TryGet(id, out ParamValue v)) throw new KeyNotFoundException("No value for parameter '" + id + "'");
            return v;
        }

        /// <summary>Copy with <paramref name="id"/> set to <paramref name="value"/>.</summary>
        public ParamSet With(string id, ParamValue value)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (value == null) throw new ArgumentNullException(nameof(value));
            var d = new Dictionary<string, ParamValue>(_values, StringComparer.Ordinal) { [id] = value };
            return new ParamSet(d);
        }

        public ParamSet Without(string id)
        {
            if (!Contains(id)) return this;
            var d = new Dictionary<string, ParamValue>(_values, StringComparer.Ordinal);
            d.Remove(id);
            return new ParamSet(d);
        }

        /// <summary>Copy with every value of <paramref name="overlay"/> written over this set's values.</summary>
        public ParamSet Overlay(ParamSet overlay)
        {
            if (overlay == null) throw new ArgumentNullException(nameof(overlay));
            var d = new Dictionary<string, ParamValue>(_values, StringComparer.Ordinal);
            foreach (string id in overlay._ids) d[id] = overlay._values[id];
            return new ParamSet(d);
        }

        public float GetFloat(string id)
        {
            return (float)Get(id).AsFloat;
        }

        /// <summary>Integer value; an unlimited value reads as <see cref="IntParam.UnlimitedValue"/>.</summary>
        public int GetInt(string id)
        {
            long v = Get(id).AsInt;
            if (v < int.MinValue || v > int.MaxValue) throw new InvalidOperationException("Value of '" + id + "' is not sanitized");
            return (int)v;
        }

        public bool IsUnlimited(string id)
        {
            return Get(id).IsUnlimited;
        }

        public bool GetBool(string id)
        {
            return Get(id).AsBool;
        }

        /// <summary>Enum / template option id.</summary>
        public string GetString(string id)
        {
            return Get(id).AsString;
        }

        public ColorRgba GetColor(string id)
        {
            return Get(id).AsColor;
        }

        public IReadOnlyList<ColorRgba> GetColors(string id)
        {
            return Get(id).AsColors;
        }

        public IReadOnlyList<string> GetStrings(string id)
        {
            return Get(id).AsStrings;
        }

        public ulong GetSeed(string id)
        {
            return Get(id).AsSeed;
        }

        /// <summary>
        /// Complete valid set for <paramref name="schema"/> on <paramref name="tier"/> (spec §7.6 validation):
        /// missing values get defaults, unknown ids are dropped, numbers are clamped to the range and tier cap and snapped
        /// to the step grid, non-finite numbers and invalid options / colours / seeds fall back to the default, lists and
        /// strings are cut to their limits. Idempotent: sanitizing the result again changes nothing and reports nothing.
        /// Never throws for any values.
        /// </summary>
        public SanitizeResult Sanitize(ParamSchema schema, DeviceTier tier)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            schema.RequireComplete();
            var issues = new List<ParamIssue>();
            var result = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var def in schema.Params)
            {
                if (_values.TryGetValue(def.Id, out ParamValue raw))
                {
                    result.Add(def.Id, def.Sanitize(raw, tier, true, issues));
                }
                else
                {
                    issues.Add(new ParamIssue(def.Id, ParamIssueCode.Missing, "missing; using default"));
                    result.Add(def.Id, def.DefaultFor(tier, true));
                }
            }
            AddUnknown(schema, issues);
            return new SanitizeResult(new ParamSet(result), issues);
        }

        /// <summary>
        /// Sanitizes only the values present (no defaults filled, tier caps not applied); unknown ids are dropped.
        /// Used for preset values, which are partial and tier-independent (spec §2.3).
        /// </summary>
        public SanitizeResult SanitizePartial(ParamSchema schema)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            schema.RequireComplete();
            var issues = new List<ParamIssue>();
            var result = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var def in schema.Params)
            {
                if (_values.TryGetValue(def.Id, out ParamValue raw))
                    result.Add(def.Id, def.Sanitize(raw, DeviceTier.A, false, issues));
            }
            AddUnknown(schema, issues);
            return new SanitizeResult(new ParamSet(result), issues);
        }

        public bool Equals(ParamSet other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other._ids.Length != _ids.Length) return false;
            for (int i = 0; i < _ids.Length; i++)
            {
                if (!string.Equals(_ids[i], other._ids[i], StringComparison.Ordinal)) return false;
                if (!_values[_ids[i]].Equals(other._values[_ids[i]])) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ParamSet);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                foreach (string id in _ids) h = h * 31 + StringComparer.Ordinal.GetHashCode(id) ^ _values[id].GetHashCode();
                return h;
            }
        }

        public override string ToString()
        {
            var parts = new string[_ids.Length];
            for (int i = 0; i < _ids.Length; i++) parts[i] = _ids[i] + "=" + _values[_ids[i]];
            return "{" + string.Join(", ", parts) + "}";
        }

        void AddUnknown(ParamSchema schema, List<ParamIssue> issues)
        {
            foreach (string id in _ids)
                if (!schema.Contains(id)) issues.Add(new ParamIssue(id, ParamIssueCode.Unknown, "unknown parameter dropped"));
        }
    }
}
