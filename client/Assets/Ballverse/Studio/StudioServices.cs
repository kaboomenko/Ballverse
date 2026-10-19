using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Platform;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>What the App hands the studio at boot (see <see cref="StudioServices.Create"/>).</summary>
    public sealed class StudioServicesOptions
    {
        /// <summary>App storage roots (persistent root, render-stream cache, export scratch). Required.</summary>
        public IAppPaths Paths { get; set; }
        /// <summary>Media bridge (<c>MediaBridgeFactory.Create()</c>). Required; not disposed by the studio.</summary>
        public IMediaBridge Media { get; set; }
        /// <summary>Simulation renderer adapter over Ballverse.Render. Required.</summary>
        public ISimFrameRenderer SimRenderer { get; set; }
        /// <summary>App lifecycle (autosave on pause, export pause/resume). Optional.</summary>
        public IAppLifecycle Lifecycle { get; set; }
        /// <summary>Keeps the screen on during export. Optional.</summary>
        public IScreenAwake ScreenAwake { get; set; }
        /// <summary>Sound packs and music. Optional (silent simulation, no music).</summary>
        public IStudioSoundProvider Sounds { get; set; }
        /// <summary>Text layer fonts. Optional (built-in font).</summary>
        public IStudioFontProvider Fonts { get; set; }
        /// <summary>Rewarded export gate. Optional (no gate).</summary>
        public IExportGate Gate { get; set; }
        /// <summary>Device tier (default fps, export time estimate, preview resolution hint).</summary>
        public DeviceTier Tier { get; set; } = DeviceTier.B;
        /// <summary>Theme background colour (Fit margins "theme", placeholders).</summary>
        public StudioColor ThemeBackground { get; set; } = new StudioColor(0.06f, 0.06f, 0.09f, 1f);
        /// <summary>Optional watermark artwork; null = procedural logo.</summary>
        public Texture WatermarkTexture { get; set; }
        /// <summary>Run the startup cleanup (spec §3.9). Default true.</summary>
        public bool CleanupAtStartup { get; set; } = true;
    }

    /// <summary>Per-export choices of the export sheet (spec §3.8 step 1).</summary>
    public sealed class StudioExportOptions
    {
        /// <summary>Resolution; null = the project's last choice, else 1080p.</summary>
        public ExportResolution? Resolution { get; set; }
        /// <summary>Fps; null = the project's last choice, else the tier default.</summary>
        public int? Fps { get; set; }
        /// <summary><see cref="WatermarkPolicy.Resolve"/> at export time (entitlement and Remote Config kill switch are the App's).</summary>
        public bool ShowWatermark { get; set; } = true;
        /// <summary>Platform masks from Remote Config (<c>RemoteConfigAdapters.ToPlatformUiMasks(values).Union()</c>); null = defaults.</summary>
        public IReadOnlyList<FloatRect> PlatformMasks { get; set; }
        /// <summary>Remote Config bitrates (<c>RemoteConfigAdapters.ToExportBitrates</c>); null = spec defaults.</summary>
        public ExportBitrateTable Bitrates { get; set; }
        /// <summary>Save to the gallery (setting, default on).</summary>
        public bool SaveToGallery { get; set; } = true;
        /// <summary>Keep a copy in the project's exports/ folder.</summary>
        public bool KeepInProject { get; set; }
    }

    /// <summary>
    /// Composition root of the studio runtime (spec §3, §7.5): storage, recent runs and clip library, projects with autosave,
    /// the compositor, the preview player and the export pipeline, wired to the App's media bridge, renderer adapter, lifecycle and
    /// providers. Create once at boot on the main thread; dispose on shutdown. See the Studio README for the threading model.
    /// </summary>
    public sealed class StudioServices : IDisposable
    {
        StudioServices()
        {
        }

        public StudioRunner Runner { get; private set; }
        public StudioStorage Storage { get; private set; }
        public ClipLibrary Clips { get; private set; }
        public StudioProjects Projects { get; private set; }
        public StudioCompositor Compositor { get; private set; }
        public StudioAudioMixer AudioMixer { get; private set; }
        public ExportPipeline Export { get; private set; }
        public IMediaBridge Media { get; private set; }
        public ISimFrameRenderer SimRenderer { get; private set; }
        public IAppLifecycle Lifecycle { get; private set; }
        public IIdSource Ids { get; private set; }
        public DeviceTier Tier { get; set; }
        public StudioColor ThemeBackground { get; set; }
        public Texture WatermarkTexture { get; set; }
        /// <summary>Report of the startup cleanup (for the log).</summary>
        public string CleanupReport { get; private set; }

        StudioExportHost _host;
        bool _disposed;

        public static StudioServices Create(StudioServicesOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (o.Paths == null) throw new ArgumentException("Paths required", nameof(o));
            if (o.Media == null) throw new ArgumentException("Media required", nameof(o));
            if (o.SimRenderer == null) throw new ArgumentException("SimRenderer required", nameof(o));
            var s = new StudioServices
            {
                Media = o.Media,
                SimRenderer = o.SimRenderer,
                Lifecycle = o.Lifecycle,
                Tier = o.Tier,
                ThemeBackground = o.ThemeBackground,
                WatermarkTexture = o.WatermarkTexture,
                Ids = GuidIdSource.Instance,
            };
            s.Runner = StudioRunner.Create();
            s.Storage = StudioStorage.FromAppPaths(o.Paths);
            s.Clips = new ClipLibrary(s.Storage, StudioClock.Instance, s.Ids);
            if (o.CleanupAtStartup) s.CleanupReport = s.Storage.CleanupAtStartup(s.Clips.Store, keepResumableExports: true);
            s.Projects = new StudioProjects(s, new ProjectLibrary(s.Clips.Store, StudioClock.Instance, s.Ids));
            s.Compositor = new StudioCompositor(o.Fonts);
            s.AudioMixer = new StudioAudioMixer(o.Media, o.Sounds, s.Storage.Exports.FullPath("audio-cache"));
            s._host = new StudioExportHost(s.Runner, o.Media, o.Lifecycle, o.ScreenAwake, o.Tier, StudioClock.Instance);
            s.Export = new ExportPipeline(o.Media, s.Storage.Exports, s.Storage.Library, s._host,
                job => new GpuExportFrameRenderer(s, s.Projects.ProjectDirectory(job.State.ProjectId), s.Projects.ProjectDirectory(job.State.ProjectId)),
                new StudioExportAudio(s.AudioMixer, s.Projects.ProjectDirectory), o.Gate);
            return s;
        }

        /// <summary>Opens the preview of an open project (the caller disposes it when leaving the studio).</summary>
        /// <exception cref="FileNotFoundException">The clip has no render stream (rebuild it from the replay first).</exception>
        public StudioPreviewPlayer OpenPreview(StudioProjectSession session, int? exportFps = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ProjectDocument doc = session.Document;
            int fps = exportFps ?? ProjectComposition.ResolveExportSettings(doc.Output, Tier).Fps;
            ClipSource clip = ClipSource.Open(session.Directory);
            try
            {
                return new StudioPreviewPlayer(this, clip, session.Directory, doc, fps, Tier);
            }
            catch
            {
                clip.Dispose();
                throw;
            }
        }

        /// <summary>Settings the export sheet shows (size and time estimates from Core, spec §3.8 step 1).</summary>
        public ExportSettings ResolveExportSettings(ProjectDocument doc, StudioExportOptions options)
        {
            options = options ?? new StudioExportOptions();
            return ProjectLibrary.ReExportSettings(doc, Tier, options.Bitrates, options.Resolution, options.Fps);
        }

        /// <summary>
        /// Exports a project (also "Экспортировать снова" with other settings, spec §3.9): builds the export snapshot (settings, watermark,
        /// masks, text boxes measured at the 1080 reference, decoded video size) and runs <see cref="ExportPipeline.StartAsync"/>.
        /// On success the export is added to the project's history. Main thread.
        /// </summary>
        public async Task<StudioExportResult> ExportAsync(string projectId, StudioExportOptions options, CancellationToken ct)
        {
            options = options ?? new StudioExportOptions();
            StudioProjectSession open = Projects.GetOpen(projectId);
            open?.SaveNow();
            ProjectDocument doc;
            if (open != null)
            {
                doc = open.Document;
            }
            else
            {
                LoadResult r = Projects.Library.Open(projectId);
                if (!r.Success) return StudioExportResult.Failure(ExportErrorCode.ProjectInvalid, "project cannot be loaded: " + r.Status, projectId, null);
                doc = r.Document;
            }
            string dir = Projects.ProjectDirectory(projectId);
            ExportSettings settings = ResolveExportSettings(doc, options);
            int ticks;
            try
            {
                using (ClipSource clip = ClipSource.Open(dir)) ticks = clip.TickCount;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                return StudioExportResult.Failure(ExportErrorCode.ClipDataMissing, e.Message, projectId, null);
            }
            IntVec2 videoSize = default(IntVec2), videoBox = default(IntVec2);
            if (doc.UserVideo != null)
            {
                string path = Path.Combine(dir, doc.UserVideo.File.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    MediaInfo info = await Media.ProbeAsync(path, ct);
                    FrameLayout layout = LayoutCalculator.ComputeSlots(settings.Width, settings.Height, ProjectComposition.BuildLayoutSpec(doc.Layout, true));
                    IntRect slot = layout.HasVideo ? layout.Video.Rect : new IntRect(0, 0, settings.Width, settings.Height);
                    videoBox = VideoDecodeSizing.Compute(info.DisplayWidth, info.DisplayHeight, Math.Max(1, slot.Width), Math.Max(1, slot.Height),
                        ProjectComposition.BuildVideoContent(doc.UserVideo));
                    VideoReaderOptions.ComputeOutputSize(info.DisplayWidth, info.DisplayHeight, videoBox.X, videoBox.Y, out int w, out int h);
                    videoSize = new IntVec2(w, h);
                }
                catch (MediaException e)
                {
                    return StudioExportResult.Failure(ExportErrorCode.UserVideoFailed, e.Message, projectId, null);
                }
                catch (OperationCanceledException)
                {
                    return StudioExportResult.Failure(ExportErrorCode.Cancelled, "cancelled", projectId, null);
                }
            }
            var request = new ExportJobRequest
            {
                JobId = Ids.NewId(),
                Project = doc,
                Settings = settings,
                Watermark = options.ShowWatermark,
                MaskRects = options.PlatformMasks,
                TextBounds = Compositor.Text.MeasureBounds(doc.Texts),
                VideoSize = videoSize,
                VideoBox = videoBox,
                StreamTicks = ticks,
                LogoAspect = StudioCompositor.LogoAspect(new CompositeStyle { WatermarkTexture = WatermarkTexture }),
                SaveToGallery = options.SaveToGallery,
                KeepInProject = options.KeepInProject,
                NowIso = StudioClock.Instance.NowIso(),
            };
            StudioExportResult result = await Export.StartAsync(request, ct);
            if (result.Succeeded) Projects.RecordExport(projectId, result);
            return result;
        }

        /// <summary>Continues an unfinished export found at startup (spec §3.8 "Продолжить экспорт"); records it in the history on success.</summary>
        public async Task<StudioExportResult> ResumeExportAsync(string jobId, CancellationToken ct)
        {
            StudioExportResult result = await Export.ResumeAsync(jobId, ct);
            if (result.Succeeded && result.ProjectId != null && Projects.Library.Repository.Exists(result.ProjectId)) Projects.RecordExport(result.ProjectId, result);
            return result;
        }

        /// <summary>
        /// Renders the cover of a project (first frame, or the chosen cover frame) into <c>thumbs/cover.png</c> for the project grid
        /// (spec §3.9). Returns the absolute path, or null when the clip cannot be drawn.
        /// </summary>
        public string RenderCover(string projectId, int width = 270)
        {
            LoadResult r = Projects.Library.Open(projectId);
            if (!r.Success) return null;
            ProjectDocument doc = Projects.GetOpen(projectId)?.Document ?? r.Document;
            string dir = Projects.ProjectDirectory(projectId);
            ClipSource clip;
            try
            {
                clip = ClipSource.Open(dir);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException)
            {
                return null;
            }
            int w = Math.Max(OutputCanvas.MinSide, width & ~1);
            int h = Math.Max(OutputCanvas.MinSide, (int)Math.Round(w * 16.0 / 9.0) & ~1);
            RenderTexture canvas = StudioCompositor.CreateCanvas(w, h, "StudioCover");
            RenderTexture readback = StudioCompositor.CreateReadbackTarget(w, h, "StudioCoverReadback");
            var cpu = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            var frames = new StudioFrameRenderer(Compositor, SimRenderer, StudioRenderQuality.Preview, 1024);
            try
            {
                frames.OpenClip(clip);
                frames.SetProject(doc, CompositeStyle.FromProject(doc, ThemeBackground, WatermarkTexture));
                StudioTimeline timeline = ProjectComposition.BuildTimeline(doc, OutputFps.Fps30, Math.Max(1, clip.TickCount));
                FrameRequest request = ProjectComposition.BuildFrameRequest(doc, w, h, default(IntVec2), false, null, null);
                StudioFramePlans plans = StudioFramePlans.Compose(request);
                VideoSample vs = timeline.SampleVideo(0);
                SlotPicture video = vs.Kind == VideoSampleKind.Black ? SlotPicture.Black : SlotPicture.Empty;
                frames.Render(canvas, plans.For(vs), timeline.SampleSim(0), 0, video, 0, null);
                // Readback conversion flips rows top-down for the encoder; a PNG wants bottom-up rows, so read the canvas
                // through the same conversion and write the rows in reverse.
                Compositor.ConvertForReadback(canvas, readback);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = readback;
                cpu.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = previous;
                FlipRows(cpu);
                byte[] png = cpu.EncodeToPNG();
                string thumbs = Path.Combine(dir, ProjectPaths.ThumbsDir);
                Directory.CreateDirectory(thumbs);
                string path = Path.Combine(thumbs, "cover.png");
                string temp = path + StudioFiles.TempSuffix;
                File.WriteAllBytes(temp, png);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return path;
            }
            finally
            {
                frames.Dispose();
                clip.Dispose();
                StudioUnity.Release(canvas);
                StudioUnity.Release(readback);
                StudioUnity.Destroy(cpu);
            }
        }

        static void FlipRows(Texture2D tex)
        {
            Unity.Collections.NativeArray<byte> data = tex.GetRawTextureData<byte>();
            int row = tex.width * 4;
            var tmp = new byte[row];
            var tmp2 = new byte[row];
            for (int y = 0; y < tex.height / 2; y++)
            {
                int a = y * row, b = (tex.height - 1 - y) * row;
                Unity.Collections.NativeArray<byte>.Copy(data, a, tmp, 0, row);
                Unity.Collections.NativeArray<byte>.Copy(data, b, tmp2, 0, row);
                Unity.Collections.NativeArray<byte>.Copy(tmp2, 0, data, a, row);
                Unity.Collections.NativeArray<byte>.Copy(tmp, 0, data, b, row);
            }
            tex.Apply(false, false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Projects?.Dispose();
            _host?.Dispose();
            AudioMixer?.ClearCache();
            Compositor?.Dispose();
            if (Runner != null) StudioUnity.Destroy(Runner.gameObject);
        }
    }
}
