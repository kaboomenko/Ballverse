using System;
using Ballverse.Core;
using Ballverse.Platform;
using Ballverse.UI.Components;
using Ballverse.UI.Glass;
using Ballverse.UI.Layout;
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
    /// elements with <c>bv-safe-top</c> / <c>bv-safe-bottom</c> / <c>bv-safe-left</c> / <c>bv-safe-right</c> /
    /// <c>bv-safe-pad-x</c>; the controller sizes them to the device insets (notch, home indicator) in panel units.
    /// The insets come from the platform layer (<see cref="ISafeAreaProvider"/>: the app's bootstrap polls
    /// <c>Screen.safeArea</c> once per frame and raises <see cref="ISafeAreaProvider.Changed"/>); the UI does not read the
    /// screen itself, it only scales the pixels to panel units when the insets or the panel width change. Without a
    /// provider (EditMode tests, a bare shell) the insets stay zero.
    /// </summary>
    public sealed class SafeAreaController : IDisposable
    {
        readonly VisualElement _root;
        readonly ISafeAreaProvider _provider;
        readonly SafeAreaTracker _state;
        bool _disposed;

        public PanelInsets Insets => _state.Insets;
        /// <summary>The device insets last received from the provider (pixels).</summary>
        public SafeAreaInsets DeviceInsets => _state.Device;
        public event Action Changed;

        public SafeAreaController(VisualElement root, ISafeAreaProvider provider)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _provider = provider;
            _state = new SafeAreaTracker(_provider != null ? _provider.Current : default);
            if (_provider != null) _provider.Changed += OnDeviceInsetsChanged;
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            Recompute(_root.layout.width);
        }

        /// <summary>
        /// A provider change (split view, resize, foldable) resizes the spacers at once: scaled with the current panel width,
        /// or the last usable one while the panel has no layout.
        /// </summary>
        void OnDeviceInsetsChanged(SafeAreaInsets insets)
        {
            if (_disposed) return;
            if (_state.SetDevice(insets, _root.layout.width)) Publish();
        }

        void OnGeometryChanged(GeometryChangedEvent e)
        {
            if (_disposed) return;
            Recompute(_root.layout.width);
        }

        /// <summary>
        /// Scales the current device insets to panel units for a panel of <paramref name="panelWidth"/> units and sizes the
        /// spacers when the result changed. Returns false while the panel is not laid out (the next geometry change retries).
        /// </summary>
        internal bool Recompute(float panelWidth)
        {
            if (!PanelInsets.IsUsablePanelWidth(panelWidth)) return false;
            if (_state.SetPanelWidth(panelWidth)) Publish();
            return true;
        }

        void Publish()
        {
            Apply(_root);
            Changed?.Invoke();
        }

        /// <summary>Sizes the spacers under <paramref name="root"/>.</summary>
        public void Apply(VisualElement root)
        {
            if (root == null) return;
            var i = _state.Insets;
            root.Query<VisualElement>(className: "bv-safe-top").ForEach(e => e.style.height = i.Top);
            root.Query<VisualElement>(className: "bv-safe-bottom").ForEach(e => e.style.height = i.Bottom);
            root.Query<VisualElement>(className: "bv-safe-left").ForEach(e => e.style.width = i.Left);
            root.Query<VisualElement>(className: "bv-safe-right").ForEach(e => e.style.width = i.Right);
            root.Query<VisualElement>(className: "bv-safe-pad-x").ForEach(e =>
            {
                e.style.paddingLeft = i.Left;
                e.style.paddingRight = i.Right;
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_provider != null) _provider.Changed -= OnDeviceInsetsChanged;
            _root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }
    }
}
