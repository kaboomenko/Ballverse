using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// The user video's current frame as a GPU texture (spec §7.5 step b, release 1: RGBA32 frames from <see cref="IVideoReader"/>).
    /// Frames are uploaded top-down as they come (<see cref="Texture2D.LoadRawTextureData(Unity.Collections.NativeArray{byte})"/> +
    /// <see cref="Texture2D.Apply(bool, bool)"/>), so the compositor samples them with the Core UV map unflipped. The same source
    /// pts is not uploaded twice. Main thread.
    /// </summary>
    public sealed class VideoTextureUploader : IDisposable
    {
        Texture2D _texture;

        public Texture2D Texture => _texture;
        public bool HasFrame { get; private set; }
        /// <summary>Source pts of the uploaded frame, or long.MinValue.</summary>
        public long PtsUs { get; private set; } = long.MinValue;

        /// <summary>Uploads <paramref name="frame"/> unless it is the one already shown; does not dispose it.</summary>
        public void Upload(VideoFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (HasFrame && frame.PtsUs == PtsUs && _texture != null && _texture.width == frame.Width && _texture.height == frame.Height) return;
            if (_texture == null || _texture.width != frame.Width || _texture.height != frame.Height)
            {
                StudioUnity.Destroy(_texture);
                _texture = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
                {
                    name = "StudioUserVideo",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            _texture.LoadRawTextureData(frame.Data);
            _texture.Apply(false, false);
            PtsUs = frame.PtsUs;
            HasFrame = true;
        }

        /// <summary>Forget the shown frame (the next upload always happens).</summary>
        public void Invalidate()
        {
            HasFrame = false;
            PtsUs = long.MinValue;
        }

        public void Dispose()
        {
            StudioUnity.Destroy(_texture);
            _texture = null;
            HasFrame = false;
        }
    }

    /// <summary>
    /// Draws one studio frame — the single code path of preview and export (spec §3, §7.5): decodes the render-stream frame of
    /// the frame's tick, renders the simulation into a slot-sized render texture through <see cref="ISimFrameRenderer"/>, evaluates
    /// the text layers at the output time (show time, entry animation, live counter from the frame's HUD) and composes everything
    /// with <see cref="StudioCompositor"/>. The caller supplies the geometry (<see cref="FramePlan"/>) and the video slot picture
    /// (preview: realtime decoder; export: frame-exact decoder). Main thread; owns render textures (dispose).
    /// </summary>
    public sealed class StudioFrameRenderer : IDisposable
    {
        readonly StudioCompositor _compositor;
        readonly ISimFrameRenderer _sim;
        readonly StudioRenderQuality _quality;
        readonly int _maxSimSide;
        readonly float _maxSimZoomDensity;
        readonly bool _ownsSim;
        readonly List<TextDraw> _texts = new List<TextDraw>(8);
        readonly RenderTexture[] _simTargets = new RenderTexture[2];
        ClipSource _clip;
        ProjectDocument _doc;
        CompositeStyle _style;
        SlotContent _simContent = SlotContent.Default;
        int _lastIteration = int.MinValue;
        int _lastTick = int.MinValue;
        int _lastExternal = int.MinValue;
        int _discontinuity;
        bool _clipBegun;

        /// <param name="sim">This frame renderer's own simulation renderer (one clip per instance: never share it with another user).</param>
        /// <param name="maxSimSide">Cap of the simulation render target's longer side (preview may use less than export).</param>
        /// <param name="ownsSimRenderer">Dispose <paramref name="sim"/> (when it is <see cref="IDisposable"/>) with this renderer.</param>
        /// <param name="maxSimZoomDensity">Zoom up to which the whole-arena simulation target grows with a pinch zoom (1 for the
        /// preview, <see cref="SimSlotSizing.ExportMaxZoomDensity"/> for export; see <see cref="SimSlotSizing.Compute"/>).</param>
        public StudioFrameRenderer(StudioCompositor compositor, ISimFrameRenderer sim, StudioRenderQuality quality, int maxSimSide = SimSlotSizing.DefaultMaxSide,
            bool ownsSimRenderer = false, float maxSimZoomDensity = 1f)
        {
            _maxSimZoomDensity = Math.Max(1f, maxSimZoomDensity);
            _compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _quality = quality;
            _maxSimSide = Math.Max(SimSlotSizing.MinSide, maxSimSide);
            _ownsSim = ownsSimRenderer;
        }

        public StudioCompositor Compositor => _compositor;
        public ProjectDocument Project => _doc;
        public CompositeStyle Style => _style;
        public ClipSource Clip => _clip;

        /// <summary>The last decoded render-stream frame (HUD values for the UI).</summary>
        public RenderFrame LastFrame { get; private set; }

        /// <summary>Opens the clip to draw (the renderer keeps it until <see cref="CloseClip"/>; the caller owns and disposes it).</summary>
        public void OpenClip(ClipSource clip)
        {
            CloseClip();
            _clip = clip ?? throw new ArgumentNullException(nameof(clip));
            _sim.BeginClip(clip.Info);
            _clipBegun = true;
            ResetHistory();
        }

        public void CloseClip()
        {
            if (_clipBegun) _sim.EndClip();
            _clipBegun = false;
            _clip = null;
        }

        /// <summary>The project (or an edit of it) to draw; text layouts are recomputed.</summary>
        public void SetProject(ProjectDocument doc, CompositeStyle style)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _style = style ?? CompositeStyle.FromProject(doc, new CompositeStyle().ThemeBackground);
            _simContent = ProjectComposition.BuildSimContent(doc.Clip);
            _compositor.Text.ClearCache();
        }

        /// <summary>Forget trail / particle history (seek).</summary>
        public void ResetHistory()
        {
            _discontinuity++;
            _lastTick = int.MinValue;
            _lastIteration = int.MinValue;
        }

        /// <summary>
        /// Draws the frame showing <paramref name="sim"/> at output time <paramref name="outputUs"/> into <paramref name="target"/>.
        /// </summary>
        /// <param name="plan">Geometry at the target's canvas size.</param>
        /// <param name="video">Video slot picture (ignored without a video slot).</param>
        /// <param name="externalDiscontinuity">Caller's seek counter (preview clock); any change drops history.</param>
        /// <param name="maskOverlay">Platform masks to tint (preview only), or null.</param>
        public void Render(RenderTexture target, FramePlan plan, SimSample sim, long outputUs, SlotPicture video, int externalDiscontinuity,
            IReadOnlyList<IntRect> maskOverlay = null)
        {
            if (_clip == null) throw new InvalidOperationException("no clip open");
            if (_doc == null) throw new InvalidOperationException("no project set");
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            // a. Simulation slot.
            RenderFrame frame = _clip.ReadFrame(sim.Tick);
            LastFrame = frame;
            bool frozen = sim.Kind != SimSampleKind.Playing;
            if (externalDiscontinuity != _lastExternal || sim.Iteration != _lastIteration || (_lastTick != int.MinValue && sim.Tick < _lastTick))
            {
                _discontinuity++;
            }
            _lastExternal = externalDiscontinuity;
            _lastIteration = sim.Iteration;
            _lastTick = sim.Tick;
            IntRect slotRect = plan.Layout.Simulation.Rect;
            SimSlotRender sizing = SimSlotSizing.Compute(Math.Max(1, slotRect.Width), Math.Max(1, slotRect.Height), _simContent, _maxSimSide,
                _sim.SupportsViewport, _maxSimZoomDensity);
            RenderTexture simTarget = SimTarget(sizing.TargetSize);
            _sim.SetFrameTime(new SimFrameTime(sim.Tick, frozen, sim.Iteration, _discontinuity, outputUs, sizing.Viewport));
            _sim.Render(frame, _clip.Shapes, _clip.Labels, simTarget, _quality);
            FitBackground simBg = _doc.Clip?.FitBackground;
            var simPicture = new SlotPicture
            {
                Kind = SlotPictureKind.Picture,
                Texture = simTarget,
                BottomUp = true,
                Mapping = sizing.Mapping,
                Content = _simContent,
                Background = simBg?.Kind ?? FitBackgroundKind.Blur,
                BackgroundColor = StudioColor.Parse(simBg?.Color, StudioColor.Black),
            };

            // b. Text layers at this output time.
            _texts.Clear();
            List<TextLayer> layers = _doc.Texts;
            if (layers != null)
            {
                int? counter = null;
                bool counterRead = false;
                for (int i = 0; i < layers.Count; i++)
                {
                    TextLayer layer = layers[i];
                    if (layer == null || string.IsNullOrEmpty(layer.Text) || !TextLayerTiming.IsVisible(layer, outputUs)) continue;
                    if (!counterRead && TextLayerTiming.UsesCounter(layer.Text))
                    {
                        counter = HudCounter.Remaining(frame);
                        counterRead = true;
                    }
                    string text = TextLayerTiming.ResolveText(layer.Text, counter);
                    TextBlock block = _compositor.Text.Layout(layer, text);
                    TextLayerState state = TextLayerTiming.Evaluate(layer, outputUs, text.Length);
                    _texts.Add(new TextDraw(block, _compositor.Text.FontOf(layer), state));
                }
            }

            // c. Compose.
            _compositor.Compose(target, plan, simPicture, video, _style, _texts, maskOverlay);
        }

        RenderTexture SimTarget(IntVec2 size)
        {
            for (int i = 0; i < _simTargets.Length; i++)
            {
                RenderTexture rt = _simTargets[i];
                if (rt != null && rt.width == size.X && rt.height == size.Y) return rt;
            }
            // Keep at most two sizes (the layout's and the expanded L4 form); replace the older one.
            int slot = _simTargets[0] == null ? 0 : _simTargets[1] == null ? 1 : 0;
            if (_simTargets[slot] != null)
            {
                StudioUnity.Release(_simTargets[slot]);
                if (slot == 0)
                {
                    _simTargets[0] = _simTargets[1];
                    _simTargets[1] = null;
                    slot = 1;
                }
            }
            RenderTexture created = StudioCompositor.CreateCanvas(size.X, size.Y, "StudioSimSlot");
            _simTargets[slot] = created;
            return created;
        }

        public void Dispose()
        {
            CloseClip();
            for (int i = 0; i < _simTargets.Length; i++)
            {
                StudioUnity.Release(_simTargets[i]);
                _simTargets[i] = null;
            }
            if (_ownsSim && _sim is IDisposable d) d.Dispose();
        }
    }
}
