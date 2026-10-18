using System;
using Ballverse.Core;
using Ballverse.UI.Components;
using Ballverse.UI.Glass;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Theming
{
    /// <summary>
    /// Applies the resolved appearance to the app root as USS classes (spec §4.4, §4.8): <c>bv-theme-dark</c> /
    /// <c>bv-theme-light</c> (colour variables), <c>bv-contrast</c>, <c>bv-text-115</c> / <c>bv-text-130</c> (font size
    /// variables), <c>bv-reduce-motion</c>; and configures the glass tier and fallbacks.
    /// </summary>
    public sealed class ThemeController
    {
        readonly VisualElement _root;
        readonly GlassSurfaces _glass;
        DeviceTier _tier = DeviceTier.B;

        public ResolvedAppearance Current { get; private set; }
        public event Action Changed;

        public ThemeController(VisualElement root, GlassSurfaces glass)
        {
            _root = root;
            _glass = glass;
        }

        /// <summary>Device tier for the glass quality (spec §8.1); the app sets it after tier detection.</summary>
        public void SetTier(DeviceTier tier)
        {
            _tier = tier;
            if (Current != null) ApplyGlass(Current);
        }

        public void Apply(ResolvedAppearance appearance)
        {
            if (appearance == null) throw new ArgumentNullException(nameof(appearance));
            if (appearance.Equals(Current)) return;
            Current = appearance;
            bool light = appearance.Theme == ResolvedTheme.Light;
            _root.EnableInClassList("bv-theme-dark", !light);
            _root.EnableInClassList("bv-theme-light", light);
            _root.EnableInClassList("bv-contrast", appearance.HighContrast);
            _root.EnableInClassList("bv-text-115", appearance.TextScale == TextScaleStep.Large);
            _root.EnableInClassList("bv-text-130", appearance.TextScale == TextScaleStep.Largest);
            _root.EnableInClassList("bv-reduce-motion", appearance.ReduceMotion);
            Tween.ReduceMotion = appearance.ReduceMotion;
            ApplyGlass(appearance);
            Changed?.Invoke();
        }

        void ApplyGlass(ResolvedAppearance appearance)
        {
            if (_glass == null) return;
            _glass.SetQuality(GlassQualityPolicy.For(_tier, appearance.ReduceTransparency));
            _glass.SetTheme(appearance.Theme);
            _glass.Backdrop = appearance.Theme == ResolvedTheme.Light
                ? new Color(0.94f, 0.95f, 0.97f, 1f)
                : new Color(0.035f, 0.04f, 0.055f, 1f);
        }
    }

    /// <summary>
    /// Safe-area handling (spec §4.3: content inside the safe zone, simulation edge to edge). Layouts mark spacer
    /// elements with <c>bv-safe-top</c> / <c>bv-safe-bottom</c> / <c>bv-safe-left</c> / <c>bv-safe-right</c>; the
    /// controller sizes them to the device insets (notch, home indicator) in panel units.
    /// </summary>
    public sealed class SafeAreaController
    {
        readonly VisualElement _root;
        Layout.SafeAreaInsets _insets;
        Rect _lastSafe;
        Vector2Int _lastScreen;

        public Layout.SafeAreaInsets Insets => _insets;
        public event Action Changed;

        public SafeAreaController(VisualElement root)
        {
            _root = root;
            _root.RegisterCallback<GeometryChangedEvent>(e => Refresh(true));
        }

        /// <summary>Call every frame (cheap): re-applies when the safe area or the screen size changed.</summary>
        public void Refresh(bool force = false)
        {
            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _lastSafe && screen == _lastScreen) return;
            float panelWidth = _root.layout.width;
            if (float.IsNaN(panelWidth) || panelWidth <= 0 || screen.x <= 0) return;
            _lastSafe = safe;
            _lastScreen = screen;
            var insets = Layout.SafeAreaInsets.FromScreen(screen.x, screen.y, safe.x, safe.y, safe.width, safe.height,
                panelWidth / screen.x);
            if (insets.Equals(_insets) && !force) return;
            _insets = insets;
            Apply(_root);
            Changed?.Invoke();
        }

        /// <summary>Sizes the spacers under <paramref name="root"/>.</summary>
        public void Apply(VisualElement root)
        {
            if (root == null) return;
            root.Query<VisualElement>(className: "bv-safe-top").ForEach(e => e.style.height = _insets.Top);
            root.Query<VisualElement>(className: "bv-safe-bottom").ForEach(e => e.style.height = _insets.Bottom);
            root.Query<VisualElement>(className: "bv-safe-left").ForEach(e => e.style.width = _insets.Left);
            root.Query<VisualElement>(className: "bv-safe-right").ForEach(e => e.style.width = _insets.Right);
            root.Query<VisualElement>(className: "bv-safe-pad-x").ForEach(e =>
            {
                e.style.paddingLeft = _insets.Left;
                e.style.paddingRight = _insets.Right;
            });
        }
    }
}
