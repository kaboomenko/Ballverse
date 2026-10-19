using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;

namespace Ballverse.UI.Params
{
    /// <summary>How the panel handled one edit (spec §2.2 panel rules).</summary>
    public enum ParamEditKind
    {
        /// <summary>The sanitized value equals what is already shown.</summary>
        Unchanged = 0,
        /// <summary>
        /// Apply now: a live parameter (recorded as a replay input event while running) or any parameter while not
        /// running (Setup / Finished: the scene is rebuilt).
        /// </summary>
        ApplyNow = 1,
        /// <summary>A structural parameter edited while running: kept pending until the user restarts.</summary>
        Staged = 2,
    }

    public readonly struct ParamEdit
    {
        public ParamEditKind Kind { get; }
        public string Id { get; }
        /// <summary>Sanitized value (tier caps applied).</summary>
        public ParamValue Value { get; }
        /// <summary>The set of visible rows changed (a <c>visibleIf</c> target was edited): rebuild the rows.</summary>
        public bool VisibilityChanged { get; }

        public ParamEdit(ParamEditKind kind, string id, ParamValue value, bool visibilityChanged)
        {
            Kind = kind;
            Id = id;
            Value = value;
            VisibilityChanged = visibilityChanged;
        }
    }

    /// <summary>
    /// State of the auto-generated settings sheet (spec §2.2, §4.3, §7.6): groups and rows built from a
    /// <see cref="ParamSchema"/> with visibility from <c>visibleIf</c>, sanitizing of every edit, and the live vs
    /// structural rule: while the simulation runs, live edits apply at once and structural edits are staged and applied
    /// with a restart. Pure C#: the view only renders <see cref="Rows"/> and forwards edits.
    /// </summary>
    public sealed class ParamPanelModel
    {
        readonly Dictionary<string, ParamValue> _pending = new Dictionary<string, ParamValue>(StringComparer.Ordinal);

        public ParamSchema Schema { get; }
        public DeviceTier Tier { get; }
        /// <summary>Values applied to the simulation (complete and sanitized).</summary>
        public ParamSet Applied { get; private set; }
        public bool HasPending => _pending.Count > 0;
        public int PendingCount => _pending.Count;

        /// <param name="applied">Current values; sanitized here, so a partial or stale set is fine.</param>
        public ParamPanelModel(ParamSchema schema, ParamSet applied, DeviceTier tier)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            if (schema.IsLayer) throw new ArgumentException("A mode layer cannot drive the panel; merge it first", nameof(schema));
            Tier = tier;
            Applied = (applied ?? ParamSet.Empty).Sanitize(schema, tier).Values;
        }

        /// <summary>Applied values with pending structural edits on top: what the panel shows.</summary>
        public ParamSet Effective
        {
            get
            {
                if (_pending.Count == 0) return Applied;
                var pairs = new List<KeyValuePair<string, ParamValue>>(_pending);
                return Applied.Overlay(new ParamSet(pairs));
            }
        }

        public bool IsPending(string id)
        {
            return id != null && _pending.ContainsKey(id);
        }

        /// <summary>Groups that have at least one visible row, in panel order (spec §2.2).</summary>
        public IReadOnlyList<ParamGroup> VisibleGroups()
        {
            var effective = Effective;
            var seen = new List<ParamGroup>();
            foreach (var def in Schema.Params)
            {
                if (seen.Contains(def.Group)) continue;
                if (Schema.IsVisible(def.Id, effective)) seen.Add(def.Group);
            }
            seen.Sort();
            return seen;
        }

        /// <summary>Visible rows of <paramref name="group"/> in schema order.</summary>
        public IReadOnlyList<ParamRowModel> Rows(ParamGroup group)
        {
            var effective = Effective;
            var rows = new List<ParamRowModel>();
            foreach (var def in Schema.Params)
            {
                if (def.Group != group || !Schema.IsVisible(def.Id, effective)) continue;
                rows.Add(ParamRowModel.Create(def, effective.Get(def.Id), _pending.ContainsKey(def.Id), Tier));
            }
            return rows;
        }

        /// <summary>Row for one parameter (whether or not it is visible).</summary>
        public ParamRowModel Row(string id)
        {
            var def = Schema.Get(id);
            return ParamRowModel.Create(def, Effective.Get(id), _pending.ContainsKey(id), Tier);
        }

        /// <summary>
        /// Sanitizes and records an edit. <paramref name="running"/>: the simulation is in Countdown / Running / Finale
        /// (spec §2.1); otherwise every edit applies now. The caller forwards <see cref="ParamEditKind.ApplyNow"/>
        /// edits to the simulation.
        /// </summary>
        public ParamEdit Edit(string id, ParamValue raw, bool running)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            var def = Schema.Get(id);
            var value = Schema.SanitizeValue(id, raw, Tier);
            var before = VisibleIds();
            var shown = _pending.TryGetValue(id, out ParamValue p) ? p : Applied.Get(id);
            ParamEditKind kind;
            if (value.Equals(shown))
            {
                kind = ParamEditKind.Unchanged;
            }
            else if (!running || def.Live)
            {
                Applied = Applied.With(id, value);
                _pending.Remove(id);
                kind = ParamEditKind.ApplyNow;
            }
            else
            {
                if (value.Equals(Applied.Get(id))) _pending.Remove(id);
                else _pending[id] = value;
                kind = ParamEditKind.Staged;
            }
            bool visibilityChanged = kind != ParamEditKind.Unchanged && !SameIds(before, VisibleIds());
            return new ParamEdit(kind, id, value, visibilityChanged);
        }

        /// <summary>
        /// The simulation's values changed from outside (preset applied, restart, generation): adopt them and drop
        /// pending edits that the new values already contain.
        /// </summary>
        public void SyncApplied(ParamSet applied)
        {
            Applied = (applied ?? ParamSet.Empty).Sanitize(Schema, Tier).Values;
            var ids = new List<string>(_pending.Keys);
            foreach (string id in ids)
                if (_pending[id].Equals(Applied.Get(id))) _pending.Remove(id);
        }

        /// <summary>All values for a restart (applied + pending); clears the pending edits.</summary>
        public ParamSet CommitPendingForRestart()
        {
            var values = Effective;
            Applied = values;
            _pending.Clear();
            return values;
        }

        public void DiscardPending()
        {
            _pending.Clear();
        }

        List<string> VisibleIds()
        {
            var effective = Effective;
            var ids = new List<string>();
            foreach (var def in Schema.Params)
                if (Schema.IsVisible(def.Id, effective)) ids.Add(def.Id);
            return ids;
        }

        static bool SameIds(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
