using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App.Tests
{
    /// <summary>Records what the composition asks of the UI; mimics the shell's navigation calls into the view models.</summary>
    public sealed class FakeUi : IAppUi
    {
        public readonly List<string> OpenedModes = new List<string>();
        public readonly List<LocalizedText> Toasts = new List<LocalizedText>();
        public int AdNotices;
        public int TrackingPrompts;
        public int TrackingDismissals;
        public int PhotosensitivityWarnings;
        public DeviceTier? Tier;
        public IBallverseViewModels ViewModels;

        public event Action ExitRequested;

        public void OpenMode(string modeId)
        {
            OpenedModes.Add(modeId);
            ViewModels?.Gallery.OpenMode(modeId);
            ViewModels?.Simulation.SetScreenActive(true);
        }

        public void ShowToast(LocalizedText text) => Toasts.Add(text);

        public void ShowAdNotice(Action shown)
        {
            AdNotices++;
            shown?.Invoke();
        }

        public void ShowTrackingPrePrompt() => TrackingPrompts++;
        public void DismissTrackingPrePrompt() => TrackingDismissals++;

        public void ShowPhotosensitivityWarning(Action acknowledged)
        {
            PhotosensitivityWarnings++;
            acknowledged?.Invoke();
        }

        public void SetDeviceTier(DeviceTier tier) => Tier = tier;

        public void RaiseExit() => ExitRequested?.Invoke();

        public void Dispose()
        {
        }
    }

    /// <summary>No GPU: counts presents and thumbnails.</summary>
    public sealed class FakeSurface : ISimSurface
    {
        public int Presents;
        public int Thumbnails;
        public LiveSimController Bound;
        public ParamSet Look;
        public DeviceTier Tier;
        public bool ReduceMotion;

        public Texture RenderTarget => null;
        public event Action TargetChanged;
        public void Bind(LiveSimController controller) => Bound = controller;
        public void ApplyLook(ParamSet values) => Look = values;
        public void SetTier(DeviceTier tier, QualityPreference preference) => Tier = tier;
        public void SetReduceMotion(bool reduce) => ReduceMotion = reduce;
        public void Present(float deltaSeconds) => Presents++;

        public Texture CaptureThumbnail()
        {
            Thumbnails++;
            return null;
        }

        public void ReleaseThumbnail(Texture texture)
        {
        }

        public void RaiseTargetChanged() => TargetChanged?.Invoke();

        public void Dispose()
        {
        }
    }

    public sealed class FakeDevice : IDeviceInfo
    {
        public string Platform { get; set; } = "android";
        public string Model { get; set; } = "test-phone";
        public string GpuName { get; set; } = "Adreno (TM) 740";
        public int SystemMemoryMb { get; set; } = 8192;
        public string LanguageTag { get; set; } = "en";
        public float FontScale { get; set; } = 1f;
        public string AppVersion { get; set; } = "1.0.0 (1)";
    }

    public sealed class FakePlatform : IPlatformActions
    {
        public int MovedToBack;
        public readonly List<string> Urls = new List<string>();
        public string Clipboard;
        public int Feedbacks;

        public void MoveTaskToBack() => MovedToBack++;
        public void OpenUrl(string url) => Urls.Add(url);
        public void OpenStorePage() => Urls.Add("store");
        public void SendFeedback(string address, string subject, string body) => Feedbacks++;
        public void CopyToClipboard(string text) => Clipboard = text;
    }

    public sealed class NullTextures : ITextureLoader
    {
        public int Loads;

        public Texture Load(string path)
        {
            Loads++;
            return null;
        }

        public void Release(Texture texture)
        {
        }
    }

    public sealed class ManualAppClock : IAppClock
    {
        public double NowSeconds { get; set; }
    }

    /// <summary>
    /// A main-thread synchronization context for plain .NET runs (Unity provides its own): the Media fake bridge requires one,
    /// and posted callbacks run when the test pumps.
    /// </summary>
    public sealed class TestPump : SynchronizationContext
    {
        readonly ConcurrentQueue<KeyValuePair<SendOrPostCallback, object>> _queue = new ConcurrentQueue<KeyValuePair<SendOrPostCallback, object>>();

        public static TestPump InstallIfMissing()
        {
            if (Current != null) return null;
            var pump = new TestPump();
            SetSynchronizationContext(pump);
            return pump;
        }

        public override void Post(SendOrPostCallback d, object state) => _queue.Enqueue(new KeyValuePair<SendOrPostCallback, object>(d, state));

        public override void Send(SendOrPostCallback d, object state) => d(state);

        public void Drain()
        {
            int n = _queue.Count;
            while (n-- > 0 && _queue.TryDequeue(out KeyValuePair<SendOrPostCallback, object> item)) item.Key(item.Value);
        }
    }

    /// <summary>
    /// GPU-free studio backend for tests: the REAL clip library (recent-runs ring, saved clips) and Core project library on a
    /// temporary folder, a timeline-only preview, and an export that goes through the rewarded gate exactly like Studio's
    /// pipeline (gate before rendering, finish reported for every outcome) and writes its file with a fake writer.
    /// </summary>
    public sealed class FakeStudioBackend : IStudioBackend
    {
        readonly StudioStorage _storage;
        readonly ProjectLibrary _library;
        readonly IExportGate _gate;
        readonly string _outputs;

        public FakeStudioBackend(IAppPaths paths, IExportGate gate)
        {
            _storage = StudioStorage.FromAppPaths(paths);
            Clips = new ClipLibrary(_storage);
            _library = new ProjectLibrary(Clips.Store, StudioClock.Instance, GuidIdSource.Instance);
            _gate = gate;
            _outputs = Path.Combine(paths.ExportScratch, StudioStorage.OutputsDir);
        }

        public ClipLibrary Clips { get; }
        public DeviceTier Tier { get; set; } = DeviceTier.B;
        public int Exports;
        public int GateRequests;
        public bool LastGateAnswer;
        public ExportRequest LastRequest;
        public readonly List<ExportResumeState> Resumable = new List<ExportResumeState>();
        public int Resumed;
        public int Discarded;
        public bool FailNextExportWith;
        public ExportErrorCode FailCode = ExportErrorCode.EncoderFailed;

        public event Action<ExportProgressInfo> ExportProgress;

        public bool IsExporting { get; private set; }
        public Texture ExportPreviewTexture => null;

        public ProjectListResult ListProjects() => _library.List();
        public ProjectDocument CreateFromRun(string runId) => _library.CreateFromRun(runId);
        public ProjectDocument CreateFromClip(string clipId) => _library.CreateFromClip(clipId);

        public IStudioProject OpenProject(string projectId, out LoadResult load)
        {
            load = _library.Open(projectId);
            if (!load.Success) return null;
            return new FakeProject(_library.Edit(load.Document), ProjectDirectory(projectId));
        }

        public ProjectDocument Duplicate(string projectId) => _library.Duplicate(projectId);
        public ProjectDocument Rename(string projectId, string name) => _library.Rename(projectId, name);
        public bool DeleteProject(string projectId) => _library.Delete(projectId);
        public string ProjectDirectory(string projectId) => _storage.Library.FullPath(_library.Repository.ProjectDir(projectId));

        public Task<VideoImportResult> ImportUserVideoAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct)
        {
            project.Apply(d => d.UserVideo = UserVideoDefaults.Create(ProjectPaths.DefaultUserVideoFile, 10 * ProjectLimits.MicrosPerSecond));
            return Task.FromResult(new VideoImportResult { Picked = true });
        }

        public void RemoveUserVideo(IStudioProject project) => project.Apply(d => d.UserVideo = null);

        /// <summary>Real Studio stream repair; set <see cref="Rebuilder"/> to rebuild evicted render streams (e.g. ModeStreamRebuilder).</summary>
        public IRenderStreamRebuilder Rebuilder
        {
            get => _rebuilder;
            set
            {
                _rebuilder = value;
                _repair = new RenderStreamRepair(value);
            }
        }

        IRenderStreamRebuilder _rebuilder;
        RenderStreamRepair _repair = new RenderStreamRepair(null);
        public FakeThumbnails LastThumbnails;

        public ClipReadiness RunReadiness(string runId) => _repair.Readiness(Clips.GetRun(runId));

        public async Task<IStudioPreview> OpenPreviewAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct)
        {
            if (!await _repair.EnsureAsync(project.Directory, progress, ct).ConfigureAwait(true))
                throw new RenderStreamUnavailableException("the clip's render stream is missing and cannot be rebuilt from its replay");
            return new FakePreview(project);
        }

        public IStudioThumbnails OpenThumbnails(IStudioProject project)
        {
            if (!RenderStreamRepair.HasReadableStream(project.Directory)) return null;
            return LastThumbnails = new FakeThumbnails();
        }

        public ExportSettings ResolveExportSettings(ProjectDocument doc, ExportRequest request)
        {
            return ProjectLibrary.ReExportSettings(doc, Tier, request?.Bitrates, request?.Resolution, request?.Fps);
        }

        public Task<StudioExportResult> ExportAsync(string projectId, ExportRequest request, CancellationToken ct)
        {
            LastRequest = request;
            string jobId = "job" + (++Exports);
            ExportSettings settings = ResolveExportSettings(_library.Open(projectId).Document, request);
            GateRequests++;
            bool passed = _gate.RequestAsync(new ExportGateRequest(projectId, jobId, (int)settings.Resolution, settings.Fps), ct).Result;
            LastGateAnswer = passed;
            StudioExportResult result;
            if (!passed)
            {
                result = StudioExportResult.Failure(ExportErrorCode.GateDeclined, "gate declined", projectId, jobId);
            }
            else if (FailNextExportWith)
            {
                FailNextExportWith = false;
                result = StudioExportResult.Failure(FailCode, "scripted failure", projectId, jobId);
            }
            else
            {
                IsExporting = true;
                for (int i = 1; i <= 4; i++)
                    ExportProgress?.Invoke(new ExportProgressInfo(jobId, ExportPhase.Frames, i / 4.0, (4 - i) * 100L, i * 30, 120, false, false));
                result = FakeWriter(projectId, jobId, settings, request);
                IsExporting = false;
                _library.RecordExport(projectId, result);
            }
            _gate.OnExportFinished(projectId, result);
            return Task.FromResult(result);
        }

        /// <summary>The "encoder": a tiny file standing for the MP4 (the real pipeline is covered by Studio's harness).</summary>
        StudioExportResult FakeWriter(string projectId, string jobId, ExportSettings settings, ExportRequest request)
        {
            Directory.CreateDirectory(_outputs);
            string path = Path.Combine(_outputs, jobId + ".mp4");
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
            return new StudioExportResult
            {
                Error = ExportErrorCode.None,
                ProjectId = projectId,
                JobId = jobId,
                OutputPath = path,
                FrameCount = 120,
                DurationUs = 2 * ProjectLimits.MicrosPerSecond,
                Resolution = (int)settings.Resolution,
                Fps = settings.Fps,
                Gallery = request != null && request.SaveToGallery ? ExportGalleryStatus.Saved : ExportGalleryStatus.NotRequested,
                IsSynthetic = true,
            };
        }

        public IReadOnlyList<ExportResumeState> ListResumable() => Resumable;

        public Task<StudioExportResult> ResumeExportAsync(string jobId, CancellationToken ct)
        {
            Resumed++;
            Resumable.Clear();
            var result = new StudioExportResult { Error = ExportErrorCode.None, JobId = jobId, OutputPath = Path.Combine(_outputs, jobId + ".mp4") };
            return Task.FromResult(result);
        }

        public void DiscardResumable(string jobId)
        {
            Discarded++;
            Resumable.Clear();
        }

        public string RenderCover(string projectId) => null;

        public StudioUsage MeasureUsage()
        {
            StudioStorageUsage u = _storage.MeasureUsage();
            return new StudioUsage { ProjectsBytes = u.ProjectsBytes, ClipsBytes = u.ClipLibraryBytes, RecentRunsBytes = u.RecentRunsBytes, ExportScratchBytes = u.ExportScratchBytes };
        }

        public int CacheClears;
        public void ClearCaches() => CacheClears++;

        public void Dispose()
        {
        }
    }

    public sealed class FakeThumbnails : IStudioThumbnails
    {
        public readonly List<Texture> Sim = new List<Texture> { null, null, null, null };
        public readonly List<Texture> Vid = new List<Texture>();
        public ProjectDocument LastDocument;
        public bool Disposed;

        public IReadOnlyList<Texture> Simulation => Sim;
        public IReadOnlyList<Texture> Video => Vid;
        public event Action Changed;
        public void SetProject(ProjectDocument doc) => LastDocument = doc;
        public void RaiseChanged() => Changed?.Invoke();
        public void Dispose() => Disposed = true;
    }

    public sealed class FakeProject : IStudioProject
    {
        readonly ProjectEditSession _edit;

        public FakeProject(ProjectEditSession edit, string directory)
        {
            _edit = edit;
            Directory = directory;
        }

        public string ProjectId => _edit.ProjectId;
        public ProjectDocument Document => _edit.Document;
        public string Directory { get; }
        public bool CanUndo => _edit.CanUndo;
        public bool CanRedo => _edit.CanRedo;
        public Exception LastSaveError => _edit.LastSaveError;
        public int Saves;
        public bool Disposed;

        public event Action<ProjectDocument> Changed;

        public IReadOnlyList<ValidationIssue> Apply(Action<ProjectDocument> edit, string coalesceKey = null)
        {
            IReadOnlyList<ValidationIssue> issues = _edit.Apply(edit, coalesceKey);
            Changed?.Invoke(_edit.Document);
            return issues;
        }

        public bool Undo()
        {
            bool ok = _edit.Undo();
            if (ok) Changed?.Invoke(_edit.Document);
            return ok;
        }

        public bool Redo()
        {
            bool ok = _edit.Redo();
            if (ok) Changed?.Invoke(_edit.Document);
            return ok;
        }

        public void SaveNow()
        {
            Saves++;
            _edit.SaveNow();
        }

        public void Dispose()
        {
            Disposed = true;
            _edit.SaveNow();
        }
    }

    /// <summary>Timeline-only preview (no textures): clock position, play state, overlays.</summary>
    public sealed class FakePreview : IStudioPreview
    {
        readonly int _ticks;
        ProjectDocument _doc;

        public FakePreview(IStudioProject project)
        {
            _doc = project.Document;
            string bvs = Path.Combine(project.Directory, ProjectPaths.ClipRenderFile);
            using (var fs = new FileStream(bvs, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new RenderStreamReader(fs, true))
            {
                _ticks = Math.Max(1, reader.TickCount);
            }
            Timeline = ProjectComposition.BuildTimeline(_doc, 30, _ticks);
        }

        public Texture Target => null;
        public event Action<Texture> TargetChanged;
        public StudioTimeline Timeline { get; private set; }
        public long DurationUs => Timeline.DurationUs;
        public long PositionUs { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool FullScreen { get; set; }
        public bool ShowPlatformMasks { get; set; }
        public Exception AudioError => null;
        public Exception VideoError => null;
        public bool ShowsWatermark;
        public int ProjectUpdates;
        public bool Disposed;

        public void SetOverlays(bool showWatermark, IReadOnlyList<FloatRect> platformMasks) => ShowsWatermark = showWatermark;

        public void SetDisplaySize(int width, int height)
        {
        }

        public void SetProject(ProjectDocument doc)
        {
            _doc = doc;
            ProjectUpdates++;
            Timeline = ProjectComposition.BuildTimeline(doc, 30, _ticks);
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Seek(long outputUs) => PositionUs = outputUs;

        public void RaiseTargetChanged() => TargetChanged?.Invoke(null);

        public void Dispose() => Disposed = true;
    }
}
