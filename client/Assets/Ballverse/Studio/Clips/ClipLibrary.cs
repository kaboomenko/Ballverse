using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;

namespace Ballverse.Studio
{
    /// <summary>
    /// A run being recorded into the recent-runs ring (spec §3.1): the simulation writes its replay, render stream and render
    /// events to the absolute paths below, then calls <see cref="Commit"/> (or <see cref="Abandon"/>). Single use.
    /// </summary>
    public sealed class RunRecording
    {
        readonly ClipLibrary _owner;
        bool _closed;

        internal RunRecording(ClipLibrary owner, string runId, string directory)
        {
            _owner = owner;
            RunId = runId;
            Directory = directory;
        }

        public string RunId { get; }
        /// <summary>Absolute folder of the run.</summary>
        public string Directory { get; }
        /// <summary>Where the replay writer writes (<c>clip.bvr</c>, the source of truth).</summary>
        public string ReplayPath => Path.Combine(Directory, ProjectPaths.ClipReplayFile);
        /// <summary>Where the render stream writer writes (<c>clip.bvs</c>).</summary>
        public string RenderStreamPath => Path.Combine(Directory, ProjectPaths.ClipRenderFile);
        /// <summary>Where the render event log goes (<c>clip.bve</c>, Render module format).</summary>
        public string EventsPath => Path.Combine(Directory, StudioFiles.ClipEventsFile);
        public bool IsClosed => _closed;

        /// <summary>The run finished: it joins the ring (oldest runs / streams evicted). The replay file must exist.</summary>
        public ClipEntry Commit(string modeId, int durationTicks, string winnerId = null)
        {
            if (_closed) throw new InvalidOperationException("recording already closed");
            _closed = true;
            return _owner.Store.CommitRun(RunId, modeId, durationTicks, _owner.Clock.NowIso(), winnerId);
        }

        /// <summary>The run is thrown away (cancelled, failed). Safe to call after a failed <see cref="Commit"/>.</summary>
        public void Abandon()
        {
            if (_closed && _owner.Store.GetRun(RunId) != null) return;
            _closed = true;
            _owner.Store.AbandonRun(RunId);
        }
    }

    /// <summary>
    /// Recent runs and saved clips for the UI (spec §3.1 "В видео", "Сохранить клип", "Предыдущие запуски"; §3.9 "Библиотека клипов"):
    /// Unity-side façade over Core <see cref="ClipStore"/> with real ids and clocks and absolute paths for the recorders. The last
    /// 5 runs keep their replay and render stream (render streams within 500 MB, <c>RenderStreamRetention</c>); a saved clip is a
    /// copy in the persistent library; "В видео" copies a run or clip into a new project (<see cref="StudioProjects"/>).
    /// Main thread (file copies of a clip take tens of ms for the render stream).
    /// </summary>
    public sealed class ClipLibrary
    {
        readonly StudioStorage _storage;

        public ClipLibrary(StudioStorage storage, IStudioClock clock = null, IIdSource ids = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            Clock = clock ?? StudioClock.Instance;
            Ids = ids ?? GuidIdSource.Instance;
            Store = new ClipStore(storage.Runs, storage.Library, DiskFileCopier.Instance, new ProjectRepository(storage.Library));
        }

        public ClipStore Store { get; }
        public IStudioClock Clock { get; }
        public IIdSource Ids { get; }

        /// <summary>Starts recording a new run.</summary>
        public RunRecording BeginRun()
        {
            string id = Ids.NewId();
            string dir = Store.BeginRun(id);
            return new RunRecording(this, id, _storage.Runs.FullPath(dir));
        }

        /// <summary>The ring, newest first.</summary>
        public IReadOnlyList<ClipEntry> ListRuns() => Store.ListRuns();

        public ClipEntry GetRun(string runId) => Store.GetRun(runId);

        /// <summary>"Сохранить клип": copies a run into the library.</summary>
        public ClipEntry SaveRunAsClip(string runId, string name = null)
        {
            return Store.SaveRunAsClip(runId, Ids.NewId(), Clock.NowIso(), name);
        }

        public IReadOnlyList<ClipEntry> ListClips() => Store.ListClips();

        public ClipEntry GetClip(string clipId) => Store.GetClip(clipId);

        public ClipEntry RenameClip(string clipId, string name) => Store.RenameClip(clipId, name);

        public bool DeleteClip(string clipId) => Store.DeleteClip(clipId);

        /// <summary>Absolute folder of a run or clip entry (holds clip.bvr / clip.bvs / clip.bve).</summary>
        public string FullDirectory(ClipEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            bool isRun = entry.Directory.StartsWith(StudioFiles.RunsRoot + "/", StringComparison.Ordinal);
            return (isRun ? _storage.Runs : _storage.Library).FullPath(entry.Directory);
        }
    }
}
