using System;
using System.IO;
using System.Linq;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Repository behaviour around I/O errors (no fallback that could later erase the newest version, spec §8 ST6),
    /// interrupted duplicates (orphans), duplicate of export files and storage usage (spec §3.9).
    /// </summary>
    public class ProjectRepositoryRecoveryTests
    {
        const string Main = "projects/p1/project.json";
        const string Bak = "projects/p1/project.json.bak";

        static (InMemoryFileStore store, ProjectRepository repo) NewRepo()
        {
            var store = new InMemoryFileStore();
            return (store, new ProjectRepository(store));
        }

        static string NameIn(InMemoryFileStore store, string path)
        {
            return ProjectJson.Deserialize(store.ReadAllText(path)).Name;
        }

        // ---- transient I/O errors ----

        [Fact]
        public void TransientReadFailureNeverLosesTheNewestVersion()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2")); // main = v2 (newest), bak = v1
            store.FailWhen((op, path) => op == FileOp.ReadText && path == Main);

            // No silent fallback to the older backup.
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Unavailable, r.Status);
            Assert.Null(r.Document);

            // A save while main cannot be read is refused and changes nothing.
            int before = store.MutationCount;
            Assert.Throws<IOException>(() => repo.Save(Doc("p1", "stale edit")));
            Assert.Equal(before, store.MutationCount);
            store.ClearFailures();
            Assert.Equal("v2", NameIn(store, Main));
            Assert.Equal("v1", NameIn(store, Bak));

            // Once readable again, editing continues from v2 and two autosaves keep v2 as the backup of the first edit.
            var doc = repo.Load("p1").Document;
            Assert.Equal("v2", doc.Name);
            doc.Texts[0].Text = "edit 1";
            repo.Save(doc);
            Assert.Equal("v2", NameIn(store, Bak));
            doc.Texts[0].Text = "edit 2";
            repo.Save(doc);
            Assert.Equal("edit 2", repo.Load("p1").Document.Texts[0].Text);
        }

        [Fact]
        public void ExistsCheckFailureIsUnavailable()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1"));
            store.FailWhen((op, path) => op == FileOp.FileExists && path == Main);
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Unavailable, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.Unavailable);
        }

        [Fact]
        public void UnreadableBackupBehindCorruptMainIsUnavailableNotCorrupt()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            store.WriteAllText(Main, "{ garbage");
            store.FailWhen((op, path) => op == FileOp.ReadText && path == Bak);
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Unavailable, r.Status);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.Corrupt && i.Path == Main);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.Unavailable && i.Path == Bak);
            store.ClearFailures();
            var again = repo.Load("p1");
            Assert.Equal(LoadSource.Backup, again.Source);
            Assert.Equal("v1", again.Document.Name);
        }

        [Fact]
        public void CorruptContentStillFallsBackToBackup()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            store.WriteAllBytes(Main, new byte[] { 0xFF, 0xFE, 0x00 }); // not UTF-8
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Ok, r.Status);
            Assert.Equal(LoadSource.Backup, r.Source);
            // Saving over a corrupt main keeps the good backup.
            repo.Save(r.Document);
            Assert.Equal("v1", NameIn(store, Bak));
        }

        [Fact]
        public void ListReportsUnreadableProjects()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1"));
            repo.Save(Doc("p2"));
            store.FailWhen((op, path) => op == FileOp.ReadText && path.StartsWith("projects/p2/", StringComparison.Ordinal));
            var list = repo.List();
            Assert.Equal(new[] { "p1" }, list.Projects.Select(p => p.Id));
            var skipped = Assert.Single(list.Skipped);
            Assert.Equal("projects/p2", skipped.Path);
            Assert.Equal(IssueCode.Unavailable, skipped.Code);
            Assert.Empty(repo.CleanupOrphans()); // never deleted
            store.ClearFailures();
            Assert.Equal(2, repo.List().Projects.Count);
        }

        // ---- orphans ----

        [Fact]
        public void CleanupOrphansRemovesOnlyFoldersWithoutProjectFiles()
        {
            var (store, repo) = NewRepo();
            repo.Create("good", "Good", Now);
            store.WriteAllText("projects/broken/project.json", "{");
            var tooNew = ProjectJson.ParseObject(ProjectJson.Serialize(Doc("future")));
            tooNew["schemaVersion"] = 9;
            store.WriteAllText("projects/future/project.json", tooNew.ToString());
            store.WriteAllBytes("projects/orphan/media/user.mp4", new byte[10]);
            store.WriteAllText("projects/tmponly/project.json.tmp", ProjectJson.Serialize(Doc("tmponly")));
            store.CreateDirectory("projects/empty");
            store.WriteAllBytes("projects/bad.name/x.bin", new byte[1]);
            repo.Save(Doc("bakonly", "B"));
            repo.Save(Doc("bakonly", "B"));
            store.DeleteFile("projects/bakonly/project.json");

            var list = repo.List();
            Assert.Equal(new[] { "projects/empty", "projects/orphan", "projects/tmponly" },
                list.Skipped.Where(i => i.Code == IssueCode.Orphan).Select(i => i.Path));
            Assert.All(list.Skipped.Where(i => i.Code == IssueCode.Orphan), i => Assert.Equal(IssueSeverity.Warning, i.Severity));

            Assert.Equal(new[] { "empty", "orphan", "tmponly" }, repo.CleanupOrphans());
            Assert.False(store.DirectoryExists("projects/orphan"));
            Assert.True(store.DirectoryExists("projects/broken"));
            Assert.True(store.DirectoryExists("projects/future"));
            Assert.True(store.DirectoryExists("projects/bad.name"));
            Assert.Equal(LoadStatus.Ok, repo.Load("good").Status);
            Assert.Equal(LoadSource.Backup, repo.Load("bakonly").Source);
            Assert.DoesNotContain(repo.List().Skipped, i => i.Code == IssueCode.Orphan);
            Assert.Empty(repo.CleanupOrphans());
        }

        [Theory]
        [InlineData(FailureKind.Throw)]
        [InlineData(FailureKind.TornWrite)]
        [InlineData(FailureKind.Crash)]
        public void DuplicateInterruptedWithoutCleanupLeavesOnlyARemovableOrphan(FailureKind kind)
        {
            var baseline = new InMemoryFileStore();
            var baseRepo = new ProjectRepository(baseline);
            baseRepo.Save(Doc("p1", "Source"));
            baseRepo.Save(Doc("p1", "Source"));
            baseline.WriteAllBytes("projects/p1/clip.bvr", new byte[] { 1, 2 });
            baseline.WriteAllBytes("projects/p1/media/user.mp4", new byte[1000]);
            baseline.WriteAllBytes("projects/p1/thumbs/0.jpg", new byte[] { 3 });
            foreach (var f in baseline.AllFiles()) baseline.Flush(f);
            var sourceFiles = baseline.AllFiles().ToArray();

            var probe = baseline.Clone();
            int start = probe.MutationCount;
            new ProjectRepository(probe).Duplicate("p1", "p2", Later);
            int steps = probe.MutationCount - start;

            for (int i = 0; i < steps; i++)
            {
                var store = baseline.Clone();
                var repo = new ProjectRepository(store);
                store.FailAtMutation(store.MutationCount + i, kind);
                // The process dies: the in-process cleanup never runs (modelled by DeleteDirectory failing).
                store.FailWhen((op, p) => op == FileOp.DeleteDirectory);
                Assert.ThrowsAny<Exception>(() => repo.Duplicate("p1", "p2", Later));
                store.SimulateCrash();
                store.ClearFailures();

                Assert.Equal(LoadStatus.NotFound, repo.Load("p2").Status);
                var list = repo.List();
                Assert.Equal(new[] { "p1" }, list.Projects.Select(p => p.Id));
                Assert.All(list.Skipped, s => Assert.Equal(IssueCode.Orphan, s.Code));
                bool leftover = store.DirectoryExists("projects/p2");
                Assert.Equal(leftover ? new[] { "p2" } : new string[0], repo.CleanupOrphans());
                Assert.False(store.DirectoryExists("projects/p2"), "step " + i);
                Assert.Equal(sourceFiles, store.AllFiles());
                Assert.Equal("Source", repo.Load("p1").Document.Name);

                // A retry succeeds.
                Assert.Equal("p2", repo.Duplicate("p1", "p2", Later).Id);
            }
        }

        // ---- duplicate and exports ----

        [Fact]
        public void DuplicateSkipsExportedFilesOfTheClearedHistory()
        {
            var (store, repo) = NewRepo();
            var d = Doc("p1");
            d.Exports[0].File = "exports/out_1.mp4";
            repo.Save(d);
            store.WriteAllBytes("projects/p1/exports/out_1.mp4", new byte[4096]);
            store.WriteAllBytes("projects/p1/exports/notes.txt", new byte[] { 1 }); // not referenced by the history: copied
            store.WriteAllBytes("projects/p1/media/user.mp4", new byte[] { 2 });

            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Empty(copy.Exports);
            Assert.False(store.FileExists("projects/p2/exports/out_1.mp4"));
            Assert.True(store.FileExists("projects/p2/exports/notes.txt"));
            Assert.True(store.FileExists("projects/p2/media/user.mp4"));
            Assert.True(store.FileExists("projects/p1/exports/out_1.mp4"));
        }

        // ---- storage usage (spec §3.9) ----

        [Fact]
        public void StorageUsageByCategory()
        {
            var (store, repo) = NewRepo();
            var d = Doc("p1");
            d.UserVideo.File = "import/user.mov";
            d.Exports[0].File = "out/final.mp4";
            repo.Save(d);
            long projectBytes = store.GetFileSize(Main);
            store.WriteAllBytes("projects/p1/clip.bvr", new byte[100]);
            store.WriteAllBytes("projects/p1/clip.bvs", new byte[200]);
            store.WriteAllBytes("projects/p1/import/user.mov", new byte[3000]);
            store.WriteAllBytes("projects/p1/media/extra.mp4", new byte[5]);
            store.WriteAllBytes("projects/p1/thumbs/0.jpg", new byte[40]);
            store.WriteAllBytes("projects/p1/thumbs/cover.jpg", new byte[2]);
            store.WriteAllBytes("projects/p1/out/final.mp4", new byte[700]);
            store.WriteAllBytes("projects/p1/exports/old.mp4", new byte[70]);
            store.WriteAllBytes("projects/p1/misc.bin", new byte[9]);

            var u = repo.GetStorageUsage("p1");
            Assert.Equal(projectBytes, u[StorageCategory.ProjectFile]);
            Assert.Equal(100, u[StorageCategory.Replay]);
            Assert.Equal(200, u[StorageCategory.RenderCache]);
            Assert.Equal(3005, u[StorageCategory.Media]);
            Assert.Equal(42, u[StorageCategory.Thumbs]);
            Assert.Equal(770, u[StorageCategory.Exports]);
            Assert.Equal(9, u[StorageCategory.Other]);
            Assert.Equal(projectBytes + 100 + 200 + 3005 + 42 + 770 + 9, u.TotalBytes);
        }

        [Fact]
        public void StorageUsageFollowsDocumentReplayPath()
        {
            var (store, repo) = NewRepo();
            var d = Doc("p1");
            d.Clip.Replay = "replays/run1.bvr";
            repo.Save(d);
            store.WriteAllBytes("projects/p1/replays/run1.bvr", new byte[500]);
            store.WriteAllBytes("projects/p1/clip.bvr", new byte[7]); // not the project's replay any more
            var u = repo.GetStorageUsage("p1");
            Assert.Equal(500, u[StorageCategory.Replay]);
            Assert.Equal(7, u[StorageCategory.Other]);
        }

        [Fact]
        public void StorageUsageOfUnloadableProjectUsesDefaultReplay()
        {
            var (store, repo) = NewRepo();
            store.WriteAllText("projects/x/project.json", "{");
            store.WriteAllBytes("projects/x/clip.bvr", new byte[11]);
            Assert.Equal(11, repo.GetStorageUsage("x")[StorageCategory.Replay]);
        }

        [Theory]
        [InlineData("clip.bvr")]
        [InlineData("media/user.mp4")]
        public void DuplicateCopiesMediaEvenWhenAnExportRecordNamesIt(string exportFile)
        {
            var (store, repo) = NewRepo();
            var d = Doc("p1");
            d.Exports.Add(new ExportRecord { At = Now, Res = 1080, Fps = 60, Ms = 1000, File = exportFile });
            repo.Save(d);
            store.WriteAllBytes("projects/p1/clip.bvr", new byte[] { 1, 2, 3 });
            store.WriteAllBytes("projects/p1/media/user.mp4", new byte[100]);
            store.WriteAllBytes("projects/p1/exports/out_1.mp4", new byte[9]);
            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Equal("clip.bvr", copy.Clip.Replay);
            Assert.Equal("media/user.mp4", copy.UserVideo.File);
            Assert.True(store.FileExists("projects/p2/clip.bvr"));
            Assert.True(store.FileExists("projects/p2/media/user.mp4"));
            Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadAllBytes("projects/p2/clip.bvr"));
            Assert.Equal(LoadStatus.Ok, repo.Load("p2").Status);
        }

        [Fact]
        public void StorageUsageOfBrokenOrMissingProject()
        {
            var (store, repo) = NewRepo();
            store.WriteAllText("projects/x/project.json", "{");
            store.WriteAllBytes("projects/x/media/user.mp4", new byte[10]);
            var u = repo.GetStorageUsage("x");
            Assert.Equal(1, u[StorageCategory.ProjectFile]);
            Assert.Equal(10, u[StorageCategory.Media]);
            Assert.Equal(0, repo.GetStorageUsage("missing").TotalBytes);
            Assert.Throws<ArgumentException>(() => repo.GetStorageUsage("../x"));
        }
    }
}
