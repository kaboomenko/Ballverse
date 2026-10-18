using System;
using Ballverse.Core;
using Ballverse.Core.RenderGeometry;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using QualityLevel = Ballverse.Core.RenderGeometry.QualityLevel;

namespace Ballverse.Render
{
    /// <summary>
    /// MonoBehaviour driver of a <see cref="BallversePresenter"/>: renders the provider's frame every <c>LateUpdate</c>
    /// at the size of the slot it is shown in. The slot is, in priority order: an explicit <see cref="PixelSizeOverride"/>;
    /// a UI Toolkit <see cref="Element"/> (the release-1 UI shell — the UI shows <see cref="Target"/> itself, e.g. with its
    /// <c>TextureSlot</c>, and listens to <see cref="TargetChanged"/>); or an optional uGUI <see cref="RawImage"/>, whose
    /// texture this component assigns. All rendering logic lives in the presenter, so a screen can also own a presenter
    /// directly without this component. Export does not use it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallverseView : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional uGUI slot. Its RectTransform size (in screen pixels) sets the render size and it shows the texture.")]
        RawImage display = null;
        [SerializeField, Tooltip("Starting real-time profile when no tier is supplied.")]
        QualityLevel startQuality = QualityLevel.A;
        [SerializeField, Tooltip("Lower the profile automatically when the average FPS stays below 55 for 3 s.")]
        bool dynamicQuality = true;
        [SerializeField] BallverseSkinAtlas skins = null;
        [SerializeField] BallverseFontSet fonts = null;

        BallversePresenter _presenter;

        /// <summary>The underlying presenter (renderer, target, governor); created on first use.</summary>
        public BallversePresenter Presenter
        {
            get
            {
                if (_presenter == null)
                {
                    _presenter = new BallversePresenter(null, startQuality);
                    _presenter.TargetChanged += OnTargetChanged;
                    _presenter.QualityChanged += OnQualityChanged;
                }
                return _presenter;
            }
        }

        /// <summary>The render context (stream tables, look, events…). Fill it before frames are provided.</summary>
        public RenderContext Context => Presenter.Context;

        public IBallverseFrameProvider Provider
        {
            get => Presenter.Provider;
            set => Presenter.Provider = value;
        }

        public BallverseRenderer Renderer => Presenter.Renderer;

        /// <summary>The texture the simulation is rendered into (null before the first frame).</summary>
        public RenderTexture Target => _presenter?.Target;

        /// <summary>The active real-time quality level.</summary>
        public QualityLevel Quality => _presenter != null ? _presenter.Quality : startQuality;

        /// <summary>UI Toolkit slot whose laid-out size sets the render size (not serialized; assign from the UI code).</summary>
        public VisualElement Element { get; set; }

        /// <summary>Panel points → pixels for <see cref="Element"/>; 0 = derive from the screen.</summary>
        public float ElementPixelsPerPoint { get; set; }

        /// <summary>Explicit render size in pixels (e.g. from a view model); (0, 0) = use the slot.</summary>
        public Vector2Int PixelSizeOverride { get; set; }

        /// <summary>Raised when the dynamic fallback lowered the quality (for analytics / diagnostics).</summary>
        public event Action<QualityLevel> QualityChanged;

        /// <summary>Raised when <see cref="Target"/> is replaced (first frame or a size change).</summary>
        public event Action<RenderTexture> TargetChanged;

        /// <summary>Sets the starting profile from the device tier (spec §8.1).</summary>
        public void SetTier(DeviceTier tier)
        {
            startQuality = QualityProfile.ForTier(tier).Level;
            Presenter.SetTier(tier);
        }

        void Awake()
        {
            BallversePresenter p = Presenter;
            if (p.Context.Skins == null) p.Context.Skins = skins;
            if (p.Context.Fonts == null) p.Context.Fonts = fonts;
        }

        void LateUpdate()
        {
            if (!TryGetSlotSize(out int w, out int h)) return;
            BallversePresenter p = Presenter;
            p.DynamicQuality = dynamicQuality;
            p.Present(w, h, Time.unscaledDeltaTime);
        }

        bool TryGetSlotSize(out int width, out int height)
        {
            Vector2Int forced = PixelSizeOverride;
            if (forced.x > 0 && forced.y > 0)
            {
                width = forced.x;
                height = forced.y;
                return true;
            }
            if (Element != null) return BallversePresenter.TryGetPixelSize(Element, ElementPixelsPerPoint, out width, out height);
            if (display != null)
            {
                Rect rect = display.rectTransform.rect;
                Canvas canvas = display.canvas;
                float scale = canvas != null ? canvas.scaleFactor : 1f;
                width = Mathf.RoundToInt(rect.width * scale);
                height = Mathf.RoundToInt(rect.height * scale);
                return width > 0 && height > 0;
            }
            width = 0;
            height = 0;
            return false;
        }

        void OnTargetChanged(RenderTexture texture)
        {
            if (display != null) display.texture = texture;
            TargetChanged?.Invoke(texture);
        }

        void OnQualityChanged(QualityLevel level)
        {
            QualityChanged?.Invoke(level);
        }

        void OnDestroy()
        {
            if (_presenter == null) return;
            _presenter.Dispose();   // raises TargetChanged(null): the RawImage is cleared
            _presenter.TargetChanged -= OnTargetChanged;
            _presenter.QualityChanged -= OnQualityChanged;
            _presenter = null;
        }
    }
}
