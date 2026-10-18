using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.UI.Components;
using Ballverse.UI.Params;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Settings sheet over the simulation (spec §2.2 panel rules, §4.3): 50% / 90% detents, no scrim so the simulation
    /// stays visible and reacts live; group switch; rows generated from the mode's <see cref="ParamSchema"/>; live edits
    /// apply at once, structural edits while running are collected and applied with "Перезапустить"; reset to preset.
    /// </summary>
    public sealed class SimSettingsSheet
    {
        readonly UiContext _ctx;
        readonly Func<IParamPanelViewModel> _vmSource;
        readonly Dictionary<string, IParamEditor> _editors = new Dictionary<string, IParamEditor>(StringComparer.Ordinal);
        readonly List<ParamGroup> _groups = new List<ParamGroup>();
        IParamPanelViewModel _vm;
        BottomSheet _sheet;
        ParamPanelModel _model;
        ParamGroup _group;
        ChipRow _groupChips;
        VisualElement _pendingBar;
        Label _pendingLabel;
        VisualElement _rows;

        public SimSettingsSheet(UiContext ctx, Func<IParamPanelViewModel> vmSource)
        {
            _ctx = ctx;
            _vmSource = vmSource;
        }

        public bool IsOpen => _sheet != null && _sheet.IsOpen;

        public void Open()
        {
            if (IsOpen) return;
            _vm = _vmSource();
            if (_vm == null || _vm.Schema == null) return;
            _model = new ParamPanelModel(_vm.Schema, _vm.Values, _vm.Tier);
            _sheet = new BottomSheet(_ctx, new SheetOptions
            {
                TitleKey = "sim.settings.title",
                Detents = SheetDetents.HalfAndFull,
                ScrimOpacity = 0f,
                Closed = OnClosed,
            });
            _groupChips = new ChipRow();
            _groupChips.AddToClassList("bv-sheet__groups");
            _groupChips.SelectionChanged += i =>
            {
                if (i >= 0 && i < _groups.Count) SelectGroup(_groups[i]);
            };
            _sheet.Toolbar.Add(_groupChips);

            _pendingBar = new VisualElement();
            _pendingBar.AddToClassList("bv-pending-bar");
            _pendingLabel = new Label();
            _pendingLabel.AddToClassList("bv-pending-bar__label");
            _pendingBar.Add(_pendingLabel);
            var restart = FormBuilder.CreateButton(_ctx, "sim.settings.restart", IconKind.Retry, "primary");
            restart.AddToClassList("bv-btn--small");
            Tap.On(restart, Restart);
            _pendingBar.Add(restart);
            _sheet.Toolbar.Add(_pendingBar);

            _rows = new VisualElement();
            _rows.AddToClassList("bv-form");
            _sheet.Content.Add(_rows);
            var footer = new FormBuilder(_ctx, _sheet.Content);
            footer.Button("sim.settings.reset_preset", IconKind.Restore, () =>
            {
                _model.DiscardPending();
                _vm.ResetToPreset();
            }, "plain");

            _group = ParamGroup.Basic;
            RebuildGroups();
            RebuildRows();
            UpdatePending();
            _vm.Changed += OnVmChanged;
            _sheet.Show();
        }

        public void Close()
        {
            _sheet?.Close();
        }

        void OnClosed()
        {
            if (_vm != null) _vm.Changed -= OnVmChanged;
            _editors.Clear();
            _sheet = null;
        }

        void RebuildGroups()
        {
            _groups.Clear();
            _groups.AddRange(_model.VisibleGroups());
            if (!_groups.Contains(_group) && _groups.Count > 0) _group = _groups[0];
            var chips = new ChipRow();
            chips.AddToClassList("bv-sheet__groups");
            foreach (var g in _groups) chips.AddChip(_ctx.L(ParamValueFormatter.GroupLabelKey(g)));
            chips.SetSelectedWithoutNotify(_groups.IndexOf(_group));
            chips.SelectionChanged += i =>
            {
                if (i >= 0 && i < _groups.Count) SelectGroup(_groups[i]);
            };
            int index = _sheet.Toolbar.IndexOf(_groupChips);
            _sheet.Toolbar.Insert(Math.Max(0, index), chips);
            _groupChips.RemoveFromHierarchy();
            _groupChips = chips;
        }

        void SelectGroup(ParamGroup group)
        {
            _group = group;
            RebuildRows();
            if (_sheet.Scroll != null) _sheet.Scroll.scrollOffset = UnityEngine.Vector2.zero;
        }

        void RebuildRows()
        {
            _rows.Clear();
            _editors.Clear();
            foreach (var row in _model.Rows(_group))
            {
                var editor = ParamEditorFactory.Create(_ctx, row, OnEdit);
                _editors[row.Id] = editor;
                _rows.Add(editor.Root);
            }
        }

        void OnEdit(string id, ParamValue raw, bool final)
        {
            if (!final)
            {
                // While dragging: live parameters (or any parameter in Setup that is live) update continuously;
                // structural ones wait for the release (scene rebuild / restart offer happens once).
                var def = _model.Schema.Get(id);
                if (!def.Live) return;
            }
            var edit = _model.Edit(id, raw, _vm.IsRunning);
            if (edit.Kind == ParamEditKind.ApplyNow) _vm.Apply(id, edit.Value);
            if (edit.VisibilityChanged)
            {
                RebuildGroups();
                RebuildRows();
            }
            else if (_editors.TryGetValue(id, out var editor))
            {
                editor.SetRow(_model.Row(id));
            }
            UpdatePending();
        }

        void Restart()
        {
            var values = _model.CommitPendingForRestart();
            _vm.RestartWith(values);
            UpdatePending();
            RefreshEditors();
        }

        void UpdatePending()
        {
            bool pending = _model.HasPending && _vm.IsRunning;
            _pendingBar.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
            if (pending) _pendingLabel.text = _ctx.Localizer.Plural("sim.settings.pending", _model.PendingCount);
        }

        void OnVmChanged()
        {
            if (_sheet == null) return;
            if (!ReferenceEquals(_vm.Schema, _model.Schema) || _vm.Tier != _model.Tier)
            {
                _model = new ParamPanelModel(_vm.Schema, _vm.Values, _vm.Tier);
                RebuildGroups();
                RebuildRows();
                UpdatePending();
                return;
            }
            var before = VisibleIds();
            _model.SyncApplied(_vm.Values);
            if (!_vm.IsRunning && _model.HasPending)
            {
                // The run ended (Finished / Setup): pending structural edits now apply directly.
                var values = _model.CommitPendingForRestart();
                foreach (string id in values.Ids)
                    if (!values.Get(id).Equals(_vm.Values.TryGet(id, out var v) ? v : null)) _vm.Apply(id, values.Get(id));
            }
            if (!SameIds(before, VisibleIds()))
            {
                RebuildGroups();
                RebuildRows();
            }
            else
            {
                RefreshEditors();
            }
            UpdatePending();
        }

        void RefreshEditors()
        {
            foreach (var kv in _editors) kv.Value.SetRow(_model.Row(kv.Key));
        }

        List<string> VisibleIds()
        {
            var ids = new List<string>();
            foreach (var g in _model.VisibleGroups())
                foreach (var r in _model.Rows(g)) ids.Add(r.Id);
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
