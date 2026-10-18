using System;
using Ballverse.Core;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using UnityEngine;
using UnityEngine.UIElements;
using QualityLevel = Ballverse.Core.RenderGeometry.QualityLevel;

namespace Ballverse.Render
{
    /// <summary>Supplies the frame to show this display frame (a live run, the studio player…).</summary>
    public interface IBallverseFrameProvider
    {
        /// <summary>
        /// The frame to draw and its tick; false when there is nothing to draw yet. <paramref name="frame"/> may be null:
        /// the presenter then reads the frame of <paramref name="tick"/> from <see cref="RenderContext.FrameSource"/>
        /// (the studio player over a stream — forward reads only, see <see cref="BallverseRenderer.Render(RenderContext, RenderTexture)"/>).
        /// </summary>
        bool TryGetFrame(out RenderFrame frame, out int tick);
    }

    /// <summary>
    /// Real-time presentation of a simulation, independent of the UI toolkit: owns the <see cref="BallverseRenderer"/>,
    /// the render texture (sized in pixels by the caller) and the dynamic quality governor (spec §7.7: start profile from
    /// the device tier, one step down when the FPS stays below 55). Call <see cref="Present"/> once per displayed frame
    /// (from <c>LateUpdate</c>) with the slot's size in pixels; show <see cref="Target"/> however the UI likes — a UI Toolkit
    /// element background (<c>Background.FromRenderTexture</c>), a uGUI <c>RawImage</c> (<see cref="BallverseView"/>) or a
    /// view model's texture property. <see cref="TargetChanged"/> fires when the texture object is replaced (resize).
    /// Export does not use it: it drives <see cref="BallverseRenderer.RenderOffline(RenderContext, RenderTexture)"/>.
    /// </summary>
    public sealed class BallversePresenter : IDisposable
    {
        /// <summary>Smallest and largest render texture side, pixels.</summary>
        public const int MinSize = 16;
        public const int MaxSize = 4096;

        readonly BallverseRenderer _renderer;
        RenderTexture _target;
        QualityGovernor _governor;
        bool _disposed;

        /// <param name="resources">Shared shaders and materials (null: the renderer creates its own).</param>
        /// <param name="start">Starting real-time profile (A, B or C).</param>
        public BallversePresenter(RenderResources resources = null, QualityLevel start = QualityLevel.A)
        {
            _renderer = new BallverseRenderer(resources);
            _governor = new QualityGovernor(start == QualityLevel.Export ? QualityLevel.A : start);
        }

        /// <summary>The render context (stream tables, look, events…). Fill it before frames are provided.</summary>
        public RenderContext Context { get; } = new RenderContext();

        public IBallverseFrameProvider Provider { get; set; }

        public BallverseRenderer Renderer => _renderer;

        /// <summary>The texture the simulation is rendered into (null before the first frame).</summary>
        public RenderTexture Target => _target;

        /// <summary>Lower the profile automatically when the average FPS stays below 55 for 3 s.</summary>
        public bool DynamicQuality { get; set; } = true;

        /// <summary>The active real-time quality level.</summary>
        public QualityLevel Quality => _governor.Level;

        /// <summary>Raised when the dynamic fallback lowered the quality (for analytics / diagnostics).</summary>
        public event Action<QualityLevel> QualityChanged;

        /// <summary>Raised when <see cref="Target"/> is replaced (first frame or a size change); the argument is the new texture.</summary>
        public event Action<RenderTexture> TargetChanged;

        /// <summary>Sets the starting profile from the device tier (spec §8.1) and restarts the governor.</summary>
        public void SetTier(DeviceTier tier)
        {
            _governor = new QualityGovernor(QualityProfile.ForTier(tier).Level);
        }

        /// <summary>Forces a real-time level (user choice in settings).</summary>
        public void SetQuality(QualityLevel level)
        {
            _governor.SetLevel(level == QualityLevel.Export ? QualityLevel.A : level);
        }

        /// <summary>
        /// Renders the provider's frame at <paramref name="pixelWidth"/> × <paramref name="pixelHeight"/> (clamped to
        /// [<see cref="MinSize"/>, <see cref="MaxSize"/>]); <paramref name="deltaSeconds"/> is the unscaled frame time for
        /// the governor. Returns true when a frame was drawn.
        /// </summary>
        public bool Present(int pixelWidth, int pixelHeight, float deltaSeconds)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BallversePresenter));
            if (Provider == null || Context.Shapes == null || pixelWidth <= 0 || pixelHeight <= 0) return false;
            if (!Provider.TryGetFrame(out RenderFrame frame, out int tick)) return false;
            if (frame == null && Context.FrameSource == null) return false;

            if (DynamicQuality && _governor.AddFrame(deltaSeconds)) QualityChanged?.Invoke(_governor.Level);
            Context.Quality = _governor.Profile;
            Context.Tick = tick;

            EnsureTarget(ClampSize(pixelWidth), ClampSize(pixelHeight));
            if (frame != null)
            {
                _renderer.Render(frame, Context, _target);
                return true;
            }
            return _renderer.Render(Context, _target);
        }

        /// <summary>Clamps a side length to [<see cref="MinSize"/>, <see cref="MaxSize"/>].</summary>
        public static int ClampSize(int pixels) => Mathf.Clamp(pixels, MinSize, MaxSize);

        /// <summary>
        /// Size in pixels of a UI Toolkit element as laid out on screen. <paramref name="pixelsPerPoint"/> converts panel
        /// points to pixels; pass 0 to derive it from the panel root covering the screen (<c>Screen.width / root width</c>),
        /// or the panel's own scale when it renders to a texture. False before the element is laid out.
        /// </summary>
        public static bool TryGetPixelSize(VisualElement element, float pixelsPerPoint, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (element == null || element.panel == null) return false;
            Rect r = element.worldBound;
            if (!(r.width > 0f) || !(r.height > 0f)) return false;
            float scale = pixelsPerPoint;
            if (!(scale > 0f))
            {
                Rect root = element.panel.visualTree.worldBound;
                scale = root.width > 0f ? Screen.width / root.width : 1f;
            }
            width = ClampSize(Mathf.RoundToInt(r.width * scale));
            height = ClampSize(Mathf.RoundToInt(r.height * scale));
            return true;
        }

        void EnsureTarget(int w, int h)
        {
            if (_target != null && _target.width == w && _target.height == h) return;
            RenderTexture old = _target;
            _target = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "BallversePresenter",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            _target.Create();
            TargetChanged?.Invoke(_target);
            if (old != null)
            {
                old.Release();
                RenderResources.Destroy(old);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _renderer.Dispose();
            if (_target != null)
            {
                RenderTexture old = _target;
                _target = null;
                TargetChanged?.Invoke(null);
                old.Release();
                RenderResources.Destroy(old);
            }
        }
    }
}
