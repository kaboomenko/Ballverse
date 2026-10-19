using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Modes;
using Ballverse.Core.Monetization;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Studio screen (spec §3.3–§3.7, §4.3): one open project (autosaved, 50-step undo) with the composited 9:16 preview, the
    /// timeline built from Core <c>StudioTimeline</c>, the layout slots from Core <c>LayoutCalculator</c> (gestures), and the
    /// five tools. Every edit goes through the project session (sanitized by Core) and reaches the preview within one frame.
    /// The preview is released before an export and reopened afterwards (memory budget, spec §7.11): every preview opening
    /// has its own generation and cancellation, so closing the project, opening another one or suspending for an export while
    /// an opening is still rebuilding the render stream discards that opening's result. Main thread.
    /// </summary>
    public sealed class StudioViewModel : ViewModelBase, IStudioViewModel
    {
        const int CanvasW = 1080;
        const int CanvasH = 1920;

        readonly AppContext _ctx;
        readonly StudioLayoutTool _layout;
        readonly StudioVideoTool _video;
        readonly StudioSimTool _sim;
        readonly StudioTextTool _text;
        readonly StudioSoundTool _sound;
        readonly List<PreviewSlotData> _slots = new List<PreviewSlotData>();
        IStudioProject _project;
        IStudioPreview _preview;
        IStudioThumbnails _thumbnails;
        bool _previewOpening;
        int _previewGeneration;
        CancellationTokenSource _previewCts;
        StudioTimelineData _timeline;
        bool _active;
        bool _suspended;
        bool _lastPlaying;
        bool _reportedPreviewError;
        long _videoSourceUs;
        CancellationTokenSource _io;

        public StudioViewModel(AppContext ctx)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _layout = new StudioLayoutTool(this);
            _video = new StudioVideoTool(this);
            _sim = new StudioSimTool(this);
            _text = new StudioTextTool(this);
            _sound = new StudioSoundTool(this);
            _timeline = new StudioTimelineData(0, null, null, null, null, 60);
            ctx.Lifecycle.Paused += OnAppPaused;
            ctx.Services.Purchases.EntitlementsChanged += _ => UpdateOverlays();
            ctx.Remote.Changed += UpdateOverlays;
        }

        /// <summary>Simulation view model for "Перегенерировать" (set by the composition).</summary>
        public SimulationViewModel Simulation { get; set; }

        /// <summary>Raised when a project's content changed on disk (projects tab).</summary>
        public event Action LibraryChanged;

        internal AppContext Ctx => _ctx;
        internal IStudioProject Project => _project;
        internal ProjectDocument Doc => _project?.Document;
        internal long VideoSourceUs => _videoSourceUs;

        public string CurrentProjectId => _project?.ProjectId;
        public bool HasProject => _project != null;

        public Texture Preview => _preview?.Target;
        public bool IsPlaying => _preview != null && _preview.IsPlaying;
        public long PositionUs => _preview != null ? _preview.PositionUs : 0;
        public StudioTimelineData Timeline => _timeline;
        public IReadOnlyList<PreviewSlotData> PreviewSlots => _slots;
        public DurationMode DurationMode => Doc?.Output.DurationMode ?? DurationMode.Auto;
        public long ManualDurationUs => Doc?.Output.ManualDurationUs ?? 15 * ProjectLimits.MicrosPerSecond;
        public bool CanUndo => _project != null && _project.CanUndo;
        public bool CanRedo => _project != null && _project.CanRedo;
        public bool FullscreenPreview { get; private set; }
        public IStudioLayoutTool Layout => _layout;
        public IStudioVideoTool Video => _video;
        public IStudioSimTool Sim => _sim;
        public IStudioTextTool Text => _text;
        public IStudioSoundTool Sound => _sound;

        // ------------------------------------------------------------------ project

        /// <summary>
        /// Opens a project for editing ("В видео", projects tab). Returns false (toast) when it cannot be opened.
        /// <paramref name="created"/>: the project was just made from a run — it takes the app-wide defaults (Settings
        /// «Водяной знак» of a NO_WATERMARK owner) without an undo step.
        /// </summary>
        public bool OpenProject(string projectId, bool created = false)
        {
            Close();
            IStudioProject project;
            try
            {
                project = _ctx.Studio.OpenProject(projectId, out LoadResult load);
                if (project == null) _ctx.Notices.Report("studio.open", new InvalidDataException("project " + projectId + ": " + load?.Status), AppStrings.OpenProjectFailed);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("studio.open", e, AppStrings.OpenProjectFailed);
                project = null;
            }
            if (project == null)
            {
                Notify();
                return false;
            }
            _project = project;
            if (created) ApplyNewProjectDefaults(project);
            _project.Changed += OnDocumentChanged;
            _io = new CancellationTokenSource();
            _text.ResetSelection();
            _videoSourceUs = 0;
            OpenPreview();
            ProbeVideo();
            Rebuild();
            Notify();
            return true;
        }

        void ApplyNewProjectDefaults(IStudioProject project)
        {
            try
            {
                if (!_ctx.Services.Entitlements.Contains(Entitlement.NoWatermark)) return;
                bool keep = _ctx.Settings.Data.KeepWatermark;
                if (project.Document.Output.Watermark != keep) project.ApplyMetadata(d => d.Output.Watermark = keep);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("studio.project_defaults", e);
            }
        }

        /// <summary>Saves and closes the open project and its preview.</summary>
        public void Close()
        {
            CancelPreviewOpening();
            ClosePreview();
            if (_io != null)
            {
                _io.Cancel();
                _io.Dispose();
                _io = null;
            }
            if (_project != null)
            {
                _project.Changed -= OnDocumentChanged;
                try
                {
                    _project.SaveNow();
                    _project.Dispose();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                {
                    _ctx.Notices.Report("studio.close", e, AppStrings.SaveFailed);
                }
                _project = null;
                LibraryChanged?.Invoke();
            }
            _slots.Clear();
            _timeline = new StudioTimelineData(0, null, null, null, null, 60);
            FullscreenPreview = false;
        }

        /// <summary>Releases the preview (before an export: its mix and textures are freed, spec §7.11).</summary>
        public void SuspendPreview()
        {
            // Also while an opening is still in flight: its result is discarded and nothing reopens until ResumePreview.
            _suspended = true;
            CancelPreviewOpening();
            ClosePreview();
            Notify();
        }

        /// <summary>Reopens the preview after an export.</summary>
        public void ResumePreview()
        {
            if (!_suspended) return;
            _suspended = false;
            if (_project != null && _preview == null) OpenPreview();
            Rebuild();
            Notify();
        }

        void OpenPreview()
        {
            if (_project == null || _preview != null || _previewOpening || _suspended) return;
            _previewOpening = true;
            int generation = ++_previewGeneration;
            _previewCts = _io != null ? CancellationTokenSource.CreateLinkedTokenSource(_io.Token) : new CancellationTokenSource();
            OpenPreviewAsync(_project, generation, _previewCts.Token);
        }

        /// <summary>Discards the opening in flight (its result is disposed when it arrives) and allows a new one.</summary>
        void CancelPreviewOpening()
        {
            _previewGeneration++;
            _previewOpening = false;
            CancellationTokenSource cts = _previewCts;
            _previewCts = null;
            // Cancel only: the opening may still register on its token, so the source is left to the collector (a linked
            // source's registration on _io ends when _io is disposed).
            cts?.Cancel();
        }

        async void OpenPreviewAsync(IStudioProject project, int generation, CancellationToken ct)
        {
            IStudioPreview preview = null;
            try
            {
                preview = await _ctx.Studio.OpenPreviewAsync(project, null, ct);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                // async void: every failure (media, render stream unavailable, storage) is reported, never rethrown.
                if (generation == _previewGeneration) _ctx.Notices.Report("studio.preview", e, AppStrings.PreviewFailed);
                else _ctx.Notices.Report("studio.preview_stale", e);
            }
            bool current = generation == _previewGeneration;
            if (current)
            {
                _previewOpening = false;
                CancellationTokenSource cts = _previewCts;
                _previewCts = null;
                cts?.Dispose();
            }
            if (preview == null) return;
            if (!current || _project != project || _suspended || ct.IsCancellationRequested || _preview != null)
            {
                SafeDispose(preview);
                return;
            }
            _preview = preview;
            _preview.TargetChanged += OnPreviewTarget;
            _reportedPreviewError = false;
            int w = _ctx.Tier == DeviceTier.C ? 432 : 540;
            _preview.SetDisplaySize(w, w * 16 / 9);
            _preview.ShowPlatformMasks = _layout.PlatformZones;
            _preview.FullScreen = FullscreenPreview;
            UpdateOverlays();
            OpenThumbnails();
            Rebuild();
            Notify();
        }

        void OpenThumbnails()
        {
            if (_project == null || _thumbnails != null) return;
            try
            {
                _thumbnails = _ctx.Studio.OpenThumbnails(_project);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("studio.thumbnails", e);
                _thumbnails = null;
            }
            if (_thumbnails != null) _thumbnails.Changed += OnThumbnails;
        }

        void CloseThumbnails()
        {
            if (_thumbnails == null) return;
            _thumbnails.Changed -= OnThumbnails;
            try
            {
                _thumbnails.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException)
            {
                _ctx.Notices.Report("studio.thumbnails_close", e);
            }
            _thumbnails = null;
        }

        void OnThumbnails()
        {
            Rebuild();
            Notify();
        }

        void ClosePreview()
        {
            CloseThumbnails();
            if (_preview == null) return;
            _preview.TargetChanged -= OnPreviewTarget;
            try
            {
                _preview.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException)
            {
                _ctx.Notices.Report("studio.preview_close", e);
            }
            _preview = null;
        }

        void SafeDispose(IStudioPreview preview)
        {
            try
            {
                preview.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException)
            {
                _ctx.Notices.Report("studio.preview_close", e);
            }
        }

        void OnPreviewTarget(Texture t) => Notify();

        void OnDocumentChanged(ProjectDocument doc)
        {
            try
            {
                _preview?.SetProject(doc);
                _thumbnails?.SetProject(doc);
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is ArgumentException)
            {
                _ctx.Notices.Report("studio.set_project", e);
            }
            Rebuild();
            Notify();
        }

        /// <summary>Applies an edit to the open project (sanitized, undoable, autosaved).</summary>
        internal void Edit(Action<ProjectDocument> change, string coalesceKey)
        {
            if (_project == null) return;
            try
            {
                _project.Apply(change, coalesceKey);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("studio.edit", e, AppStrings.SaveFailed);
            }
        }

        void ProbeVideo()
        {
            ProjectDocument doc = Doc;
            if (doc?.UserVideo == null || _project == null) return;
            string path = Path.Combine(_project.Directory, doc.UserVideo.File.Replace('/', Path.DirectorySeparatorChar));
            CancellationToken ct = _io.Token;
            string projectId = _project.ProjectId;
            ProbeVideoAsync(path, projectId, ct);
        }

        async void ProbeVideoAsync(string path, string projectId, CancellationToken ct)
        {
            try
            {
                MediaInfo info = await _ctx.Media.ProbeAsync(path, ct);
                if (_project == null || _project.ProjectId != projectId) return;
                _videoSourceUs = info.DurationUs;
                Rebuild();
                Notify();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e) when (e is MediaException || e is IOException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("studio.probe", e);
            }
        }

        // ------------------------------------------------------------------ derived data

        void Rebuild()
        {
            ProjectDocument doc = Doc;
            _slots.Clear();
            if (doc == null)
            {
                _timeline = new StudioTimelineData(0, null, null, null, null, 60);
                return;
            }
            BuildTimeline(doc);
            BuildSlots(doc);
        }

        void BuildTimeline(ProjectDocument doc)
        {
            StudioTimeline tl = _preview?.Timeline;
            int fps = tl != null ? tl.Fps : ProjectComposition.ResolveExportSettings(doc.Output, _ctx.Tier).Fps;
            int available = tl != null ? tl.Spec.Clip.AvailableTicks : Math.Max(1, doc.Clip.TrimOutTick);
            if (tl == null)
            {
                try
                {
                    tl = ProjectComposition.BuildTimeline(doc, fps, available);
                }
                catch (ArgumentException e)
                {
                    _ctx.Notices.Report("studio.timeline", e);
                    _timeline = new StudioTimelineData(0, null, null, null, null, fps);
                    return;
                }
            }
            ClipSettings clip = doc.Clip;
            long availUs = ProjectTiming.TicksToUs(available);
            var sim = new TimelineTrackData(true, availUs, ProjectTiming.TicksToUs(clip.TrimInTick),
                ProjectTiming.TicksToUs(Math.Min(clip.TrimOutTick, available)), tl.ClipStartUs, ReadyThumbnails(_thumbnails?.Simulation));
            TimelineTrackData video = TimelineTrackData.Empty;
            UserVideoSettings uv = doc.UserVideo;
            if (uv != null && doc.Layout.Type != LayoutType.L4)
            {
                long source = Math.Max(_videoSourceUs, uv.TrimOutUs);
                video = new TimelineTrackData(true, source, uv.TrimInUs, uv.TrimOutUs, tl.VideoStartUs, ReadyThumbnails(_thumbnails?.Video));
            }
            var texts = new List<TimelineTextItem>(doc.Texts.Count);
            for (int i = 0; i < doc.Texts.Count; i++)
            {
                TextLayer t = doc.Texts[i];
                texts.Add(new TimelineTextItem(i, t.InUs, t.OutUs, t.Text));
            }
            TimelineMusicItem music = null;
            MusicSettings m = doc.Audio.Music;
            if (m != null && !string.IsNullOrEmpty(m.Track))
            {
                music = new TimelineMusicItem(LocalizedText.FromLiteral(Ballverse.Sim.ProceduralMusic.NameOf(m.Track)), 0, tl.DurationUs, m.StartUs,
                    StudioSoundTool.TrackLengthUs(m.Track));
            }
            _timeline = new StudioTimelineData(tl.DurationUs, sim, video, texts, music, fps);
        }

        /// <summary>The thumbnails that are ready, in order (the track shows them evenly spaced).</summary>
        static IReadOnlyList<Texture> ReadyThumbnails(IReadOnlyList<Texture> all)
        {
            if (all == null || all.Count == 0) return Array.Empty<Texture>();
            var ready = new List<Texture>(all.Count);
            foreach (Texture t in all)
            {
                if (t != null) ready.Add(t);
            }
            return ready.Count == all.Count ? all : (IReadOnlyList<Texture>)ready;
        }

        void BuildSlots(ProjectDocument doc)
        {
            bool hasVideo = doc.UserVideo != null;
            FrameLayout layout;
            try
            {
                layout = LayoutCalculator.ComputeSlots(CanvasW, CanvasH, ProjectComposition.BuildLayoutSpec(doc.Layout, hasVideo));
            }
            catch (ArgumentException e)
            {
                _ctx.Notices.Report("studio.layout", e);
                return;
            }
            IReadOnlyList<SlotGeometry> slots = layout.SlotsBackToFront;
            for (int i = 0; i < slots.Count; i++)
            {
                SlotGeometry g = slots[i];
                bool pip = layout.HasWindow && i == 1;
                _slots.Add(new PreviewSlotData(g.Kind, FloatRect.FromPixels(g.Rect, CanvasW, CanvasH), pip));
            }
        }

        internal void UpdateOverlays()
        {
            if (_preview == null || Doc == null) return;
            try
            {
                bool owns = _ctx.Services.Entitlements.Contains(Entitlement.NoWatermark);
                bool watermark = WatermarkPolicy.Resolve(Doc.Output.Watermark, owns, _ctx.Remote.WatermarkEnabled);
                _preview.SetOverlays(watermark, _ctx.Remote.MaskUnion);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("studio.overlays", e);
            }
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(float deltaSeconds)
        {
            if (_preview == null) return;
            bool playing = _preview.IsPlaying;
            if (playing != _lastPlaying)
            {
                _lastPlaying = playing;
                Notify();
            }
            if (!_reportedPreviewError && (_preview.AudioError != null || _preview.VideoError != null))
            {
                _reportedPreviewError = true;
                _ctx.Notices.Report("studio.preview_media", _preview.VideoError ?? _preview.AudioError, AppStrings.PreviewFailed);
            }
        }

        // ------------------------------------------------------------------ IStudioViewModel

        public void TogglePlay()
        {
            if (_preview == null) return;
            if (_preview.IsPlaying) _preview.Pause();
            else _preview.Play();
            _lastPlaying = _preview.IsPlaying;
            Notify();
        }

        public void Seek(long us, bool dragging)
        {
            if (_preview == null) return;
            if (dragging && _preview.IsPlaying) _preview.Pause();
            _preview.Seek(Math.Max(0, Math.Min(_timeline.DurationUs, us)));
            Notify();
        }

        public void Undo()
        {
            if (_project != null && _project.Undo()) Notify();
        }

        public void Redo()
        {
            if (_project != null && _project.Redo()) Notify();
        }

        public void SetFullscreenPreview(bool fullscreen)
        {
            FullscreenPreview = fullscreen;
            if (_preview != null) _preview.FullScreen = fullscreen;
            Notify();
        }

        public void SetSimTrim(long startUs, long endUs)
        {
            int available = _preview?.Timeline != null ? _preview.Timeline.Spec.Clip.AvailableTicks : SimConstants.MaxDurationTicks;
            int a = UsToTicks(startUs);
            int b = UsToTicks(endUs);
            a = Math.Max(0, Math.Min(a, available - 1));
            b = Math.Max(a + 1, Math.Min(b, available));
            Edit(d =>
            {
                d.Clip.TrimInTick = a;
                d.Clip.TrimOutTick = b;
            }, "sim_trim");
        }

        public void SetVideoTrim(long startUs, long endUs)
        {
            if (Doc?.UserVideo == null) return;
            long max = _videoSourceUs > 0 ? _videoSourceUs : Doc.UserVideo.TrimOutUs;
            long a = Math.Max(0, Math.Min(startUs, max - 1));
            long b = Math.Max(a + 1, Math.Min(endUs, max));
            Edit(d =>
            {
                if (d.UserVideo == null) return;
                d.UserVideo.TrimInUs = a;
                d.UserVideo.TrimOutUs = b;
            }, "video_trim");
        }

        public void SetVideoOffset(long offsetUs)
        {
            long v = Math.Max(-UserVideoTiming.MaxAbsOffsetUs, Math.Min(UserVideoTiming.MaxAbsOffsetUs, offsetUs));
            Edit(d =>
            {
                if (d.UserVideo != null) d.UserVideo.OffsetUs = v;
            }, "video_offset");
        }

        public void NudgeVideo(int frames)
        {
            UserVideoSettings uv = Doc?.UserVideo;
            if (uv == null) return;
            long next = UserVideoTiming.NudgeOffsetUs(uv.OffsetUs, frames, Math.Max(1, _timeline.Fps));
            SetVideoOffset(next);
        }

        public void SetDurationMode(DurationMode mode)
        {
            Edit(d => d.Output.DurationMode = mode, null);
        }

        public void SetManualDuration(long us)
        {
            long v = Math.Max(ProjectLimits.MinDurationUs, Math.Min(ProjectLimits.MaxDurationUs, us));
            Edit(d =>
            {
                d.Output.DurationMode = DurationMode.Manual;
                d.Output.ManualDurationUs = v;
            }, "manual_duration");
        }

        public void PanZoomSlot(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final)
        {
            if (Doc == null) return;
            float factor = float.IsNaN(zoomFactor) || zoomFactor <= 0f ? 1f : zoomFactor;
            Edit(d =>
            {
                double[] pan;
                double zoom;
                if (slot == SlotKind.Video)
                {
                    if (d.UserVideo == null) return;
                    pan = d.UserVideo.Pan ?? new double[2];
                    zoom = d.UserVideo.Zoom;
                }
                else
                {
                    pan = d.Clip.Pan ?? new double[2];
                    zoom = d.Clip.Zoom;
                }
                var next = new[]
                {
                    Clamp(pan[0] + panDelta.X, ProjectLimits.MinPan, ProjectLimits.MaxPan),
                    Clamp(pan[1] + panDelta.Y, ProjectLimits.MinPan, ProjectLimits.MaxPan),
                };
                double z = Clamp(zoom * factor, ProjectLimits.MinZoom, ProjectLimits.MaxZoom);
                if (slot == SlotKind.Video)
                {
                    d.UserVideo.Pan = next;
                    d.UserVideo.Zoom = z;
                }
                else
                {
                    d.Clip.Pan = next;
                    d.Clip.Zoom = z;
                }
            }, "panzoom_" + slot);
        }

        public void ResetSlotTransform(SlotKind slot)
        {
            Edit(d =>
            {
                if (slot == SlotKind.Video)
                {
                    if (d.UserVideo == null) return;
                    d.UserVideo.Pan = new[] { 0.0, 0.0 };
                    d.UserVideo.Zoom = 1.0;
                }
                else
                {
                    d.Clip.Pan = new[] { 0.0, 0.0 };
                    d.Clip.Zoom = 1.0;
                }
            }, null);
        }

        public void Back()
        {
            Close();
            Notify();
        }

        public void SetScreenActive(bool active)
        {
            _active = active;
            try
            {
                _ctx.Services.RewardedExport.SetInStudio(active);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("studio.rewarded_preload", e);
            }
            if (_ctx.Ads != null) _ctx.Ads.InStudio = active;
            if (!active && _preview != null && _preview.IsPlaying) _preview.Pause();
            if (active && _project != null && _preview == null && !_suspended) OpenPreview();
            Notify();
        }

        /// <summary>Imports (or replaces) the user video through the system picker (spec §3.2).</summary>
        internal async void ImportVideo()
        {
            IStudioProject project = _project;
            if (project == null || _io == null) return;
            CancellationToken ct = _io.Token;
            try
            {
                VideoImportResult result = await _ctx.Studio.ImportUserVideoAsync(project, null, ct);
                if (!result.Picked || _project != project) return;
                _videoSourceUs = result.Info != null ? result.Info.DurationUs : 0;
                if (result.LargeFileWarning) _ctx.Notices.Toast(AppStrings.ImportLargeFile);
                if (result.IsHdr) _ctx.Notices.Toast(AppStrings.ImportHdr);
                if (result.Trimmed) _ctx.Notices.Toast(AppStrings.ImportTrimmed);
                if (Doc != null && Doc.Layout.Type == LayoutType.L4) Edit(d => d.Layout.Type = LayoutType.L1, null);
                Rebuild();
                Notify();
            }
            catch (OperationCanceledException)
            {
            }
            catch (MediaException e)
            {
                string key = e.Code == MediaErrorCode.InsufficientSpace ? AppStrings.ImportNoSpace
                    : e.Code == MediaErrorCode.UnsupportedFormat || e.Code == MediaErrorCode.DrmProtected || e.Code == MediaErrorCode.NoVideoTrack
                        || e.Code == MediaErrorCode.Corrupt || e.Code == MediaErrorCode.HdrUnsupported
                        ? AppStrings.ImportUnsupported
                        : AppStrings.ImportFailed;
                _ctx.Notices.Report("studio.import", e, key);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("studio.import", e, AppStrings.ImportFailed);
            }
        }

        internal void RemoveVideo()
        {
            if (_project == null) return;
            try
            {
                _ctx.Studio.RemoveUserVideo(_project);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("studio.remove_video", e, AppStrings.SaveFailed);
            }
        }

        internal void SetPlatformZones(bool visible)
        {
            if (_preview != null) _preview.ShowPlatformMasks = visible;
            Notify();
        }

        /// <summary>"Перегенерировать": the simulation screen with the clip's mode, parameters and seed.</summary>
        internal void Regenerate()
        {
            if (_project == null || Simulation == null) return;
            if (ClipParams.TryRead(_project.Directory, _ctx.Modes, out IModeDefinition mode, out ParamSet values, out _))
            {
                Simulation.OpenWith(mode, values);
            }
            else if (Doc?.Clip.ModeId != null)
            {
                Simulation.Open(Doc.Clip.ModeId);
            }
        }

        void OnAppPaused()
        {
            if (_preview != null && _preview.IsPlaying) _preview.Pause();
            try
            {
                _project?.SaveNow();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("studio.autosave", e);
            }
        }

        internal void NotifyChanged() => Notify();

        internal static int UsToTicks(long us)
        {
            return (int)Math.Round(us * (double)SimConstants.TickRate / ProjectLimits.MicrosPerSecond);
        }

        internal static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            return v < min ? min : v > max ? max : v;
        }

        internal static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        internal static ColorRgba ParseColor(string hex, ColorRgba fallback)
        {
            return hex != null && ColorRgba.TryParse(hex, out ColorRgba c) ? c : fallback;
        }

        internal static string FormatColor(ColorRgba c) => c.ToString();
    }
}
