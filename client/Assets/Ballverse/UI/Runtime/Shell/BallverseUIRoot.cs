using System;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI
{
    /// <summary>
    /// Scene entry point of the UI: creates a <see cref="UIDocument"/> with runtime <see cref="PanelSettings"/> (no
    /// editor-made assets needed), builds the <see cref="UiShell"/> and drives it every frame.
    /// Usage from the composition root: <c>var ui = BallverseUIRoot.Create(viewModels); ui.Shell.OpenMode("ring_escape");</c>
    /// </summary>
    public sealed class BallverseUIRoot : MonoBehaviour
    {
        /// <summary>Reference layout size in panel units (portrait phone, spec §4.1 portrait only).</summary>
        public static readonly Vector2Int ReferenceResolution = new Vector2Int(390, 844);

        UIDocument _document;
        PanelSettings _panelSettings;
#if ENABLE_LEGACY_INPUT_MANAGER
        Vector2 _tilt;
#endif

        public UiShell Shell { get; private set; }
        public UIDocument Document => _document;

        /// <summary>Creates the UI root object and shell.</summary>
        /// <param name="sortingOrder">Panel sorting order relative to other UI panels / canvases.</param>
        public static BallverseUIRoot Create(IBallverseViewModels viewModels, int sortingOrder = 0, Transform parent = null)
        {
            if (viewModels == null) throw new ArgumentNullException(nameof(viewModels));
            var go = new GameObject("BallverseUI");
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            var root = go.AddComponent<BallverseUIRoot>();
            root.Build(viewModels, sortingOrder);
            return root;
        }

        void Build(IBallverseViewModels viewModels, int sortingOrder)
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.name = "BallversePanelSettings";
            _panelSettings.themeStyleSheet = Resources.Load<ThemeStyleSheet>(UiAssets.ThemePath);
            if (_panelSettings.themeStyleSheet == null)
                Debug.LogError("[Ballverse.UI] Runtime theme not found: Resources/" + UiAssets.ThemePath);
            _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _panelSettings.referenceResolution = ReferenceResolution;
            _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            _panelSettings.match = 0f;
            _panelSettings.sortingOrder = sortingOrder;

            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _panelSettings;
            gameObject.SetActive(true);

            var root = _document.rootVisualElement;
            root.AddToClassList("bv-panel-root");
            Shell = new UiShell(root, viewModels);
        }

        void Update()
        {
            Shell?.Update(Time.unscaledDeltaTime);
            UpdateTilt();
        }

        void LateUpdate()
        {
            Shell?.LateUpdate();
        }

        /// <summary>Glass edge highlight follows device tilt by ± 4° (spec §4.5), off under reduced motion.</summary>
        void UpdateTilt()
        {
            if (Shell == null) return;
            var light = new Vector2(-0.7071f, -0.7071f);
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!Components.Tween.ReduceMotion && SystemInfo.supportsGyroscope)
            {
                if (!Input.gyro.enabled) Input.gyro.enabled = true;
                var g = Input.gyro.gravity;
                var target = new Vector2(Mathf.Clamp(g.x, -1f, 1f), Mathf.Clamp(-g.y, -1f, 1f));
                _tilt = Vector2.Lerp(_tilt, target, 0.1f);
                float angle = Mathf.Clamp(_tilt.x * 4f, -4f, 4f) * Mathf.Deg2Rad;
                light = new Vector2(light.x * Mathf.Cos(angle) - light.y * Mathf.Sin(angle),
                    light.x * Mathf.Sin(angle) + light.y * Mathf.Cos(angle));
            }
#endif
            Shell.Glass.SetLightDirection(light);
        }

        void OnDestroy()
        {
            Shell?.Dispose();
            Shell = null;
            if (_panelSettings != null) Destroy(_panelSettings);
        }
    }
}
