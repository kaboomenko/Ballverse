using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Studio;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>Per-export choices handed to the studio runtime (mapped onto Studio's <c>StudioExportOptions</c>).</summary>
    public sealed class ExportRequest
    {
        /// <summary>Null = the project's last choice, else 1080p.</summary>
        public ExportResolution? Resolution { get; set; }

        /// <summary>Null = the project's last choice, else the tier default.</summary>
        public int? Fps { get; set; }

        /// <summary>Core <c>WatermarkPolicy.Resolve</c> at export time.</summary>
        public bool ShowWatermark { get; set; } = true;

        /// <summary>Platform UI masks from Remote Config (union of TikTok / Reels / Shorts zones).</summary>
        public IReadOnlyList<FloatRect> PlatformMasks { get; set; }

        /// <summary>Remote Config bitrates; null = spec defaults.</summary>
        public ExportBitrateTable Bitrates { get; set; }

        public bool SaveToGallery { get; set; } = true;
    }

    /// <summary>Outcome of the gallery video import (Studio <c>UserVideoImport</c> without the Unity types).</summary>
    public sealed class VideoImportResult
    {
        public bool Picked { get; set; }
        public MediaInfo Info { get; set; }
        /// <summary>Spec §3.2: files over 500 MB get a hint (the import still succeeds).</summary>
        public bool LargeFileWarning { get; set; }
        public bool IsHdr { get; set; }
        /// <summary>The video was longer than 180 s and was trimmed.</summary>
        public bool Trimmed { get; set; }
    }

    /// <summary>Storage used by the studio (Settings → Storage, spec §3.9).</summary>
    public sealed class StudioUsage
    {
        public long ProjectsBytes { get; set; }
        public long ClipsBytes { get; set; }
        public long RecentRunsBytes { get; set; }
        public long ExportScratchBytes { get; set; }
    }

    /// <summary>An open project: edits with undo and autosave (Studio <c>StudioProjectSession</c>).</summary>
    public interface IStudioProject : IDisposable
    {
        string ProjectId { get; }
        ProjectDocument Document { get; }
        /// <summary>Absolute project folder (clip.bvr / clip.bvs / clip.bve, media/).</summary>
        string Directory { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        Exception LastSaveError { get; }

        /// <summary>The document changed (edit, undo, redo); the argument is the new document.</summary>
        event Action<ProjectDocument> Changed;

        /// <summary>Applies an edit (sanitized; one undo step, coalesced with the previous edit of the same key within 1 s).</summary>
        IReadOnlyList<ValidationIssue> Apply(Action<ProjectDocument> edit, string coalesceKey = null);

        /// <summary>A change that is not a user edit (new-project defaults): no undo step, Undo never takes it back.</summary>
        void ApplyMetadata(Action<ProjectDocument> change);

        bool Undo();
        bool Redo();

        /// <summary>Writes the project now (pause, leaving the studio).</summary>
        void SaveNow();
    }

    /// <summary>The studio's realtime composited preview (Studio <c>StudioPreviewPlayer</c>).</summary>
    public interface IStudioPreview : IDisposable
    {
        Texture Target { get; }
        event Action<Texture> TargetChanged;
        StudioTimeline Timeline { get; }
        long DurationUs { get; }
        long PositionUs { get; }
        bool IsPlaying { get; }
        bool FullScreen { get; set; }
        bool ShowPlatformMasks { get; set; }
        Exception AudioError { get; }
        Exception VideoError { get; }
        void SetOverlays(bool showWatermark, IReadOnlyList<FloatRect> platformMasks);
        void SetDisplaySize(int width, int height);
        void SetProject(ProjectDocument doc);
        void Play();
        void Pause();
        void Seek(long outputUs);
    }

    /// <summary>Background frame thumbnails of the timeline tracks S and V (Studio <c>TimelineThumbnails</c>).</summary>
    public interface IStudioThumbnails : IDisposable
    {
        /// <summary>Track S thumbnails over the full clip, in time order (null entries while pending).</summary>
        IReadOnlyList<Texture> Simulation { get; }
        /// <summary>Track V thumbnails over the full source video (empty without a user video).</summary>
        IReadOnlyList<Texture> Video { get; }
        /// <summary>A thumbnail became ready.</summary>
        event Action Changed;
        void SetProject(ProjectDocument doc);
    }

    /// <summary>
    /// What the App needs from the studio runtime (spec §3, §7.5): recent runs and clips, projects, the realtime preview and
    /// the MP4 export with resume. Device builds use <see cref="StudioBackend"/> over Ballverse.Studio's
    /// <c>StudioServices</c>; EditMode tests use a fake that keeps the real clip library and project files but needs no GPU.
    /// Main thread.
    /// </summary>
    public interface IStudioBackend : IDisposable
    {
        /// <summary>Recent runs ring and saved clips.</summary>
        ClipLibrary Clips { get; }

        DeviceTier Tier { get; set; }

        ProjectListResult ListProjects();

        /// <summary>Whether a recent run can become a project now (its render stream may have been evicted, spec §3.1).</summary>
        ClipReadiness RunReadiness(string runId);

        /// <summary>"В видео" from a recent run.</summary>
        /// <exception cref="RenderStreamUnavailableException">No render stream and it cannot be rebuilt.</exception>
        ProjectDocument CreateFromRun(string runId);
        ProjectDocument CreateFromClip(string clipId);

        /// <summary>Opens a project for editing, or returns null (see <paramref name="load"/>).</summary>
        IStudioProject OpenProject(string projectId, out LoadResult load);

        ProjectDocument Duplicate(string projectId);
        ProjectDocument Rename(string projectId, string name);
        bool DeleteProject(string projectId);
        string ProjectDirectory(string projectId);

        /// <summary>System video picker → project media folder → user video of the project.</summary>
        /// <exception cref="MediaException">Copy / probe failed (the copy is deleted).</exception>
        Task<VideoImportResult> ImportUserVideoAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct);

        void RemoveUserVideo(IStudioProject project);

        /// <summary>
        /// Opens the preview of an open project, first rebuilding an evicted render stream from the replay (dispose it when
        /// leaving the studio or before exporting).
        /// </summary>
        /// <exception cref="RenderStreamUnavailableException">No render stream and it cannot be rebuilt.</exception>
        Task<IStudioPreview> OpenPreviewAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct);

        /// <summary>Starts the timeline thumbnails of an open project whose render stream is present, or returns null.</summary>
        IStudioThumbnails OpenThumbnails(IStudioProject project);

        event Action<ExportProgressInfo> ExportProgress;
        bool IsExporting { get; }

        /// <summary>The frame being encoded (progress screen), or null.</summary>
        Texture ExportPreviewTexture { get; }

        ExportSettings ResolveExportSettings(ProjectDocument doc, ExportRequest request);

        /// <summary>Runs an export (the rewarded gate is the <see cref="IExportGate"/> given at creation).</summary>
        Task<StudioExportResult> ExportAsync(string projectId, ExportRequest request, CancellationToken ct);

        /// <summary>Exports interrupted by the system (spec §3.8 RW4) that can continue.</summary>
        IReadOnlyList<ExportResumeState> ListResumable();

        Task<StudioExportResult> ResumeExportAsync(string jobId, CancellationToken ct);
        void DiscardResumable(string jobId);

        /// <summary>Renders <c>thumbs/cover.png</c> of a project; returns its path or null.</summary>
        string RenderCover(string projectId);

        StudioUsage MeasureUsage();

        /// <summary>Drops regenerable caches (decoded audio, thumbnails).</summary>
        void ClearCaches();
    }
}
