using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>
    /// Settings sheet of the simulation (spec §2.2 panel rules): the mode's merged schema, the applied values, live edits
    /// (recorded as PARM at the next tick) and structural edits (scene rebuild in Setup / Finished, restart while running).
    /// </summary>
    public sealed class ParamPanelViewModel : ViewModelBase, IParamPanelViewModel
    {
        readonly SimulationViewModel _sim;
        readonly AppContext _ctx;

        public ParamPanelViewModel(SimulationViewModel sim, AppContext ctx)
            : base(ctx.Notices.Report)
        {
            _sim = sim;
            _ctx = ctx;
        }

        public ParamSchema Schema => _sim.Mode?.Schema;
        public ParamSet Values => _sim.CurrentValues;
        public DeviceTier Tier => _ctx.Tier;

        public bool IsRunning
        {
            get
            {
                SimPhaseView p = _sim.Phase;
                return p == SimPhaseView.Countdown || p == SimPhaseView.Running || p == SimPhaseView.Finale;
            }
        }

        public void Apply(string id, ParamValue value) => _sim.ApplyParam(id, value);

        public void RestartWith(ParamSet values) => _sim.RestartWith(values);

        public void ResetToPreset() => _sim.PresetsModel.ResetToSelected();

        internal void NotifyChanged() => Notify();
    }

    /// <summary>
    /// Presets sheet (spec §2.3): the mode's built-in presets, remote presets ("Новое") and the user's presets (up to 20 per
    /// mode, saved in <c>presets/user_presets.json</c>). Applying one restarts the run with its prepared parameters.
    /// </summary>
    public sealed class PresetsViewModel : ViewModelBase, IPresetsViewModel
    {
        /// <summary>Selection id when the values came from 🎲 or a clip (no preset).</summary>
        public const string CustomId = "custom";

        readonly SimulationViewModel _sim;
        readonly AppContext _ctx;
        readonly List<PresetItemData> _items = new List<PresetItemData>();
        readonly Dictionary<string, Preset> _byId = new Dictionary<string, Preset>(StringComparer.Ordinal);
        string _selected;

        public PresetsViewModel(SimulationViewModel sim, AppContext ctx)
            : base(ctx.Notices.Report)
        {
            _sim = sim;
            _ctx = ctx;
        }

        public IReadOnlyList<PresetItemData> Presets => _items;
        public string SelectedId => _selected;

        public LocalizedText CurrentName
        {
            get
            {
                if (_selected != null && _byId.TryGetValue(_selected, out Preset p)) return NameOf(p);
                return LocalizedText.FromKey("presets.custom");
            }
        }

        public bool IsModified
        {
            get
            {
                IModeDefinition mode = _sim.Mode;
                ParamSet current = _sim.CurrentValues;
                if (mode == null || current == null || _selected == null || !_byId.TryGetValue(_selected, out Preset p)) return false;
                if (ReferenceEquals(current, _modifiedFor) && _selected == _modifiedPreset) return _modified;
                ParamSet preset = ModeParams.ApplyPreset(mode, p, _ctx.Tier).Values;
                _modified = !SameIgnoringPacing(preset, current);
                _modifiedFor = current;
                _modifiedPreset = _selected;
                return _modified;
            }
        }

        ParamSet _modifiedFor;
        string _modifiedPreset;
        bool _modified;

        public int UserPresetCount => _sim.Mode != null ? _ctx.Presets.Library.CountForMode(_sim.Mode.Info.Id) : 0;
        public int MaxUserPresets => UserPresetLibrary.MaxPerMode;

        /// <summary>Rebuilds the list for the current mode (mode opened, remote presets changed).</summary>
        public void Rebuild()
        {
            _items.Clear();
            _byId.Clear();
            IModeDefinition mode = _sim.Mode;
            if (mode == null)
            {
                Notify();
                return;
            }
            foreach (Preset p in mode.BuiltInPresets) Add(p);
            foreach (Preset p in _ctx.Remote.RemotePresetsFor(mode.Info.Id)) Add(p);
            foreach (Preset p in _ctx.Presets.Library.ForMode(mode.Info.Id)) Add(p);
            string id = mode.Info.Id;
            if (!_ctx.Settings.Data.LastPreset.TryGetValue(id, out _selected) || (_selected != CustomId && !_byId.ContainsKey(_selected)))
                _selected = mode.BuiltInPresets.Count > 0 ? mode.BuiltInPresets[0].Id : null;
            Notify();
        }

        void Add(Preset p)
        {
            if (_byId.ContainsKey(p.Id)) return;
            _byId.Add(p.Id, p);
            _items.Add(new PresetItemData(p.Id, NameOf(p), p.Source, p.IsNew, null));
        }

        static LocalizedText NameOf(Preset p)
        {
            if (p.Source == PresetSource.User || string.IsNullOrEmpty(p.NameKey)) return LocalizedText.FromLiteral(p.Name ?? p.Id);
            return LocalizedText.FromKey(p.NameKey);
        }

        public void Apply(string id)
        {
            IModeDefinition mode = _sim.Mode;
            if (mode == null || id == null || !_byId.TryGetValue(id, out Preset p)) return;
            ParamSet values = ModeParams.ApplyPreset(mode, p, _ctx.Tier).Values;
            Select(id);
            _sim.ApplyPresetValues(values);
        }

        /// <summary>"Сбросить к пресету".</summary>
        public void ResetToSelected()
        {
            if (_selected != null && _byId.ContainsKey(_selected)) Apply(_selected);
        }

        /// <summary>The values no longer come from a preset (🎲).</summary>
        public void MarkCustom()
        {
            Select(CustomId);
        }

        public UserPresetError SaveCurrentAs(string name)
        {
            IModeDefinition mode = _sim.Mode;
            if (mode == null || _sim.CurrentValues == null) return UserPresetError.NotFound;
            string id = "user." + Guid.NewGuid().ToString("N").Substring(0, 12);
            UserPresetError error = _ctx.Presets.Library.TryAdd(id, mode.Info.Id, name, _sim.CurrentValues, out Preset preset);
            if (error != UserPresetError.None) return error;
            _ctx.Presets.MarkDirty();
            _ctx.Presets.Save();
            Rebuild();
            Select(preset.Id);
            return UserPresetError.None;
        }

        public UserPresetError Rename(string id, string name)
        {
            UserPresetError error = _ctx.Presets.Library.TryRename(id, name, out _);
            if (error != UserPresetError.None) return error;
            _ctx.Presets.MarkDirty();
            _ctx.Presets.Save();
            Rebuild();
            return UserPresetError.None;
        }

        public void Delete(string id)
        {
            if (!_ctx.Presets.Library.Delete(id)) return;
            _ctx.Presets.MarkDirty();
            _ctx.Presets.Save();
            if (_selected == id) Select(CustomId);
            Rebuild();
        }

        void Select(string id)
        {
            _selected = id;
            IModeDefinition mode = _sim.Mode;
            if (mode != null) _ctx.Settings.Update(d => d.LastPreset[mode.Info.Id] = id);
            Notify();
        }

        internal void NotifyChanged() => Notify();

        static bool SameIgnoringPacing(ParamSet a, ParamSet b)
        {
            var ids = new HashSet<string>(a.Ids, StringComparer.Ordinal);
            ids.UnionWith(b.Ids);
            foreach (string id in ids)
            {
                if (id == CommonParams.Seed || id == CommonParams.TimeScale) continue;
                bool ha = a.TryGet(id, out ParamValue va);
                bool hb = b.TryGet(id, out ParamValue vb);
                if (ha != hb) return false;
                if (ha && !va.Equals(vb)) return false;
            }
            return true;
        }
    }
}
