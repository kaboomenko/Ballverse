using System;
using System.Linq;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Media references in project.json never name the project's own files (spec §7.4), and Duplicate never copies the
    /// source's project.json, backup or temp files, whatever the document or folder holds (spec §3.9).
    /// </summary>
    public class ReservedPathTests
    {
        [Theory]
        [InlineData("project.json")]
        [InlineData("project.json.bak")]
        [InlineData("project.json.tmp")]
        [InlineData("project.json.bak.tmp")]
        [InlineData("PROJECT.JSON")]
        [InlineData("Project.Json.Bak")]
        public void ProjectFileNamesAreReserved(string path)
        {
            Assert.True(ProjectPaths.IsSafeRelativePath(path));
            Assert.True(ProjectPaths.IsProjectFileName(path));
            Assert.True(ProjectPaths.IsReservedFileName(path));
            Assert.False(ProjectPaths.IsSafeMediaPath(path));
        }

        [Theory]
        [InlineData("clip.bvs")]
        [InlineData("CLIP.BVS")]
        public void RenderStreamIsReservedButNotAProjectFile(string path)
        {
            Assert.False(ProjectPaths.IsProjectFileName(path));
            Assert.True(ProjectPaths.IsReservedFileName(path));
            Assert.False(ProjectPaths.IsSafeMediaPath(path));
        }

        [Theory]
        [InlineData("clip.bvr")]
        [InlineData("media/user.mp4")]
        [InlineData("media/project.json")]   // only the folder root is reserved
        [InlineData("project.json.bak2")]
        [InlineData("project.jso")]
        [InlineData("exports/out_1.mp4")]
        public void OrdinaryPathsAreMediaPaths(string path)
        {
            Assert.False(ProjectPaths.IsReservedFileName(path));
            Assert.True(ProjectPaths.IsSafeMediaPath(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("../project.json")]
        [InlineData("/project.json")]
        public void UnsafePathsAreNotMediaPaths(string path)
        {
            Assert.False(ProjectPaths.IsSafeMediaPath(path));
            Assert.False(ProjectPaths.IsReservedFileName(path));
        }

        // ---- sanitizer ----

        static SanitizeResult Run(Action<ProjectDocument> mutate)
        {
            var d = Golden();
            mutate(d);
            return ProjectValidator.Sanitize(d);
        }

        static void HasUnsafePath(SanitizeResult r, string path)
        {
            Assert.Contains(r.Issues, i => i.Path == path && i.Code == IssueCode.UnsafePath && i.Severity == IssueSeverity.Warning
                && i.Message.Contains("reserved"));
        }

        [Theory]
        [InlineData("project.json")]
        [InlineData("project.json.bak")]
        [InlineData("Project.JSON.tmp")]
        [InlineData("clip.bvs")]
        public void ReplayNamingAReservedFileIsReset(string path)
        {
            var r = Run(d => d.Clip.Replay = path);
            Assert.Equal(ProjectPaths.ClipReplayFile, r.Document.Clip.Replay);
            HasUnsafePath(r, "clip.replay");
        }

        [Theory]
        [InlineData("project.json.bak")]
        [InlineData("PROJECT.JSON")]
        [InlineData("project.json.bak.tmp")]
        public void UserVideoNamingAReservedFileIsReset(string path)
        {
            var r = Run(d => d.UserVideo.File = path);
            Assert.Equal(ProjectPaths.DefaultUserVideoFile, r.Document.UserVideo.File);
            HasUnsafePath(r, "userVideo.file");
        }

        [Fact]
        public void ExportFileNamingAReservedFileIsCleared()
        {
            var r = Run(d => d.Exports[0].File = "project.json");
            Assert.Single(r.Document.Exports);
            Assert.Null(r.Document.Exports[0].File);
            HasUnsafePath(r, "exports[0].file");
        }

        [Fact]
        public void MediaPathInsideASubfolderIsKept()
        {
            var r = Run(d => d.UserVideo.File = "media/project.json");
            Assert.True(r.IsClean);
            Assert.Equal("media/project.json", r.Document.UserVideo.File);
        }

        // ---- repository ----

        const string SrcMain = "projects/p1/project.json";
        const string SrcBak = "projects/p1/project.json.bak";

        /// <summary>Saves v1 then v2 of p1 (so a backup exists), then overwrites project.json with an unsanitized hand edit.</summary>
        static (InMemoryFileStore store, ProjectRepository repo) SourceWithHandEdit(Action<ProjectDocument> edit)
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            Assert.True(store.FileExists(SrcBak));
            var d = Doc("p1", "v2");
            edit(d);
            store.WriteAllText(SrcMain, ProjectJson.Serialize(d));
            store.WriteAllBytes("projects/p1/clip.bvr", new byte[] { 7 });
            store.WriteAllBytes("projects/p1/media/user.mp4", new byte[] { 1, 2 });
            return (store, repo);
        }

        [Fact]
        public void DuplicateDoesNotCopyBackupNamedAsUserVideo()
        {
            // QA finding: userVideo.file = "project.json.bak" made Duplicate copy the source backup into the copy.
            var (store, repo) = SourceWithHandEdit(d => d.UserVideo.File = ProjectPaths.BackupFile);
            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Equal(ProjectPaths.DefaultUserVideoFile, copy.UserVideo.File);
            Assert.False(store.FileExists("projects/p2/project.json.bak"));
            Assert.True(store.FileExists("projects/p2/media/user.mp4"));
            var loaded = repo.Load("p2");
            Assert.Equal(LoadStatus.Ok, loaded.Status);
            Assert.Equal(LoadSource.Main, loaded.Source);
            Assert.Equal(Later, loaded.Document.CreatedAt);
        }

        [Fact]
        public void DuplicateDoesNotCopyMainNamedAsReplay()
        {
            // Copying the source's project.json first would make Save rotate the source into the copy's backup.
            var (store, repo) = SourceWithHandEdit(d => d.Clip.Replay = ProjectPaths.ProjectFile);
            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Equal(ProjectPaths.ClipReplayFile, copy.Clip.Replay);
            Assert.False(store.FileExists("projects/p2/project.json.bak"));
            Assert.Equal(new byte[] { 7 }, store.ReadAllBytes("projects/p2/clip.bvr"));
            Assert.Equal("p2", ProjectJson.Deserialize(store.ReadAllText("projects/p2/project.json")).Id);
        }

        [Fact]
        public void DuplicateSkipsProjectFilesInAnyLetterCase()
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            // Aliases of the project files on a case-insensitive file system, and a folder squatting on a project file name.
            store.WriteAllText("projects/p1/PROJECT.JSON.BAK", ProjectJson.Serialize(Doc("p1", "stale")));
            store.WriteAllText("projects/p1/Project.json.tmp", "{");
            store.WriteAllBytes("projects/p1/project.json.tmp/x.bin", new byte[] { 1 });
            store.WriteAllBytes("projects/p1/media/project.json", new byte[] { 5 });

            repo.Duplicate("p1", "p2", Later);
            var files = store.ListFiles("projects/p2").ToArray();
            Assert.Equal(new[] { ProjectPaths.ProjectFile }, files);
            Assert.DoesNotContain(ProjectPaths.TempFile, store.ListDirectories("projects/p2"));
            Assert.Equal(new byte[] { 5 }, store.ReadAllBytes("projects/p2/media/project.json"));
            Assert.Equal(LoadSource.Main, repo.Load("p2").Source);
        }

        [Fact]
        public void DuplicateOfProjectLoadedFromBackupStartsWithoutBackup()
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Save(Doc("p1", "v1"));
            repo.Save(Doc("p1", "v2"));
            store.WriteAllText(SrcMain, "{ broken");
            Assert.Equal(LoadSource.Backup, repo.Load("p1").Source);

            var copy = repo.Duplicate("p1", "p2", Later);
            Assert.Equal("v1", copy.Name);
            Assert.False(store.FileExists("projects/p2/project.json.bak"));
            var loaded = repo.Load("p2");
            Assert.Equal(LoadSource.Main, loaded.Source);
            Assert.Equal("v1", loaded.Document.Name);
            Assert.Equal("{ broken", store.ReadAllText(SrcMain)); // source untouched
        }

        [Fact]
        public void StorageUsageCountsCaseVariantProjectFilesAsProjectFile()
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Save(Doc("p1"));
            long before = repo.GetStorageUsage("p1")[StorageCategory.ProjectFile];
            store.WriteAllBytes("projects/p1/PROJECT.JSON.BAK", new byte[13]);
            var u = repo.GetStorageUsage("p1");
            Assert.Equal(before + 13, u[StorageCategory.ProjectFile]);
            Assert.Equal(0, u[StorageCategory.Other]);
        }
    }
}
