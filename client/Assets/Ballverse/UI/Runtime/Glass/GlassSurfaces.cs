using System;
using System.Collections.Generic;
using Ballverse.UI.Layout;
using Ballverse.UI.Theming;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Glass
{
    /// <summary>
    /// The BallverseGlass material for UI Toolkit (spec §4.5). UI Toolkit in Unity 6 does not run custom shaders per
    /// element (as a long-stable API), so the material is split in two:
    /// <list type="number">
    /// <item>once per frame <see cref="GlassBackdropRenderer"/> blurs the content behind the UI (the simulation render
    /// target or studio preview, which the UI shows itself) and composes refraction, edge highlight and adaptive tint
    /// for the rectangles of all visible glass surfaces into one texture at 1/4 (tier C: 1/8) resolution;</item>
    /// <item>each surface (class <c>bv-glass</c>) shows that texture as its background image, sized to the panel and
    /// offset by its own position, so it displays exactly the part behind it; rounded corners, the crisp 1 px edge and
    /// the content stay native UI Toolkit.</item>
    /// </list>
    /// Fallbacks (root classes for USS): <c>bv-glass-mode-solid</c> under "Reduce Transparency" (92% matte surfaces),
    /// <c>bv-glass-mode-frosted</c> when there is no backdrop source or the shader is unavailable (translucent tint
    /// over the app background). Surfaces beyond <see cref="GlassBackdropRenderer.MaxRects"/> get
    /// <c>bv-glass--overflow</c> (frosted).
    /// </summary>
    public sealed class GlassSurfaces : IDisposable
    {
        public const string SurfaceClass = "bv-glass";
        public const string OverflowClass = "bv-glass--overflow";
        const string TrackedClass = "bv-glass--tracked";
        public const string ModeBackdropClass = "bv-glass-mode-backdrop";
        public const string ModeFrostedClass = "bv-glass-mode-frosted";
        public const string ModeSolidClass = "bv-glass-mode-solid";

        sealed class Surface
        {
            public VisualElement Element;
            public bool Applied;
            public int AppliedVersion;
            public Rect LastBound;
        }

        readonly VisualElement _appRoot;
        readonly List<Surface> _surfaces = new List<Surface>();
        readonly HashSet<VisualElement> _known = new HashSet<VisualElement>();
        readonly GlassRect[] _rects = new GlassRect[GlassBackdropRenderer.MaxRects];
        readonly List<Surface> _visible = new List<Surface>();
        GlassBackdropRenderer _renderer;
        GlassQuality _quality = GlassQualityPolicy.For(Core.DeviceTier.B, false);
        ResolvedTheme _theme = ResolvedTheme.Dark;
        Texture _source;
        VisualElement _sourceSlot;
        string _modeClass;
        Vector2 _lightDir = new Vector2(-0.7071f, -0.7071f);

        public GlassSurfaces(VisualElement appRoot)
        {
            _appRoot = appRoot ?? throw new ArgumentNullException(nameof(appRoot));
            SetModeClass(ModeFrostedClass);
        }

        public GlassQuality Quality => _quality;
        public GlassMode Mode => _quality.Mode;
        /// <summary>Background colour behind the source slot (theme background), used outside the slot.</summary>
        public Color Backdrop { get; set; } = new Color(0.04f, 0.045f, 0.06f, 1f);

        /// <summary>Registers every <c>.bv-glass</c> element under <paramref name="root"/>.</summary>
        public void Scan(VisualElement root)
        {
            if (root == null) return;
            root.Query<VisualElement>(className: SurfaceClass).ForEach(Register);
        }

        public void Register(VisualElement element)
        {
            if (element == null) return;
            if (!element.ClassListContains(TrackedClass))
            {
                // Re-register automatically when a pruned (detached) element is attached again.
                element.AddToClassList(TrackedClass);
                element.RegisterCallback<AttachToPanelEvent>(e => Register(element));
            }
            if (!_known.Add(element)) return;
            _surfaces.Add(new Surface { Element = element });
        }

        public void Unregister(VisualElement element)
        {
            if (element == null || !_known.Remove(element)) return;
            for (int i = _surfaces.Count - 1; i >= 0; i--)
            {
                if (_surfaces[i].Element != element) continue;
                GlassBackgroundStyle.Clear(element);
                _surfaces.RemoveAt(i);
            }
        }

        public void SetQuality(GlassQuality quality)
        {
            _quality = quality ?? throw new ArgumentNullException(nameof(quality));
        }

        public void SetTheme(ResolvedTheme theme)
        {
            _theme = theme;
        }

        /// <summary>
        /// The texture behind the UI and the element that shows it with "cover" fit (the backdrop maps screen positions
        /// through that element). Call with null to clear (only clears when <paramref name="slot"/> is the current slot).
        /// </summary>
        public void SetSource(Texture source, VisualElement slot)
        {
            if (source == null)
            {
                if (slot == null || slot == _sourceSlot)
                {
                    _source = null;
                    _sourceSlot = null;
                }
                return;
            }
            _source = source;
            _sourceSlot = slot;
        }

        /// <summary>Highlight light direction from device tilt (spec §4.5 ± 4°); ignored under reduced motion by the caller.</summary>
        public void SetLightDirection(Vector2 directionPanelSpace)
        {
            if (directionPanelSpace.sqrMagnitude > 1e-6f) _lightDir = directionPanelSpace.normalized;
        }

        /// <summary>Per-frame update (after layout of the previous frame, before rendering).</summary>
        public void Update()
        {
            PruneDetached();
            if (_quality.Mode == GlassMode.Solid)
            {
                SetModeClass(ModeSolidClass);
                ClearAll();
                return;
            }
            if (_renderer == null) _renderer = new GlassBackdropRenderer();
            if (!_renderer.Supported || _source == null || _sourceSlot == null || _sourceSlot.panel == null)
            {
                SetModeClass(ModeFrostedClass);
                ClearAll();
                return;
            }

            var panelSize = new Vector2(_appRoot.layout.width, _appRoot.layout.height);
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0 || panelSize.y <= 0) return;

            _visible.Clear();
            foreach (var s in _surfaces)
            {
                if (!IsShown(s.Element)) continue;
                var wb = s.Element.worldBound;
                if (wb.width <= 0 || wb.height <= 0 || wb.yMax < 0 || wb.y > panelSize.y) continue;
                _visible.Add(s);
            }
            SetModeClass(ModeBackdropClass);
            if (_visible.Count == 0) return;

            int count = Mathf.Min(_visible.Count, GlassBackdropRenderer.MaxRects);
            for (int i = 0; i < count; i++)
            {
                var e = _visible[i].Element;
                _rects[i] = new GlassRect { Bounds = e.worldBound, Radius = e.resolvedStyle.borderTopLeftRadius };
            }

            var slot = _sourceSlot.worldBound;
            var slotUv = new Rect(slot.x / panelSize.x, 1f - (slot.y + slot.height) / panelSize.y,
                slot.width / panelSize.x, slot.height / panelSize.y);
            AspectFit.CoverUv(_source.width / (float)Mathf.Max(1, _source.height), slot.width / Mathf.Max(1f, slot.height),
                out float sx, out float sy, out float ox, out float oy);
            var output = _renderer.Render(_source, slotUv, new Vector4(sx, sy, ox, oy), Screen.width, Screen.height, panelSize,
                _quality, _theme == ResolvedTheme.Light ? GlassTint.Light : GlassTint.Dark, Backdrop, _rects, count, _lightDir);
            if (output == null)
            {
                SetModeClass(ModeFrostedClass);
                ClearAll();
                return;
            }

            for (int i = 0; i < _visible.Count; i++)
            {
                var s = _visible[i];
                var e = s.Element;
                bool overflow = i >= GlassBackdropRenderer.MaxRects;
                e.EnableInClassList(OverflowClass, overflow);
                if (overflow)
                {
                    if (s.Applied) GlassBackgroundStyle.Clear(e);
                    s.Applied = false;
                    continue;
                }
                var wb = e.worldBound;
                if (!s.Applied || s.AppliedVersion != _renderer.OutputVersion)
                {
                    GlassBackgroundStyle.Apply(e, output, panelSize, wb);
                    s.Applied = true;
                    s.AppliedVersion = _renderer.OutputVersion;
                    s.LastBound = wb;
                }
                else if (wb.position != s.LastBound.position)
                {
                    GlassBackgroundStyle.Move(e, wb);
                    s.LastBound = wb;
                }
                e.MarkDirtyRepaint();
            }
        }

        static bool IsShown(VisualElement e)
        {
            if (e.panel == null) return false;
            for (var v = e; v != null; v = v.parent)
            {
                var rs = v.resolvedStyle;
                if (rs.display == DisplayStyle.None || rs.visibility == Visibility.Hidden || rs.opacity <= 0.01f) return false;
            }
            return true;
        }

        void PruneDetached()
        {
            for (int i = _surfaces.Count - 1; i >= 0; i--)
            {
                var s = _surfaces[i];
                if (s.Element.panel != null) continue;
                // Detached elements of destroyed screens/sheets: forget them (cached screens re-register on show).
                _known.Remove(s.Element);
                _surfaces.RemoveAt(i);
            }
        }

        void ClearAll()
        {
            foreach (var s in _surfaces)
            {
                if (s.Applied) GlassBackgroundStyle.Clear(s.Element);
                s.Applied = false;
                s.Element.RemoveFromClassList(OverflowClass);
            }
        }

        void SetModeClass(string cls)
        {
            if (_modeClass == cls) return;
            if (_modeClass != null) _appRoot.RemoveFromClassList(_modeClass);
            _modeClass = cls;
            _appRoot.AddToClassList(cls);
        }

        public void Dispose()
        {
            ClearAll();
            _renderer?.Dispose();
            _renderer = null;
        }
    }
}
