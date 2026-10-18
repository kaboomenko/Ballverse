using System;
using Ballverse.Core.Params.Presets;
using Ballverse.UI.Components;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Presets sheet (spec §2.3): built-in, new (remote, "Новое" badge) and own presets; tap applies; long press on an
    /// own preset renames or deletes it; "save current" asks for a name (1–32 characters, up to 20 per mode).
    /// </summary>
    public sealed class PresetsSheet
    {
        readonly UiContext _ctx;
        readonly Func<IPresetsViewModel> _vmSource;
        IPresetsViewModel _vm;
        BottomSheet _sheet;
        VisualElement _list;

        public PresetsSheet(UiContext ctx, Func<IPresetsViewModel> vmSource)
        {
            _ctx = ctx;
            _vmSource = vmSource;
        }

        public void Open()
        {
            if (_sheet != null && _sheet.IsOpen) return;
            _vm = _vmSource();
            if (_vm == null) return;
            _sheet = new BottomSheet(_ctx, new SheetOptions
            {
                TitleKey = "presets.title",
                Detents = SheetDetents.HalfAndFull,
                ScrimOpacity = 0.2f,
                Closed = () =>
                {
                    if (_vm != null) _vm.Changed -= Rebuild;
                    _sheet = null;
                },
            });
            _list = new VisualElement();
            _list.AddToClassList("bv-form");
            _sheet.Content.Add(_list);
            _vm.Changed += Rebuild;
            Rebuild();
            _sheet.Show();
        }

        public void Close()
        {
            _sheet?.Close();
        }

        void Rebuild()
        {
            if (_sheet == null) return;
            _list.Clear();
            AddSection(PresetSource.Remote, "presets.section.new");
            AddSection(PresetSource.BuiltIn, "presets.section.builtin");
            AddSection(PresetSource.User, "presets.section.mine");
            var form = new FormBuilder(_ctx, _list);
            form.Note(_ctx.Format("presets.count", _vm.UserPresetCount, _vm.MaxUserPresets));
            var save = form.Button("presets.save_current", IconKind.Plus, SaveCurrent, "secondary");
            save.SetEnabled(_vm.UserPresetCount < _vm.MaxUserPresets);
        }

        void AddSection(PresetSource source, string titleKey)
        {
            bool any = false;
            foreach (var p in _vm.Presets)
            {
                if (p.Source != source) continue;
                if (!any)
                {
                    _list.Add(_ctx.KeyLabel(titleKey, "bv-section-title"));
                    any = true;
                }
                _list.Add(BuildItem(p));
            }
        }

        VisualElement BuildItem(PresetItemData p)
        {
            var row = new VisualElement();
            row.AddToClassList("bv-row");
            row.AddToClassList("bv-row--nav");
            row.AddToClassList("bv-preset");
            bool selected = p.Id == _vm.SelectedId;
            row.EnableInClassList("bv-preset--selected", selected);
            var thumb = new VisualElement();
            thumb.AddToClassList("bv-preset__thumb");
            TextureSlot.Set(thumb, p.Thumbnail);
            row.Add(thumb);
            row.Add(UiContext.TextLabel(_ctx.L(p.Name), "bv-row__label"));
            if (p.IsNew)
            {
                var badge = new VisualElement();
                badge.AddToClassList("bv-badge");
                badge.AddToClassList("bv-badge--accent");
                badge.Add(_ctx.KeyLabel("badge.new", "bv-badge__label"));
                row.Add(badge);
            }
            if (selected) row.Add(Icons.Create(IconKind.Check, "bv-row__trailing-icon"));
            string id = p.Id;
            if (p.Source == PresetSource.User)
            {
                row.AddManipulator(new TapOrLongPress(() => Apply(id), () => ShowUserActions(p)));
                var more = new VisualElement();
                more.AddToClassList("bv-icon-btn");
                more.AddToClassList("bv-icon-btn--small");
                more.Add(Icons.Create(IconKind.More));
                Tap.On(more, () => ShowUserActions(p));
                row.Add(more);
            }
            else
            {
                Tap.On(row, () => Apply(id));
            }
            return row;
        }

        void Apply(string id)
        {
            _vm.Apply(id);
            Close();
        }

        void ShowUserActions(PresetItemData p)
        {
            var spec = new DialogSpec { Title = p.Name };
            spec.Buttons.Add(DialogButton.Key("common.rename", DialogButtonStyle.Secondary, () => Rename(p)));
            spec.Buttons.Add(DialogButton.Key("common.delete", DialogButtonStyle.Destructive, () =>
                _ctx.Dialogs.Confirm("presets.delete_title", "presets.delete_body", "common.delete", true, () => _vm.Delete(p.Id))));
            spec.Buttons.Add(DialogButton.Key("common.cancel", DialogButtonStyle.Plain, null));
            _ctx.Dialogs.Show(spec);
        }

        void Rename(PresetItemData p)
        {
            AskName("presets.rename_title", _ctx.L(p.Name), name => _vm.Rename(p.Id, name));
        }

        void SaveCurrent()
        {
            AskName("presets.save_title", "", name => _vm.SaveCurrentAs(name));
        }

        void AskName(string titleKey, string initial, Func<string, UserPresetError> submit)
        {
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey(titleKey),
                TextFieldValue = initial ?? "",
                TextFieldMaxLength = Preset.MaxNameLength,
            };
            spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("common.save"), DialogButtonStyle.Primary, text =>
            {
                var error = submit(text ?? "");
                if (error != UserPresetError.None) _ctx.Toasts.Show(_ctx.L(ErrorKey(error)));
            }));
            spec.Buttons.Add(DialogButton.Key("common.cancel", DialogButtonStyle.Plain, null));
            _ctx.Dialogs.Show(spec);
        }

        static string ErrorKey(UserPresetError error)
        {
            switch (error)
            {
                case UserPresetError.InvalidName: return "presets.error.name";
                case UserPresetError.LimitReached: return "presets.error.limit";
                case UserPresetError.NotFound: return "presets.error.not_found";
                default: return "presets.error.generic";
            }
        }
    }

    /// <summary>Long press on 🎲: style and duration of the generation (spec §2.4, §4.3).</summary>
    public sealed class GenerateSheet
    {
        readonly UiContext _ctx;
        readonly Func<ISimulationViewModel> _vmSource;
        BottomSheet _sheet;

        public GenerateSheet(UiContext ctx, Func<ISimulationViewModel> vmSource)
        {
            _ctx = ctx;
            _vmSource = vmSource;
        }

        public void Open()
        {
            if (_sheet != null && _sheet.IsOpen) return;
            var vm = _vmSource();
            if (vm == null) return;
            _sheet = new BottomSheet(_ctx, new SheetOptions
            {
                TitleKey = "generate.title",
                Detents = SheetDetents.FitContent,
                ScrimOpacity = 0.25f,
                Closed = () => _sheet = null,
            });
            string style = vm.SelectedStyleId;
            string duration = vm.SelectedDurationId;
            var form = new FormBuilder(_ctx, _sheet.Content);
            var styles = vm.GenerationStyles;
            var durations = vm.GenerationDurations;
            if (styles.Count > 0)
            {
                var labels = new string[styles.Count];
                int selected = 0;
                for (int i = 0; i < styles.Count; i++)
                {
                    labels[i] = _ctx.L(styles[i].Label);
                    if (styles[i].Id == style) selected = i;
                }
                style = styles[selected].Id;
                form.Chips("generate.style", labels, selected, i => style = styles[i].Id);
            }
            if (!vm.IsInteractive && durations.Count > 0)
            {
                var labels = new string[durations.Count];
                int selected = 0;
                for (int i = 0; i < durations.Count; i++)
                {
                    labels[i] = _ctx.L(durations[i].Label);
                    if (durations[i].Id == duration) selected = i;
                }
                duration = durations[selected].Id;
                form.Chips("generate.duration", labels, selected, i => duration = durations[i].Id);
            }
            else if (vm.IsInteractive)
            {
                form.NoteKey("generate.interactive_note");
            }
            form.Button("generate.go", IconKind.Dice, () =>
            {
                _sheet?.Close();
                vm.Generate(style, duration);
            }, "primary");
            _sheet.Show();
        }

        public void Close()
        {
            _sheet?.Close();
        }
    }
}
