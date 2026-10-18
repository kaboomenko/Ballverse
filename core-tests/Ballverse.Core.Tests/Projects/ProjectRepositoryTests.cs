using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Projects;
using Newtonsoft.Json.Linq;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectRepositoryTests
    {
        const string Main = "projects/p1/project.json";
        const string Bak = "projects/p1/project.json.bak";

        static (InMemoryFileStore store, ProjectRepository repo) NewRepo()
        {
            var store = new InMemoryFileStore();
            return (store, new ProjectRepository(store));
        }

        [Fact]
        public void SaveThenLoadRoundTrips()
        {
            var (store, repo) = NewRepo();
            var doc = Doc("p1");
            var saved = repo.Save(doc);
            Assert.True(saved.IsClean);
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Ok, r.Status);
            Assert.Equal(LoadSource.Main, r.Source);
            Assert.Empty(r.Issues);
            Assert.False(r.Migrated);
            Assert.Equal(ProjectJson.Serialize(doc), ProjectJson.Serialize(r.Document));
            Assert.Equal(new[] { Main }, store.AllFiles());
        }

        [Fact]
        public void SaveFollowsAtomicProtocol()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            int start = store.MutationCount;
            repo.Save(Doc("p1", "v2"));
            Assert.Equal(new[]
            {
                "WriteText projects/p1/project.json.tmp",
                "Flush projects/p1/project.json.tmp",
                "Copy projects/p1/project.json -> projects/p1/project.json.bak.tmp",
                "Flush projects/p1/project.json.bak.tmp",
                "Replace projects/p1/project.json.bak.tmp -> projects/p1/project.json.bak",
                "Replace projects/p1/project.json.tmp -> projects/p1/project.json",
            }, store.MutationLog.Skip(start));
            Assert.Equal("v2", repo.Load("p1").Document.Name);
            Assert.Equal("v1", ProjectJson.Deserialize(store.ReadAllText(Bak)).Name);
        }

        [Fact]
        public void SaveWritesSanitizedDocumentAndReportsWarnings()
        {
            var (store, repo) = NewRepo();
            var doc = Doc("p1");
            doc.Layout.Ratio = 0.9;
            var r = repo.Save(doc);
            Assert.Contains(r.Issues, i => i.Path == "layout.ratio");
            Assert.Equal(0.7, ProjectJson.Deserialize(store.ReadAllText(Main)).Layout.Ratio);
        }

        [Fact]
        public void SaveRejectsUnrepairableDocuments()
        {
            var (store, repo) = NewRepo();
            var bad = Doc("../evil");
            Assert.Throws<ArgumentException>(() => repo.Save(bad));
            var noClip = Doc("p1");
            noClip.Clip = null;
            Assert.Throws<ArgumentException>(() => repo.Save(noClip));
            Assert.Empty(store.AllFiles());
        }

        [Fact]
        public void LoadFallsBackToBackupWhenMainCorrupt()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            store.WriteAllText(Main, "{ \"schemaVersion\": 1, \"id\": ");
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Ok, r.Status);
            Assert.Equal(LoadSource.Backup, r.Source);
            Assert.Equal("old", r.Document.Name);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.Corrupt && i.Path == Main);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.UsedBackup);
        }

        [Theory]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{\"schemaVersion\":\"one\"}")]
        [InlineData("{\"schemaVersion\":1}")]
        [InlineData("{\"schemaVersion\":1,\"id\":\"p1\",\"clip\":{\"trimOutTick\":\"x\"}}")]
        [InlineData("{\"schemaVersion\":1,\"id\":\"p1\",\"clip\":null}")]
        public void AnyBrokenMainFallsBack(string content)
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            store.WriteAllText(Main, content);
            var r = repo.Load("p1");
            Assert.Equal(LoadSource.Backup, r.Source);
            Assert.Equal("old", r.Document.Name);
        }

        [Fact]
        public void NonUtf8MainFallsBack()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            store.WriteAllBytes(Main, new byte[] { 0xC3, 0x28, 0xFF });
            Assert.Equal(LoadSource.Backup, repo.Load("p1").Source);
        }

        [Fact]
        public void UnreadableMainIsUnavailableWithoutFallback()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            store.FailWhen((op, path) => op == FileOp.ReadText && path == Main);
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Unavailable, r.Status);
            Assert.Equal(LoadSource.None, r.Source);
            Assert.Null(r.Document);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.Unavailable && i.Path == Main);
            Assert.DoesNotContain(r.Issues, i => i.Code == IssueCode.UsedBackup);
            store.ClearFailures();
            Assert.Equal("new", repo.Load("p1").Document.Name);
        }

        [Fact]
        public void MissingMainUsesBackup()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            store.DeleteFile(Main);
            var r = repo.Load("p1");
            Assert.Equal(LoadSource.Backup, r.Source);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.UsedBackup);
        }

        [Fact]
        public void BothBrokenIsCorruptAndNothingIsNotFound()
        {
            var (store, repo) = NewRepo();
            Assert.Equal(LoadStatus.NotFound, repo.Load("p1").Status);
            repo.Save(Doc("p1"));
            repo.Save(Doc("p1"));
            store.WriteAllText(Main, "x");
            store.WriteAllText(Bak, "y");
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Corrupt, r.Status);
            Assert.Null(r.Document);
            Assert.Equal(LoadSource.None, r.Source);
            Assert.Equal(2, r.Issues.Count(i => i.Code == IssueCode.Corrupt));
        }

        [Fact]
        public void NewerSchemaIsRefusedWithoutFallbackAndNotOverwritten()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "old"));
            repo.Save(Doc("p1", "new"));
            var o = ProjectJson.ParseObject(store.ReadAllText(Main));
            o["schemaVersion"] = 2;
            store.WriteAllText(Main, o.ToString());
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.TooNew, r.Status);
            Assert.Equal(2, r.FileSchemaVersion);
            Assert.Contains(r.Issues, i => i.Code == IssueCode.TooNew);

            var ex = Assert.Throws<ProjectMigrationException>(() => repo.Save(Doc("p1", "clobber")));
            Assert.Equal(MigrationError.TooNew, ex.Error);
            Assert.Equal(2, (int)ProjectJson.ParseObject(store.ReadAllText(Main))["schemaVersion"]);
        }

        [Fact]
        public void LoadMigratesOlderFiles()
        {
            var store = new InMemoryFileStore();
            // Target 2: v1 files get a "tags" array, then the v2 document is read by the v1 DTO (unknown keys ignored).
            var migrator = new ProjectMigrator(2, new[] { new ProjectMigrationStep(1, o => { o["tags"] = new JArray(); return o; }) });
            store.WriteAllText(Main, ReadData("project_v1.json").Replace("6f1c2a9e-3b7d-4c11-9a0e-5d2f8b7c4e10", "p1"));
            var r = new ProjectRepository(store, "projects", migrator).Load("p1");
            // Sanitizer expects CurrentSchemaVersion (1), so a v2 result is an error in this build: proves migration ran.
            Assert.Equal(LoadStatus.Corrupt, r.Status);
            Assert.Contains(r.Issues, i => i.Message.Contains("schemaVersion"));
        }

        [Fact]
        public void LoadReportsMigrationFromOlderSchemaWithDefaultTarget()
        {
            var store = new InMemoryFileStore();
            var migrator = new ProjectMigrator(1, new[]
            {
                new ProjectMigrationStep(0, o =>
                {
                    o["name"] = o["title"];
                    o.Remove("title");
                    return o;
                }),
            });
            var o0 = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o0["schemaVersion"] = 0;
            o0["id"] = "p1";
            o0["title"] = o0["name"];
            o0.Remove("name");
            store.WriteAllText(Main, o0.ToString());
            var r = new ProjectRepository(store, "projects", migrator).Load("p1");
            Assert.Equal(LoadStatus.Ok, r.Status);
            Assert.True(r.Migrated);
            Assert.Equal(0, r.FileSchemaVersion);
            Assert.Equal("Ring Escape 12", r.Document.Name);
        }

        [Fact]
        public void LoadUsesFolderIdAndSanitizes()
        {
            var (store, repo) = NewRepo();
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o["layout"]["ratio"] = 3;
            store.WriteAllText(Main, o.ToString()); // id in file differs from folder "p1"
            var r = repo.Load("p1");
            Assert.Equal(LoadStatus.Ok, r.Status);
            Assert.Equal("p1", r.Document.Id);
            Assert.Equal(0.7, r.Document.Layout.Ratio);
            Assert.Contains(r.Issues, i => i.Path == "id");
            Assert.Contains(r.Issues, i => i.Path == "layout.ratio");
        }

        [Fact]
        public void CorruptMainDoesNotReplaceGoodBackupOnSave()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2")); // bak = v1
            store.WriteAllText(Main, "garbage");
            repo.Save(Doc("p1", "v3"));
            Assert.Equal("v3", repo.Load("p1").Document.Name);
            Assert.Equal("v1", ProjectJson.Deserialize(store.ReadAllText(Bak)).Name);
        }

        [Fact]
        public void UnsafeIdsAreRejectedEverywhere()
        {
            var (_, repo) = NewRepo();
            foreach (var id in new[] { "..", "../x", "a/b", "", null, "a\\b", "/x" })
            {
                Assert.Throws<ArgumentException>(() => repo.Load(id));
                Assert.Throws<ArgumentException>(() => repo.Delete(id));
                Assert.Throws<ArgumentException>(() => repo.Create(id, "n", Now));
                Assert.Throws<ArgumentException>(() => repo.Duplicate("p1", id, Now));
                Assert.Throws<ArgumentException>(() => repo.Rename(id, "n", Now));
            }
            Assert.Throws<ArgumentException>(() => new ProjectRepository(new InMemoryFileStore(), "../root"));
        }

        // ---- create / list / duplicate / rename / delete ----

        [Fact]
        public void CreateWritesDefaultProject()
        {
            var (_, repo) = NewRepo();
            var d = repo.Create("p1", "First", Now);
            Assert.Equal("p1", d.Id);
            Assert.Equal(Now, d.CreatedAt);
            Assert.Equal(Now, d.UpdatedAt);
            Assert.Equal(ModeCatalog.All[0].Id, d.Clip.ModeId);
            var r = repo.Load("p1");
            Assert.Equal("First", r.Document.Name);
            Assert.True(repo.Exists("p1"));
            Assert.Throws<InvalidOperationException>(() => repo.Create("p1", "Again", Now));
            Assert.Throws<ArgumentException>(() => repo.Create("p2", "x", "not a time"));
        }

        [Fact]
        public void CreateFromDocument()
        {
            var (_, repo) = NewRepo();
            var d = repo.Create(ProjectDefaults.NewProject("p9", "Race", Now, "marble_race", 600));
            Assert.Equal("marble_race", repo.Load("p9").Document.Clip.ModeId);
            Assert.Equal(600, d.Clip.TrimOutTick);
        }

        [Fact]
        public void ListSortsNewestFirstAndSkipsBroken()
        {
            var (store, repo) = NewRepo();
            repo.Create("a", "A", "2026-10-18T10:00:00Z");
            repo.Create("b", "B", "2026-10-18T12:00:00Z");
            repo.Create("c", "C", "2026-10-18T14:00:00+03:00"); // = 11:00Z
            repo.Create("d", "D", "2026-10-18T12:00:00Z"); // ties with b, ordered by id
            store.WriteAllText("projects/broken/project.json", "{");
            store.CreateDirectory("projects/empty");
            Assert.Equal(IssueCode.Orphan, repo.List().Skipped.Single(i => i.Path == "projects/empty").Code);
            store.CreateDirectory("projects/bad.name");
            store.WriteAllText("projects/stray.txt", "not a folder");
            var tooNew = ProjectJson.ParseObject(ProjectJson.Serialize(Doc("future")));
            tooNew["schemaVersion"] = 5;
            store.WriteAllText("projects/future/project.json", tooNew.ToString());

            var list = repo.List();
            Assert.Equal(new[] { "b", "d", "c", "a" }, list.Projects.Select(p => p.Id));
            Assert.Equal(new[] { "projects/bad.name", "projects/broken", "projects/empty", "projects/future" }, list.Skipped.Select(i => i.Path));
            Assert.Equal(IssueCode.TooNew, list.Skipped.Single(i => i.Path == "projects/future").Code);
            Assert.Equal(IssueCode.InvalidId, list.Skipped.Single(i => i.Path == "projects/bad.name").Code);
            var b = list.Projects[0];
            Assert.Equal("B", b.Name);
            Assert.Equal(ModeCatalog.All[0].Id, b.ModeId);
            Assert.Equal(LoadSource.Main, b.Source);
            Assert.Equal(ProjectLimits.MaxDurationUs, b.DurationUs);
        }

        [Fact]
        public void ListEmptyStoreAndBackupSource()
        {
            var (store, repo) = NewRepo();
            Assert.Empty(repo.List().Projects);
            repo.Save(Doc("p1"));
            repo.Save(Doc("p1"));
            store.WriteAllText(Main, "corrupt");
            Assert.Equal(LoadSource.Backup, repo.List().Projects.Single().Source);
        }

        [Fact]
        public void DuplicateCopiesMediaAndResetsMetadata()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "Original"));
            store.WriteAllBytes("projects/p1/clip.bvr", new byte[] { 1, 2, 3 });
            store.WriteAllBytes("projects/p1/media/user.mp4", new byte[] { 4, 5 });
            store.WriteAllBytes("projects/p1/thumbs/0.jpg", new byte[] { 6 });
            store.WriteAllText("projects/p1/project.json.tmp", "stale temp");
            repo.Save(Doc("p1", "Original")); // creates .bak too

            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Equal("p2", copy.Id);
            Assert.Equal("Original", copy.Name);
            Assert.Equal(Later, copy.CreatedAt);
            Assert.Equal(Later, copy.UpdatedAt);
            Assert.Empty(copy.Exports);
            Assert.Equal(new byte[] { 1, 2, 3 }, store.ReadAllBytes("projects/p2/clip.bvr"));
            Assert.Equal(new byte[] { 4, 5 }, store.ReadAllBytes("projects/p2/media/user.mp4"));
            Assert.Equal(new byte[] { 6 }, store.ReadAllBytes("projects/p2/thumbs/0.jpg"));
            Assert.False(store.FileExists("projects/p2/project.json.bak"));
            Assert.False(store.FileExists("projects/p2/project.json.tmp"));
            Assert.Equal("p2", repo.Load("p2").Document.Id);
            // Source untouched.
            Assert.Single(repo.Load("p1").Document.Exports);

            Assert.Equal("Copy", repo.Duplicate("p1", "p3", Later, "Copy").Name);
            Assert.Throws<InvalidOperationException>(() => repo.Duplicate("p1", "p2", Later));
            Assert.Throws<InvalidOperationException>(() => repo.Duplicate("missing", "p4", Later));
        }

        [Fact]
        public void FailedDuplicateLeavesNoProject()
        {
            var baseline = new InMemoryFileStore();
            var baseRepo = new ProjectRepository(baseline);
            baseRepo.Save(Doc("p1"));
            baseline.WriteAllBytes("projects/p1/clip.bvr", new byte[] { 1 });
            baseline.WriteAllBytes("projects/p1/media/user.mp4", new byte[] { 2 });

            var probe = baseline.Clone();
            int start = probe.MutationCount;
            new ProjectRepository(probe).Duplicate("p1", "p2", Later);
            int steps = probe.MutationCount - start;
            Assert.True(steps > 5);

            for (int i = 0; i < steps; i++)
            {
                var store = baseline.Clone();
                var repo = new ProjectRepository(store);
                store.FailAtMutation(store.MutationCount + i);
                Assert.ThrowsAny<Exception>(() => repo.Duplicate("p1", "p2", Later));
                Assert.Equal(LoadStatus.NotFound, repo.Load("p2").Status);
                Assert.False(store.DirectoryExists("projects/p2"), "step " + i);
                Assert.Equal(LoadStatus.Ok, repo.Load("p1").Status);
            }
        }

        [Fact]
        public void RenameUpdatesNameAndTime()
        {
            var (_, repo) = NewRepo();
            repo.Create("p1", "Old", Now);
            var d = repo.Rename("p1", "New", Later);
            Assert.Equal("New", d.Name);
            var loaded = repo.Load("p1").Document;
            Assert.Equal("New", loaded.Name);
            Assert.Equal(Later, loaded.UpdatedAt);
            Assert.Equal(Now, loaded.CreatedAt);
            Assert.Throws<InvalidOperationException>(() => repo.Rename("nope", "x", Later));
            Assert.Throws<ArgumentException>(() => repo.Rename("p1", "x", "bad time"));
            Assert.Equal(ProjectDefaults.UntitledName, repo.Rename("p1", "   ", Later).Name);
        }

        [Fact]
        public void DeleteRemovesWholeFolderOnly()
        {
            var (store, repo) = NewRepo();
            repo.Create("p1", "A", Now);
            repo.Create("p10", "B", Now);
            store.WriteAllBytes("projects/p1/media/user.mp4", new byte[] { 1 });
            Assert.True(repo.Delete("p1"));
            Assert.False(repo.Exists("p1"));
            Assert.DoesNotContain(store.AllFiles(), f => f.StartsWith("projects/p1/"));
            Assert.Equal(LoadStatus.Ok, repo.Load("p10").Status);
            Assert.False(repo.Delete("p1"));
        }

        [Fact]
        public void CustomRoot()
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store, "data/projects");
            repo.Create("p1", "A", Now);
            Assert.True(store.FileExists("data/projects/p1/project.json"));
            Assert.Single(repo.List().Projects);
        }

        // ---- atomicity under failure injection ----

        public static IEnumerable<object[]> Kinds()
        {
            yield return new object[] { FailureKind.Throw, false };
            yield return new object[] { FailureKind.Throw, true };
            yield return new object[] { FailureKind.TornWrite, false };
            yield return new object[] { FailureKind.TornWrite, true };
            yield return new object[] { FailureKind.Crash, false };
        }

        /// <summary>
        /// For every mutating step of Save and every failure kind (optionally followed by power loss),
        /// Load returns exactly the old or the new document and never a broken one; a retry then succeeds.
        /// </summary>
        [Theory]
        [MemberData(nameof(Kinds))]
        public void SaveIsAtomicUnderAnySingleFailure(FailureKind kind, bool crashAfter)
        {
            var baseline = new InMemoryFileStore();
            var baseRepo = new ProjectRepository(baseline);
            baseRepo.Save(Doc("p1", "v1"));
            baseRepo.Save(Doc("p1", "v2")); // main = v2, bak = v1
            string oldJson = ProjectJson.Serialize(baseRepo.Load("p1").Document);
            var newDoc = Doc("p1", "v3");
            newDoc.Texts[0].Text = "new text";
            string newJson = ProjectJson.Serialize(ProjectValidator.Sanitize(newDoc).Document);

            int steps = CountSaveSteps(baseline, newDoc);
            Assert.Equal(6, steps);
            bool sawOld = false;
            for (int i = 0; i < steps; i++)
            {
                var store = baseline.Clone();
                var repo = new ProjectRepository(store);
                store.FailAtMutation(store.MutationCount + i, kind);
                Assert.ThrowsAny<Exception>(() => repo.Save(newDoc));
                if (crashAfter) store.SimulateCrash();
                store.ClearFailures();

                var r = repo.Load("p1");
                Assert.Equal(LoadStatus.Ok, r.Status);
                string got = ProjectJson.Serialize(r.Document);
                Assert.True(got == oldJson || got == newJson, "step " + i + " (" + store.MutationLog[store.MutationLog.Count - 1] + ") produced a broken document");
                sawOld |= got == oldJson;

                // Recovery: the next save succeeds and wins.
                repo.Save(newDoc);
                Assert.Equal(newJson, ProjectJson.Serialize(repo.Load("p1").Document));
            }
            Assert.True(sawOld);
        }

        [Theory]
        [MemberData(nameof(Kinds))]
        public void FirstSaveIsAtomicUnderAnySingleFailure(FailureKind kind, bool crashAfter)
        {
            var newDoc = Doc("p1", "v1");
            string newJson = ProjectJson.Serialize(ProjectValidator.Sanitize(newDoc).Document);
            int steps = CountSaveSteps(new InMemoryFileStore(), newDoc);
            Assert.Equal(4, steps); // mkdir, write, flush, replace
            for (int i = 0; i < steps; i++)
            {
                var store = new InMemoryFileStore();
                var repo = new ProjectRepository(store);
                store.FailAtMutation(i, kind);
                Assert.ThrowsAny<Exception>(() => repo.Save(newDoc));
                if (crashAfter) store.SimulateCrash();
                var r = repo.Load("p1");
                Assert.True(r.Status == LoadStatus.NotFound || (r.Status == LoadStatus.Ok && ProjectJson.Serialize(r.Document) == newJson), "step " + i + ": " + r.Status);
            }
        }

        [Fact]
        public void SuccessfulSaveSurvivesPowerLoss()
        {
            var (store, repo) = NewRepo();
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            store.SimulateCrash();
            var r = repo.Load("p1");
            Assert.Equal(LoadSource.Main, r.Source);
            Assert.Equal("v2", r.Document.Name);
            Assert.Equal("v1", ProjectJson.Deserialize(store.ReadAllText(Bak)).Name);
        }

        [Fact]
        public void RandomFailureSequencesNeverBreakTheProject()
        {
            // Many saves with random single failures, crashes and external corruption of one file at a time.
            var rng = Rng.FromSeed(0xA70_1C, 3);
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Save(Doc("p1", "n0"));
            var committed = new List<string> { "n0" };
            var lastSource = LoadSource.Main;
            int corruptedMain = 0, corruptedBak = 0;
            for (int i = 1; i <= 200; i++)
            {
                string name = "n" + i;
                int c = rng.NextInt(0, 8);
                if (c == 0 && lastSource == LoadSource.Main && store.FileExists(Bak))
                {
                    // Garbage in the backup: the good main file is still used.
                    store.WriteAllText(Bak, rng.NextInt(0, 2) == 0 ? "" : "{\"schemaVersion\":1,");
                    corruptedBak++;
                    var rb = repo.Load("p1");
                    Assert.Equal(LoadSource.Main, rb.Source);
                    Assert.Equal(committed[committed.Count - 1], rb.Document.Name);
                }
                else if (c == 1 && lastSource == LoadSource.Main && BackupLoads(store))
                {
                    // Garbage in the main file: the backup (an older committed version) is used and becomes the current state.
                    store.WriteAllBytes(Main, new byte[] { 0xC3, 0x28 });
                    corruptedMain++;
                    var rm = repo.Load("p1");
                    Assert.Equal(LoadStatus.Ok, rm.Status);
                    Assert.Equal(LoadSource.Backup, rm.Source);
                    Assert.Contains(rm.Document.Name, committed);
                    committed.Add(rm.Document.Name);
                }
                if (rng.NextInt(0, 3) == 0) store.FailAtMutation(store.MutationCount + rng.NextInt(0, 6), (FailureKind)rng.NextInt(0, 3));
                try
                {
                    repo.Save(Doc("p1", name));
                    committed.Add(name);
                }
                catch (Exception)
                {
                    if (rng.NextInt(0, 2) == 0) store.SimulateCrash();
                }
                store.ClearFailures();
                var r = repo.Load("p1");
                Assert.Equal(LoadStatus.Ok, r.Status);
                // Old or new: the result is the last committed version, or the one just attempted (failure after the final rename).
                Assert.True(r.Document.Name == committed[committed.Count - 1] || r.Document.Name == name, "iteration " + i + ": " + r.Document.Name);
                if (r.Document.Name == name && committed[committed.Count - 1] != name) committed.Add(name);
                lastSource = r.Source;
            }
            Assert.True(corruptedMain > 5 && corruptedBak > 5, "main " + corruptedMain + ", bak " + corruptedBak);
        }

        static bool BackupLoads(InMemoryFileStore store)
        {
            if (!store.FileExists(Bak)) return false;
            try
            {
                return !ProjectValidator.Sanitize(ProjectJson.Deserialize(store.ReadAllText(Bak)), "p1").HasErrors;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static int CountSaveSteps(InMemoryFileStore baseline, ProjectDocument doc)
        {
            var probe = baseline.Clone();
            int before = probe.MutationCount;
            new ProjectRepository(probe).Save(doc);
            return probe.MutationCount - before;
        }
    }
}
