using System;
using System.Collections.Generic;
using System.IO;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Project folders on an <see cref="IFileStore"/> (spec §7.4 "Проект", §3.9, §8 ST6).
    /// <para>Save is atomic: temp file -> flush -> previous good version copied to <c>project.json.bak</c> (via its own
    /// temp + flush + atomic replace) -> atomic replace of <c>project.json</c>. After a failure at any single step, Load
    /// returns either the previous or the new document, never a broken one.</para>
    /// <para>Load parses, migrates and sanitizes; a missing main file or one with unusable content falls back to the backup
    /// (reported via <see cref="LoadResult.Source"/>). A file from a newer schema, or one that cannot be read because of an I/O
    /// error (<see cref="LoadStatus.Unavailable"/>), is refused without fallback, so the newer main file is never silently
    /// replaced by an older backup and then erased by the next saves (spec §8 ST6).</para>
    /// Ids and timestamps are always passed in by the caller (Core is pure).
    /// </summary>
    public sealed class ProjectRepository
    {
        readonly IFileStore _store;
        readonly string _root;
        readonly ProjectMigrator _migrator;

        /// <param name="store">File store.</param>
        /// <param name="root">Folder holding the project folders (spec: "projects").</param>
        /// <param name="migrator">Schema migrator; <see cref="ProjectMigrator.Default"/> when null.</param>
        public ProjectRepository(IFileStore store, string root = ProjectPaths.ProjectsRoot, ProjectMigrator migrator = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (!ProjectPaths.IsSafeRelativePath(root)) throw new ArgumentException("Unsafe root: " + root, nameof(root));
            _root = root;
            _migrator = migrator ?? ProjectMigrator.Default;
        }

        /// <summary>Store path of the folder of project <paramref name="id"/>.</summary>
        public string ProjectDir(string id)
        {
            return ProjectPaths.ProjectDir(id, _root);
        }

        /// <summary>Store path of a file inside the folder of project <paramref name="id"/>.</summary>
        public string FilePath(string id, string relativePath)
        {
            return ProjectPaths.InProject(id, relativePath, _root);
        }

        /// <summary>True when the project folder exists (even if broken).</summary>
        public bool Exists(string id)
        {
            return _store.DirectoryExists(ProjectDir(id));
        }

        /// <summary>
        /// Atomically writes <paramref name="document"/> (sanitized first; timestamps are not touched).
        /// Throws <see cref="ArgumentException"/> when the document has unrepairable errors,
        /// <see cref="ProjectMigrationException"/> when the existing file is from a newer schema,
        /// <see cref="IOException"/> when the existing file cannot be read (nothing is written, so it is not lost),
        /// and the store's exception when a write fails (the previous version then stays loadable).
        /// </summary>
        /// <returns>The sanitized document that was written and the warnings applied.</returns>
        public SanitizeResult Save(ProjectDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var sanitized = ProjectValidator.Sanitize(document);
            if (sanitized.HasErrors)
            {
                throw new ArgumentException("Project cannot be saved: " + FirstError(sanitized.Issues), nameof(document));
            }
            var doc = sanitized.Document;
            string id = doc.Id;
            string dir = ProjectDir(id);
            string main = FilePath(id, ProjectPaths.ProjectFile);
            string temp = FilePath(id, ProjectPaths.TempFile);
            string bak = FilePath(id, ProjectPaths.BackupFile);
            string bakTemp = FilePath(id, ProjectPaths.BackupTempFile);
            string json = ProjectJson.Serialize(doc);

            bool rotateBackup = false;
            if (_store.FileExists(main))
            {
                var current = TryLoadFile(id, main, new List<ValidationIssue>());
                if (current.Status == LoadStatus.TooNew)
                {
                    throw new ProjectMigrationException(MigrationError.TooNew, current.FileVersion,
                        "Refusing to overwrite " + main + " written by a newer schema.");
                }
                if (current.Status == LoadStatus.Unavailable)
                {
                    // Without reading it we cannot tell whether it is the newest good version; keep it and let the caller retry.
                    throw new IOException("Cannot read " + main + "; save aborted so the existing version is kept.", current.Error);
                }
                // Only a good main becomes the backup; a corrupt main must not replace a good backup.
                rotateBackup = current.Status == LoadStatus.Ok;
            }

            if (!_store.DirectoryExists(dir)) _store.CreateDirectory(dir);
            _store.WriteAllText(temp, json);
            _store.Flush(temp);
            if (rotateBackup)
            {
                _store.Copy(main, bakTemp);
                _store.Flush(bakTemp);
                _store.Replace(bakTemp, bak);
            }
            _store.Replace(temp, main);
            return sanitized;
        }

        /// <summary>Loads project <paramref name="id"/>: parse, migrate, sanitize, falling back to the backup.</summary>
        public LoadResult Load(string id)
        {
            ProjectPaths.RequireSafeId(id, nameof(id));
            var issues = new List<ValidationIssue>();
            string main = FilePath(id, ProjectPaths.ProjectFile);
            string bak = FilePath(id, ProjectPaths.BackupFile);

            var mainIssues = new List<ValidationIssue>();
            var first = TryLoadFile(id, main, mainIssues);
            if (first.Status == LoadStatus.Ok)
            {
                return new LoadResult(LoadStatus.Ok, first.Document, LoadSource.Main, first.FileVersion, mainIssues);
            }
            issues.AddRange(mainIssues);
            if (first.Status == LoadStatus.TooNew || first.Status == LoadStatus.Unavailable)
            {
                return new LoadResult(first.Status, null, LoadSource.None, first.FileVersion, issues);
            }

            var bakIssues = new List<ValidationIssue>();
            var second = TryLoadFile(id, bak, bakIssues);
            if (second.Status == LoadStatus.Ok)
            {
                issues.Add(new ValidationIssue(main, IssueCode.UsedBackup, IssueSeverity.Warning,
                    first.Status == LoadStatus.NotFound ? "Main file missing; backup used." : "Main file unusable; backup used."));
                issues.AddRange(bakIssues);
                return new LoadResult(LoadStatus.Ok, second.Document, LoadSource.Backup, second.FileVersion, issues);
            }
            issues.AddRange(bakIssues);
            if (second.Status == LoadStatus.TooNew || second.Status == LoadStatus.Unavailable)
            {
                return new LoadResult(second.Status, null, LoadSource.None, second.FileVersion, issues);
            }
            var status = first.Status == LoadStatus.NotFound && second.Status == LoadStatus.NotFound ? LoadStatus.NotFound : LoadStatus.Corrupt;
            return new LoadResult(status, null, LoadSource.None, -1, issues);
        }

        /// <summary>
        /// Enumerates project folders (spec §3.9 project list). Broken folders, folders with unsafe names, unreadable ones and
        /// projects from a newer schema are skipped and reported in <see cref="ProjectListResult.Skipped"/>. A folder with neither
        /// project.json nor its backup (interrupted create or duplicate) is reported as <see cref="IssueCode.Orphan"/>
        /// (warning); <see cref="CleanupOrphans"/> removes such folders.
        /// </summary>
        public ProjectListResult List()
        {
            var projects = new List<ProjectSummary>();
            var skipped = new List<ValidationIssue>();
            foreach (var name in _store.ListDirectories(_root))
            {
                string path = _root + "/" + name;
                if (!ProjectPaths.IsSafeId(name))
                {
                    skipped.Add(new ValidationIssue(path, IssueCode.InvalidId, IssueSeverity.Error, "Folder name is not a safe project id."));
                    continue;
                }
                LoadResult r;
                try
                {
                    r = Load(name);
                }
                catch (Exception e)
                {
                    skipped.Add(new ValidationIssue(path, IssueCode.Corrupt, IssueSeverity.Error, "Load failed: " + e.Message));
                    continue;
                }
                if (!r.Success)
                {
                    if (r.Status == LoadStatus.NotFound)
                    {
                        skipped.Add(new ValidationIssue(path, IssueCode.Orphan, IssueSeverity.Warning, "Folder without project.json or backup."));
                        continue;
                    }
                    var code = r.Status == LoadStatus.TooNew ? IssueCode.TooNew : r.Status == LoadStatus.Unavailable ? IssueCode.Unavailable : IssueCode.Corrupt;
                    skipped.Add(new ValidationIssue(path, code, IssueSeverity.Error, "Project skipped: " + r.Status + "."));
                    continue;
                }
                var d = r.Document;
                projects.Add(new ProjectSummary(d.Id, d.Name, d.CreatedAt, d.UpdatedAt, d.Clip.ModeId, ProjectTiming.ReelDurationUs(d), r.Source));
            }
            projects.Sort((a, b) =>
            {
                ProjectValidator.TryParseTimestamp(a.UpdatedAt, out var ta);
                ProjectValidator.TryParseTimestamp(b.UpdatedAt, out var tb);
                int c = tb.UtcTicks.CompareTo(ta.UtcTicks);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return new ProjectListResult(projects, skipped);
        }

        /// <summary>
        /// Deletes project folders that hold neither project.json nor project.json.bak (status <see cref="LoadStatus.NotFound"/>,
        /// both confirmed missing without I/O errors): leftovers of a create or duplicate interrupted by a crash, which never
        /// became a project. Call at app start (as for temporary export files, spec §3.9), never while a create, duplicate or
        /// media import runs. Folders with unsafe names are left alone.
        /// </summary>
        /// <returns>Ids of the removed folders, in name order.</returns>
        public IReadOnlyList<string> CleanupOrphans()
        {
            var removed = new List<string>();
            foreach (var name in _store.ListDirectories(_root))
            {
                if (!ProjectPaths.IsSafeId(name)) continue;
                if (Load(name).Status != LoadStatus.NotFound) continue;
                _store.DeleteDirectory(ProjectDir(name));
                removed.Add(name);
            }
            return removed;
        }

        /// <summary>
        /// Creates and saves a new project with default settings (<see cref="ProjectDefaults.NewProject"/>). The clip mode
        /// defaults to the first catalog mode until the caller sets the recorded clip and saves again.
        /// Throws <see cref="InvalidOperationException"/> when the folder already holds a project.
        /// </summary>
        public ProjectDocument Create(string id, string name, string nowIso)
        {
            return Create(ProjectDefaults.NewProject(id, name, nowIso, ModeCatalog.All[0].Id));
        }

        /// <summary>Saves a new, fully formed project; throws <see cref="InvalidOperationException"/> if its folder already holds one.</summary>
        public ProjectDocument Create(ProjectDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ProjectPaths.RequireSafeId(document.Id, nameof(document));
            RequireTimestamp(document.CreatedAt, nameof(document));
            if (_store.FileExists(FilePath(document.Id, ProjectPaths.ProjectFile)) || _store.FileExists(FilePath(document.Id, ProjectPaths.BackupFile)))
            {
                throw new InvalidOperationException("Project already exists: " + document.Id);
            }
            return Save(document).Document;
        }

        /// <summary>
        /// Copies project <paramref name="sourceId"/> with all its media to <paramref name="newId"/> (spec §3.9 "дублировать").
        /// The copy gets new timestamps, an empty export history and the source name unless <paramref name="newName"/> is given;
        /// exported videos referenced by the source history are therefore not copied.
        /// project.json is written last, so an interrupted duplicate is never listed as a project; partial copies are removed,
        /// and a folder left by a crash is reported as <see cref="IssueCode.Orphan"/> and removed by <see cref="CleanupOrphans"/>.
        /// </summary>
        public ProjectDocument Duplicate(string sourceId, string newId, string nowIso, string newName = null)
        {
            ProjectPaths.RequireSafeId(sourceId, nameof(sourceId));
            ProjectPaths.RequireSafeId(newId, nameof(newId));
            RequireTimestamp(nowIso, nameof(nowIso));
            var src = Load(sourceId);
            if (!src.Success) throw new InvalidOperationException("Cannot duplicate " + sourceId + ": " + src.Status);
            string newDir = ProjectDir(newId);
            if (_store.DirectoryExists(newDir)) throw new InvalidOperationException("Project already exists: " + newId);

            var doc = ProjectJson.Clone(src.Document);
            doc.Id = newId;
            if (newName != null) doc.Name = newName;
            doc.CreatedAt = nowIso;
            doc.UpdatedAt = nowIso;
            doc.Exports = new List<ExportRecord>();
            // Membership only; never enumerated.
            var skip = new HashSet<string>(StringComparer.Ordinal) { ProjectPaths.ProjectFile, ProjectPaths.BackupFile, ProjectPaths.TempFile, ProjectPaths.BackupTempFile };
            foreach (var e in src.Document.Exports)
            {
                if (e.File != null) skip.Add(e.File);
            }
            // Files the copy still references are always copied, even if a (hand-edited) history entry names them.
            if (doc.Clip?.Replay != null) skip.Remove(doc.Clip.Replay);
            if (doc.UserVideo?.File != null) skip.Remove(doc.UserVideo.File);
            try
            {
                _store.CreateDirectory(newDir);
                CopyTree(ProjectDir(sourceId), newDir, "", skip);
                return Save(doc).Document;
            }
            catch
            {
                TryDeleteDirectory(newDir);
                throw;
            }
        }

        /// <summary>Renames a project and sets <c>updatedAt</c> (spec §3.9 "переименовать").</summary>
        public ProjectDocument Rename(string id, string newName, string nowIso)
        {
            RequireTimestamp(nowIso, nameof(nowIso));
            var r = Load(id);
            if (!r.Success) throw new InvalidOperationException("Cannot rename " + id + ": " + r.Status);
            var doc = r.Document;
            doc.Name = newName;
            doc.UpdatedAt = nowIso;
            return Save(doc).Document;
        }

        /// <summary>
        /// Bytes used by the folder of project <paramref name="id"/>, by category (spec §3.9 "Хранилище": projects, clips, user
        /// videos, thumbnail cache). Categories follow the §7.4 layout and, when the project loads, its <c>clip.replay</c>,
        /// <c>userVideo.file</c> and <c>exports[].file</c> references (the default <c>clip.bvr</c> otherwise).
        /// </summary>
        public ProjectStorageUsage GetStorageUsage(string id)
        {
            string dir = ProjectDir(id);
            var r = Load(id);
            string userVideo = r.Success && r.Document.UserVideo != null ? r.Document.UserVideo.File : null;
            string replay = r.Success && r.Document.Clip?.Replay != null ? r.Document.Clip.Replay : ProjectPaths.ClipReplayFile;
            var exports = new HashSet<string>(StringComparer.Ordinal); // membership only
            if (r.Success)
            {
                foreach (var e in r.Document.Exports)
                {
                    if (e.File != null) exports.Add(e.File);
                }
            }
            var usage = new long[(int)StorageCategory.Other + 1];
            AddUsage(dir, "", replay, userVideo, exports, usage);
            return new ProjectStorageUsage(usage);
        }

        /// <summary>Deletes the whole project folder (spec §3.9 "удалить"). Returns false when it did not exist.</summary>
        public bool Delete(string id)
        {
            string dir = ProjectDir(id);
            if (!_store.DirectoryExists(dir)) return false;
            _store.DeleteDirectory(dir);
            return true;
        }

        // ---- internals ----

        struct FileAttempt
        {
            public LoadStatus Status;
            public ProjectDocument Document;
            public int FileVersion;
            public Exception Error;
        }

        FileAttempt TryLoadFile(string id, string path, List<ValidationIssue> issues)
        {
            var attempt = new FileAttempt { Status = LoadStatus.Corrupt, FileVersion = -1 };
            try
            {
                if (!_store.FileExists(path))
                {
                    attempt.Status = LoadStatus.NotFound;
                    return attempt;
                }
                var obj = ProjectJson.ParseObject(_store.ReadAllText(path));
                var migrated = _migrator.Migrate(obj);
                attempt.FileVersion = migrated.FromVersion;
                if (!migrated.Success)
                {
                    bool tooNew = migrated.Error == MigrationError.TooNew;
                    attempt.Status = tooNew ? LoadStatus.TooNew : LoadStatus.Corrupt;
                    issues.Add(new ValidationIssue(path, tooNew ? IssueCode.TooNew : IssueCode.Corrupt, IssueSeverity.Error, migrated.Message));
                    return attempt;
                }
                var doc = ProjectJson.ToDocument(migrated.Document);
                var sanitized = ProjectValidator.Sanitize(doc, id);
                if (sanitized.HasErrors)
                {
                    issues.Add(new ValidationIssue(path, IssueCode.Corrupt, IssueSeverity.Error, "Unrepairable: " + FirstError(sanitized.Issues)));
                    return attempt;
                }
                issues.AddRange(sanitized.Issues);
                attempt.Status = LoadStatus.Ok;
                attempt.Document = sanitized.Document;
                return attempt;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // The store could not read the file: maybe temporary, says nothing about its content.
                issues.Add(new ValidationIssue(path, IssueCode.Unavailable, IssueSeverity.Error, e.GetType().Name + ": " + e.Message));
                attempt.Status = LoadStatus.Unavailable;
                attempt.Document = null;
                attempt.Error = e;
                return attempt;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // Non-UTF-8, malformed JSON or wrong value types: the file content is unusable.
                issues.Add(new ValidationIssue(path, IssueCode.Corrupt, IssueSeverity.Error, e.GetType().Name + ": " + e.Message));
                attempt.Status = LoadStatus.Corrupt;
                attempt.Document = null;
                return attempt;
            }
        }

        /// <summary>Copies the tree under <paramref name="from"/>, skipping files whose project-relative path is in <paramref name="skip"/>.</summary>
        void CopyTree(string from, string to, string relPrefix, HashSet<string> skip)
        {
            foreach (var file in _store.ListFiles(from))
            {
                if (skip.Contains(relPrefix + file)) continue;
                string dst = to + "/" + file;
                _store.Copy(from + "/" + file, dst);
                _store.Flush(dst);
            }
            foreach (var dir in _store.ListDirectories(from))
            {
                _store.CreateDirectory(to + "/" + dir);
                CopyTree(from + "/" + dir, to + "/" + dir, relPrefix + dir + "/", skip);
            }
        }

        void AddUsage(string dir, string relPrefix, string replay, string userVideo, HashSet<string> exports, long[] usage)
        {
            foreach (var file in _store.ListFiles(dir))
            {
                usage[(int)Categorize(relPrefix + file, replay, userVideo, exports)] += _store.GetFileSize(dir + "/" + file);
            }
            foreach (var sub in _store.ListDirectories(dir))
            {
                AddUsage(dir + "/" + sub, relPrefix + sub + "/", replay, userVideo, exports, usage);
            }
        }

        static StorageCategory Categorize(string rel, string replay, string userVideo, HashSet<string> exports)
        {
            if (rel == ProjectPaths.ProjectFile || rel == ProjectPaths.BackupFile || rel == ProjectPaths.TempFile || rel == ProjectPaths.BackupTempFile)
            {
                return StorageCategory.ProjectFile;
            }
            if (rel == replay) return StorageCategory.Replay;
            if (rel == ProjectPaths.ClipRenderFile) return StorageCategory.RenderCache;
            if (rel == userVideo || rel.StartsWith(ProjectPaths.MediaDir + "/", StringComparison.Ordinal)) return StorageCategory.Media;
            if (rel.StartsWith(ProjectPaths.ThumbsDir + "/", StringComparison.Ordinal)) return StorageCategory.Thumbs;
            if (exports.Contains(rel) || rel.StartsWith(ProjectPaths.ExportsDir + "/", StringComparison.Ordinal)) return StorageCategory.Exports;
            return StorageCategory.Other;
        }

        void TryDeleteDirectory(string dir)
        {
            try
            {
                _store.DeleteDirectory(dir);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                // Best effort: a leftover folder without project.json is reported by List as Orphan and removed by CleanupOrphans.
            }
        }

        static void RequireTimestamp(string value, string paramName)
        {
            if (!ProjectValidator.IsTimestamp(value)) throw new ArgumentException("Not an ISO-8601 timestamp: '" + value + "'", paramName);
        }

        static string FirstError(IReadOnlyList<ValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == IssueSeverity.Error) return issues[i].ToString();
            }
            return "unknown error";
        }
    }
}
