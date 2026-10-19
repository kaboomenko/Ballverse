using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render
{
    /// <summary>
    /// Draws one <see cref="RenderFrame"/> of a simulation into a <see cref="RenderTexture"/> (spec §7.7). The same call
    /// renders the live preview, the studio scrub and every frame of the offline export (§7.5 step a), so preview equals
    /// export by construction:
    /// <list type="number">
    /// <item><see cref="RenderComposer"/> (pure C#, unit-tested) turns the frame, shape table, render events and look into
    /// batches, ball / particle instances and labels.</item>
    /// <item>This class uploads them and records one command buffer: background → shapes under the balls → trails → SDF
    /// balls (instanced) → shapes over the balls (Helix) → particles (instanced) → labels, into an HDR multisampled scene
    /// buffer; then bloom and the composite into the target. The command buffer runs immediately
    /// (<see cref="Graphics.ExecuteCommandBuffer"/>) and does not depend on a camera, the render pipeline's renderer
    /// features or URP Volumes, so the export loop may render many frames per Unity frame.</item>
    /// </list>
    /// The target should be an sRGB colour texture (e.g. ARGB32 with sRGB read/write in a linear project). Not thread-safe;
    /// call from the main thread. Allocation-free per frame once buffers reached their peak sizes.
    /// </summary>
    public sealed class BallverseRenderer : IDisposable
    {
        /// <summary>Mesh sets rotated per rendered frame, matching the export's maximum frames in flight (§7.5 e).</summary>
        const int FrameSets = 3;

        readonly RenderResources _resources;
        readonly bool _ownsResources;
        readonly RenderComposer _composer;
        readonly ComposeSettings _compose = new ComposeSettings();
        readonly CommandBuffer _cmd = new CommandBuffer { name = "Ballverse.Render" };
        readonly RenderTargets _targets = new RenderTargets();
        readonly InstanceDrawer _instances = new InstanceDrawer();
        readonly BatchMeshUploader[] _under = new BatchMeshUploader[FrameSets];
        readonly BatchMeshUploader[] _trails = new BatchMeshUploader[FrameSets];
        readonly BatchMeshUploader[] _over = new BatchMeshUploader[FrameSets];
        readonly LabelRenderer[] _labels = new LabelRenderer[FrameSets];
        readonly RenderFrame _sourceFrame = new RenderFrame();
        HudEntry[] _hud = new HudEntry[16];
        int _hudCount;
        int _frameSet;
        IReadOnlyList<ShapeDefinition> _boundShapes;
        RenderQuantization _boundQuantization;
        RenderGridSpec _boundGrid;
        bool _disposed;
        bool _warnedIncomplete;

        /// <param name="resources">Shared shaders and materials; null creates (and later disposes) a private set.</param>
        public BallverseRenderer(RenderResources resources = null)
        {
            _ownsResources = resources == null;
            _resources = resources ?? new RenderResources();
            _composer = new RenderComposer();
            for (int i = 0; i < FrameSets; i++)
            {
                _under[i] = new BatchMeshUploader("BallverseUnder" + i);
                _trails[i] = new BatchMeshUploader("BallverseTrails" + i);
                _over[i] = new BatchMeshUploader("BallverseOver" + i);
                _labels[i] = new LabelRenderer();
            }
        }

        /// <summary>The composer (draw lists of the last frame; diagnostics and tests).</summary>
        public RenderComposer Composer => _composer;

        /// <summary>HUD values of the last rendered frame (spec §7.4 HUD state), for the UI layer to display.</summary>
        public ReadOnlySpan<HudEntry> Hud => new ReadOnlySpan<HudEntry>(_hud, 0, _hudCount);

        /// <summary>Whether the HUD should be shown (look parameter <c>hud</c>) for the last frame.</summary>
        public bool HudVisible { get; private set; } = true;

        /// <summary>
        /// Renders <paramref name="frame"/> (the frame of <c>context.Tick</c>) into <paramref name="target"/> with
        /// <c>context.Quality</c>. Real-time profiles render the scene at their render scale and upscale. Use this when the
        /// caller owns the frame (a live run). When the frame comes from a stream reader, use
        /// <see cref="Render(RenderContext, RenderTexture)"/> so the reader is only read forwards.
        /// </summary>
        public void Render(RenderFrame frame, RenderContext context, RenderTexture target)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            RenderCore(frame, context, target);
        }

        /// <summary>
        /// Reads the frame of <c>context.Tick</c> from <c>context.FrameSource</c> and renders it (studio player, export).
        /// The history ticks and the shown tick are read in increasing order (<see cref="RenderComposer.SyncAndRead"/>), so a
        /// <see cref="RenderStreamFrameSource"/> never rewinds, whatever the ticks-per-output-frame ratio. Returns false
        /// (nothing drawn) when the source cannot supply the tick. The frame read is available as <see cref="LastFrame"/>.
        /// </summary>
        public bool Render(RenderContext context, RenderTexture target)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.FrameSource == null) throw new ArgumentException("RenderContext.FrameSource is required", nameof(context));
            return RenderCore(null, context, target);
        }

        /// <summary>The frame read by the last <see cref="Render(RenderContext, RenderTexture)"/> (reused buffer).</summary>
        public RenderFrame LastFrame => _sourceFrame;

        bool RenderCore(RenderFrame frame, RenderContext context, RenderTexture target)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(BallverseRenderer));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (context.Shapes == null) throw new ArgumentException("RenderContext.Shapes is required", nameof(context));
            if (context.Quantization == null) throw new ArgumentException("RenderContext.Quantization is required", nameof(context));
            if (!_resources.IsComplete && !_warnedIncomplete)
            {
                _warnedIncomplete = true;
                Debug.LogError("[Ballverse.Render] Some renderer shaders failed to load; missing layers are skipped.");
            }

            BindStream(context);

            QualityProfile quality = context.Quality ?? QualityProfile.A;
            int width = target.width;
            int height = target.height;
            _compose.View = context.View;
            _compose.Look = context.Look ?? _compose.Look;
            _compose.Quality = quality;
            _compose.Camera = context.Camera;
            _compose.Helix = context.Helix ?? _compose.Helix;
            _compose.ReduceMotion = context.ReduceMotion;
            _compose.CanvasWidth = width;
            _compose.CanvasHeight = height;

            int tick = Math.Max(0, context.Tick);
            if (frame == null)
            {
                if (!_composer.SyncAndRead(tick, context.FrameSource, _sourceFrame, _compose)) return false;
                frame = _sourceFrame;
            }
            else
            {
                _composer.Sync(tick, frame, context.FrameSource, _compose);
            }
            CopyHud(frame, context);
            ReadOnlySpan<RenderEvent> events = context.Events != null
                ? context.Events.GetWindow(tick, VfxSystem.MaxEffectTicks)
                : ReadOnlySpan<RenderEvent>.Empty;
            _composer.Compose(tick, frame, events, _compose);

            RenderTheme theme = _compose.Look.Theme ?? RenderThemeCatalog.Default;
            SceneSize(width, height, quality, theme, out int sceneW, out int sceneH, out bool pixelated);
            BloomMode bloom = _composer.BloomIntensity > 0f ? quality.Bloom : BloomMode.Off;
            _targets.Ensure(sceneW, sceneH, pixelated ? 1 : quality.Msaa, pixelated, bloom);

            _frameSet = (_frameSet + 1) % FrameSets;
            BatchMeshUploader under = _under[_frameSet], trails = _trails[_frameSet], over = _over[_frameSet];
            under.Upload(_composer.Under);
            trails.Upload(_composer.Trails);
            over.Upload(_composer.Over);

            RecordCommands(context, theme, quality, target, under, trails, over, bloom);
            Graphics.ExecuteCommandBuffer(_cmd);
            return true;
        }

        /// <summary>
        /// Offline export render (spec §7.5): the Export profile (full bloom and trails, 4× MSAA, render scale 1 of the output
        /// frame) regardless of the device tier and of <c>context.Quality</c>, which is left unchanged.
        /// </summary>
        public void RenderOffline(RenderFrame frame, RenderContext context, RenderTexture target)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (context == null) throw new ArgumentNullException(nameof(context));
            QualityProfile saved = context.Quality;
            context.Quality = QualityProfile.Export;
            try
            {
                RenderCore(frame, context, target);
            }
            finally
            {
                context.Quality = saved;
            }
        }

        /// <summary>
        /// Offline export render of the frame of <c>context.Tick</c> read from <c>context.FrameSource</c> (the export loop:
        /// forward reads only, see <see cref="Render(RenderContext, RenderTexture)"/>). Returns false when the source cannot
        /// supply the tick.
        /// </summary>
        public bool RenderOffline(RenderContext context, RenderTexture target)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.FrameSource == null) throw new ArgumentException("RenderContext.FrameSource is required", nameof(context));
            QualityProfile saved = context.Quality;
            context.Quality = QualityProfile.Export;
            try
            {
                return RenderCore(null, context, target);
            }
            finally
            {
                context.Quality = saved;
            }
        }

        static void SceneSize(int width, int height, QualityProfile quality, RenderTheme theme, out int w, out int h, out bool pixelated)
        {
            pixelated = theme.PixelGrid > 0 && theme.PixelGrid < height;
            if (pixelated)
            {
                h = theme.PixelGrid;
                w = Math.Max(1, Mathf.RoundToInt(theme.PixelGrid * (float)width / height));
                return;
            }
            float s = quality.RenderScale;
            w = Math.Max(1, Mathf.RoundToInt(width * s));
            h = Math.Max(1, Mathf.RoundToInt(height * s));
        }

        void BindStream(RenderContext ctx)
        {
            if (ReferenceEquals(ctx.Shapes, _boundShapes) && ReferenceEquals(ctx.Quantization, _boundQuantization) && ReferenceEquals(ctx.Grid, _boundGrid))
            {
                return;
            }
            _boundShapes = ctx.Shapes;
            _boundQuantization = ctx.Quantization;
            _boundGrid = ctx.Grid;
            _composer.SetStream(ctx.Quantization, ctx.Shapes, ctx.Grid);
            _instances.ResetSkins();
        }

        void CopyHud(RenderFrame frame, RenderContext ctx)
        {
            ReadOnlySpan<HudEntry> hud = frame.Hud;
            if (_hud.Length < hud.Length) _hud = new HudEntry[Mathf.NextPowerOfTwo(hud.Length)];
            hud.CopyTo(_hud);
            _hudCount = hud.Length;
            HudVisible = ctx.Look == null || ctx.Look.Hud;
        }

        void RecordCommands(RenderContext ctx, RenderTheme theme, QualityProfile quality, RenderTexture target,
            BatchMeshUploader under, BatchMeshUploader trails, BatchMeshUploader over, BloomMode bloom)
        {
            CommandBuffer cmd = _cmd;
            cmd.Clear();
            RenderTexture scene = _targets.Scene;
            ViewRect view = _composer.View;

            // Scene pass.
            // Multisampled scene: resolve at the end of the pass so the post passes sample the resolved image.
            RenderBufferStoreAction sceneStore = scene.antiAliasing > 1 ? RenderBufferStoreAction.StoreAndResolve : RenderBufferStoreAction.Store;
            cmd.SetRenderTarget(scene, RenderBufferLoadAction.DontCare, sceneStore);
            cmd.ClearRenderTarget(false, true, Color.black);
            Matrix4x4 proj = Matrix4x4.Ortho(view.MinX, view.MaxX, view.MinY, view.MaxY, -10f, 10f);
            cmd.SetGlobalMatrix(ShaderIds.ViewProj, GL.GetGPUProjectionMatrix(proj, true));

            DrawBackground(cmd, view, scene.width, scene.height);
            Material batch = _resources.BatchMaterial;
            if (batch != null && under.IndexCount > 0) cmd.DrawMesh(under.Mesh, Matrix4x4.identity, batch, 0, 0);
            if (batch != null && trails.IndexCount > 0) cmd.DrawMesh(trails.Mesh, Matrix4x4.identity, batch, 0, 0);
            _instances.DrawBalls(cmd, _resources, _composer.Balls, ctx, quality.SimplifiedGlass);
            if (batch != null && over.IndexCount > 0) cmd.DrawMesh(over.Mesh, Matrix4x4.identity, batch, 0, 0);
            _instances.DrawParticles(cmd, _resources, _composer.Particles);
            _labels[_frameSet].Draw(cmd, _resources, _composer.Labels, ctx, theme, _composer.PixelsPerMetre);

            // Post: bloom chain + composite into the target.
            Material post = _resources.PostMaterial;
            Texture bloomTex = Texture2D.blackTexture;
            float intensity = 0f;
            if (post != null && bloom != BloomMode.Off && _targets.BloomLevels > 0)
            {
                int levels = _targets.BloomLevels;
                cmd.SetGlobalVector(ShaderIds.BloomParams, new Vector4(_composer.BloomThreshold, Mathf.Max(0.05f, _composer.BloomThreshold * 0.5f), 0f, 0f));
                Blit(cmd, scene, _targets.Bloom(0), post, 0);
                for (int i = 1; i < levels; i++) Blit(cmd, _targets.Bloom(i - 1), _targets.Bloom(i), post, 1);
                for (int i = levels - 2; i >= 0; i--) Blit(cmd, _targets.Bloom(i + 1), _targets.Bloom(i), post, 2, load: true);
                bloomTex = _targets.Bloom(0);
                intensity = _composer.BloomIntensity;
            }
            if (post != null)
            {
                cmd.SetGlobalTexture(ShaderIds.Scene, scene);
                cmd.SetGlobalTexture(ShaderIds.Bloom, bloomTex);
                cmd.SetGlobalVector(ShaderIds.BloomParams, new Vector4(_composer.BloomThreshold, 0.5f, intensity, _composer.Background.Vignette));
                cmd.SetGlobalVector(ShaderIds.Grade, new Vector4(theme.Saturation, theme.Contrast, 0f, 0f));
                cmd.SetGlobalVector(ShaderIds.GradeTint, ColorConvert.ToWorking(theme.Tint));
                cmd.SetRenderTarget(target, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.DrawProcedural(Matrix4x4.identity, post, 3, MeshTopology.Triangles, 3);
            }
            else
            {
                cmd.Blit(scene, target);
            }
        }

        static void Blit(CommandBuffer cmd, RenderTexture source, RenderTexture destination, Material material, int pass, bool load = false)
        {
            cmd.SetGlobalTexture(ShaderIds.Source, source);
            cmd.SetGlobalVector(ShaderIds.SourceTexelSize, new Vector4(1f / source.width, 1f / source.height, source.width, source.height));
            cmd.SetRenderTarget(destination, load ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
        }

        void DrawBackground(CommandBuffer cmd, in ViewRect view, int width, int height)
        {
            Material bg = _resources.BackgroundMaterial;
            if (bg == null) return;
            BackgroundParams p = _composer.Background;
            cmd.SetGlobalVector(ShaderIds.BgTop, ColorConvert.ToWorking(p.Top));
            cmd.SetGlobalVector(ShaderIds.BgBottom, ColorConvert.ToWorking(p.Bottom));
            cmd.SetGlobalVector(ShaderIds.BgGrid, ColorConvert.ToWorking(p.GridColor));
            cmd.SetGlobalVector(ShaderIds.BgStar, ColorConvert.ToWorking(p.StarColor));
            cmd.SetGlobalVector(ShaderIds.BgParams, new Vector4((float)p.Style, p.Time, p.GridSpacing, 0f));
            cmd.SetGlobalVector(ShaderIds.BgView, new Vector4(view.MinX, view.MinY, view.Width, view.Height));
            cmd.SetGlobalVector(ShaderIds.BgPixels, new Vector4(width, height, 1f / width, 1f / height));
            cmd.DrawProcedural(Matrix4x4.identity, bg, 0, MeshTopology.Triangles, 3);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cmd.Release();
            _targets.Dispose();
            for (int i = 0; i < FrameSets; i++)
            {
                _under[i].Dispose();
                _trails[i].Dispose();
                _over[i].Dispose();
                _labels[i].Dispose();
            }
            if (_ownsResources) _resources.Dispose();
        }
    }
}
