using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Params;
using Ballverse.UI.Components;
using Ballverse.UI.Params;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>Editor of one parameter row in the auto-generated settings sheet (spec §7.6).</summary>
    public interface IParamEditor
    {
        string Id { get; }
        VisualElement Root { get; }
        /// <summary>Shows a new snapshot of the row without raising edits.</summary>
        void SetRow(ParamRowModel row);
    }

    /// <summary>
    /// Creates editors by <see cref="ParamEditorKind"/>: slider (float / int with ∞), toggle, segmented / chips
    /// (enum, template), colour swatches, colour list, string list, seed. Edits are reported as raw values; the sheet
    /// sanitizes them through <see cref="ParamPanelModel"/>. <c>final</c> is false while a slider is dragged.
    /// </summary>
    public static class ParamEditorFactory
    {
        public delegate void EditHandler(string id, ParamValue value, bool final);

        public static IParamEditor Create(UiContext ctx, ParamRowModel row, EditHandler edit)
        {
            switch (row.Kind)
            {
                case ParamEditorKind.Slider:
                case ParamEditorKind.IntSlider:
                    return new SliderEditor(ctx, row, edit);
                case ParamEditorKind.Toggle:
                    return new ToggleEditor(ctx, row, edit);
                case ParamEditorKind.Segmented:
                case ParamEditorKind.Chips:
                    return new ChoiceEditor(ctx, row, edit);
                case ParamEditorKind.Color:
                    return new ColorEditor(ctx, row, edit);
                case ParamEditorKind.ColorList:
                    return new ColorListEditor(ctx, row, edit);
                case ParamEditorKind.StringList:
                    return new StringListEditor(ctx, row, edit);
                case ParamEditorKind.Seed:
                    return new SeedEditor(ctx, row, edit);
                default:
                    throw new ArgumentOutOfRangeException(nameof(row), row.Kind, "Unsupported editor");
            }
        }

        internal static VisualElement NewRow(string modifier, ParamRowModel row)
        {
            var e = new VisualElement();
            e.AddToClassList("bv-row");
            e.AddToClassList(modifier);
            e.AddToClassList("bv-param");
            e.EnableInClassList("bv-row--pending", row.Pending);
            return e;
        }

        internal static VisualElement Header(UiContext ctx, ParamRowModel row, out Label value)
        {
            var header = new VisualElement();
            header.AddToClassList("bv-row__header");
            var label = ctx.KeyLabel(row.LabelKey, "bv-row__label");
            header.Add(label);
            var pending = new VisualElement();
            pending.AddToClassList("bv-param__pending-dot");
            header.Add(pending);
            value = new Label();
            value.AddToClassList("bv-row__value");
            header.Add(value);
            return header;
        }
    }

    sealed class SliderEditor : IParamEditor
    {
        readonly UiContext _ctx;
        readonly ParamEditorFactory.EditHandler _edit;
        readonly BvSlider _slider;
        readonly Label _value;
        readonly Label _capNote;
        readonly VisualElement _unlimited;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public SliderEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            _ctx = ctx;
            _edit = edit;
            Root = ParamEditorFactory.NewRow("bv-row--slider", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out _value));
            var line = new VisualElement();
            line.AddToClassList("bv-param__slider-line");
            _slider = new BvSlider();
            _slider.AddToClassList("bv-param__slider");
            line.Add(_slider);
            if (row.AllowUnlimited)
            {
                _unlimited = new VisualElement();
                _unlimited.AddToClassList("bv-chip");
                _unlimited.AddToClassList("bv-param__unlimited");
                _unlimited.Add(ctx.KeyLabel("params.unlimited_short", "bv-chip__label"));
                Tap.On(_unlimited, ToggleUnlimited);
                line.Add(_unlimited);
            }
            Root.Add(line);
            _capNote = new Label();
            _capNote.AddToClassList("bv-row__note");
            Root.Add(_capNote);
            _slider.ValueChanged += OnSlider;
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            bool unlimited = row.Value.IsUnlimited;
            double v = unlimited ? row.Max : row.Value.AsFloat;
            // Never yank the thumb away from the finger while it is dragged.
            if (!_slider.ClassListContains("bv-slider--active")) _slider.Configure(row.Min, row.Max, row.Step, v);
            _slider.SetEnabled(!unlimited);
            _unlimited?.EnableInClassList("bv-chip--selected", unlimited);
            _value.text = ParamValueFormatter.Format(row, _ctx.Localizer);
            _capNote.style.display = row.TierCapped ? DisplayStyle.Flex : DisplayStyle.None;
            if (row.TierCapped)
                _capNote.text = _ctx.Format("params.tier_cap", ParamValueFormatter.FormatWithUnit(row.Max, row.Step, row.Unit, _ctx.Localizer));
        }

        void OnSlider(double v, bool final)
        {
            var value = _row.Kind == ParamEditorKind.IntSlider ? ParamValue.Int((long)Math.Round(v)) : ParamValue.Float(v);
            _value.text = _row.Kind == ParamEditorKind.IntSlider
                ? ParamValueFormatter.FormatWithUnit(Math.Round(v), _row.Step, _row.Unit, _ctx.Localizer)
                : ParamValueFormatter.FormatWithUnit(v, _row.Step, _row.Unit, _ctx.Localizer);
            _edit(_row.Id, value, final);
        }

        void ToggleUnlimited()
        {
            bool unlimited = _row.Value.IsUnlimited;
            _edit(_row.Id, unlimited ? ParamValue.Int((long)Math.Round(_slider.Value)) : ParamValue.Unlimited, true);
        }
    }

    sealed class ToggleEditor : IParamEditor
    {
        readonly BvSwitch _switch;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public ToggleEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            Root = ParamEditorFactory.NewRow("bv-row--inline", row);
            var texts = new VisualElement();
            texts.AddToClassList("bv-row__texts");
            texts.Add(ctx.KeyLabel(row.LabelKey, "bv-row__label"));
            Root.Add(texts);
            var pending = new VisualElement();
            pending.AddToClassList("bv-param__pending-dot");
            Root.Add(pending);
            _switch = new BvSwitch();
            _switch.ValueChanged += v => edit(_row.Id, ParamValue.Bool(v), true);
            Root.Add(_switch);
            // The whole row is the 44 pt tap target (spec §4.4); the switch itself is smaller.
            Tap.On(Root, _switch.Toggle, false);
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            _switch.SetValueWithoutNotify(row.Value.AsBool);
        }
    }

    sealed class ChoiceEditor : IParamEditor
    {
        readonly SegmentedControl _segmented;
        readonly ChipRow _chips;
        readonly Label _value;
        readonly UiContext _ctx;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public ChoiceEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            _ctx = ctx;
            Root = ParamEditorFactory.NewRow("bv-row--control", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out _value));
            if (row.Kind == ParamEditorKind.Segmented)
            {
                _segmented = new SegmentedControl();
                foreach (var o in row.Options) _segmented.AddSegment(ctx.L(o.LabelKey));
                _segmented.SelectionChanged += i => edit(_row.Id, ParamValue.String(_row.Options[i].Id), true);
                Root.Add(_segmented);
            }
            else
            {
                _chips = new ChipRow();
                foreach (var o in row.Options) _chips.AddChip(ctx.L(o.LabelKey));
                _chips.SelectionChanged += i => edit(_row.Id, ParamValue.String(_row.Options[i].Id), true);
                Root.Add(_chips);
            }
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            int index = -1;
            for (int i = 0; i < row.Options.Count; i++)
                if (row.Options[i].Id == row.Value.AsString) index = i;
            _segmented?.SetSelectedWithoutNotify(index);
            _chips?.SetSelectedWithoutNotify(index);
            _value.text = row.Kind == ParamEditorKind.Chips ? ParamValueFormatter.Format(row, _ctx.Localizer) : "";
        }
    }

    sealed class ColorEditor : IParamEditor
    {
        readonly ColorSwatches _swatches;
        readonly VisualElement _current;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public ColorEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            Root = ParamEditorFactory.NewRow("bv-row--control", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out Label value));
            value.style.display = DisplayStyle.None;
            _current = new VisualElement();
            _current.AddToClassList("bv-color-dot");
            Root.Q<VisualElement>(className: "bv-row__header").Add(_current);
            _swatches = new ColorSwatches();
            _swatches.Picked += c => edit(_row.Id, ParamValue.Color(c), true);
            Root.Add(_swatches);
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            var c = row.Value.AsColor;
            _swatches.SetSelectedWithoutNotify(c);
            _current.style.backgroundColor = ColorSwatches.ToColor(c);
        }
    }

    sealed class ColorListEditor : IParamEditor
    {
        readonly UiContext _ctx;
        readonly ParamEditorFactory.EditHandler _edit;
        readonly VisualElement _chips;
        readonly Label _value;
        readonly VisualElement _useTheme;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public ColorListEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            _ctx = ctx;
            _edit = edit;
            Root = ParamEditorFactory.NewRow("bv-row--control", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out _value));
            _chips = new VisualElement();
            _chips.AddToClassList("bv-color-list");
            Root.Add(_chips);
            Root.Add(ctx.KeyLabel("params.colors.add_hint", "bv-row__note"));
            var palette = new ColorSwatches();
            palette.Picked += Add;
            Root.Add(palette);
            _useTheme = FormBuilder.CreateButton(ctx, "params.colors.use_theme", IconKind.Palette, "plain");
            Tap.On(_useTheme, () => _edit(_row.Id, ParamValue.Colors(new ColorRgba[0]), true));
            Root.Add(_useTheme);
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            _value.text = ParamValueFormatter.Format(row, _ctx.Localizer);
            _chips.Clear();
            var colors = row.Value.AsColors;
            for (int i = 0; i < colors.Count; i++)
            {
                var chip = new VisualElement();
                chip.AddToClassList("bv-color-dot");
                chip.AddToClassList("bv-color-dot--removable");
                chip.style.backgroundColor = ColorSwatches.ToColor(colors[i]);
                chip.Add(Icons.Create(IconKind.Close, "bv-color-dot__remove"));
                int index = i;
                Tap.On(chip, () => Remove(index));
                _chips.Add(chip);
            }
            _useTheme.style.display = colors.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Add(ColorRgba c)
        {
            var list = new List<ColorRgba>(_row.Value.AsColors);
            if (_row.MaxCount > 0 && list.Count >= _row.MaxCount) return;
            list.Add(c);
            _edit(_row.Id, ParamValue.Colors(list), true);
        }

        void Remove(int index)
        {
            var list = new List<ColorRgba>(_row.Value.AsColors);
            if (index < 0 || index >= list.Count || list.Count <= _row.MinCount) return;
            list.RemoveAt(index);
            _edit(_row.Id, ParamValue.Colors(list), true);
        }
    }

    sealed class StringListEditor : IParamEditor
    {
        readonly UiContext _ctx;
        readonly TextField _field;
        readonly Label _value;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public StringListEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            _ctx = ctx;
            Root = ParamEditorFactory.NewRow("bv-row--control", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out _value));
            _field = new TextField { multiline = true, isDelayed = true };
            _field.AddToClassList("bv-textfield");
            _field.AddToClassList("bv-textfield--multiline");
            _field.RegisterValueChangedCallback(e =>
                edit(_row.Id, ParamValue.Strings(StringListParam.SplitPastedText(e.newValue)), true));
            Root.Add(_field);
            Root.Add(ctx.KeyLabel("params.names.hint", "bv-row__note"));
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            _value.text = ParamValueFormatter.Format(row, _ctx.Localizer);
            _field.SetValueWithoutNotify(string.Join("\n", row.Value.AsStrings));
        }
    }

    sealed class SeedEditor : IParamEditor
    {
        readonly TextField _field;
        ParamRowModel _row;

        public string Id => _row.Id;
        public VisualElement Root { get; }

        public SeedEditor(UiContext ctx, ParamRowModel row, ParamEditorFactory.EditHandler edit)
        {
            Root = ParamEditorFactory.NewRow("bv-row--control", row);
            Root.Add(ParamEditorFactory.Header(ctx, row, out Label value));
            value.style.display = DisplayStyle.None;
            var line = new VisualElement();
            line.AddToClassList("bv-param__seed-line");
            _field = new TextField { isDelayed = true, maxLength = 20 };
            _field.AddToClassList("bv-textfield");
            _field.AddToClassList("bv-param__seed-field");
            _field.RegisterValueChangedCallback(e =>
            {
                if (SeedParam.TryParse(e.newValue, out ulong seed)) edit(_row.Id, ParamValue.Seed(seed), true);
                else
                {
                    ctx.Toasts.Show(ctx.L("params.seed.invalid"));
                    _field.SetValueWithoutNotify(_row.Value.AsSeed.ToString(CultureInfo.InvariantCulture));
                }
            });
            line.Add(_field);
            var copy = new VisualElement();
            copy.AddToClassList("bv-icon-btn");
            copy.Add(Icons.Create(IconKind.Copy));
            Tap.On(copy, () =>
            {
                GUIUtility.systemCopyBuffer = _row.Value.AsSeed.ToString(CultureInfo.InvariantCulture);
                ctx.Toasts.Show(ctx.L("params.seed.copied"));
            });
            line.Add(copy);
            Root.Add(line);
            SetRow(row);
        }

        public void SetRow(ParamRowModel row)
        {
            _row = row;
            Root.EnableInClassList("bv-row--pending", row.Pending);
            _field.SetValueWithoutNotify(row.Value.AsSeed.ToString(CultureInfo.InvariantCulture));
        }
    }
}
