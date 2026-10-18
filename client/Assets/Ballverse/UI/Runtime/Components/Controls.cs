using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.UI.Params;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    /// <summary>
    /// Slider with a 44 pt touch area, grid snapping and optional magnets (spec §3.3, §4.4). Reports changes while
    /// dragging (<c>final = false</c>) and on release (<c>final = true</c>).
    /// </summary>
    public sealed class BvSlider : VisualElement
    {
        readonly VisualElement _track;
        readonly VisualElement _fill;
        readonly VisualElement _thumb;
        double _min;
        double _max = 1;
        double _step;
        double _value;
        int _pointer = -1;
        Vector2 _downPos;
        bool _dragging;
        const float DragSlop = 6f;

        /// <summary>(value, final) — final is true on release / tap.</summary>
        public event Action<double, bool> ValueChanged;
        /// <summary>Optional extra snapping applied after the grid (e.g. proportion magnets).</summary>
        public Func<double, double> Snap { get; set; }

        public BvSlider()
        {
            AddToClassList("bv-slider");
            _track = new VisualElement();
            _track.AddToClassList("bv-slider__track");
            _fill = new VisualElement();
            _fill.AddToClassList("bv-slider__fill");
            _track.Add(_fill);
            _thumb = new VisualElement();
            _thumb.AddToClassList("bv-slider__thumb");
            Add(_track);
            Add(_thumb);
            _track.pickingMode = PickingMode.Ignore;
            _fill.pickingMode = PickingMode.Ignore;
            _thumb.pickingMode = PickingMode.Ignore;
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCancelEvent>(e => EndDrag());
            RegisterCallback<GeometryChangedEvent>(e => Layout());
        }

        public double Value => _value;

        public void Configure(double min, double max, double step, double value)
        {
            _min = min;
            _max = Math.Max(min, max);
            _step = step;
            SetValueWithoutNotify(value);
        }

        public void SetValueWithoutNotify(double value)
        {
            _value = ParamValueFormatter.SnapToStep(value, _min, _max, _step);
            Layout();
        }

        void Layout()
        {
            float w = layout.width;
            if (float.IsNaN(w) || w <= 0) return;
            float t = _max > _min ? (float)((_value - _min) / (_max - _min)) : 0f;
            t = Mathf.Clamp01(t);
            float thumb = _thumb.resolvedStyle.width;
            if (float.IsNaN(thumb)) thumb = 0;
            float usable = Mathf.Max(0f, w - thumb);
            _thumb.style.left = usable * t;
            _fill.style.width = Length.Percent(t * 100f);
        }

        double ValueAt(float localX)
        {
            float thumb = _thumb.resolvedStyle.width;
            if (float.IsNaN(thumb)) thumb = 0;
            float usable = Mathf.Max(1f, layout.width - thumb);
            float t = Mathf.Clamp01((localX - thumb * 0.5f) / usable);
            double v = _min + t * (_max - _min);
            v = ParamValueFormatter.SnapToStep(v, _min, _max, _step);
            if (Snap != null) v = ParamValueFormatter.SnapToStep(Snap(v), _min, _max, _step);
            return v;
        }

        void OnDown(PointerDownEvent e)
        {
            if (_pointer >= 0 || !enabledInHierarchy) return;
            // Do not capture yet: a vertical swipe that starts on the slider must still scroll the sheet. The drag
            // starts once the movement is mostly horizontal; a tap without movement jumps to the touched value.
            _pointer = e.pointerId;
            _downPos = e.position;
            _dragging = false;
        }

        void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != _pointer) return;
            Vector2 d = (Vector2)e.position - _downPos;
            if (!_dragging)
            {
                if (Mathf.Abs(d.y) > DragSlop && Mathf.Abs(d.y) > Mathf.Abs(d.x))
                {
                    _pointer = -1;
                    return;
                }
                if (Mathf.Abs(d.x) <= DragSlop) return;
                _dragging = true;
                this.CapturePointer(_pointer);
                AddToClassList("bv-slider--active");
            }
            Update(e.localPosition.x, false);
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != _pointer) return;
            Update(e.localPosition.x, true);
            EndDrag();
            e.StopPropagation();
        }

        void EndDrag()
        {
            int id = _pointer;
            _pointer = -1;
            _dragging = false;
            RemoveFromClassList("bv-slider--active");
            if (id >= 0 && this.HasPointerCapture(id)) this.ReleasePointer(id);
        }

        void Update(float x, bool final)
        {
            double v = ValueAt(x);
            bool changed = Math.Abs(v - _value) > 1e-12;
            _value = v;
            Layout();
            if (changed || final) ValueChanged?.Invoke(_value, final);
        }
    }

    /// <summary>On/off switch.</summary>
    public sealed class BvSwitch : VisualElement
    {
        bool _value;

        public event Action<bool> ValueChanged;

        public BvSwitch()
        {
            AddToClassList("bv-switch");
            var knob = new VisualElement();
            knob.AddToClassList("bv-switch__knob");
            knob.pickingMode = PickingMode.Ignore;
            Add(knob);
            Tap.On(this, () =>
            {
                SetValueWithoutNotify(!_value);
                ValueChanged?.Invoke(_value);
            });
        }

        public bool Value => _value;

        public void SetValueWithoutNotify(bool value)
        {
            _value = value;
            EnableInClassList("bv-switch--on", value);
        }
    }

    /// <summary>Segmented control (settings groups, layouts, resolution / FPS).</summary>
    public sealed class SegmentedControl : VisualElement
    {
        readonly List<VisualElement> _segments = new List<VisualElement>();
        int _selected = -1;

        public event Action<int> SelectionChanged;

        public SegmentedControl()
        {
            AddToClassList("bv-segmented");
        }

        public int Selected => _selected;
        public int Count => _segments.Count;

        /// <summary>Adds a segment with a text label and/or icon.</summary>
        public VisualElement AddSegment(string text, IconKind icon = IconKind.None)
        {
            var seg = new VisualElement();
            seg.AddToClassList("bv-segmented__item");
            if (icon != IconKind.None) seg.Add(Icons.Create(icon, "bv-segmented__icon"));
            if (text != null)
            {
                var label = new Label(text);
                label.AddToClassList("bv-segmented__label");
                label.pickingMode = PickingMode.Ignore;
                seg.Add(label);
            }
            int index = _segments.Count;
            Tap.On(seg, () =>
            {
                if (index == _selected) return;
                SetSelectedWithoutNotify(index);
                SelectionChanged?.Invoke(index);
            });
            _segments.Add(seg);
            Add(seg);
            return seg;
        }

        public void SetLabel(int index, string text)
        {
            var label = _segments[index].Q<Label>();
            if (label != null) label.text = text;
        }

        public void RemoveAllSegments()
        {
            _segments.Clear();
            Clear();
            _selected = -1;
        }

        public void SetSelectedWithoutNotify(int index)
        {
            _selected = index;
            for (int i = 0; i < _segments.Count; i++) _segments[i].EnableInClassList("bv-segmented__item--selected", i == index);
        }
    }

    /// <summary>Horizontally scrolling chips for long option lists.</summary>
    public sealed class ChipRow : VisualElement
    {
        readonly ScrollView _scroll;
        readonly List<VisualElement> _chips = new List<VisualElement>();
        int _selected = -1;

        public event Action<int> SelectionChanged;

        public ChipRow()
        {
            AddToClassList("bv-chips");
            _scroll = new ScrollView(ScrollViewMode.Horizontal);
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _scroll.AddToClassList("bv-chips__scroll");
            Add(_scroll);
        }

        public int Selected => _selected;

        public VisualElement AddChip(string text)
        {
            var chip = new VisualElement();
            chip.AddToClassList("bv-chip");
            var label = new Label(text);
            label.AddToClassList("bv-chip__label");
            label.pickingMode = PickingMode.Ignore;
            chip.Add(label);
            int index = _chips.Count;
            Tap.On(chip, () =>
            {
                if (index == _selected) return;
                SetSelectedWithoutNotify(index);
                SelectionChanged?.Invoke(index);
            });
            _chips.Add(chip);
            _scroll.Add(chip);
            return chip;
        }

        public void SetSelectedWithoutNotify(int index)
        {
            _selected = index;
            for (int i = 0; i < _chips.Count; i++) _chips[i].EnableInClassList("bv-chip--selected", i == index);
        }
    }

    /// <summary>Palette of colour swatches (colour parameters, text fill).</summary>
    public sealed class ColorSwatches : VisualElement
    {
        /// <summary>Curated palette: neon and pastel tones that read well on dark and light arenas.</summary>
        public static readonly ColorRgba[] Palette =
        {
            new ColorRgba(0xFF, 0x3B, 0x5C), new ColorRgba(0xFF, 0x8A, 0x00), new ColorRgba(0xFF, 0xD6, 0x0A),
            new ColorRgba(0x7C, 0xFF, 0x4F), new ColorRgba(0x00, 0xE5, 0xA8), new ColorRgba(0x00, 0xD1, 0xFF),
            new ColorRgba(0x3D, 0x7B, 0xFF), new ColorRgba(0x8B, 0x5C, 0xFF), new ColorRgba(0xD9, 0x46, 0xEF),
            new ColorRgba(0xFF, 0x6F, 0xB5), new ColorRgba(0xFF, 0xFF, 0xFF), new ColorRgba(0xB8, 0xC0, 0xCC),
            new ColorRgba(0x5A, 0x63, 0x70), new ColorRgba(0x10, 0x10, 0x18), new ColorRgba(0xF5, 0xE6, 0xC8),
            new ColorRgba(0x8C, 0x5A, 0x3C),
        };

        readonly List<VisualElement> _items = new List<VisualElement>();

        public event Action<ColorRgba> Picked;

        public ColorSwatches()
        {
            AddToClassList("bv-swatches");
            foreach (var c in Palette)
            {
                var sw = new VisualElement();
                sw.AddToClassList("bv-swatch");
                sw.style.backgroundColor = ToColor(c);
                var captured = c;
                Tap.On(sw, () =>
                {
                    SetSelectedWithoutNotify(captured);
                    Picked?.Invoke(captured);
                });
                _items.Add(sw);
                Add(sw);
            }
        }

        public void SetSelectedWithoutNotify(ColorRgba color)
        {
            for (int i = 0; i < Palette.Length; i++) _items[i].EnableInClassList("bv-swatch--selected", Palette[i] == color);
        }

        public static Color ToColor(ColorRgba c)
        {
            return new Color32(c.R, c.G, c.B, c.A);
        }
    }

    /// <summary>Indeterminate activity indicator (rotating arc).</summary>
    public sealed class Spinner : VisualElement
    {
        IVisualElementScheduledItem _spin;
        float _angle;

        public Spinner()
        {
            AddToClassList("bv-spinner");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(e => _spin = schedule.Execute(() =>
            {
                _angle = (_angle + (Tween.ReduceMotion ? 0f : 9f)) % 360f;
                MarkDirtyRepaint();
            }).Every(16));
            RegisterCallback<DetachFromPanelEvent>(e => _spin?.Pause());
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float radius = Mathf.Min(r.width, r.height) * 0.5f - 2f;
            if (radius <= 1f) return;
            var p = ctx.painter2D;
            p.lineWidth = 2.5f;
            p.lineCap = LineCap.Round;
            p.strokeColor = resolvedStyle.color;
            p.BeginPath();
            p.Arc(r.center, radius, Angle.Degrees(_angle), Angle.Degrees(_angle + 270f), ArcDirection.Clockwise);
            p.Stroke();
        }
    }
}
