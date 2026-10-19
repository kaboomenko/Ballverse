using System;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Render;
using UnityEngine;
using QualityLevel = Ballverse.Core.RenderGeometry.QualityLevel;

namespace Ballverse.Sim
{
    /// <summary>
    /// Draws a <see cref="LiveSimController"/>'s run through Ballverse.Render (<see cref="BallversePresenter"/>): the stream
    /// tables come from the run's visual table (live view, no copy), past frames from its frame ring (trails, camera), VFX from
    /// its render events, the look from its parameters. Owns the render texture the simulation screen shows edge to edge.
    /// Call <see cref="Present"/> once per displayed frame (LateUpdate). Main thread.
    /// </summary>
    public sealed class LiveSimView : IBallverseFrameProvider, IDisposable
    {
        readonly BallversePresenter _presenter;
        readonly RenderThemeCatalog _themes;
        LiveSimController _controller;
        int _boundGeneration = -1;
        bool _disposed;

        public LiveSimView(RenderResources resources = null, RenderThemeCatalog themes = null)
        {
            _presenter = new BallversePresenter(resources);
            _presenter.Provider = this;
            _presenter.TargetChanged += t => TargetChanged?.Invoke(t);
            _themes = themes ?? new RenderThemeCatalog();
        }

        /// <summary>The texture the run is drawn into (null before the first frame).</summary>
        public RenderTexture Target => _presenter.Target;

        public event Action<RenderTexture> TargetChanged;

        public BallversePresenter Presenter => _presenter;

        public RenderContext Context => _presenter.Context;

        /// <summary>Starting quality from the device tier (dynamic fallback below 55 FPS stays on).</summary>
        public void SetTier(DeviceTier tier) => _presenter.SetTier(tier);

        public void SetQuality(QualityLevel level) => _presenter.SetQuality(level);

        public bool ReduceMotion
        {
            get => _presenter.Context.ReduceMotion;
            set => _presenter.Context.ReduceMotion = value;
        }

        public void Bind(LiveSimController controller)
        {
            _controller = controller;
            _boundGeneration = -1;
        }

        /// <summary>Applies the look parameters (theme, colours, trail, glow, labels, HUD…). Call when they change.</summary>
        public void ApplyLook(ParamSet values)
        {
            if (values == null) return;
            _presenter.Context.Look.Apply(values, _themes);
        }

        /// <summary>Draws the current frame at <paramref name="width"/> × <paramref name="height"/> pixels.</summary>
        public bool Present(int width, int height, float deltaSeconds)
        {
            if (_disposed || _controller == null || !_controller.HasRun) return false;
            if (_boundGeneration != _controller.RunGeneration)
            {
                _boundGeneration = _controller.RunGeneration;
                RenderContext ctx = _presenter.Context;
                ctx.Quantization = _controller.Quantization;
                ctx.Shapes = _controller.Shapes;
                ctx.Labels = _controller.Labels;
                ctx.Grid = null;
                ctx.ModeId = _controller.Mode.Info.Id;
                ctx.FrameSource = _controller.FrameRing;
                ctx.Events = _controller.Events;
                ApplyLook(_controller.Values);
            }
            return _presenter.Present(width, height, deltaSeconds);
        }

        bool IBallverseFrameProvider.TryGetFrame(out RenderFrame frame, out int tick)
        {
            LiveSimController c = _controller;
            if (c == null || !c.HasRun || c.Frame == null)
            {
                frame = null;
                tick = 0;
                return false;
            }
            frame = c.Frame;
            tick = c.Tick;
            return true;
        }

        /// <summary>A small copy of the last drawn frame (run ring thumbnails), or null. The caller releases it with <see cref="ReleaseThumbnail"/>.</summary>
        public RenderTexture CaptureThumbnail(int width)
        {
            RenderTexture source = _presenter.Target;
            if (source == null || width <= 0) return null;
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (source.height / (float)Mathf.Max(1, source.width))));
            var thumb = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "BallverseRunThumb",
                hideFlags = HideFlags.HideAndDontSave,
            };
            thumb.Create();
            Graphics.Blit(source, thumb);
            return thumb;
        }

        public static void ReleaseThumbnail(Texture texture)
        {
            if (texture == null) return;
            if (texture is RenderTexture rt) rt.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _presenter.Dispose();
        }
    }
}
