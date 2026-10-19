using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Studio;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// The studio stand-in when the studio runtime could not start (storage error, full disk: spec §7.12 — a native or storage
    /// failure never takes the app down). The simulation keeps working (unrecorded: <see cref="Clips"/> is null), the gallery,
    /// shop and settings work; «В видео», projects and export answer with a toast. Every member is safe to call: lists are
    /// empty, actions throw <see cref="InvalidOperationException"/> (which every App call site handles) or return null.
    /// </summary>
    public sealed class UnavailableStudioBackend : IStudioBackend
    {
        static readonly ProjectListResult s_empty = new ProjectListResult(Array.Empty<ProjectSummary>(), Array.Empty<ValidationIssue>());

        public UnavailableStudioBackend(Exception cause)
        {
            Cause = cause;
        }

        /// <summary>Why the studio did not start.</summary>
        public Exception Cause { get; }

        public ClipLibrary Clips => null;
        public DeviceTier Tier { get; set; } = DeviceTier.B;

        public event Action<ExportProgressInfo> ExportProgress
        {
            add { }
            remove { }
        }

        public bool IsExporting => false;
        public Texture ExportPreviewTexture => null;

        public ProjectListResult ListProjects() => s_empty;
        public ClipReadiness RunReadiness(string runId) => throw Unavailable();
        public ProjectDocument CreateFromRun(string runId) => throw Unavailable();
        public ProjectDocument CreateFromClip(string clipId) => throw Unavailable();

        public IStudioProject OpenProject(string projectId, out LoadResult load)
        {
            load = null;
            throw Unavailable();
        }

        public ProjectDocument Duplicate(string projectId) => throw Unavailable();
        public ProjectDocument Rename(string projectId, string name) => throw Unavailable();
        public bool DeleteProject(string projectId) => false;
        public string ProjectDirectory(string projectId) => throw Unavailable();
        public Task<VideoImportResult> ImportUserVideoAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct) => Task.FromException<VideoImportResult>(Unavailable());
        public void RemoveUserVideo(IStudioProject project) => throw Unavailable();
        public Task<IStudioPreview> OpenPreviewAsync(IStudioProject project, IProgress<double> progress, CancellationToken ct) => Task.FromException<IStudioPreview>(Unavailable());
        public IStudioThumbnails OpenThumbnails(IStudioProject project) => null;
        public ExportSettings ResolveExportSettings(ProjectDocument doc, ExportRequest request) => throw Unavailable();
        public Task<StudioExportResult> ExportAsync(string projectId, ExportRequest request, CancellationToken ct) => Task.FromException<StudioExportResult>(Unavailable());
        public IReadOnlyList<ExportResumeState> ListResumable() => Array.Empty<ExportResumeState>();
        public Task<StudioExportResult> ResumeExportAsync(string jobId, CancellationToken ct) => Task.FromException<StudioExportResult>(Unavailable());

        public void DiscardResumable(string jobId)
        {
        }

        public string RenderCover(string projectId) => null;
        public StudioUsage MeasureUsage() => new StudioUsage();

        public void ClearCaches()
        {
        }

        public void Dispose()
        {
        }

        InvalidOperationException Unavailable() => new InvalidOperationException("The studio is unavailable: " + (Cause?.Message ?? "not started"), Cause);
    }
}
