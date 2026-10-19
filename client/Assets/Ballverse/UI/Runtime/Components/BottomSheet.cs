using System;
using System.Collections.Generic;
using Ballverse.UI.Layout;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    public enum SheetDetents
    {
        /// <summary>50% / 90% of the screen height (spec §4.3 settings sheet).</summary>
        HalfAndFull = 0,
        /// <summary>Height of the content, at most 90%.</summary>
        FitContent = 1,
        /// <summary>Always 90%.</summary>
        Full = 2,
    }

    public sealed class SheetOptions
    {
        public string TitleKey;
        public SheetDetents Detents = SheetDetents.HalfAndFull;
        /// <summary>For <see cref="SheetDetents.HalfAndFull"/>: open at the higher detent.</summary>
        public bool StartExpanded;
        /// <summary>Scrim darkness 0–1; 0 keeps the simulation fully visible (settings sheet, spec §4.3).</summary>
        public float ScrimOpacity = 0.35f;
        /// <summary>Swipe down / scrim tap / back closes the sheet.</summary>
        public bool Dismissible = true;
        /// <summary>Wrap the content in a vertical scroll view (ignored for <see cref="SheetDetents.FitContent"/>).</summary>
        public bool Scrollable = true;
        /// <summary>Glass material (spec §4.5) or a solid surface (long lists, prices).</summary>
        public bool Glass = true;
        public float HalfFraction = 0.5f;
        public float FullFraction = 0.9f;
        public Action Closed;
    }

    /// <summary>
    /// Bottom sheet with two detents and swipe-to-dismiss (spec §4.3, §4.6). Drag on the grabber or header; release
    /// snaps by <see cref="SheetDetentSolver"/>. Content goes into <see cref="Content"/>; <see cref="Toolbar"/> holds
    /// fixed controls under the title (e.g. the settings group switch).
    /// </summary>
    public sealed class BottomSheet
    {
        readonly UiContext _ctx;
        readonly SheetOptions _options;
        readonly VisualElement _root;
        readonly VisualElement _scrim;
        readonly VisualElement _panel;
        readonly VisualElement _dragZone;
        readonly Label _title;
        float[] _detents = { 0f };
        int _detentIndex;
        float _visible;
        float _panelHeight;
        bool _measured;
        bool _closing;
        Tween _tween;
        int _dragPointer = -1;
        float _dragStartY;
        float _dragStartVisible;
        float _lastY;
        float _lastT;
        float _velocity;

        public VisualElement Root => _root;
        public VisualElement Panel => _panel;
        public VisualElement Toolbar { get; }
        public VisualElement Content { get; }
        public ScrollView Scroll { get; }
        public VisualElement HeaderTrailing { get; }
        public bool IsOpen { get; private set; }
        /// <summary>Close() was called and the sheet is animating out; <see cref="SheetOptions.Closed"/> follows.</summary>
        public bool IsClosing => IsOpen && _closing;
        /// <summary>Open and not on its way out: the sheet a screen should keep working with.</summary>
        public bool IsActive => IsOpen && !_closing;
        public SheetOptions Options => _options;
        internal SheetHost Host { get; set; }

        public BottomSheet(UiContext ctx, SheetOptions options)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _options = options ?? new SheetOptions();

            _root = new VisualElement();
            _root.AddToClassList("bv-sheet-root");
            _root.AddToClassList("bv-fill");
            // The root never takes touches itself: the scrim does when the sheet dims the screen, otherwise the
            // content behind (the live simulation) stays interactive (spec §4.3).
            _root.pickingMode = PickingMode.Ignore;

            _scrim = new VisualElement();
            _scrim.AddToClassList("bv-sheet-scrim");
            _scrim.AddToClassList("bv-fill");
            _scrim.style.opacity = 0f;
            if (_options.ScrimOpacity <= 0f) _scrim.pickingMode = PickingMode.Ignore;
            _root.Add(_scrim);
            if (_options.Dismissible) _scrim.RegisterCallback<PointerDownEvent>(e => Close());

            _panel = new VisualElement();
            _panel.AddToClassList("bv-sheet");
            _panel.AddToClassList(_options.Glass ? "bv-glass" : "bv-surface-solid");
            _panel.style.visibility = Visibility.Hidden;
            _root.Add(_panel);

            _dragZone = new VisualElement();
            _dragZone.AddToClassList("bv-sheet__drag");
            var grabber = new VisualElement();
            grabber.AddToClassList("bv-sheet__grabber");
            _dragZone.Add(grabber);

            var header = new VisualElement();
            header.AddToClassList("bv-sheet__header");
            _title = new Label();
            _title.AddToClassList("bv-sheet__title");
            if (!string.IsNullOrEmpty(_options.TitleKey)) ctx.Binder.SetKey(_title, _options.TitleKey);
            header.Add(_title);
            HeaderTrailing = new VisualElement();
            HeaderTrailing.AddToClassList("bv-sheet__header-trailing");
            header.Add(HeaderTrailing);
            _dragZone.Add(header);
            _panel.Add(_dragZone);

            Toolbar = new VisualElement();
            Toolbar.AddToClassList("bv-sheet__toolbar");
            _panel.Add(Toolbar);

            // Fit-content sheets size to their content; a scroll view would not report a content height.
            if (_options.Scrollable && _options.Detents != SheetDetents.FitContent)
            {
                Scroll = new ScrollView(ScrollViewMode.Vertical);
                Scroll.AddToClassList("bv-sheet__scroll");
                Scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                Scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                Scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                _panel.Add(Scroll);
                Content = Scroll.contentContainer;
            }
            else
            {
                Content = new VisualElement();
                Content.AddToClassList("bv-sheet__content");
                _panel.Add(Content);
            }
            var safe = new VisualElement();
            safe.AddToClassList("bv-safe-bottom");
            _panel.Add(safe);

            _dragZone.RegisterCallback<PointerDownEvent>(OnDragDown);
            _dragZone.RegisterCallback<PointerMoveEvent>(OnDragMove);
            _dragZone.RegisterCallback<PointerUpEvent>(OnDragUp);
            _dragZone.RegisterCallback<PointerCancelEvent>(e => EndDrag(0f));
            _root.RegisterCallback<GeometryChangedEvent>(e => Remeasure());
            _panel.RegisterCallback<GeometryChangedEvent>(e => Remeasure());
        }

        public void SetTitle(string text)
        {
            _ctx.Binder.Forget(_title);
            _title.text = text ?? "";
        }

        public void SetTitleKey(string key)
        {
            _ctx.Binder.SetKey(_title, key);
        }

        /// <summary>Shows the sheet through the shell's sheet host.</summary>
        public BottomSheet Show()
        {
            _ctx.Sheets.Present(this);
            return this;
        }

        internal void OnPresented()
        {
            IsOpen = true;
            _closing = false;
            _measured = false;
            _ctx.Prepare(_root);
            Remeasure();
        }

        /// <summary>Animates out and removes the sheet; invokes <see cref="SheetOptions.Closed"/> once.</summary>
        public void Close()
        {
            if (!IsOpen || _closing) return;
            _closing = true;
            AnimateTo(0f, Ease.InCubic, 0.22f, () =>
            {
                IsOpen = false;
                Host?.OnSheetClosed(this);
                _options.Closed?.Invoke();
            });
        }

        /// <summary>Removes the sheet at once, without animation (shell teardown); <see cref="SheetOptions.Closed"/> runs once.</summary>
        internal void CloseImmediately()
        {
            if (!IsOpen) return;
            _closing = true;
            _tween?.Cancel();
            IsOpen = false;
            Host?.OnSheetClosed(this);
            _options.Closed?.Invoke();
        }

        /// <summary>Switches between the 50% and 90% detents.</summary>
        public void SetExpanded(bool expanded)
        {
            if (_detents.Length < 2) return;
            _detentIndex = expanded ? _detents.Length - 1 : 0;
            AnimateTo(_detents[_detentIndex], Ease.Spring, Tween.DefaultDuration, null);
        }

        void Remeasure()
        {
            if (!IsOpen || _closing) return;
            float h = _root.layout.height;
            if (float.IsNaN(h) || h <= 0f) return;
            float full = Mathf.Round(h * _options.FullFraction);
            switch (_options.Detents)
            {
                case SheetDetents.HalfAndFull:
                    _panel.style.height = full;
                    _panelHeight = full;
                    _detents = new[] { Mathf.Round(h * _options.HalfFraction), full };
                    break;
                case SheetDetents.Full:
                    _panel.style.height = full;
                    _panelHeight = full;
                    _detents = new[] { full };
                    break;
                default:
                {
                    _panel.style.maxHeight = full;
                    float ph = _panel.layout.height;
                    if (float.IsNaN(ph) || ph <= 0f) return;
                    _panelHeight = ph;
                    _detents = new[] { ph };
                    break;
                }
            }
            if (!_measured)
            {
                _measured = true;
                _detentIndex = _options.StartExpanded ? _detents.Length - 1 : 0;
                _visible = 0f;
                ApplyVisible();
                _panel.style.visibility = Visibility.Visible;
                AnimateTo(_detents[_detentIndex], Ease.Spring, Tween.DefaultDuration, null);
            }
            else if (_dragPointer < 0 && (_tween == null || !_tween.IsRunning))
            {
                _detentIndex = Mathf.Clamp(_detentIndex, 0, _detents.Length - 1);
                _visible = _detents[_detentIndex];
                ApplyVisible();
            }
        }

        void ApplyVisible()
        {
            float offset = Mathf.Max(0f, _panelHeight - _visible);
            _panel.style.translate = new Translate(0, offset);
            float max = _detents.Length > 0 ? _detents[0] : 1f;
            float t = max > 0f ? Mathf.Clamp01(_visible / max) : 0f;
            _scrim.style.opacity = t * _options.ScrimOpacity;
        }

        void AnimateTo(float target, Ease ease, float duration, Action done)
        {
            _tween?.Cancel();
            float from = _visible;
            _tween = Tween.Run(_panel, duration, ease, k =>
            {
                _visible = Mathf.LerpUnclamped(from, target, k);
                ApplyVisible();
            }, done);
        }

        void OnDragDown(PointerDownEvent e)
        {
            if (_dragPointer >= 0 || _closing) return;
            _tween?.Cancel();
            _dragPointer = e.pointerId;
            _dragStartY = e.position.y;
            _dragStartVisible = _visible;
            _lastY = e.position.y;
            _lastT = Time.unscaledTime;
            _velocity = 0f;
            _dragZone.CapturePointer(_dragPointer);
            e.StopPropagation();
        }

        void OnDragMove(PointerMoveEvent e)
        {
            if (e.pointerId != _dragPointer) return;
            float now = Time.unscaledTime;
            float dt = now - _lastT;
            if (dt > 0.0001f) _velocity = Mathf.Lerp(_velocity, (e.position.y - _lastY) / dt, 0.6f);
            _lastY = e.position.y;
            _lastT = now;
            _visible = SheetDetentSolver.DragHeight(_dragStartVisible, e.position.y - _dragStartY, _detents[_detents.Length - 1]);
            ApplyVisible();
        }

        void OnDragUp(PointerUpEvent e)
        {
            if (e.pointerId != _dragPointer) return;
            EndDrag(_velocity);
        }

        void EndDrag(float velocity)
        {
            if (_dragPointer < 0) return;
            int id = _dragPointer;
            _dragPointer = -1;
            if (_dragZone.HasPointerCapture(id)) _dragZone.ReleasePointer(id);
            var target = SheetDetentSolver.Solve(_detents, _visible, velocity, _options.Dismissible);
            if (target.Dismiss)
            {
                Close();
                return;
            }
            _detentIndex = target.DetentIndex;
            AnimateTo(_detents[_detentIndex], Ease.Spring, Tween.DefaultDuration, null);
        }
    }

    /// <summary>Layer that stacks bottom sheets above the screens.</summary>
    public sealed class SheetHost
    {
        readonly VisualElement _layer;
        readonly List<BottomSheet> _stack = new List<BottomSheet>();

        public event Action Changed;

        public SheetHost(VisualElement layer)
        {
            _layer = layer;
            _layer.pickingMode = PickingMode.Ignore;
        }

        public int Count => _stack.Count;
        public bool AnyOpen => _stack.Count > 0;
        public BottomSheet Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public void Present(BottomSheet sheet)
        {
            if (sheet == null || _stack.Contains(sheet)) return;
            sheet.Host = this;
            _stack.Add(sheet);
            _layer.Add(sheet.Root);
            sheet.OnPresented();
            Changed?.Invoke();
        }

        /// <summary>Closes the top dismissible sheet (system back). Returns false when there is none.</summary>
        public bool CloseTop()
        {
            var top = Top;
            if (top == null) return false;
            if (top.Options.Dismissible) top.Close();
            return true;
        }

        public void CloseAll()
        {
            for (int i = _stack.Count - 1; i >= 0; i--) _stack[i].Close();
        }

        /// <summary>Removes every sheet without animation (shell teardown).</summary>
        public void DismissAllImmediately()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                if (i < _stack.Count) _stack[i].CloseImmediately();
            }
        }

        internal void OnSheetClosed(BottomSheet sheet)
        {
            _stack.Remove(sheet);
            sheet.Root.RemoveFromHierarchy();
            Changed?.Invoke();
        }
    }
}
