using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Studio
{
    /// <summary>Copies a file between two stores (they may be the same store).</summary>
    public interface IFileCopier
    {
        /// <summary>Copies <paramref name="fromPath"/> of <paramref name="from"/> to <paramref name="toPath"/> of <paramref name="to"/>
        /// (replacing it, parent folders created) and makes the copy durable.</summary>
        void Copy(IFileStore from, string fromPath, IFileStore to, string toPath);
    }

    /// <summary>
    /// Portable <see cref="IFileCopier"/>: <see cref="IFileStore.Copy"/> inside one store, otherwise read all bytes and write them
    /// (fine for replays and render streams of tens of MB; the Unity layer uses a streaming disk copy instead).
    /// </summary>
    public sealed class BufferedFileCopier : IFileCopier
    {
        public static readonly BufferedFileCopier Instance = new BufferedFileCopier();

        public void Copy(IFileStore from, string fromPath, IFileStore to, string toPath)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            if (ReferenceEquals(from, to)) from.Copy(fromPath, toPath);
            else to.WriteAllBytes(toPath, from.ReadAllBytes(fromPath));
            to.Flush(toPath);
        }
    }

    /// <summary>
    /// Recent runs and the clip library (spec §3.1, §3.9, §7.4) on two <see cref="IFileStore"/>s:
    /// <list type="bullet">
    /// <item><b>Runs</b> (cache store, <c>runs/{runId}/</c>): every simulation run records its replay, render stream and render
    /// events there (<see cref="BeginRun"/> → the simulation writes the files → <see cref="CommitRun"/>). Only the last 5 committed
    /// runs are kept, and their render streams only within 500 MiB (<see cref="RunRetention"/>; replays stay).</item>
    /// <item><b>Saved clips</b> (persistent store, <c>clips/{clipId}/</c>): "Сохранить клип" copies a run there.</item>
    /// <item><b>Projects</b>: "В видео" copies a run or clip into a new project folder (<c>projects/{id}/clip.bvr</c>, <c>clip.bvs</c>,
    /// <c>clip.bve</c>) and creates its <c>project.json</c> last, so an interrupted copy is an orphan folder that
    /// <see cref="ProjectRepository.CleanupOrphans"/> removes.</item>
    /// </list>
    /// Metadata files are written last and atomically: a folder without one is unfinished and is cleaned up. Ids and timestamps
    /// come from the caller (Core is pure). Not thread-safe: use from one thread (the Unity main thread).
    /// </summary>
    public sealed class ClipStore
    {
        readonly IFileStore _runs;
        readonly IFileStore _library;
        readonly IFileCopier _copier;
        readonly ProjectRepository _projects;
        readonly int _maxRuns;
        readonly long _maxStreamBytes;

        /// <param name="runs">Cache store of the recent runs.</param>
        /// <param name="library">Persistent store holding <c>clips/</c> and <c>projects/</c>.</param>
        /// <param name="copier">Cross-store copy; <see cref="BufferedFileCopier"/> when null.</param>
        /// <param name="projects">Project repository on <paramref name="library"/>; created when null.</param>
        public ClipStore(IFileStore runs, IFileStore library, IFileCopier copier = null, ProjectRepository projects = null,
            int maxRuns = RenderStreamRetention.DefaultMaxStreams, long maxStreamBytes = RenderStreamRetention.DefaultMaxTotalBytes)
        {
            _runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _copier = copier ?? BufferedFileCopier.Instance;
            _projects = projects ?? new ProjectRepository(library);
            if (maxRuns < 1) throw new ArgumentOutOfRangeException(nameof(maxRuns));
            if (maxStreamBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxStreamBytes));
            _maxRuns = maxRuns;
            _maxStreamBytes = maxStreamBytes;
        }

        public IFileStore RunStore => _runs;
        public IFileStore LibraryStore => _library;
        public ProjectRepository Projects => _projects;

        // ---------------------------------------------------------------- runs

        /// <summary>Creates the empty folder of a new run; the simulation then writes <see cref="StudioFiles.ClipDataFiles"/> into it.</summary>
        /// <exception cref="InvalidOperationException">The folder exists.</exception>
        public string BeginRun(string runId)
        {
            string dir = StudioFiles.RunDir(runId);
            if (_runs.DirectoryExists(dir)) throw new InvalidOperationException("Run already exists: " + runId);
            _runs.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// Finishes a run: writes its metadata (newest sequence number) and applies the ring-buffer retention. The replay must exist.
        /// </summary>
        /// <exception cref="FileNotFoundException">The run has no replay.</exception>
        public ClipEntry CommitRun(string runId, string modeId, int durationTicks, string nowIso, string winnerId = null)
        {
            string dir = StudioFiles.RunDir(runId);
            if (string.IsNullOrEmpty(modeId)) throw new ArgumentException("mode id required", nameof(modeId));
            if (durationTicks < 1) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            if (!_runs.FileExists(dir + "/" + ProjectPaths.ClipReplayFile)) throw new FileNotFoundException("Run has no replay: " + runId, ProjectPaths.ClipReplayFile);
            long seq = 0;
            foreach (ClipEntry e in ListRuns()) seq = Math.Max(seq, e.Record.Sequence);
            var record = new ClipRecord
            {
                Id = runId,
                ModeId = modeId,
                CreatedAt = nowIso,
                DurationTicks = Math.Min(durationTicks, SimConstants.MaxDurationTicks),
                Sequence = seq + 1,
                WinnerId = winnerId,
            };
            WriteRecord(_runs, dir + "/" + StudioFiles.RunMetaFile, record);
            ApplyRunRetention();
            return Describe(_runs, dir, record);
        }

        /// <summary>Deletes a run that will not be committed (cancelled or failed recording).</summary>
        public void AbandonRun(string runId)
        {
            _runs.DeleteDirectory(StudioFiles.RunDir(runId));
        }

        /// <summary>Committed runs, newest first ("Предыдущие запуски").</summary>
        public IReadOnlyList<ClipEntry> ListRuns()
        {
            var list = new List<ClipEntry>();
            foreach (string id in _runs.ListDirectories(StudioFiles.RunsRoot))
            {
                if (!ProjectPaths.IsSafeId(id)) continue;
                string dir = StudioFiles.RunDir(id);
                ClipRecord r = ReadRecord(_runs, dir + "/" + StudioFiles.RunMetaFile);
                if (r == null || r.Id != id) continue;
                list.Add(Describe(_runs, dir, r));
            }
            list.Sort((a, b) =>
            {
                int c = b.Record.Sequence.CompareTo(a.Record.Sequence);
                if (c != 0) return c;
                c = string.CompareOrdinal(b.Record.CreatedAt, a.Record.CreatedAt);
                return c != 0 ? c : string.CompareOrdinal(b.Id, a.Id);
            });
            return list;
        }

        /// <summary>The committed run <paramref name="runId"/>, or null.</summary>
        public ClipEntry GetRun(string runId)
        {
            if (!ProjectPaths.IsSafeId(runId)) return null;
            string dir = StudioFiles.RunDir(runId);
            ClipRecord r = ReadRecord(_runs, dir + "/" + StudioFiles.RunMetaFile);
            return r == null || r.Id != runId ? null : Describe(_runs, dir, r);
        }

        /// <summary>Deletes runs beyond the ring and evicts render streams beyond the budget (spec §7.4).</summary>
        public RunRetentionPlan ApplyRunRetention()
        {
            RunRetentionPlan plan = RunRetention.Decide(ListRuns(), _maxRuns, _maxStreamBytes);
            foreach (string id in plan.DeleteRuns) _runs.DeleteDirectory(StudioFiles.RunDir(id));
            foreach (string id in plan.EvictStreams)
            {
                string dir = StudioFiles.RunDir(id);
                _runs.DeleteFile(dir + "/" + ProjectPaths.ClipRenderFile);
                _runs.DeleteFile(dir + "/" + StudioFiles.ClipEventsFile);
            }
            return plan;
        }

        /// <summary>
        /// Startup cleanup: deletes run folders without valid metadata (recordings interrupted by a crash) except
        /// <paramref name="activeRunId"/>, then applies the retention. Returns the deleted folder names.
        /// </summary>
        public IReadOnlyList<string> CleanupRuns(string activeRunId = null)
        {
            var deleted = new List<string>();
            foreach (string id in _runs.ListDirectories(StudioFiles.RunsRoot))
            {
                if (id == activeRunId) continue;
                bool valid = ProjectPaths.IsSafeId(id) && GetRun(id) != null;
                if (valid) continue;
                _runs.DeleteDirectory(StudioFiles.RunsRoot + "/" + id);
                deleted.Add(id);
            }
            ApplyRunRetention();
            return deleted;
        }

        // ---------------------------------------------------------------- saved clips

        /// <summary>"Сохранить клип" (spec §3.1): copies a committed run into the clip library.</summary>
        /// <exception cref="InvalidOperationException">The run does not exist or the clip id is taken.</exception>
        public ClipEntry SaveRunAsClip(string runId, string clipId, string nowIso, string name = null)
        {
            ClipEntry run = GetRun(runId) ?? throw new InvalidOperationException("No such run: " + runId);
            string dir = StudioFiles.ClipDir(clipId);
            if (_library.DirectoryExists(dir)) throw new InvalidOperationException("Clip already exists: " + clipId);
            try
            {
                _library.CreateDirectory(dir);
                CopyClipFiles(_runs, run.Directory, _library, dir);
                ClipRecord record = run.Record.Clone();
                record.Id = clipId;
                record.CreatedAt = nowIso;
                record.Sequence = 0;
                record.Name = name;
                record.SourceRunId = runId;
                WriteRecord(_library, dir + "/" + StudioFiles.ClipMetaFile, record);
                return Describe(_library, dir, record);
            }
            catch
            {
                TryDelete(_library, dir);
                throw;
            }
        }

        /// <summary>Saved clips, newest first.</summary>
        public IReadOnlyList<ClipEntry> ListClips()
        {
            var list = new List<ClipEntry>();
            foreach (string id in _library.ListDirectories(StudioFiles.ClipsRoot))
            {
                ClipEntry e = GetClip(id);
                if (e != null) list.Add(e);
            }
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(b.Record.CreatedAt, a.Record.CreatedAt);
                return c != 0 ? c : string.CompareOrdinal(b.Id, a.Id);
            });
            return list;
        }

        /// <summary>The saved clip <paramref name="clipId"/>, or null.</summary>
        public ClipEntry GetClip(string clipId)
        {
            if (!ProjectPaths.IsSafeId(clipId)) return null;
            string dir = StudioFiles.ClipDir(clipId);
            ClipRecord r = ReadRecord(_library, dir + "/" + StudioFiles.ClipMetaFile);
            return r == null || r.Id != clipId ? null : Describe(_library, dir, r);
        }

        /// <summary>Renames a saved clip (atomic metadata rewrite).</summary>
        public ClipEntry RenameClip(string clipId, string name)
        {
            ClipEntry e = GetClip(clipId) ?? throw new InvalidOperationException("No such clip: " + clipId);
            ClipRecord r = e.Record.Clone();
            r.Name = name;
            WriteRecord(_library, e.Directory + "/" + StudioFiles.ClipMetaFile, r);
            return Describe(_library, e.Directory, r);
        }

        public bool DeleteClip(string clipId)
        {
            string dir = StudioFiles.ClipDir(clipId);
            if (!_library.DirectoryExists(dir)) return false;
            _library.DeleteDirectory(dir);
            return true;
        }

        /// <summary>Deletes clip folders without valid metadata (a save interrupted by a crash). Returns the deleted names.</summary>
        public IReadOnlyList<string> CleanupClips()
        {
            var deleted = new List<string>();
            foreach (string id in _library.ListDirectories(StudioFiles.ClipsRoot))
            {
                if (ProjectPaths.IsSafeId(id) && GetClip(id) != null) continue;
                _library.DeleteDirectory(StudioFiles.ClipsRoot + "/" + id);
                deleted.Add(id);
            }
            return deleted;
        }

        // ---------------------------------------------------------------- projects

        /// <summary>"В видео" from a recent run (spec §3.1): new project with the run as its clip.</summary>
        public ProjectDocument CreateProjectFromRun(string runId, string projectId, string name, string nowIso)
        {
            ClipEntry run = GetRun(runId) ?? throw new InvalidOperationException("No such run: " + runId);
            return CreateProject(_runs, run, projectId, name, nowIso);
        }

        /// <summary>"В видео" from a saved clip (spec §3.9).</summary>
        public ProjectDocument CreateProjectFromClip(string clipId, string projectId, string name, string nowIso)
        {
            ClipEntry clip = GetClip(clipId) ?? throw new InvalidOperationException("No such clip: " + clipId);
            return CreateProject(_library, clip, projectId, name, nowIso);
        }

        ProjectDocument CreateProject(IFileStore source, ClipEntry clip, string projectId, string name, string nowIso)
        {
            ProjectPaths.RequireSafeId(projectId, nameof(projectId));
            string dir = _projects.ProjectDir(projectId);
            if (_library.DirectoryExists(dir)) throw new InvalidOperationException("Project already exists: " + projectId);
            try
            {
                _library.CreateDirectory(dir);
                CopyClipFiles(source, clip.Directory, _library, dir);
                ProjectDocument doc = ProjectDefaults.NewProject(projectId, string.IsNullOrEmpty(name) ? ProjectDefaults.UntitledName : name,
                    nowIso, clip.Record.ModeId, clip.Record.DurationTicks);
                return _projects.Create(doc);
            }
            catch
            {
                TryDelete(_library, dir);
                throw;
            }
        }

        // ---------------------------------------------------------------- helpers

        void CopyClipFiles(IFileStore from, string fromDir, IFileStore to, string toDir)
        {
            foreach (string file in StudioFiles.ClipDataFiles)
            {
                string src = fromDir + "/" + file;
                if (!from.FileExists(src))
                {
                    if (file == ProjectPaths.ClipReplayFile) throw new FileNotFoundException("Clip has no replay: " + fromDir, file);
                    continue; // render stream / events are a rebuildable cache (spec §7.4)
                }
                _copier.Copy(from, src, to, toDir + "/" + file);
            }
        }

        static ClipEntry Describe(IFileStore store, string dir, ClipRecord record)
        {
            long replay = Size(store, dir + "/" + ProjectPaths.ClipReplayFile, out bool hasReplay);
            long stream = Size(store, dir + "/" + ProjectPaths.ClipRenderFile, out bool hasStream);
            long events = Size(store, dir + "/" + StudioFiles.ClipEventsFile, out bool hasEvents);
            return new ClipEntry(record, dir, hasReplay, hasStream, hasEvents, replay, stream + events);
        }

        static long Size(IFileStore store, string path, out bool exists)
        {
            exists = false;
            try
            {
                if (!store.FileExists(path)) return 0;
                long size = store.GetFileSize(path);
                exists = true;
                return size;
            }
            catch (IOException)
            {
                return 0;
            }
        }

        static ClipRecord ReadRecord(IFileStore store, string path)
        {
            try
            {
                return store.FileExists(path) ? ClipRecord.TryParse(store.ReadAllText(path)) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        static void WriteRecord(IFileStore store, string path, ClipRecord record)
        {
            string temp = path + StudioFiles.TempSuffix;
            store.WriteAllText(temp, record.ToJson());
            store.Flush(temp);
            store.Replace(temp, path);
        }

        static void TryDelete(IFileStore store, string dir)
        {
            try
            {
                store.DeleteDirectory(dir);
            }
            catch (IOException)
            {
                // best effort: startup cleanup removes the folder later
            }
            catch (UnauthorizedAccessException)
            {
                // best effort
            }
        }
    }
}
