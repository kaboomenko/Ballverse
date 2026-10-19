using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Render;
using Ballverse.Sim;
using Ballverse.Studio;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Studio's <see cref="ISimFrameRenderer"/> over Ballverse.Render (the Studio assembly does not reference Render): every
    /// frame is drawn by <see cref="BallverseRenderer"/> from the clip's own render-stream reader, so trails and camera history
    /// are rebuilt from the stream for the shown tick (the determinism contract of the seam: an export segment redrawn after a
    /// pause, or a resumed export, gives the same picture). The look comes from the clip's replay parameters with the live
    /// look changes (PARM) applied at their tick (<see cref="ClipLookTimeline"/>), VFX from
    /// <c>clip.bve</c>. Preview uses the device tier's real-time profile, export the Export profile. Main thread.
    /// </summary>
    public sealed class StudioSimRenderer : ISimFrameRenderer, IDisposable
    {
        readonly BallverseRenderer _renderer;
        readonly RenderContext _ctx = new RenderContext();
        readonly ModeRegistry _modes;
        readonly RenderThemeCatalog _themes = new RenderThemeCatalog();
        readonly Func<int, string> _labelLookup;
        IReadOnlyList<string> _labels;
        ClipLookTimeline _look;
        FileStream _stream;
        RenderStreamReader _reader;
        bool _disposed;

        public StudioSimRenderer(ModeRegistry modes, RenderResources resources)
        {
            _modes = modes;
            _renderer = new BallverseRenderer(resources);
            _labelLookup = LookupLabel;
        }

        /// <summary>Tier of the preview profile.</summary>
        public DeviceTier Tier { get; set; } = DeviceTier.B;

        /// <summary>The renderer always draws the whole arena (Ballverse.Render has no arena-window camera).</summary>
        public bool SupportsViewport => false;

        public void BeginClip(SimClipInfo clip)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            EndClip();
            _ctx.Quantization = clip.Header.Quantization;
            _ctx.Grid = clip.Header.Grid;
            _ctx.Shapes = clip.Shapes;
            _labels = clip.Labels;
            _ctx.Labels = _labelLookup;
            _ctx.ModeId = clip.ModeId ?? "";
            _ctx.Events = null;
            _ctx.FrameSource = null;
            try
            {
                _stream = new FileStream(clip.RenderStreamPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
                _reader = new RenderStreamReader(_stream, true);
                _ctx.FrameSource = new RenderStreamFrameSource(_reader);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("[Ballverse.App] Studio renderer: own stream reader unavailable, drawing without history: " + e.Message);
                CloseReader();
            }
            if (!string.IsNullOrEmpty(clip.EventsPath))
            {
                try
                {
                    using (var fs = new FileStream(clip.EventsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        _ctx.Events = RenderEventLog.ReadFrom(fs);
                    }
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
                {
                    Debug.LogWarning("[Ballverse.App] Studio renderer: clip events unreadable: " + e.Message);
                }
            }
            string dir = string.IsNullOrEmpty(clip.ReplayPath) ? null : Path.GetDirectoryName(clip.ReplayPath);
            _look = null;
            if (dir != null && ClipLookTimeline.TryRead(dir, _modes, out ClipLookTimeline look))
            {
                _look = look;
                if (look.Advance(0, out ParamSet values)) _ctx.Look.Apply(values, _themes);
            }
        }

        public void SetFrameTime(SimFrameTime time)
        {
            _ctx.Tick = Math.Max(0, time.Tick);
            // Live look changes recorded during the run (PARM) appear at their tick in the preview and the export.
            if (_look != null && _look.Advance(_ctx.Tick, out ParamSet values)) _ctx.Look.Apply(values, _themes);
        }

        public void Render(RenderFrame frame, IReadOnlyList<ShapeDefinition> shapes, IReadOnlyList<string> labels, RenderTexture target, StudioRenderQuality quality)
        {
            if (_disposed || target == null) return;
            if (shapes != null) _ctx.Shapes = shapes;
            if (labels != null) _labels = labels;
            if (quality == StudioRenderQuality.Export)
            {
                if (_ctx.FrameSource != null && _renderer.RenderOffline(_ctx, target)) return;
                if (frame != null) _renderer.RenderOffline(frame, _ctx, target);
                return;
            }
            _ctx.Quality = QualityProfile.ForTier(Tier);
            if (_ctx.FrameSource != null && _renderer.Render(_ctx, target)) return;
            if (frame != null) _renderer.Render(frame, _ctx, target);
        }

        public void EndClip()
        {
            CloseReader();
            _ctx.FrameSource = null;
            _ctx.Events = null;
        }

        string LookupLabel(int id)
        {
            IReadOnlyList<string> labels = _labels;
            return labels != null && id > 0 && id < labels.Count ? labels[id] : null;
        }

        void CloseReader()
        {
            _reader?.Dispose();
            _stream?.Dispose();
            _reader = null;
            _stream = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            EndClip();
            _renderer.Dispose();
        }
    }

    /// <summary>
    /// Studio's <see cref="IStudioSoundProvider"/>: the clip's sound pack with the synthesized sample bank and built-in melody
    /// (the same ones as the live run, so the export sounds like the app, spec §7.8), and the built-in music tracks.
    /// </summary>
    public sealed class StudioSoundProvider : IStudioSoundProvider
    {
        public Task<StudioSoundAssets> LoadSoundsAsync(string soundPackId, string melodyId, CancellationToken ct)
        {
            SoundPackId id = string.Equals(soundPackId, "None", StringComparison.OrdinalIgnoreCase) ? SoundPackId.None : ProceduralSampleBank.ParsePack(soundPackId);
            if (id == SoundPackId.None) return Task.FromResult<StudioSoundAssets>(null);
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Melody melody = null;
                if (id == SoundPackId.Melody) BuiltInMelodies.TryGet(melodyId, out melody);
                return new StudioSoundAssets(SoundPackCatalog.Get(id), LiveSoundSetup.BankFor(id), melody);
            }, ct);
        }

        public Task<float[]> LoadMusicAsync(string trackId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(trackId)) return Task.FromResult<float[]>(null);
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                return ProceduralMusic.Render(trackId);
            }, ct);
        }
    }

    /// <summary><see cref="IStudioProject"/> over Studio's <see cref="StudioProjectSession"/>.</summary>
    public sealed class StudioProjectAdapter : IStudioProject
    {
        public StudioProjectAdapter(StudioProjectSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Session.Changed += OnChanged;
        }

        public StudioProjectSession Session { get; }
        public string ProjectId => Session.ProjectId;
        public ProjectDocument Document => Session.Document;
        public string Directory => Session.Directory;
        public bool CanUndo => Session.CanUndo;
        public bool CanRedo => Session.CanRedo;
        public Exception LastSaveError => Session.LastSaveError;

        public event Action<ProjectDocument> Changed;

        void OnChanged(ProjectDocument doc) => Changed?.Invoke(doc);

        public IReadOnlyList<Core.Projects.ValidationIssue> Apply(Action<ProjectDocument> edit, string coalesceKey = null) => Session.Apply(edit, coalesceKey);
        public void ApplyMetadata(Action<ProjectDocument> change) => Session.ApplyMetadata(change);
        public bool Undo() => Session.Undo();
        public bool Redo() => Session.Redo();
        public void SaveNow() => Session.SaveNow();

        public void Dispose()
        {
            Session.Changed -= OnChanged;
            Session.Dispose();
        }
    }

    /// <summary><see cref="IStudioPreview"/> over Studio's <see cref="StudioPreviewPlayer"/>.</summary>
    public sealed class StudioPreviewAdapter : IStudioPreview
    {
        readonly StudioPreviewPlayer _player;

        public StudioPreviewAdapter(StudioPreviewPlayer player)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _player.TargetChanged += OnTarget;
        }

        public Texture Target => _player.Target;
        public event Action<Texture> TargetChanged;
        public StudioTimeline Timeline => _player.Timeline;
        public long DurationUs => _player.DurationUs;
        public long PositionUs => _player.PositionUs;
        public bool IsPlaying => _player.IsPlaying;

        public bool FullScreen
        {
            get => _player.FullScreen;
            set => _player.FullScreen = value;
        }

        public bool ShowPlatformMasks
        {
            get => _player.ShowPlatformMasks;
            set => _player.ShowPlatformMasks = value;
        }

        public Exception AudioError => _player.AudioError;
        public Exception VideoError => _player.VideoError;

        void OnTarget(RenderTexture rt) => TargetChanged?.Invoke(rt);

        public void SetOverlays(bool showWatermark, IReadOnlyList<Core.Layout.FloatRect> platformMasks) => _player.SetOverlays(showWatermark, platformMasks);
        public void SetDisplaySize(int width, int height) => _player.SetDisplaySize(width, height);
        public void SetProject(ProjectDocument doc) => _player.SetProject(doc);
        public void Play() => _player.Play();
        public void Pause() => _player.Pause();
        public void Seek(long outputUs) => _player.Seek(outputUs);

        public void Dispose()
        {
            _player.TargetChanged -= OnTarget;
            _player.Dispose();
        }
    }

    /// <summary><see cref="IStudioThumbnails"/> over Studio's <see cref="TimelineThumbnails"/> (live texture views of both tracks).</summary>
    public sealed class StudioThumbnailsAdapter : IStudioThumbnails
    {
        readonly TimelineThumbnails _thumbs;

        public StudioThumbnailsAdapter(TimelineThumbnails thumbs)
        {
            _thumbs = thumbs ?? throw new ArgumentNullException(nameof(thumbs));
            Simulation = new TextureView(thumbs.Simulation);
            Video = new TextureView(thumbs.Video);
            _thumbs.Changed += OnChanged;
        }

        public IReadOnlyList<Texture> Simulation { get; }
        public IReadOnlyList<Texture> Video { get; }
        public event Action Changed;

        void OnChanged() => Changed?.Invoke();

        public void SetProject(ProjectDocument doc) => _thumbs.SetProject(doc);

        public void Dispose()
        {
            _thumbs.Changed -= OnChanged;
            _thumbs.Dispose();
        }

        /// <summary>Allocation-free projection of thumbnails onto their textures (null while pending).</summary>
        sealed class TextureView : IReadOnlyList<Texture>
        {
            readonly IReadOnlyList<TimelineThumbnail> _source;

            public TextureView(IReadOnlyList<TimelineThumbnail> source) => _source = source;

            public int Count => _source.Count;
            public Texture this[int index] => _source[index].Texture;

            public IEnumerator<Texture> GetEnumerator()
            {
                for (int i = 0; i < _source.Count; i++) yield return _source[i].Texture;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }

    /// <summary>
    /// <see cref="IStudioBackend"/> over Ballverse.Studio's <see cref="StudioServices"/> (device builds). Creates the services
    /// with the App's renderer adapter, sound provider and rewarded gate, logs the startup cleanup, and maps the App's port
    /// types onto Studio's.
    /// </summary>
    public sealed class StudioBackend : IStudioBackend
    {
        readonly StudioServices _services;
        readonly Action<DeviceTier> _tierChanged;

        /// <param name="tierChanged">Told about tier changes (the renderer factory reads the new tier for the next renderer).</param>
        public StudioBackend(StudioServices services, Action<DeviceTier> tierChanged)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _tierChanged = tierChanged;
            _services.Export.Progress += OnProgress;
            if (!string.IsNullOrEmpty(_services.CleanupReport)) Debug.Log("[Ballverse.Studio] startup cleanup: " + _services.CleanupReport);
        }

        public StudioServices Services => _services;
        public ClipLibrary Clips => _services.Clips;

        public DeviceTier Tier
        {
            get => _services.Tier;
            set
            {
                _services.Tier = value;
                _tierChanged?.Invoke(value);
            }
        }

        public event Action<ExportProgressInfo> ExportProgress;

        public bool IsExporting => _services.Export.IsRunning;

        public Texture ExportPreviewTexture => (_services.Export.CurrentRenderer as GpuExportFrameRenderer)?.PreviewTexture;

        void OnProgress(ExportProgressInfo info) => ExportProgress?.Invoke(info);

        public ProjectListResult ListProjects() => _services.Projects.List();
        public ClipReadiness RunReadiness(string runId) => _services.Projects.RunReadiness(runId);
        public ProjectDocument CreateFromRun(string runId) => _services.Projects.CreateFromRun(runId);
        public ProjectDocument CreateFromClip(string clipId) => _services.Projects.CreateFromClip(clipId);

        public IStudioProject OpenProject(string projectId, out LoadResult load)
        {
            StudioProjectSession session = _services.Projects.Open(projectId, out load);
            return session != null ? new StudioProjectAdapter(session) : null;
        }

        public ProjectDocument Duplicate(string projectId) => _services.Projects.Duplicate(projectId);
        public ProjectDocument Rename(string projectId, string name) => _services.Projects.Rename(projectId, name);
        public bool DeleteProject(string projectId) => _services.Projects.Delete(projectId);
        public string ProjectDirectory(string projectId) => _services.Projects.ProjectDirectory(projectId);

        public async Task<VideoImportResult> ImportUserVideoAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct)
        {
            StudioProjectSession session = Session(project);
            UserVideoImport r = await _services.Projects.ImportUserVideoAsync(session, progress, ct);
            return new VideoImportResult
            {
                Picked = r.Status == Media.PickStatus.Picked,
                Info = r.Info,
                LargeFileWarning = r.LargeFileWarning,
                IsHdr = r.IsHdr,
                Trimmed = r.Trimmed,
            };
        }

        public void RemoveUserVideo(IStudioProject project) => _services.Projects.RemoveUserVideo(Session(project));

        public async Task<IStudioPreview> OpenPreviewAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct)
        {
            StudioPreviewPlayer player = await _services.OpenPreviewAsync(Session(project), null, progress, ct);
            return new StudioPreviewAdapter(player);
        }

        public IStudioThumbnails OpenThumbnails(IStudioProject project)
        {
            try
            {
                return new StudioThumbnailsAdapter(_services.OpenThumbnails(Session(project)));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        public ExportSettings ResolveExportSettings(ProjectDocument doc, ExportRequest request)
        {
            return _services.ResolveExportSettings(doc, ToStudioOptions(request));
        }

        public Task<StudioExportResult> ExportAsync(string projectId, ExportRequest request, CancellationToken ct)
        {
            return _services.ExportAsync(projectId, ToStudioOptions(request), ct);
        }

        public IReadOnlyList<ExportResumeState> ListResumable() => _services.Export.ListResumable();
        public Task<StudioExportResult> ResumeExportAsync(string jobId, CancellationToken ct) => _services.ResumeExportAsync(jobId, ct);
        public void DiscardResumable(string jobId) => _services.Export.DiscardResumable(jobId);
        public string RenderCover(string projectId) => _services.RenderCover(projectId);

        public StudioUsage MeasureUsage()
        {
            StudioStorageUsage u = _services.Storage.MeasureUsage();
            return new StudioUsage
            {
                ProjectsBytes = u.ProjectsBytes,
                ClipsBytes = u.ClipLibraryBytes,
                RecentRunsBytes = u.RecentRunsBytes,
                ExportScratchBytes = u.ExportScratchBytes,
            };
        }

        public void ClearCaches() => _services.AudioMixer.ClearCache();

        /// <summary>The App's per-export choices as Studio's <see cref="StudioExportOptions"/> (every field carried over).</summary>
        public static StudioExportOptions ToStudioOptions(ExportRequest r)
        {
            r = r ?? new ExportRequest();
            return new StudioExportOptions
            {
                Resolution = r.Resolution,
                Fps = r.Fps,
                ShowWatermark = r.ShowWatermark,
                PlatformMasks = r.PlatformMasks,
                Bitrates = r.Bitrates,
                SaveToGallery = r.SaveToGallery,
            };
        }

        static StudioProjectSession Session(IStudioProject project)
        {
            if (project is StudioProjectAdapter a) return a.Session;
            throw new ArgumentException("Not a project of this studio backend", nameof(project));
        }

        public void Dispose()
        {
            _services.Export.Progress -= OnProgress;
            _services.Dispose();
        }
    }
}
