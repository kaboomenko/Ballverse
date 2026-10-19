using System;
using System.IO;
using System.Linq;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class ClipStoreTests
    {
        const string Now = "2026-10-18T12:00:00Z";

        readonly InMemoryFileStore _runs = new InMemoryFileStore();
        readonly InMemoryFileStore _library = new InMemoryFileStore();

        ClipStore Store(int maxRuns = 5, long maxStreamBytes = 500L * 1024 * 1024)
        {
            return new ClipStore(_runs, _library, null, null, maxRuns, maxStreamBytes);
        }

        static string Iso(int minute) => "2026-10-18T12:" + minute.ToString("D2") + ":00Z";

        ClipEntry Record(ClipStore store, string id, int streamBytes = 100, bool withStream = true, int minute = 0)
        {
            string dir = store.BeginRun(id);
            _runs.WriteAllBytes(dir + "/clip.bvr", new byte[] { 1, 2, 3 });
            if (withStream)
            {
                _runs.WriteAllBytes(dir + "/clip.bvs", new byte[streamBytes]);
                _runs.WriteAllBytes(dir + "/clip.bve", new byte[] { 9 });
            }
            return store.CommitRun(id, "ring_escape", 600, Iso(minute), "red");
        }

        [Fact]
        public void CommittedRunsAreListedNewestFirstWithSequence()
        {
            var store = Store();
            Record(store, "r1");
            Record(store, "r2");
            ClipEntry third = Record(store, "r3");
            Assert.Equal(3, third.Record.Sequence);
            Assert.Equal(new[] { "r3", "r2", "r1" }, store.ListRuns().Select(e => e.Id));
            Assert.True(third.HasReplay && third.HasRenderStream && third.HasEvents);
            Assert.Equal(3, third.ReplayBytes);
            Assert.Equal(101, third.StreamBytes);
            Assert.Equal("red", third.Record.WinnerId);
        }

        [Fact]
        public void RingKeepsOnlyTheLastFiveRuns()
        {
            var store = Store();
            for (int i = 1; i <= 7; i++) Record(store, "r" + i);
            Assert.Equal(new[] { "r7", "r6", "r5", "r4", "r3" }, store.ListRuns().Select(e => e.Id));
            Assert.False(_runs.DirectoryExists("runs/r1"));
            Assert.False(_runs.DirectoryExists("runs/r2"));
        }

        [Fact]
        public void StreamsBeyondTheBudgetAreEvictedButReplaysStay()
        {
            var store = Store(5, 250);
            Record(store, "r1", 100);
            Record(store, "r2", 100);
            Record(store, "r3", 100); // newest three = 303 bytes > 250: r1 loses its stream
            var runs = store.ListRuns();
            Assert.Equal(new[] { "r3", "r2", "r1" }, runs.Select(e => e.Id));
            ClipEntry r1 = runs.Single(e => e.Id == "r1");
            Assert.True(r1.HasReplay);
            Assert.False(r1.HasRenderStream);
            Assert.False(r1.HasEvents);
            Assert.True(runs.Single(e => e.Id == "r3").HasRenderStream);
        }

        [Fact]
        public void NewestStreamIsKeptEvenAboveTheBudget()
        {
            var store = Store(5, 10);
            Record(store, "big", 1000);
            Assert.True(store.GetRun("big").HasRenderStream);
        }

        [Fact]
        public void CommitWithoutReplayThrowsAndBeginTwiceThrows()
        {
            var store = Store();
            store.BeginRun("r1");
            Assert.Throws<FileNotFoundException>(() => store.CommitRun("r1", "ring_escape", 60, Now));
            Assert.Throws<InvalidOperationException>(() => store.BeginRun("r1"));
            Assert.Throws<ArgumentException>(() => store.BeginRun("../x"));
        }

        [Fact]
        public void CleanupRemovesUnfinishedRunsExceptTheActiveOne()
        {
            var store = Store();
            Record(store, "done");
            store.BeginRun("crashed");
            _runs.WriteAllBytes("runs/crashed/clip.bvr", new byte[] { 1 });
            store.BeginRun("active");
            _runs.WriteAllText("runs/garbage/run.json", "{not json");
            var deleted = store.CleanupRuns("active");
            Assert.Equal(new[] { "crashed", "garbage" }, deleted.OrderBy(x => x, StringComparer.Ordinal));
            Assert.True(_runs.DirectoryExists("runs/active"));
            Assert.True(_runs.DirectoryExists("runs/done"));
        }

        [Fact]
        public void AbandonDeletesTheRun()
        {
            var store = Store();
            store.BeginRun("r1");
            store.AbandonRun("r1");
            Assert.False(_runs.DirectoryExists("runs/r1"));
        }

        [Fact]
        public void SaveRunAsClipCopiesFilesIntoTheLibrary()
        {
            var store = Store();
            Record(store, "r1", 50);
            ClipEntry clip = store.SaveRunAsClip("r1", "c1", Iso(5), "Best run");
            Assert.Equal("clips/c1", clip.Directory);
            Assert.Equal(new byte[] { 1, 2, 3 }, _library.ReadAllBytes("clips/c1/clip.bvr"));
            Assert.Equal(50, _library.ReadAllBytes("clips/c1/clip.bvs").Length);
            Assert.True(_library.FileExists("clips/c1/clip.bve"));
            Assert.Equal("Best run", clip.Record.Name);
            Assert.Equal("r1", clip.Record.SourceRunId);
            Assert.Equal(0, clip.Record.Sequence);
            Assert.Equal(600, clip.Record.DurationTicks);
            Assert.Throws<InvalidOperationException>(() => store.SaveRunAsClip("r1", "c1", Iso(6)));
            Assert.Throws<InvalidOperationException>(() => store.SaveRunAsClip("missing", "c2", Iso(6)));
        }

        [Fact]
        public void ClipWithoutStreamIsSavedFromReplayOnly()
        {
            var store = Store();
            Record(store, "r1", withStream: false);
            ClipEntry clip = store.SaveRunAsClip("r1", "c1", Iso(5));
            Assert.True(clip.HasReplay);
            Assert.False(clip.HasRenderStream);
        }

        [Fact]
        public void ClipsAreListedNewestFirstAndCanBeRenamedAndDeleted()
        {
            var store = Store();
            Record(store, "r1");
            store.SaveRunAsClip("r1", "a", Iso(1));
            store.SaveRunAsClip("r1", "b", Iso(3));
            store.SaveRunAsClip("r1", "c", Iso(2));
            Assert.Equal(new[] { "b", "c", "a" }, store.ListClips().Select(c => c.Id));
            Assert.Equal("New", store.RenameClip("c", "New").Record.Name);
            Assert.Equal("New", store.GetClip("c").Record.Name);
            Assert.True(store.DeleteClip("c"));
            Assert.False(store.DeleteClip("c"));
            Assert.Null(store.GetClip("c"));
        }

        [Fact]
        public void FailedClipCopyLeavesNoFolder()
        {
            var store = Store();
            Record(store, "r1");
            _library.FailWhen((op, path) => op == FileOp.WriteBytes && path.EndsWith("clip.bvs", StringComparison.Ordinal));
            Assert.ThrowsAny<IOException>(() => store.SaveRunAsClip("r1", "c1", Iso(1)));
            _library.ClearFailures();
            Assert.False(_library.DirectoryExists("clips/c1"));
        }

        [Fact]
        public void CleanupClipsRemovesFoldersWithoutMetadata()
        {
            var store = Store();
            Record(store, "r1");
            store.SaveRunAsClip("r1", "good", Iso(1));
            _library.WriteAllBytes("clips/half/clip.bvr", new byte[] { 1 });
            Assert.Equal(new[] { "half" }, store.CleanupClips());
            Assert.NotNull(store.GetClip("good"));
        }

        [Fact]
        public void ProjectFromRunHasTheClipFilesAndDefaults()
        {
            var store = Store();
            Record(store, "r1", 77);
            ProjectDocument doc = store.CreateProjectFromRun("r1", "p1", "Ring Escape 1", Now);
            Assert.Equal("p1", doc.Id);
            Assert.Equal("ring_escape", doc.Clip.ModeId);
            Assert.Equal(600, doc.Clip.TrimOutTick);
            Assert.Equal(77, _library.ReadAllBytes("projects/p1/clip.bvs").Length);
            Assert.True(_library.FileExists("projects/p1/clip.bvr"));
            Assert.True(_library.FileExists("projects/p1/clip.bve"));
            LoadResult loaded = store.Projects.Load("p1");
            Assert.True(loaded.Success);
            Assert.Equal("Ring Escape 1", loaded.Document.Name);
            Assert.Throws<InvalidOperationException>(() => store.CreateProjectFromRun("r1", "p1", "x", Now));
        }

        [Fact]
        public void ProjectFromClipAndUntitledName()
        {
            var store = Store();
            Record(store, "r1");
            store.SaveRunAsClip("r1", "c1", Iso(1));
            ProjectDocument doc = store.CreateProjectFromClip("c1", "p2", null, Now);
            Assert.Equal(ProjectDefaults.UntitledName, doc.Name);
            Assert.True(_library.FileExists("projects/p2/clip.bvr"));
        }

        [Fact]
        public void FailedProjectCopyLeavesNoFolder()
        {
            var store = Store();
            Record(store, "r1");
            _library.FailWhen((op, path) => op == FileOp.WriteText && path.Contains("project.json"));
            Assert.ThrowsAny<IOException>(() => store.CreateProjectFromRun("r1", "p1", "x", Now));
            _library.ClearFailures();
            Assert.False(_library.DirectoryExists("projects/p1"));
        }

        [Fact]
        public void RecordParsingRejectsBadData()
        {
            Assert.Null(ClipRecord.TryParse(null));
            Assert.Null(ClipRecord.TryParse("{"));
            Assert.Null(ClipRecord.TryParse("{\"id\":\"../x\",\"modeId\":\"m\",\"durationTicks\":5}"));
            Assert.Null(ClipRecord.TryParse("{\"id\":\"a\",\"modeId\":\"m\",\"durationTicks\":0}"));
            Assert.Null(ClipRecord.TryParse("{\"schemaVersion\":99,\"id\":\"a\",\"modeId\":\"m\",\"durationTicks\":5}"));
            ClipRecord r = ClipRecord.TryParse("{\"id\":\"a\",\"modeId\":\"m\",\"durationTicks\":999999}");
            Assert.Equal(SimConstants.MaxDurationTicks, r.DurationTicks);
            ClipRecord round = ClipRecord.TryParse(r.ToJson());
            Assert.Equal("a", round.Id);
        }
    }
}
