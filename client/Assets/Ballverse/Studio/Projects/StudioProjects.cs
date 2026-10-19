using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Media;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>Outcome of a gallery import into a project (spec §3.2).</summary>
    public sealed class UserVideoImport
    {
        public PickStatus Status { get; set; }
        /// <summary>Probe of the copied file (null when cancelled).</summary>
        public MediaInfo Info { get; set; }
        /// <summary>Project-relative path of the copy ("media/import_….mp4").</summary>
        public string File { get; set; }
        /// <summary>Spec §3.2: the file takes more than 25 % of the free space (warning shown at import).</summary>
        public bool LargeFileWarning { get; set; }
        /// <summary>HDR source: the decoder tone-maps to SDR where the device can; otherwise colours may look paler (spec §3.2).</summary>
        public bool IsHdr { get; set; }
        /// <summary>The source is longer than the 180 s fragment the reel can use (only the first 180 s are selected).</summary>
        public bool Trimmed { get; set; }
    }

    /// <summary>
    /// An open project in the studio (spec §3.9): edits through <see cref="Apply"/> (sanitized, undo), autosave 1 s after the last
    /// change with the JSON write on a worker thread, a synchronous save when the app goes to the background and on
    /// <see cref="Dispose"/>. Main thread.
    /// </summary>
    public sealed class StudioProjectSession : IDisposable
    {
        readonly StudioProjects _owner;
        readonly ProjectEditSession _edit;
        Task _write;
        long _token;
        bool _completionPending;
        bool _disposed;
        int _importsInFlight;

        internal StudioProjectSession(StudioProjects owner, ProjectEditSession edit, IReadOnlyList<ValidationIssue> loadIssues, LoadSource source)
        {
            _owner = owner;
            _edit = edit;
            LoadIssues = loadIssues;
            LoadedFrom = source;
        }

        public string ProjectId => _edit.ProjectId;
        public ProjectDocument Document => _edit.Document;
        public ProjectEditSession Edit => _edit;
        public int Revision => _edit.Revision;
        /// <summary>Warnings of loading (values repaired, backup used).</summary>
        public IReadOnlyList<ValidationIssue> LoadIssues { get; }
        public LoadSource LoadedFrom { get; }
        /// <summary>Absolute project folder.</summary>
        public string Directory => _owner.ProjectDirectory(ProjectId);
        public bool IsDirty => _edit.IsDirty;
        public bool CanUndo => _edit.CanUndo;
        public bool CanRedo => _edit.CanRedo;
        public Exception LastSaveError => _edit.LastSaveError;

        /// <summary>Raised on the main thread after every applied edit, undo or redo (preview: <see cref="StudioPreviewPlayer.SetProject"/>).</summary>
        public event Action<ProjectDocument> Changed;

        /// <summary>Applies an edit (see <see cref="ProjectEditSession.Apply"/>); pass a coalesce key for continuous gestures.</summary>
        public IReadOnlyList<ValidationIssue> Apply(Action<ProjectDocument> edit, string coalesceKey = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StudioProjectSession));
            IReadOnlyList<ValidationIssue> issues = _edit.Apply(edit, coalesceKey);
            Changed?.Invoke(_edit.Document);
            return issues;
        }

        /// <summary>Applies a non-undoable metadata change (see <see cref="ProjectEditSession.ApplyMetadata"/>).</summary>
        public void ApplyMetadata(Action<ProjectDocument> change)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StudioProjectSession));
            _edit.ApplyMetadata(change);
            Changed?.Invoke(_edit.Document);
        }

        public bool Undo()
        {
            if (!_edit.Undo()) return false;
            Changed?.Invoke(_edit.Document);
            return true;
        }

        public bool Redo()
        {
            if (!_edit.Redo()) return false;
            Changed?.Invoke(_edit.Document);
            return true;
        }

        /// <summary>Saves now if needed and waits for any running autosave (call before leaving the studio).</summary>
        public void SaveNow()
        {
            WaitForSave();
            try
            {
                _edit.SaveNow();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("[Studio] Project save failed: " + e.Message);
            }
        }

        internal void Tick()
        {
            if (_disposed || _completionPending) return;
            if (!_edit.TryBeginSave(out ProjectDocument snapshot, out long token)) return;
            ProjectEditSession edit = _edit;
            _token = token;
            _completionPending = true;
            _write = Task.Run(() => edit.WriteSnapshot(snapshot));
            _write.ContinueWith(Complete, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Reports the worker write to the autosave scheduler (once; main thread).</summary>
        void Complete(Task write)
        {
            if (!_completionPending || !ReferenceEquals(write, _write)) return;
            _completionPending = false;
            if (write.IsFaulted) _edit.FailSave(_token, write.Exception?.GetBaseException());
            else _edit.CompleteSave(_token);
        }

        internal void OnAppPaused()
        {
            // The OS may kill the app without further notice: write synchronously (spec §3.9 "при уходе приложения в фон").
            SaveNow();
        }

        void WaitForSave()
        {
            Task write = _write;
            if (write == null || !_completionPending) return;
            try
            {
                // The worker write is a few KB. Wait for the write itself (not its main-thread continuation, which cannot run
                // while this thread waits) and report it inline, so the following synchronous save covers the newer edits.
                write.Wait(2000);
            }
            catch (AggregateException)
            {
                // reported below as a failed save
            }
            if (write.IsCompleted) Complete(write);
        }

        /// <summary>A gallery import into this project's <c>media/</c> folder started / ended (no media cleanup meanwhile).</summary>
        internal void BeginImport() => _importsInFlight++;

        internal void EndImport() => _importsInFlight--;

        public void Dispose()
        {
            if (_disposed) return;
            SaveNow();
            _disposed = true;
            // The undo history ends here, so the saved document is the only one that can become current again: imported videos it
            // does not reference (replaced or removed while editing) are deleted now, never during the session (QA round 2: Undo
            // after a replace must find the previous copy). Skipped when the save failed (the file on disk may still reference an
            // older copy) or while an import is still writing into media/.
            if (!IsDirty && LastSaveError == null && _importsInFlight == 0) _owner.DeleteUnreferencedMedia(ProjectId, _edit.Document);
            _owner.OnSessionClosed(this);
        }
    }

    /// <summary>
    /// Project lifecycle for the studio UI (spec §3.9): create from a run or a saved clip ("В видео"), list, open (autosaving
    /// <see cref="StudioProjectSession"/>), duplicate, rename, delete, storage usage, gallery import of the user video and the
    /// export history. Wraps Core <see cref="ProjectLibrary"/>. Main thread.
    /// </summary>
    public sealed class StudioProjects : IDisposable
    {
        readonly StudioServices _services;
        readonly ProjectLibrary _library;
        readonly Dictionary<string, StudioProjectSession> _open = new Dictionary<string, StudioProjectSession>(StringComparer.Ordinal);

        internal StudioProjects(StudioServices services, ProjectLibrary library)
        {
            _services = services;
            _library = library;
            services.Runner.Tick += OnTick;
            if (services.Lifecycle != null) services.Lifecycle.Paused += OnPaused;
        }

        public ProjectLibrary Library => _library;

        public ProjectListResult List() => _library.List();

        /// <summary>Whether "В видео" can be offered for a recent run (its render stream may have been evicted, spec §3.1).</summary>
        public ClipReadiness RunReadiness(string runId) => _services.StreamRepair.Readiness(_library.Clips.GetRun(runId));

        /// <summary>Whether "В видео" can be offered for a saved clip.</summary>
        public ClipReadiness SavedClipReadiness(string clipId) => _services.StreamRepair.Readiness(_library.Clips.GetClip(clipId));

        /// <summary>
        /// "В видео" from a recent run. A run whose render stream was evicted is accepted when it can be rebuilt (the preview
        /// rebuilds it on open); otherwise <see cref="RenderStreamUnavailableException"/> instead of a project that cannot be drawn.
        /// </summary>
        public ProjectDocument CreateFromRun(string runId, string name = null)
        {
            RequireDrawable(RunReadiness(runId), "run " + runId);
            return _library.CreateFromRun(runId, name);
        }

        /// <summary>"В видео" from a saved clip (same rule as <see cref="CreateFromRun"/>).</summary>
        public ProjectDocument CreateFromClip(string clipId, string name = null)
        {
            RequireDrawable(SavedClipReadiness(clipId), "clip " + clipId);
            return _library.CreateFromClip(clipId, name);
        }

        static void RequireDrawable(ClipReadiness readiness, string what)
        {
            if (readiness == ClipReadiness.Missing) throw new InvalidOperationException("No such " + what);
            if (readiness == ClipReadiness.Unavailable)
                throw new RenderStreamUnavailableException("The " + what + " has no render stream and it cannot be rebuilt from its replay");
        }

        /// <summary>Absolute folder of a project.</summary>
        public string ProjectDirectory(string projectId) => _services.Storage.Library.FullPath(_library.Repository.ProjectDir(projectId));

        /// <summary>
        /// Opens a project for editing; returns the same session while it is open (then <paramref name="result"/> describes that
        /// session's load). Null when it cannot be loaded (see <paramref name="result"/>: too new, unavailable, corrupt).
        /// </summary>
        public StudioProjectSession Open(string projectId, out LoadResult result)
        {
            if (_open.TryGetValue(projectId, out StudioProjectSession existing))
            {
                result = new LoadResult(LoadStatus.Ok, existing.Document, existing.LoadedFrom, existing.Document.SchemaVersion, existing.LoadIssues);
                return existing;
            }
            result = _library.Open(projectId);
            if (!result.Success) return null;
            // Leftovers of a session that ended without a clean close (crash, failed save). Only from the main file: a document
            // loaded from the backup may be older than the one the unreadable main file described.
            if (result.Source == LoadSource.Main) DeleteUnreferencedMedia(projectId, result.Document);
            var session = new StudioProjectSession(this, _library.Edit(result.Document), result.Issues, result.Source);
            _open[projectId] = session;
            return session;
        }

        /// <summary>The open session of a project, or null.</summary>
        public StudioProjectSession GetOpen(string projectId)
        {
            return projectId != null && _open.TryGetValue(projectId, out StudioProjectSession s) ? s : null;
        }

        public ProjectDocument Duplicate(string projectId, string newName = null)
        {
            GetOpen(projectId)?.SaveNow();
            return _library.Duplicate(projectId, newName);
        }

        public ProjectDocument Rename(string projectId, string newName)
        {
            StudioProjectSession open = GetOpen(projectId);
            if (open != null)
            {
                open.Apply(d => d.Name = newName);
                open.SaveNow();
                return open.Document;
            }
            return _library.Rename(projectId, newName);
        }

        public bool Delete(string projectId)
        {
            StudioProjectSession open = GetOpen(projectId);
            if (open != null)
            {
                _open.Remove(projectId);
                open.Dispose();
            }
            return _library.Delete(projectId);
        }

        public ProjectStorageUsage StorageUsage(string projectId) => _library.StorageUsage(projectId);

        /// <summary>
        /// Adds a finished export to the history (through the open session when the project is being edited, so two writers never
        /// overlap). Returns the updated document or null.
        /// </summary>
        public ProjectDocument RecordExport(string projectId, StudioExportResult result)
        {
            if (result == null || !result.Succeeded) return null;
            StudioProjectSession open = GetOpen(projectId);
            if (open == null) return _library.RecordExport(projectId, result);
            string at = _library.Clock.NowIso();
            // Not an undoable edit: Undo after an export must not drop the history entry ("Экспортировать снова", spec §3.9).
            open.ApplyMetadata(d => ExportHistory.Add(d, new ExportRecord { At = at, Res = result.Resolution, Fps = result.Fps, Ms = Math.Max(0, result.ElapsedMs), File = result.ProjectFile }));
            open.SaveNow();
            return open.Document;
        }

        /// <summary>
        /// "Видео из галереи" (spec §3.2): system picker → copy into <c>media/</c> of the project (with progress) → probe → the project's
        /// user video is set to the first ≤ 180 s. The change is undoable, so the previous imported copy stays on disk until the
        /// session closes (then every copy the saved document does not reference is deleted, <see cref="DeleteUnreferencedMedia"/>).
        /// </summary>
        public async Task<UserVideoImport> ImportUserVideoAsync(StudioProjectSession session, IProgress<double> progress, CancellationToken ct)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            string mediaDir = Path.Combine(session.Directory, ProjectPaths.MediaDir);
            long freeBefore = SafeFreeSpace();
            session.BeginImport();
            PickResult pick;
            try
            {
                pick = await _services.Media.PickVideoAsync(mediaDir, progress, ct);
            }
            finally
            {
                session.EndImport();
            }
            if (pick.Status != PickStatus.Picked) return new UserVideoImport { Status = pick.Status };
            string relative = ProjectPaths.MediaDir + "/" + Path.GetFileName(pick.Path);
            if (!ProjectPaths.IsSafeMediaPath(relative))
            {
                string safe = ProjectPaths.MediaDir + "/import_" + _services.Ids.NewId() + Path.GetExtension(pick.Path).ToLowerInvariant();
                if (!ProjectPaths.IsSafeMediaPath(safe)) safe = ProjectPaths.MediaDir + "/import_" + _services.Ids.NewId() + ".mp4";
                File.Move(pick.Path, Path.Combine(session.Directory, safe.Replace('/', Path.DirectorySeparatorChar)));
                relative = safe;
            }
            double volume = session.Document.Audio?.User ?? 1.0;
            UserVideoSettings video = UserVideoDefaults.Create(relative, pick.Info.DurationUs, volume);
            session.Apply(d => d.UserVideo = video);
            session.SaveNow();
            long freeAfter = SafeFreeSpace();
            return new UserVideoImport
            {
                Status = PickStatus.Picked,
                Info = pick.Info,
                File = relative,
                LargeFileWarning = ImportPolicy.IsLargeFile(pick.Info.FileSizeBytes, freeBefore, freeAfter),
                IsHdr = pick.Info.IsHdr,
                Trimmed = pick.Info.DurationUs > ProjectLimits.MaxDurationUs,
            };
        }

        /// <summary>
        /// Removes the user video from the project (undoable: the file stays until the session closes, see
        /// <see cref="DeleteUnreferencedMedia"/>).
        /// </summary>
        public void RemoveUserVideo(StudioProjectSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            session.Apply(d => d.UserVideo = null);
            session.SaveNow();
        }

        long SafeFreeSpace()
        {
            try
            {
                return _services.Media.Signals?.FreeSpaceBytes ?? -1;
            }
            catch (Exception e) when (e is MediaException || e is InvalidOperationException)
            {
                return -1;
            }
        }

        /// <summary>
        /// Deletes the files in the project's <c>media/</c> folder that <paramref name="document"/> does not reference
        /// (<see cref="ProjectMediaCleanup"/>) nor any running or resumable export of the project (its job keeps a snapshot of the
        /// document it exports). Call only when no undo / redo snapshot can bring another reference back: at open and at close of
        /// the session.
        /// </summary>
        internal void DeleteUnreferencedMedia(string projectId, ProjectDocument document)
        {
            try
            {
                string projectDir = ProjectDirectory(projectId);
                string mediaDir = Path.Combine(projectDir, ProjectPaths.MediaDir);
                if (!Directory.Exists(mediaDir)) return;
                var files = new List<string>();
                foreach (string full in Directory.GetFiles(mediaDir)) files.Add(ProjectPaths.MediaDir + "/" + Path.GetFileName(full));
                var documents = new List<ProjectDocument> { document };
                IReadOnlyList<ExportResumeState> jobs = _services.Export?.ListResumable();
                if (jobs != null)
                {
                    foreach (ExportResumeState job in jobs)
                    {
                        if (job?.Project != null && string.Equals(job.ProjectId, projectId, StringComparison.Ordinal)) documents.Add(job.Project);
                    }
                }
                foreach (string relative in ProjectMediaCleanup.Unreferenced(documents, files))
                {
                    try
                    {
                        File.Delete(Path.Combine(projectDir, relative.Replace('/', Path.DirectorySeparatorChar)));
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        // an unreferenced file only costs space; the next open retries and the storage screen shows it
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("[Studio] Media cleanup of project " + projectId + " failed: " + e.Message);
            }
        }

        internal void OnSessionClosed(StudioProjectSession session)
        {
            if (_open.TryGetValue(session.ProjectId, out StudioProjectSession s) && ReferenceEquals(s, session)) _open.Remove(session.ProjectId);
        }

        void OnTick(float dt)
        {
            foreach (StudioProjectSession s in _open.Values) s.Tick();
        }

        void OnPaused()
        {
            foreach (StudioProjectSession s in _open.Values) s.OnAppPaused();
        }

        public void Dispose()
        {
            _services.Runner.Tick -= OnTick;
            if (_services.Lifecycle != null) _services.Lifecycle.Paused -= OnPaused;
            var sessions = new List<StudioProjectSession>(_open.Values);
            foreach (StudioProjectSession s in sessions) s.Dispose();
            _open.Clear();
        }
    }
}
