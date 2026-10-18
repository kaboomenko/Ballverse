using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Turns one <see cref="RenderFrame"/> (+ the stream's shape table, the render events and the look) into draw lists:
    /// the engine-independent half of the renderer (spec §7.7). The Unity renderer only uploads and draws what this
    /// produces, so preview and export share one code path and every decision here is unit-tested.
    /// <para>Draw order: background → <see cref="Under"/> → <see cref="Trails"/> → <see cref="Balls"/> → <see cref="Over"/> →
    /// <see cref="Particles"/> → <see cref="Labels"/>. In the 2D view all shapes and overlays are in <see cref="Under"/>; the
    /// Helix view puts platforms above the ball into <see cref="Over"/>.</para>
    /// <para>History: trails and camera smoothing depend on recent ticks. Call <see cref="Sync"/> (caller-owned frame, e.g.
    /// a live run) or <see cref="SyncAndRead"/> (frame read from the source, e.g. a stream reader: forward reads only) before
    /// <see cref="Compose"/>; they replay only the ticks not seen yet (sequential playback reads nothing extra) and after a
    /// seek refill the needed window from the frame source, so a frame looks the same however it was reached.</para>
    /// Allocation-free per frame once buffers reached their peak sizes.
    /// </summary>
    public sealed class RenderComposer
    {
        /// <summary>Trail history kept per body, ticks (the longest trail style, 1 s).</summary>
        public const int TrailCapacityTicks = SimConstants.TickRate;

        static readonly CameraSettings s_defaultHelixCamera = CameraSettings.ForHelix();

        readonly uint[] _palette = new uint[256];
        readonly ShapeGeometryCache _cache;
        readonly TrailTracker _trails;
        readonly CameraTrack _camera = new CameraTrack();
        readonly HelixTowerBuilder _helix = new HelixTowerBuilder();
        readonly VfxSystem _vfx = new VfxSystem();
        readonly VfxSettings _vfxSettings = new VfxSettings();
        readonly RenderFrame _scratchFrame = new RenderFrame();
        readonly LocalGeometry _overlay = new LocalGeometry();
        readonly CameraSettings _fixedCamera = new CameraSettings();
        readonly CameraSettings _helixCamera = CameraSettings.ForHelix();
        Particle[] _particles3 = new Particle[64];
        BallInstance[] _balls = new BallInstance[64];
        ParticleInstance[] _particles = new ParticleInstance[64];
        LabelInstance[] _labels = new LabelInstance[32];
        HelixSplat[] _splats = new HelixSplat[16];
        int _ballCount;
        int _particleCount;
        int _labelCount;
        int _lastSyncedTick = -1;
        RenderQuantization _quantization;
        IReadOnlyList<ShapeDefinition> _shapes;
        RenderGridSpec _grid;

        public RenderComposer(int maxTrailBodies = 256, int shapeCacheCapacity = 1024)
        {
            _cache = new ShapeGeometryCache(shapeCacheCapacity);
            _trails = new TrailTracker(maxTrailBodies, TrailCapacityTicks);
        }

        // ------------------------------------------------------------------ outputs

        public GeometryBatch Under { get; } = new GeometryBatch(1024, 3072);
        public GeometryBatch Over { get; } = new GeometryBatch(256, 768);
        public GeometryBatch Trails { get; } = new GeometryBatch(512, 1536);
        public ReadOnlySpan<BallInstance> Balls => new ReadOnlySpan<BallInstance>(_balls, 0, _ballCount);
        public ReadOnlySpan<ParticleInstance> Particles => new ReadOnlySpan<ParticleInstance>(_particles, 0, _particleCount);
        public ReadOnlySpan<LabelInstance> Labels => new ReadOnlySpan<LabelInstance>(_labels, 0, _labelCount);
        public BackgroundParams Background { get; private set; }
        /// <summary>The world rectangle shown (drawing-plane metres), including camera shake.</summary>
        public ViewRect View { get; private set; }
        public float PixelsPerMetre { get; private set; }
        /// <summary>Bloom intensity for this frame (theme × glow), 0 = off.</summary>
        public float BloomIntensity { get; private set; }
        public float BloomThreshold { get; private set; }
        /// <summary>The resolved 256-entry palette of the last frame.</summary>
        public ReadOnlySpan<uint> Palette => _palette;
        /// <summary>Polygons that failed the simplicity check in the last frame (drawn with the fallback triangulation).</summary>
        public int SelfIntersectingShapes { get; private set; }
        /// <summary>The shape cache (diagnostics).</summary>
        public ShapeGeometryCache ShapeCache => _cache;
        public bool ParticlesCapped => _vfx.Capped;

        // ------------------------------------------------------------------ stream binding & history

        /// <summary>Binds a stream (quantization, shape table, optional grid layer); clears caches and history.</summary>
        public void SetStream(RenderQuantization quantization, IReadOnlyList<ShapeDefinition> shapes, RenderGridSpec grid = null)
        {
            _quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
            _shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
            _grid = grid;
            _cache.Reset(shapes, quantization);
            ResetHistory();
        }

        public void ResetHistory()
        {
            _trails.Reset();
            _camera.Reset();
            _lastSyncedTick = -1;
        }

        /// <summary>Ticks of history the settings need (trail length, camera smoothing / "only down" window).</summary>
        public static int HistoryTicks(ComposeSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            int trail = TrailBuildSettings.FullLengthTicks(settings.Look.Trail);
            CameraSettings cam = settings.Camera ?? (settings.View == ModeView.Helix ? s_defaultHelixCamera : null);
            int camera = 0;
            if (cam != null && cam.Mode != CameraMode.Fixed)
            {
                camera = CameraTrack.FilterTicks(cam.SmoothingSeconds) + (cam.Mode == CameraMode.Follow && cam.FollowOnlyDown ? CameraTrack.MinWindowTicks : 0);
            }
            return Math.Max(trail, camera);
        }

        /// <summary>
        /// Brings trails and camera history to <paramref name="tick"/>, whose frame is <paramref name="frame"/>. Missing
        /// ticks are read from <paramref name="source"/> (may be null: then only the given frame is recorded and a gap
        /// restarts trails). Allocation-free.
        /// <para>When <paramref name="frame"/> itself comes from the same stream reader as <paramref name="source"/>, use
        /// <see cref="SyncAndRead"/> instead: reading tick t first and then the skipped ticks t − k … t − 1 makes the reader
        /// rewind and re-decode its block on every output frame.</para>
        /// </summary>
        public void Sync(int tick, RenderFrame frame, IRenderFrameSource source, ComposeSettings settings)
        {
            if (_quantization == null) throw new InvalidOperationException("SetStream must be called first");
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (tick == _lastSyncedTick) return;
            int from = BeginSync(tick, settings);
            ReplayHistory(from, tick, source);
            Record(tick, frame);
            _lastSyncedTick = tick;
        }

        /// <summary>
        /// Reads the frame of <paramref name="tick"/> from <paramref name="source"/> into <paramref name="frame"/> and brings
        /// trails and camera history to it. Every tick is read in increasing order — the history ticks first, the shown tick
        /// last — so a <see cref="RenderStreamFrameSource"/> only ever decodes forwards and the work per output frame is linear
        /// in the ticks it advances (the studio player and the export loop at any ticks-per-frame ratio). Returns false, with
        /// <paramref name="frame"/> unchanged, when the source cannot supply <paramref name="tick"/>. Allocation-free.
        /// </summary>
        public bool SyncAndRead(int tick, IRenderFrameSource source, RenderFrame frame, ComposeSettings settings)
        {
            if (_quantization == null) throw new InvalidOperationException("SetStream must be called first");
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (tick == _lastSyncedTick) return source.TryReadFrame(tick, frame);
            int from = BeginSync(tick, settings);
            ReplayHistory(from, tick, source);
            if (from < tick) _lastSyncedTick = tick - 1;
            if (!source.TryReadFrame(tick, frame)) return false;
            Record(tick, frame);
            _lastSyncedTick = tick;
            return true;
        }

        /// <summary>First tick to replay before <paramref name="tick"/>; resets the history when it cannot continue.</summary>
        int BeginSync(int tick, ComposeSettings settings)
        {
            int window = HistoryTicks(settings);
            if (_lastSyncedTick >= 0 && tick > _lastSyncedTick && tick - _lastSyncedTick <= window + 1)
            {
                return _lastSyncedTick + 1;
            }
            _trails.Reset();
            _camera.Reset();
            _lastSyncedTick = -1;
            return Math.Max(0, tick - window);
        }

        void ReplayHistory(int from, int tick, IRenderFrameSource source)
        {
            if (source == null) return;
            for (int t = from; t < tick; t++)
            {
                if (source.TryReadFrame(t, _scratchFrame)) Record(t, _scratchFrame);
            }
        }

        void Record(int tick, RenderFrame frame)
        {
            _trails.Record(tick, frame, _quantization, _shapes);
            _camera.Record(tick, frame, _quantization, _shapes);
        }

        // ------------------------------------------------------------------ compose

        /// <summary>
        /// Builds the draw lists of <paramref name="frame"/> at <paramref name="tick"/>. <paramref name="events"/> must be
        /// tick-ordered and include every event up to <see cref="VfxSystem.MaxEffectTicks"/> ticks back
        /// (<c>log.GetWindow(tick, VfxSystem.MaxEffectTicks)</c>).
        /// </summary>
        public void Compose(int tick, RenderFrame frame, ReadOnlySpan<RenderEvent> events, ComposeSettings settings)
        {
            if (_quantization == null) throw new InvalidOperationException("SetStream must be called first");
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            LookSettings look = settings.Look;
            RenderTheme theme = look.Theme ?? RenderThemeCatalog.Default;
            QualityProfile quality = settings.Quality;
            look.ResolvePalette(_palette);

            Under.Clear();
            Over.Clear();
            Trails.Clear();
            _ballCount = 0;
            _particleCount = 0;
            _labelCount = 0;
            SelfIntersectingShapes = 0;
            _cache.BeginFrame();

            bool helix = settings.View == ModeView.Helix;
            if (helix && settings.Camera == null) _helixCamera.ViewHeight = settings.Helix.ViewHeight;
            CameraSettings cam = settings.Camera ?? (helix ? _helixCamera : _fixedCamera);
            int w = settings.CanvasWidth, h = settings.CanvasHeight;
            ViewRect view = _camera.Evaluate(tick, cam, w, h, out float focusY);
            float towerAxis = helix ? TowerAxisX(frame) : 0f;
            if (helix && cam.Mode == CameraMode.Follow)
            {
                // The follow anchor (38 % from the top, M05) applies to the ball as drawn: the projection puts the ball on the
                // front of its path circle, below the focus height, so shift the view by that drop.
                var focus = new HelixProjection(towerAxis, focusY, settings.Helix.TiltRadians, settings.Helix.CameraDistance, settings.Helix.BallPathRadius);
                focus.ProjectBody(towerAxis, focusY, out _, out float drawnY, out _);
                view.CenterY -= focusY - drawnY;
            }
            if (VfxSystem.TryGetShake(events, tick, settings.ReduceMotion, out float shakeX, out float shakeY))
            {
                view.CenterX += shakeX;
                view.CenterY += shakeY;
            }
            View = view;
            float ppm = w / view.Width;
            PixelsPerMetre = ppm;
            TessellationSettings tess = TessellationSettings.Create(ppm, quality.TolerancePixels);

            BloomIntensity = quality.Bloom == BloomMode.Off ? 0f : theme.BloomIntensity * look.Glow * 2f;
            BloomThreshold = theme.BloomThreshold;
            Background = new BackgroundParams
            {
                Style = look.EffectiveBackground,
                Top = look.Background == BackgroundOption.Color ? look.BackgroundColor : theme.BackgroundTop,
                Bottom = look.Background == BackgroundOption.Color ? look.BackgroundColor : theme.BackgroundBottom,
                GridColor = theme.GridColor,
                GridSpacing = 1f,
                StarColor = theme.StarColor,
                Time = tick / (float)SimConstants.TickRate,
                Vignette = theme.Vignette,
            };

            if (frame.HasGrid && _grid != null) AddGridLayer(frame, theme);

            if (helix)
            {
                var proj = new HelixProjection(towerAxis, focusY, settings.Helix.TiltRadians, settings.Helix.CameraDistance, settings.Helix.BallPathRadius);
                ComposeHelix(tick, frame, events, settings, theme, tess, proj);
                ComposeBallsAndLabels(frame, events, tick, settings, theme, ref proj);
                TrailBuildSettings trail = TrailBuildSettings.Create(look.Trail, quality.TrailFraction, theme.TrailEmission, _palette[PaletteSlots.Accent]);
                TrailRibbonBuilder.Build(_trails, tick, trail, _palette, Trails, ref proj);
                ComposeParticles(tick, events, settings, theme, ref proj, true);
            }
            else
            {
                var proj = new PlanarProjector();
                ComposeShapes2D(frame, theme, tess);
                ComposeOverlays2D(tick, frame, events, theme, tess);
                ComposeBallsAndLabels(frame, events, tick, settings, theme, ref proj);
                TrailBuildSettings trail = TrailBuildSettings.Create(look.Trail, quality.TrailFraction, theme.TrailEmission, _palette[PaletteSlots.Accent]);
                TrailRibbonBuilder.Build(_trails, tick, trail, _palette, Trails, ref proj);
                ComposeParticles(tick, events, settings, theme, ref proj, false);
            }
        }

        /// <summary>The tower axis: x of the first visible ring body (platform), else the arena centre.</summary>
        float TowerAxisX(RenderFrame frame)
        {
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0 || (uint)b.ShapeId >= (uint)_shapes.Count) continue;
                if (_shapes[b.ShapeId].Kind == ShapeKind.Ring) return _quantization.DequantizePosition(b.X);
            }
            return 0f;
        }

        bool IsCircle(in BodyState b)
        {
            return (uint)b.ShapeId < (uint)_shapes.Count && _shapes[b.ShapeId].Kind == ShapeKind.Circle;
        }

        uint ShapeColor(in BodyState b)
        {
            uint c = _palette[b.Color];
            if ((b.Flags & BodyFlags.Dead) != 0) c = ColorMath.Lerp(c, _palette[PaletteSlots.Muted], 0.65f);
            return c;
        }

        void ComposeShapes2D(RenderFrame frame, RenderTheme theme, in TessellationSettings tess)
        {
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0) continue;
                if ((uint)b.ShapeId >= (uint)_shapes.Count) continue;
                if (_shapes[b.ShapeId].Kind == ShapeKind.Circle) continue;
                LocalGeometry g = _cache.Get(b.ShapeId, b.Scale, tess, out float drawScale, out TessellationStatus status);
                if (status == TessellationStatus.SelfIntersecting) SelfIntersectingShapes++;
                if (g.IndexCount == 0) continue;
                float angle = RenderQuantization.DequantizeAngle(b.Angle);
                float emission = theme.ShapeEmission * ((b.Flags & BodyFlags.Highlight) != 0 ? 1.6f : 1f);
                if ((b.Flags & BodyFlags.Dead) != 0) emission = 1f;
                float x = _quantization.DequantizePosition(b.X), y = _quantization.DequantizePosition(b.Y);
                uint color = ShapeColor(b);
                if ((b.Flags & RenderBodyFlags.FadeOutsideSafeZone) != 0)
                {
                    float extent = ShapeTessellator.BoundingRadius(_shapes[b.ShapeId], _quantization, _quantization.DequantizeScale(b.Scale));
                    color = ColorMath.WithAlpha(color, ColorMath.A(color) / 255f * SafeZoneFade.Alpha(x, y, extent));
                }
                Under.AppendTransformed(g, x, y, MathF.Cos(angle), MathF.Sin(angle), drawScale, color, emission);
            }
        }

        void ComposeOverlays2D(int tick, RenderFrame frame, ReadOnlySpan<RenderEvent> events, RenderTheme theme, in TessellationSettings tess)
        {
            int flashTicks = (int)Math.Ceiling(VfxSystem.FlashSeconds * SimConstants.TickRate);
            int splatTicks = (int)Math.Ceiling(VfxSystem.SplatSeconds * SimConstants.TickRate);
            for (int i = 0; i < events.Length; i++)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) break;
                int age = tick - e.Tick;
                if (e.Type == RenderEventType.Splat && age < splatTicks)
                {
                    float fade = VfxSystem.Fade(age, VfxSystem.SplatSeconds);
                    _overlay.Clear();
                    ShapeTessellator.AddDisc(_overlay, 0f, 0f, Math.Max(0.02f, e.Size), tess);
                    Under.AppendTransformed(_overlay, e.X, e.Y, 1f, 0f, 1f, ColorMath.WithAlpha(_palette[e.ColorIndex], 0.85f * fade), theme.ShapeEmission);
                    continue;
                }
                if (e.Type != RenderEventType.Hit || e.TargetBodyId < 0 || age >= flashTicks) continue;
                if (!frame.TryGetBody(e.TargetBodyId, out BodyState target)) continue;
                if ((target.Flags & BodyFlags.Visible) == 0 || (uint)target.ShapeId >= (uint)_shapes.Count) continue;
                float flash = VfxSystem.Fade(age, VfxSystem.FlashSeconds);
                if (flash <= 0f) continue;
                ShapeDefinition shape = _shapes[target.ShapeId];
                float scale = _quantization.DequantizeScale(target.Scale);
                _overlay.Clear();
                if (!BuildHitHighlight(shape, scale, e, tess)) continue;
                float angle = RenderQuantization.DequantizeAngle(target.Angle);
                uint color = ColorMath.WithAlpha(ColorMath.Lerp(_palette[e.ColorIndex], ColorMath.White, 0.25f), flash);
                Under.AppendTransformed(_overlay, _quantization.DequantizePosition(target.X), _quantization.DequantizePosition(target.Y),
                    MathF.Cos(angle), MathF.Sin(angle), 1f, color, Math.Max(theme.ShapeEmission, 1f) * 2.2f);
            }
        }

        /// <summary>Body-local highlight of the hit part of a shape (ring arc around the contact, polyline / polygon edge).</summary>
        bool BuildHitHighlight(ShapeDefinition shape, float scale, in RenderEvent e, in TessellationSettings tess)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Ring:
                {
                    float radius = _quantization.DequantizeScale(shape.Radius) * scale;
                    float t = ShapeTessellator.StrokeThickness(shape, _quantization, scale) * 1.2f;
                    float inner = Math.Max(0f, radius - t * 0.5f), outer = radius + t * 0.5f;
                    const float halfWidth = 0.21f;   // ≈ 12° each side
                    int n = ArcMath.ArcSegments(outer, 2f * halfWidth, tess);
                    int first = _overlay.VertexCount;
                    for (int k = 0; k <= n; k++)
                    {
                        float a = e.LocalAngle - halfWidth + 2f * halfWidth * k / n;
                        float c = MathF.Cos(a), s = MathF.Sin(a);
                        _overlay.AddVertex(c * inner, s * inner);
                        _overlay.AddVertex(c * outer, s * outer);
                    }
                    for (int k = 0; k < n; k++)
                    {
                        int v = first + 2 * k;
                        _overlay.AddTriangle(v, v + 1, v + 3);
                        _overlay.AddTriangle(v, v + 3, v + 2);
                    }
                    return true;
                }
                case ShapeKind.Polyline:
                case ShapeKind.Polygon:
                {
                    ReadOnlySpan<ShapePoint> pts = shape.Points;
                    int seg = e.Feature;
                    bool closed = shape.Kind == ShapeKind.Polygon || shape.Closed;
                    int segments = closed ? pts.Length : pts.Length - 1;
                    if (seg < 0 || seg >= segments) return false;
                    ShapePoint p0 = pts[seg];
                    ShapePoint p1 = pts[(seg + 1) % pts.Length];
                    float ax = _quantization.DequantizePosition(p0.X) * scale, ay = _quantization.DequantizePosition(p0.Y) * scale;
                    float bx = _quantization.DequantizePosition(p1.X) * scale, by = _quantization.DequantizePosition(p1.Y) * scale;
                    float half = shape.Kind == ShapeKind.Polyline ? ShapeTessellator.StrokeThickness(shape, _quantization, scale) * 0.6f : 0.04f;
                    float dx = bx - ax, dy = by - ay;
                    float len = MathF.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-6f) return false;
                    float nx = -dy / len * half, ny = dx / len * half;
                    int a0 = _overlay.AddVertex(ax + nx, ay + ny);
                    int a1 = _overlay.AddVertex(ax - nx, ay - ny);
                    int b1 = _overlay.AddVertex(bx - nx, by - ny);
                    int b0 = _overlay.AddVertex(bx + nx, by + ny);
                    _overlay.AddTriangle(a0, a1, b1);
                    _overlay.AddTriangle(a0, b1, b0);
                    return true;
                }
                default:
                    return false;
            }
        }

        void ComposeHelix(int tick, RenderFrame frame, ReadOnlySpan<RenderEvent> events, ComposeSettings settings, RenderTheme theme,
            in TessellationSettings tess, in HelixProjection proj)
        {
            // The ball's top decides which platforms are drawn over the ball (a platform the ball is passing through stays
            // under it for the whole transit, M05).
            float ballTop = float.NaN;
            bool highlighted = false;
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0 || !IsCircle(b)) continue;
                bool hl = (b.Flags & BodyFlags.Highlight) != 0;
                if (float.IsNaN(ballTop) || (hl && !highlighted))
                {
                    ballTop = _quantization.DequantizePosition(b.Y) + Math.Max(0f, _quantization.DequantizeScale(b.Scale));
                    highlighted = hl;
                }
            }

            int splatCount = 0;
            int splatTicks = (int)Math.Ceiling(VfxSystem.SplatSeconds * SimConstants.TickRate);
            for (int i = 0; i < events.Length; i++)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) break;
                if (e.Type != RenderEventType.Splat || e.TargetBodyId < 0 || tick - e.Tick >= splatTicks) continue;
                float fade = VfxSystem.Fade(tick - e.Tick, VfxSystem.SplatSeconds);
                if (fade <= 0f) continue;
                if (splatCount == _splats.Length) Array.Resize(ref _splats, splatCount * 2);
                _splats[splatCount++] = new HelixSplat
                {
                    BodyId = e.TargetBodyId,
                    LocalAngle = e.LocalAngle,
                    Radius = Math.Max(0.05f, e.Size),
                    Color = ColorMath.WithAlpha(_palette[e.ColorIndex], fade),
                };
            }

            _helix.Build(frame, _quantization, _shapes, _palette, theme, settings.Helix, proj, tess, ballTop,
                new ReadOnlySpan<HelixSplat>(_splats, 0, splatCount), Under, Over);

            // Other non-circle bodies: flat at the ball path depth.
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0 || (uint)b.ShapeId >= (uint)_shapes.Count) continue;
                ShapeKind kind = _shapes[b.ShapeId].Kind;
                if (kind == ShapeKind.Circle || kind == ShapeKind.Ring) continue;
                LocalGeometry g = _cache.Get(b.ShapeId, b.Scale, tess, out float drawScale, out TessellationStatus status);
                if (status == TessellationStatus.SelfIntersecting) SelfIntersectingShapes++;
                if (g.IndexCount == 0) continue;
                float angle = RenderQuantization.DequantizeAngle(b.Angle);
                float c = MathF.Cos(angle) * drawScale, s = MathF.Sin(angle) * drawScale;
                float bx = _quantization.DequantizePosition(b.X), by = _quantization.DequantizePosition(b.Y);
                uint color = ShapeColor(b);
                int baseVertex = Under.VertexCount;
                ReadOnlySpan<Float2> src = g.Positions;
                for (int k = 0; k < src.Length; k++)
                {
                    float x = bx + c * src[k].X - s * src[k].Y;
                    float y = by + s * src[k].X + c * src[k].Y;
                    proj.ProjectPoint(x, y, -proj.BallPathRadius, out float px, out float py, out _);
                    Under.AddVertex(px, py, color, theme.ShapeEmission);
                }
                ReadOnlySpan<int> idx = g.Indices;
                for (int k = 0; k + 2 < idx.Length; k += 3) Under.AddTriangle(baseVertex + idx[k], baseVertex + idx[k + 1], baseVertex + idx[k + 2]);
            }
        }

        void ComposeBallsAndLabels<TProj>(RenderFrame frame, ReadOnlySpan<RenderEvent> events, int tick, ComposeSettings settings,
            RenderTheme theme, ref TProj proj) where TProj : struct, IPointProjector
        {
            LookSettings look = settings.Look;
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            BallFinish finish = theme.BallFinish;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0 || !IsCircle(b)) continue;
                float radius = _quantization.DequantizeScale(b.Scale);
                if (!(radius > 0f)) continue;
                proj.ProjectBody(_quantization.DequantizePosition(b.X), _quantization.DequantizePosition(b.Y), out float x, out float y, out float scale);
                radius *= scale * VfxSystem.GetPulse(events, tick, b.Id);
                float rx = radius, ry = radius, squashAngle = 0f;
                if (VfxSystem.TryGetSquash(events, tick, b.Id, out float amount, out float nx, out float ny))
                {
                    rx = radius * (1f - amount);
                    ry = radius * (1f + amount * 0.6f);
                    squashAngle = MathF.Atan2(ny, nx);
                }
                uint color = _palette[b.Color];
                float emission = theme.BallEmission;
                if ((b.Flags & BodyFlags.Highlight) != 0) emission *= 1.8f;
                if ((b.Flags & BodyFlags.Dead) != 0)
                {
                    color = ColorMath.WithAlpha(ColorMath.Lerp(color, _palette[PaletteSlots.Muted], 0.6f), 0.55f);
                    emission = 1f;
                }
                BallSkin skin = look.Skin;
                if ((skin == BallSkin.Emoji || skin == BallSkin.Flag) && b.SkinId == BodyState.NoSkin) skin = BallSkin.Solid;
                if (skin == BallSkin.Letter && b.LabelId == BodyState.NoLabel) skin = BallSkin.Solid;
                if (_ballCount == _balls.Length) Array.Resize(ref _balls, _ballCount * 2);
                _balls[_ballCount++] = new BallInstance
                {
                    X = x,
                    Y = y,
                    RadiusX = rx,
                    RadiusY = ry,
                    SquashAngle = squashAngle,
                    SkinAngle = RenderQuantization.DequantizeAngle(b.Angle),
                    Color = color,
                    Emission = emission,
                    Skin = skin,
                    Finish = skin == BallSkin.Solid ? finish : BallFinish.None,
                    SkinId = b.SkinId,
                    LabelId = b.LabelId,
                    BodyId = b.Id,
                    Flags = b.Flags,
                };

                bool letter = skin == BallSkin.Letter;
                if (b.LabelId != BodyState.NoLabel && (letter || look.Labels != LabelMode.Off))
                {
                    if (_labelCount == _labels.Length) Array.Resize(ref _labels, _labelCount * 2);
                    _labels[_labelCount++] = new LabelInstance
                    {
                        X = x,
                        Y = y,
                        Height = radius * (letter ? 1.0f : 0.62f),
                        MaxWidth = radius * 1.7f,
                        LabelId = b.LabelId,
                        Color = ColorMath.WithAlpha(ColorMath.ContrastText(color), ColorMath.A(color) / 255f),
                        BodyId = b.Id,
                        Letter = letter,
                    };
                }
            }
        }

        void ComposeParticles<TProj>(int tick, ReadOnlySpan<RenderEvent> events, ComposeSettings settings, RenderTheme theme,
            ref TProj proj, bool helix) where TProj : struct, IPointProjector
        {
            QualityProfile quality = settings.Quality;
            int max = quality.MaxParticles;
            if (_particles3.Length < max) _particles3 = new Particle[max];
            if (_particles.Length < max) _particles = new ParticleInstance[max];
            _vfxSettings.Impact = settings.Look.Impact;
            _vfxSettings.Break = settings.Look.Break;
            _vfxSettings.CountFactor = quality.ParticleFraction;
            _vfxSettings.Palette = _palette;
            _vfxSettings.ConfettiColors = theme.PaletteCount;
            _vfxSettings.Emission = Math.Max(1f, theme.BallEmission) * 1.2f;
            _vfxSettings.HorizontalBreaks = helix;
            _vfxSettings.TiltSin = MathF.Sin(settings.Helix.TiltRadians);
            _vfxSettings.ReduceMotion = settings.ReduceMotion;
            int n = _vfx.Evaluate(events, tick, _vfxSettings, _shapes, _quantization, _particles3, max);
            for (int i = 0; i < n; i++)
            {
                ref Particle p = ref _particles3[i];
                float x, y, scale;
                if (float.IsNaN(p.Z)) proj.ProjectBody(p.X, p.Y, out x, out y, out scale);
                else proj.ProjectPoint(p.X, p.Y, p.Z, out x, out y, out scale);
                float rotation = p.Rotation;
                if (helix && p.Shape == ParticleShape.Streak && !float.IsNaN(p.Z))
                {
                    // Orient streaks along the projected velocity.
                    proj.ProjectPoint(p.X + p.VX * 0.02f, p.Y + p.VY * 0.02f, p.Z + p.VZ * 0.02f, out float x2, out float y2, out _);
                    rotation = MathF.Atan2(y2 - y, x2 - x);
                }
                _particles[i] = new ParticleInstance
                {
                    X = x,
                    Y = y,
                    SizeX = p.SizeX * scale,
                    SizeY = p.Shape == ParticleShape.Ring ? p.SizeY : p.SizeY * scale,
                    Rotation = rotation,
                    Color = p.Color,
                    Emission = p.Emission,
                    Shape = p.Shape,
                };
            }
            _particleCount = n;
        }

        void AddGridLayer(RenderFrame frame, RenderTheme theme)
        {
            RenderGridSpec spec = _grid;
            if (frame.GridWidth != spec.Width || frame.GridHeight != spec.Height) return;
            float minX = _quantization.DequantizePosition(spec.MinX), minY = _quantization.DequantizePosition(spec.MinY);
            float maxX = _quantization.DequantizePosition(spec.MaxX), maxY = _quantization.DequantizePosition(spec.MaxY);
            ReadOnlySpan<byte> cells = frame.Grid;
            if (spec.Topology == GridTopology.Square)
            {
                float cw = (maxX - minX) / spec.Width, ch = (maxY - minY) / spec.Height;
                Under.Reserve(cells.Length * 4, cells.Length * 6);
                for (int y = 0; y < spec.Height; y++)
                {
                    for (int x = 0; x < spec.Width; x++)
                    {
                        uint c = _palette[cells[y * spec.Width + x]];
                        float x0 = minX + x * cw, y0 = minY + y * ch;
                        int a = Under.AddVertex(x0, y0, c, 1f);
                        int b = Under.AddVertex(x0 + cw, y0, c, 1f);
                        int d = Under.AddVertex(x0 + cw, y0 + ch, c, 1f);
                        int e = Under.AddVertex(x0, y0 + ch, c, 1f);
                        Under.AddQuad(a, b, d, e);
                    }
                }
                return;
            }
            // Pointy-top hexagons (GridTopology.HexOddRowsShifted), stretched to the bounds like the topology defines.
            float w = (maxX - minX) / (spec.Width + 0.5f);
            float hexH = (maxY - minY) / (0.75f * spec.Height + 0.25f);
            Under.Reserve(cells.Length * 7, cells.Length * 18);
            for (int y = 0; y < spec.Height; y++)
            {
                for (int x = 0; x < spec.Width; x++)
                {
                    spec.CellCenter(x, y, out float cx, out float cy);
                    uint c = _palette[cells[y * spec.Width + x]];
                    int center = Under.AddVertex(cx, cy, c, 1f);
                    int first = Under.VertexCount;
                    Under.AddVertex(cx, cy + hexH * 0.5f, c, 1f);
                    Under.AddVertex(cx + w * 0.5f, cy + hexH * 0.25f, c, 1f);
                    Under.AddVertex(cx + w * 0.5f, cy - hexH * 0.25f, c, 1f);
                    Under.AddVertex(cx, cy - hexH * 0.5f, c, 1f);
                    Under.AddVertex(cx - w * 0.5f, cy - hexH * 0.25f, c, 1f);
                    Under.AddVertex(cx - w * 0.5f, cy + hexH * 0.25f, c, 1f);
                    for (int k = 0; k < 6; k++) Under.AddTriangle(center, first + k, first + (k + 1) % 6);
                }
            }
        }
    }
}
