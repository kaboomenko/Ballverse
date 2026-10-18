using System;
using Ballverse.Core;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using UnityEngine;
using UnityEngine.UI;
using QualityLevel = Ballverse.Core.RenderGeometry.QualityLevel;

namespace Ballverse.Render
{
    /// <summary>Supplies the frame to show this display frame (a live run, the studio player…).</summary>
    public interface IBallverseFrameProvider
    {
        /// <summary>The frame to draw and its tick; false when there is nothing to draw yet.</summary>
        bool TryGetFrame(out RenderFrame frame, out int tick);
    }

    /// <summary>
    /// Real-time presentation of a simulation on screen: renders the provider's frame every <c>LateUpdate</c> into a
    /// render texture shown by a <see cref="RawImage"/> (the studio's simulation slot or a full-screen overlay), with the
    /// quality profile chosen by device tier and lowered dynamically when the FPS drops (spec §7.7). Export does not use
    /// this component: it drives <see cref="BallverseRenderer.RenderOffline"/> directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BallverseView : MonoBehaviour
    {
        [SerializeField, Tooltip("Where the simulation is shown. Its RectTransform size (in screen pixels) sets the render size.")]
        RawImage display = null;
        [SerializeField, Tooltip("Starting real-time profile when no tier is supplied.")]
        QualityLevel startQuality = QualityLevel.A;
        [SerializeField, Tooltip("Lower the profile automatically when the average FPS stays below 55 for 3 s.")]
        bool dynamicQuality = true;
        [SerializeField] BallverseSkinAtlas skins = null;
        [SerializeField] BallverseFontSet fonts = null;

        BallverseRenderer _renderer;
        RenderTexture _target;
        QualityGovernor _governor;

        /// <summary>The render context (stream tables, look, events…). Fill it before frames are provided.</summary>
        public RenderContext Context { get; } = new RenderContext();

        public IBallverseFrameProvider Provider { get; set; }

        public BallverseRenderer Renderer => _renderer;

        /// <summary>The texture the simulation is rendered into (null before the first frame).</summary>
        public RenderTexture Target => _target;

        /// <summary>The active real-time quality level.</summary>
        public QualityLevel Quality => _governor != null ? _governor.Level : startQuality;

        /// <summary>Raised when the dynamic fallback lowered the quality (for analytics / diagnostics).</summary>
        public event Action<QualityLevel> QualityChanged;

        /// <summary>Sets the starting profile from the device tier (spec §8.1).</summary>
        public void SetTier(DeviceTier tier)
        {
            startQuality = QualityProfile.ForTier(tier).Level;
            _governor = new QualityGovernor(startQuality);
        }

        void Awake()
        {
            _renderer = new BallverseRenderer();
            _governor = new QualityGovernor(startQuality == QualityLevel.Export ? QualityLevel.A : startQuality);
            if (Context.Skins == null) Context.Skins = skins;
            if (Context.Fonts == null) Context.Fonts = fonts;
        }

        void LateUpdate()
        {
            if (Provider == null || display == null || Context.Shapes == null) return;
            if (!Provider.TryGetFrame(out RenderFrame frame, out int tick) || frame == null) return;

            if (dynamicQuality && _governor.AddFrame(Time.unscaledDeltaTime)) QualityChanged?.Invoke(_governor.Level);
            Context.Quality = _governor.Profile;
            Context.Tick = tick;

            EnsureTarget();
            if (_target == null) return;
            _renderer.Render(frame, Context, _target);
        }

        void EnsureTarget()
        {
            Rect rect = display.rectTransform.rect;
            Canvas canvas = display.canvas;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            int w = Mathf.Clamp(Mathf.RoundToInt(rect.width * scale), 16, 4096);
            int h = Mathf.Clamp(Mathf.RoundToInt(rect.height * scale), 16, 4096);
            if (_target != null && _target.width == w && _target.height == h) return;
            if (_target != null)
            {
                _target.Release();
                RenderResources.Destroy(_target);
            }
            _target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "BallverseView",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            _target.Create();
            display.texture = _target;
        }

        void OnDestroy()
        {
            _renderer?.Dispose();
            _renderer = null;
            if (_target != null)
            {
                if (display != null && display.texture == _target) display.texture = null;
                _target.Release();
                RenderResources.Destroy(_target);
                _target = null;
            }
        }
    }
}
