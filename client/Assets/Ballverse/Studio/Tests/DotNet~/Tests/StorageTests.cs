using System;
using System.IO;
using System.Linq;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Tests.Projects;
using Ballverse.Media;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>A unique temp folder per test, deleted afterwards.</summary>
    public sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ballverse-studio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Sub(string name)
        {
            string p = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(p);
            return p;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // a test left a file open; the OS temp cleanup removes it
            }
        }
    }

    /// <summary>The platform store must pass the same contract as the Core reference stores (spec §7.4 atomic write).</summary>
    public sealed class UnityFileStoreContractTests : FileStoreContractTests
    {
        readonly TempFolder _dir = new TempFolder();
        readonly UnityFileStore _store;

        public UnityFileStoreContractTests()
        {
            _store = new UnityFileStore(_dir.Path);
        }

        protected override IFileStore Store => _store;

        public override void Dispose()
        {
            _dir.Dispose();
        }
    }

    public sealed class UnityFileStoreTests : IDisposable
    {
        readonly TempFolder _dir = new TempFolder();

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void PathsStayInsideTheRoot()
        {
            var store = new UnityFileStore(_dir.Path);
            Assert.Throws<ArgumentException>(() => store.FullPath("../x"));
            Assert.Throws<ArgumentException>(() => store.FullPath("a/../../x"));
            Assert.Throws<ArgumentException>(() => store.FullPath(Path.GetFullPath("/etc/passwd")));
            Assert.Throws<ArgumentException>(() => store.FullPath("a\\b"));
            Assert.Throws<ArgumentException>(() => store.DeleteDirectory(""));
            Assert.Equal(store.Root, store.FullPath(""));
            Assert.Equal(Path.Combine(store.Root, "a", "b.txt"), store.FullPath("a/b.txt"));
        }

        [Fact]
        public void InvalidUtf8IsRejectedOnRead()
        {
            var store = new UnityFileStore(_dir.Path);
            store.WriteAllBytes("bad.json", new byte[] { 0x7B, 0xC3, 0x28, 0x7D });
            Assert.ThrowsAny<ArgumentException>(() => store.ReadAllText("bad.json"));
        }

        [Fact]
        public void SuffixCleanupAndSizes()
        {
            var store = new UnityFileStore(_dir.Path);
            store.WriteAllBytes("projects/p1/project.json.tmp", new byte[10]);
            store.WriteAllBytes("projects/p1/project.json", new byte[20]);
            store.WriteAllBytes("projects/p1/media/x.tmp", new byte[5]);
            Assert.Equal(35, store.DirectorySize("projects"));
            Assert.Equal(2, store.DeleteFilesWithSuffix("projects", ".tmp"));
            Assert.Equal(20, store.DirectorySize("projects"));
            Assert.Equal(0, store.DirectorySize("missing"));
            Assert.False(DirectorySync.IsAvailable); // not compiled in outside Android players
        }

        [Fact]
        public void StartupCleanupRemovesScratchAndUnfinishedRecords()
        {
            var storage = new StudioStorage(_dir.Sub("data"), _dir.Sub("runs"), _dir.Sub("export"));
            var clips = new ClipLibrary(storage);
            RunRecording finished = clips.BeginRun();
            File.WriteAllBytes(finished.ReplayPath, new byte[] { 1 });
            finished.Commit("ring_escape", 120);
            RunRecording crashed = clips.BeginRun();
            File.WriteAllBytes(crashed.ReplayPath, new byte[] { 1 });
            storage.Exports.WriteAllBytes("stale/segments/seg_0000.mp4", new byte[] { 1 });
            storage.Exports.WriteAllBytes(StudioStorage.OutputsDir + "/old.mp4", new byte[] { 1 });
            storage.Library.WriteAllText("projects/orphan/media/user.mp4", "x");
            storage.Library.WriteAllText("projects/p1/project.json.tmp", "x");
            string report = storage.CleanupAtStartup(clips.Store);
            Assert.Contains("exports: 2 job folders", report);
            Assert.Contains("runs: 1 unfinished runs", report);
            Assert.Contains("projects: 2 orphan projects", report); // orphan + p1 (temp only, no project.json)
            Assert.Single(clips.ListRuns());
            Assert.False(storage.Exports.DirectoryExists("stale"));
            StudioStorageUsage usage = storage.MeasureUsage();
            Assert.True(usage.RecentRunsBytes > 0);
            Assert.False(usage.ShouldWarn);
        }
    }

    public sealed class ClipLibraryTests : IDisposable
    {
        readonly TempFolder _dir = new TempFolder();

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void RecordSaveAndCreateProjectOnDisk()
        {
            var storage = new StudioStorage(_dir.Sub("data"), _dir.Sub("runs"), _dir.Sub("export"));
            var clips = new ClipLibrary(storage);
            RunRecording run = clips.BeginRun();
            Assert.True(Directory.Exists(run.Directory));
            File.WriteAllBytes(run.ReplayPath, new byte[] { 1, 2 });
            File.WriteAllBytes(run.RenderStreamPath, new byte[1000]);
            File.WriteAllBytes(run.EventsPath, new byte[] { 3 });
            ClipEntry entry = run.Commit("helix_drop", 900, "p2");
            Assert.Throws<InvalidOperationException>(() => run.Commit("helix_drop", 900));
            run.Abandon(); // no-op after a successful commit
            Assert.NotNull(clips.GetRun(run.RunId));
            Assert.Equal(run.Directory, clips.FullDirectory(entry));

            ClipEntry clip = clips.SaveRunAsClip(run.RunId, "Mine");
            Assert.True(File.Exists(Path.Combine(clips.FullDirectory(clip), "clip.bvs")));
            Assert.Equal(1000, new FileInfo(Path.Combine(clips.FullDirectory(clip), "clip.bvs")).Length);

            var library = new ProjectLibrary(clips.Store, clips.Clock, clips.Ids);
            ProjectDocument doc = library.CreateFromClip(clip.Id);
            Assert.Equal("Mine", doc.Name);
            string projectDir = storage.Library.FullPath(library.Repository.ProjectDir(doc.Id));
            Assert.True(File.Exists(Path.Combine(projectDir, "clip.bvr")));
            Assert.True(File.Exists(Path.Combine(projectDir, "project.json")));
            Assert.Equal(900, library.Open(doc.Id).Document.Clip.TrimOutTick);
        }

        [Fact]
        public void AbandonedRunIsDeleted()
        {
            var storage = new StudioStorage(_dir.Sub("data"), _dir.Sub("runs"), _dir.Sub("export"));
            var clips = new ClipLibrary(storage);
            RunRecording run = clips.BeginRun();
            run.Abandon();
            Assert.False(Directory.Exists(run.Directory));
            Assert.Empty(clips.ListRuns());
        }
    }

    public sealed class PcmFileSourceTests : IDisposable
    {
        readonly TempFolder _dir = new TempFolder();

        public void Dispose() => _dir.Dispose();

        [Fact]
        public void ReadsFramesAtAnyPosition()
        {
            var samples = new float[2 * 1000];
            for (int i = 0; i < samples.Length; i++) samples[i] = i;
            string path = Path.Combine(_dir.Path, "a.pcm");
            PcmFile.Write(path, samples, 0, samples.Length);
            using (var src = new PcmFileSource(path))
            {
                Assert.Equal(1000, src.FrameCount);
                var buf = new float[20];
                Assert.Equal(10, src.Read(500, buf));
                Assert.Equal(1000f, buf[0]);
                Assert.Equal(1019f, buf[19]);
                Assert.Equal(4, src.Read(996, buf));
                Assert.Equal(1999f, buf[7]);
                Assert.Equal(0, src.Read(1000, buf));
                Assert.Equal(0, src.Read(-1, buf));
                Assert.Equal(2, src.Read(0, new float[5]));
            }
        }
    }
}
