using System;
using System.Collections.Generic;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>Wall clock and monotonic time for the studio (Core is pure: the Unity layer passes the real clocks).</summary>
    public interface IStudioClock
    {
        /// <summary>Current UTC time as ISO-8601 ("2026-10-18T12:00:00Z").</summary>
        string NowIso();

        /// <summary>Monotonic milliseconds (autosave debounce, export timing).</summary>
        long NowMs();
    }

    /// <summary>Source of new folder ids (<see cref="ProjectPaths.IsSafeId"/>), e.g. GUIDs without dashes.</summary>
    public interface IIdSource
    {
        string NewId();
    }

    /// <summary>
    /// Project lifecycle for the studio UI (spec §3.9): create from a recent run or a saved clip ("В видео"), list, open,
    /// duplicate, rename, delete, storage usage, export history and "Экспортировать снова" settings. Wraps
    /// <see cref="ProjectRepository"/> and <see cref="ClipStore"/>; ids and time come from the injected sources. Not thread-safe:
    /// call from one thread (the Unity main thread; the autosave write itself may run on a worker, see <see cref="ProjectEditSession"/>).
    /// </summary>
    public sealed class ProjectLibrary
    {
        readonly ClipStore _clips;
        readonly IStudioClock _clock;
        readonly IIdSource _ids;

        public ProjectLibrary(ClipStore clips, IStudioClock clock, IIdSource ids)
        {
            _clips = clips ?? throw new ArgumentNullException(nameof(clips));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        }

        public ClipStore Clips => _clips;
        public ProjectRepository Repository => _clips.Projects;
        public IStudioClock Clock => _clock;

        /// <summary>Projects for the grid, newest change first (broken folders are reported in <see cref="ProjectListResult.Skipped"/>).</summary>
        public ProjectListResult List()
        {
            return Repository.List();
        }

        /// <summary>"В видео" from a recent run: new project named after the mode ("Ring Escape 3") unless <paramref name="name"/> is given.</summary>
        public ProjectDocument CreateFromRun(string runId, string name = null)
        {
            ClipEntry run = _clips.GetRun(runId) ?? throw new InvalidOperationException("No such run: " + runId);
            return _clips.CreateProjectFromRun(runId, NewProjectId(), name ?? DefaultName(run.Record.ModeId), _clock.NowIso());
        }

        /// <summary>"В видео" from a saved clip.</summary>
        public ProjectDocument CreateFromClip(string clipId, string name = null)
        {
            ClipEntry clip = _clips.GetClip(clipId) ?? throw new InvalidOperationException("No such clip: " + clipId);
            return _clips.CreateProjectFromClip(clipId, NewProjectId(), name ?? clip.Record.Name ?? DefaultName(clip.Record.ModeId), _clock.NowIso());
        }

        /// <summary>Loads a project (migrated and sanitized, backup fallback).</summary>
        public LoadResult Open(string projectId)
        {
            return Repository.Load(projectId);
        }

        /// <summary>Starts editing an opened document with autosave and undo.</summary>
        public ProjectEditSession Edit(ProjectDocument document)
        {
            return new ProjectEditSession(Repository, document, _clock);
        }

        public ProjectDocument Duplicate(string projectId, string newName = null)
        {
            return Repository.Duplicate(projectId, NewProjectId(), _clock.NowIso(), newName);
        }

        public ProjectDocument Rename(string projectId, string newName)
        {
            return Repository.Rename(projectId, newName, _clock.NowIso());
        }

        public bool Delete(string projectId)
        {
            return Repository.Delete(projectId);
        }

        public ProjectStorageUsage StorageUsage(string projectId)
        {
            return Repository.GetStorageUsage(projectId);
        }

        /// <summary>
        /// Appends a finished export to the project's history (spec §3.9) and remembers its resolution and fps for the next
        /// export, then saves. Returns the saved document, or null when the project no longer loads (deleted meanwhile).
        /// </summary>
        public ProjectDocument RecordExport(string projectId, StudioExportResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!result.Succeeded) return null;
            LoadResult r = Repository.Load(projectId);
            if (!r.Success) return null;
            ProjectDocument doc = r.Document;
            ExportHistory.Add(doc, new ExportRecord
            {
                At = _clock.NowIso(),
                Res = result.Resolution,
                Fps = result.Fps,
                Ms = Math.Max(0, result.ElapsedMs),
                File = result.ProjectFile,
            });
            return Repository.Save(doc).Document;
        }

        /// <summary>
        /// Settings for "Экспортировать снова" (spec §3.9): the given resolution / fps, else the last export's, else the defaults
        /// (<see cref="ProjectComposition.ResolveExportSettings"/>). No simulation is re-run: the clip's render stream is reused.
        /// </summary>
        public static ExportSettings ReExportSettings(ProjectDocument doc, DeviceTier tier, ExportBitrateTable bitrates = null,
            ExportResolution? resolution = null, int? fps = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            ExportRecord last = ExportHistory.Latest(doc);
            ExportResolution? res = resolution ?? (last != null ? ProjectComposition.ResolutionOf(last.Res) : (ExportResolution?)null);
            int? f = fps ?? (last != null && OutputFps.IsValid(last.Fps) ? last.Fps : (int?)null);
            return ProjectComposition.ResolveExportSettings(doc.Output, tier, bitrates, res, f);
        }

        /// <summary>Default project name from the mode ("Ring Escape"), plus a number when the name is taken.</summary>
        public string DefaultName(string modeId)
        {
            string baseName = ModeCatalog.TryGetById(modeId ?? string.Empty, out ModeInfo info) ? info.NameEn : ProjectDefaults.UntitledName;
            var taken = new HashSet<string>(StringComparer.Ordinal); // membership only
            foreach (ProjectSummary p in Repository.List().Projects) taken.Add(p.Name ?? string.Empty);
            if (!taken.Contains(baseName)) return baseName;
            for (int n = 2; n < 10000; n++)
            {
                string candidate = baseName + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate)) return candidate;
            }
            return baseName;
        }

        string NewProjectId()
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string id = _ids.NewId();
                if (ProjectPaths.IsSafeId(id) && !Repository.Exists(id)) return id;
            }
            throw new InvalidOperationException("Id source returned no usable id");
        }
    }

    /// <summary>
    /// One open project in the studio (spec §3.9 "Автосохранение после каждого изменения (с задержкой 1 с) и при уходе приложения в
    /// фон", §3.4 "Отмена / повтор 50 шагов"): edits go through <see cref="Apply"/>, which records an undo step and marks the
    /// document dirty for the <see cref="AutosaveScheduler"/>. Saving is split so the Unity layer can write on a worker thread:
    /// <see cref="TryBeginSave"/> hands out an immutable snapshot and a token, the write runs, then <see cref="CompleteSave"/> or
    /// <see cref="FailSave"/>. <see cref="SaveNow"/> writes synchronously (app going to the background). Not thread-safe except
    /// that the snapshot may be written from any thread.
    /// </summary>
    public sealed class ProjectEditSession
    {
        /// <summary>Undo depth (spec §3.4).</summary>
        public const int MaxUndoSteps = 50;
        /// <summary>Edits with the same coalesce key within this time form one undo step (slider drags), ms.</summary>
        public const long CoalesceMs = 1000;

        readonly ProjectRepository _repository;
        readonly IStudioClock _clock;
        readonly AutosaveScheduler _autosave = new AutosaveScheduler();
        readonly LinkedList<string> _undo = new LinkedList<string>();
        readonly LinkedList<string> _redo = new LinkedList<string>();
        string _lastKey;
        long _lastKeyMs;
        ProjectDocument _document;

        internal ProjectEditSession(ProjectRepository repository, ProjectDocument document, IStudioClock clock)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (document == null) throw new ArgumentNullException(nameof(document));
            _document = ProjectJson.Clone(document);
        }

        /// <summary>The current document. Treat as read-only; change it through <see cref="Apply"/>.</summary>
        public ProjectDocument Document => _document;

        public string ProjectId => _document.Id;

        /// <summary>Incremented by every applied edit, undo and redo (the preview rebuilds its plan when it changes).</summary>
        public int Revision { get; private set; }

        public bool IsDirty => _autosave.IsDirty;
        public bool IsSaving => _autosave.IsSaving;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Last save error (null after a successful save).</summary>
        public Exception LastSaveError { get; private set; }

        /// <summary>
        /// Applies an edit: <paramref name="edit"/> mutates a copy of the document, which is sanitized (values clamped like on load)
        /// and becomes current; an undo step is recorded unless the same <paramref name="coalesceKey"/> was used within
        /// <see cref="CoalesceMs"/>. Returns the sanitize warnings. Throws <see cref="ArgumentException"/> (document unchanged) when the
        /// edit makes the document unrepairable.
        /// </summary>
        public IReadOnlyList<ValidationIssue> Apply(Action<ProjectDocument> edit, string coalesceKey = null)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            long now = _clock.NowMs();
            string before = ProjectJson.Serialize(_document);
            ProjectDocument copy = ProjectJson.Clone(_document);
            edit(copy);
            copy.Id = _document.Id;
            copy.UpdatedAt = _clock.NowIso();
            SanitizeResult sanitized = ProjectValidator.Sanitize(copy, _document.Id);
            if (sanitized.HasErrors) throw new ArgumentException("edit makes the project invalid", nameof(edit));
            bool coalesce = coalesceKey != null && coalesceKey == _lastKey && now - _lastKeyMs <= CoalesceMs && _undo.Count > 0;
            if (!coalesce) Push(_undo, before);
            _redo.Clear();
            _lastKey = coalesceKey;
            _lastKeyMs = now;
            _document = sanitized.Document;
            Revision++;
            _autosave.MarkChanged(now);
            return sanitized.Issues;
        }

        /// <summary>
        /// Applies a metadata change that is not a user edit (an export added to the history, spec §3.9): it changes the current
        /// document and every undo / redo snapshot alike, so Undo never takes it back, and records no undo step. Marks the
        /// document dirty. Throws <see cref="ArgumentException"/> (nothing changed) when the change makes the document unrepairable.
        /// </summary>
        public void ApplyMetadata(Action<ProjectDocument> change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            ProjectDocument current = Changed(_document, change);
            if (current == null) throw new ArgumentException("change makes the project invalid", nameof(change));
            RewriteSnapshots(_undo, change);
            RewriteSnapshots(_redo, change);
            current.UpdatedAt = _clock.NowIso();
            _document = current;
            Revision++;
            _autosave.MarkChanged(_clock.NowMs());
        }

        ProjectDocument Changed(ProjectDocument doc, Action<ProjectDocument> change)
        {
            ProjectDocument copy = ProjectJson.Clone(doc);
            change(copy);
            copy.Id = _document.Id;
            SanitizeResult sanitized = ProjectValidator.Sanitize(copy, _document.Id);
            return sanitized.HasErrors ? null : sanitized.Document;
        }

        void RewriteSnapshots(LinkedList<string> list, Action<ProjectDocument> change)
        {
            for (LinkedListNode<string> node = list.First; node != null; node = node.Next)
            {
                ProjectDocument changed = Changed(ProjectJson.Deserialize(node.Value), change);
                if (changed != null) node.Value = ProjectJson.Serialize(changed);
            }
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            Push(_redo, ProjectJson.Serialize(_document));
            Restore(_undo);
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            Push(_undo, ProjectJson.Serialize(_document));
            Restore(_redo);
            return true;
        }

        /// <summary>
        /// When an autosave is due, returns true with a snapshot to write (<see cref="WriteSnapshot"/>) and its token; the caller then
        /// reports <see cref="CompleteSave"/> / <see cref="FailSave"/>. False while clean, inside the debounce or while a save runs.
        /// </summary>
        public bool TryBeginSave(out ProjectDocument snapshot, out long token)
        {
            snapshot = null;
            token = -1;
            if (!_autosave.ShouldSave(_clock.NowMs())) return false;
            token = _autosave.BeginSave();
            snapshot = ProjectJson.Clone(_document);
            return true;
        }

        /// <summary>Writes a snapshot (safe on a worker thread while the session keeps editing).</summary>
        public void WriteSnapshot(ProjectDocument snapshot)
        {
            _repository.Save(snapshot);
        }

        public void CompleteSave(long token)
        {
            LastSaveError = null;
            _autosave.CompleteSave(token);
        }

        public void FailSave(long token, Exception error)
        {
            LastSaveError = error;
            _autosave.FailSave(token, _clock.NowMs());
        }

        /// <summary>
        /// Synchronous save, e.g. when the app goes to the background (spec §3.9). Saves only when there are unsaved changes. Returns
        /// false when nothing needed saving; rethrows the store's exception after recording it.
        /// </summary>
        public bool SaveNow()
        {
            if (!_autosave.IsDirty) return false;
            if (_autosave.IsSaving)
            {
                // A worker save is writing the same files: never write concurrently. Its snapshot covers the edits up to its
                // start; later edits are saved right after it (the scheduler's due time moves to now).
                _autosave.OnAppBackgrounded(_clock.NowMs());
                return false;
            }
            long token = _autosave.BeginSave();
            try
            {
                _repository.Save(ProjectJson.Clone(_document));
                CompleteSave(token);
                return true;
            }
            catch (Exception e)
            {
                FailSave(token, e);
                throw;
            }
        }

        void Restore(LinkedList<string> from)
        {
            string json = from.Last.Value;
            from.RemoveLast();
            ProjectDocument doc = ProjectJson.Deserialize(json);
            doc.UpdatedAt = _clock.NowIso();
            _document = doc;
            _lastKey = null;
            Revision++;
            _autosave.MarkChanged(_clock.NowMs());
        }

        static void Push(LinkedList<string> list, string json)
        {
            list.AddLast(json);
            while (list.Count > MaxUndoSteps) list.RemoveFirst();
        }
    }

    /// <summary>Defaults of a freshly imported user video (spec §3.2, §3.4).</summary>
    public static class UserVideoDefaults
    {
        /// <summary>
        /// Video settings for an imported file <paramref name="relativeFile"/> (inside the project folder) of
        /// <paramref name="sourceDurationUs"/>: the first ≤ 180 s, no offset, cover fill, volume from the project's audio block.
        /// </summary>
        public static UserVideoSettings Create(string relativeFile, long sourceDurationUs, double volume = 1.0)
        {
            if (!ProjectPaths.IsSafeMediaPath(relativeFile)) throw new ArgumentException("Unsafe media path: " + relativeFile, nameof(relativeFile));
            long duration = Math.Max(1, Math.Min(sourceDurationUs, ProjectLimits.MaxSourceVideoUs));
            return new UserVideoSettings
            {
                File = relativeFile,
                TrimInUs = 0,
                TrimOutUs = Math.Min(duration, ProjectLimits.MaxDurationUs),
                OffsetUs = 0,
                Volume = volume,
            };
        }
    }
}
