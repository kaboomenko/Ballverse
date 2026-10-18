using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    /// <summary>Slider row: label, current value text and the slider.</summary>
    public sealed class SliderRow
    {
        readonly Func<double, string> _format;

        public VisualElement Root { get; }
        public BvSlider Slider { get; }
        public Label ValueLabel { get; }

        internal SliderRow(VisualElement root, BvSlider slider, Label valueLabel, Func<double, string> format)
        {
            Root = root;
            Slider = slider;
            ValueLabel = valueLabel;
            _format = format;
        }

        public void SetValueWithoutNotify(double value)
        {
            Slider.SetValueWithoutNotify(value);
            ValueLabel.text = _format != null ? _format(Slider.Value) : "";
        }

        internal void RefreshLabel()
        {
            ValueLabel.text = _format != null ? _format(Slider.Value) : "";
        }
    }

    /// <summary>
    /// Builds the standard settings rows used by every sheet and the settings screen (one visual language, 44 pt
    /// targets, 4 pt grid). All labels are localization keys.
    /// </summary>
    public sealed class FormBuilder
    {
        readonly UiContext _ctx;

        public VisualElement Container { get; }

        public FormBuilder(UiContext ctx, VisualElement container)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public Label Section(string key)
        {
            var label = _ctx.KeyLabel(key, "bv-section-title");
            Container.Add(label);
            return label;
        }

        public SliderRow Slider(string labelKey, double min, double max, double step, double value, Func<double, string> format,
            Action<double, bool> changed)
        {
            var row = NewRow("bv-row--slider");
            var header = new VisualElement();
            header.AddToClassList("bv-row__header");
            header.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var valueLabel = new Label();
            valueLabel.AddToClassList("bv-row__value");
            header.Add(valueLabel);
            row.Add(header);
            var slider = new BvSlider();
            slider.Configure(min, max, step, value);
            row.Add(slider);
            var result = new SliderRow(row, slider, valueLabel, format);
            result.RefreshLabel();
            slider.ValueChanged += (v, final) =>
            {
                result.RefreshLabel();
                changed?.Invoke(v, final);
            };
            Container.Add(row);
            return result;
        }

        public BvSwitch Toggle(string labelKey, bool value, Action<bool> changed, string noteKey = null)
        {
            var row = NewRow("bv-row--inline");
            var texts = new VisualElement();
            texts.AddToClassList("bv-row__texts");
            texts.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            if (noteKey != null) texts.Add(_ctx.KeyLabel(noteKey, "bv-row__note"));
            row.Add(texts);
            var sw = new BvSwitch();
            sw.SetValueWithoutNotify(value);
            sw.ValueChanged += v => changed?.Invoke(v);
            row.Add(sw);
            Container.Add(row);
            return sw;
        }

        /// <param name="labelKey">Row label; null for a control without a label.</param>
        public SegmentedControl Segmented(string labelKey, IList<string> labels, int selected, Action<int> changed,
            IList<IconKind> icons = null)
        {
            var row = NewRow("bv-row--control");
            if (labelKey != null) row.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var seg = new SegmentedControl();
            int n = Math.Max(labels?.Count ?? 0, icons?.Count ?? 0);
            for (int i = 0; i < n; i++)
                seg.AddSegment(labels != null && i < labels.Count ? labels[i] : null, icons != null && i < icons.Count ? icons[i] : IconKind.None);
            seg.SetSelectedWithoutNotify(selected);
            seg.SelectionChanged += i => changed?.Invoke(i);
            row.Add(seg);
            Container.Add(row);
            return seg;
        }

        public ChipRow Chips(string labelKey, IList<string> labels, int selected, Action<int> changed)
        {
            var row = NewRow("bv-row--control");
            if (labelKey != null) row.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var chips = new ChipRow();
            foreach (string l in labels) chips.AddChip(l);
            chips.SetSelectedWithoutNotify(selected);
            chips.SelectionChanged += i => changed?.Invoke(i);
            row.Add(chips);
            Container.Add(row);
            return chips;
        }

        public ColorSwatches Colors(string labelKey, ColorRgba value, Action<ColorRgba> picked)
        {
            var row = NewRow("bv-row--control");
            if (labelKey != null) row.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var sw = new ColorSwatches();
            sw.SetSelectedWithoutNotify(value);
            sw.Picked += c => picked?.Invoke(c);
            row.Add(sw);
            Container.Add(row);
            return sw;
        }

        /// <summary>Navigation / action row: icon, label, trailing value, chevron or lock.</summary>
        public VisualElement NavRow(IconKind icon, string labelKey, string value, Action tap, bool locked = false, bool chevron = true)
        {
            var row = NewRow("bv-row--nav");
            if (icon != IconKind.None) row.Add(Icons.Create(icon, "bv-row__icon"));
            row.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var valueLabel = UiContext.TextLabel(value ?? "", "bv-row__value");
            valueLabel.name = "value";
            row.Add(valueLabel);
            if (locked) row.Add(Icons.Create(IconKind.Lock, "bv-row__trailing-icon"));
            else if (chevron && tap != null) row.Add(Icons.Create(IconKind.ChevronRight, "bv-row__trailing-icon"));
            if (tap != null) Tap.On(row, tap);
            Container.Add(row);
            return row;
        }

        /// <param name="style">"primary", "secondary", "destructive" or "plain".</param>
        public VisualElement Button(string labelKey, IconKind icon, Action tap, string style = "secondary")
        {
            var b = CreateButton(_ctx, labelKey, icon, style);
            b.AddToClassList("bv-btn--block");
            Tap.On(b, tap);
            Container.Add(b);
            return b;
        }

        public Label Note(string text, string className = "bv-note")
        {
            var label = UiContext.TextLabel(text, className);
            Container.Add(label);
            return label;
        }

        public Label NoteKey(string key, string className = "bv-note")
        {
            var label = _ctx.KeyLabel(key, className);
            Container.Add(label);
            return label;
        }

        /// <summary>Text input; <paramref name="committed"/> fires on Enter or focus loss.</summary>
        public TextField Text(string labelKey, string value, int maxLength, bool multiline, Action<string> committed)
        {
            var row = NewRow("bv-row--control");
            if (labelKey != null) row.Add(_ctx.KeyLabel(labelKey, "bv-row__label"));
            var field = new TextField { value = value ?? "", maxLength = maxLength, multiline = multiline, isDelayed = true };
            field.AddToClassList("bv-textfield");
            if (multiline) field.AddToClassList("bv-textfield--multiline");
            field.RegisterValueChangedCallback(e => committed?.Invoke(e.newValue));
            row.Add(field);
            Container.Add(row);
            return field;
        }

        public VisualElement Divider()
        {
            var d = new VisualElement();
            d.AddToClassList("bv-divider");
            Container.Add(d);
            return d;
        }

        VisualElement NewRow(string modifier)
        {
            var row = new VisualElement();
            row.AddToClassList("bv-row");
            row.AddToClassList(modifier);
            return row;
        }

        /// <summary>Standalone button element (label key + optional icon) with the given style.</summary>
        public static VisualElement CreateButton(UiContext ctx, string labelKey, IconKind icon, string style)
        {
            var b = new VisualElement();
            b.AddToClassList("bv-btn");
            b.AddToClassList("bv-btn--" + (style ?? "secondary"));
            if (icon != IconKind.None) b.Add(Icons.Create(icon, "bv-btn__icon"));
            if (labelKey != null)
            {
                var label = ctx.KeyLabel(labelKey, "bv-btn__label");
                label.pickingMode = PickingMode.Ignore;
                b.Add(label);
            }
            return b;
        }
    }
}
