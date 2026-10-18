using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ballverse.Core.Projects;
using Xunit;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Contract every <see cref="IFileStore"/> must meet for <see cref="ProjectRepository"/> (spec §7.4 atomic write).
    /// Run for <see cref="InMemoryFileStore"/> and for <see cref="DiskFileStore"/>, a reference implementation of the
    /// recipe documented on <see cref="IFileStore.Replace"/>; the platform store in the Unity layer should derive a test class too.
    /// </summary>
    public abstract class FileStoreContractTests : IDisposable
    {
        protected abstract IFileStore Store { get; }

        public virtual void Dispose()
        {
        }

        [Fact]
        public void ReplaceOntoMissingDestinationCreatesIt()
        {
            Store.WriteAllText("d/a.tmp", "new");
            Store.Flush("d/a.tmp");
            Store.Replace("d/a.tmp", "d/a");
            Assert.Equal("new", Store.ReadAllText("d/a"));
            Assert.False(Store.FileExists("d/a.tmp"));
        }

        [Fact]
        public void ReplaceOntoExistingDestinationOverwritesIt()
        {
            Store.WriteAllText("d/a", "old");
            Store.WriteAllText("d/a.tmp", "new");
            Store.Replace("d/a.tmp", "d/a");
            Assert.Equal("new", Store.ReadAllText("d/a"));
            Assert.False(Store.FileExists("d/a.tmp"));
            Assert.Equal(new[] { "a" }, Store.ListFiles("d"));
        }

        [Fact]
        public void ReplaceOfMissingSourceThrowsIOException()
        {
            Assert.ThrowsAny<IOException>(() => Store.Replace("d/none", "d/a"));
        }

        [Fact]
        public void MoveRefusesExistingDestination()
        {
            Store.WriteAllText("d/a", "a");
            Store.WriteAllText("d/b", "b");
            Assert.ThrowsAny<IOException>(() => Store.Move("d/a", "d/b"));
            Assert.Equal("a", Store.ReadAllText("d/a"));
            Assert.Equal("b", Store.ReadAllText("d/b"));
            Store.Move("d/a", "d/c");
            Assert.Equal("a", Store.ReadAllText("d/c"));
            Assert.False(Store.FileExists("d/a"));
        }

        [Fact]
        public void CopyReplacesDestinationAndKeepsSource()
        {
            Store.WriteAllBytes("d/a", new byte[] { 1, 2, 3 });
            Store.WriteAllBytes("d/b", new byte[] { 9 });
            Store.Copy("d/a", "d/b");
            Assert.Equal(new byte[] { 1, 2, 3 }, Store.ReadAllBytes("d/b"));
            Assert.Equal(new byte[] { 1, 2, 3 }, Store.ReadAllBytes("d/a"));
        }

        [Fact]
        public void TextIsUtf8WithoutBom()
        {
            const string text = "Кто выживет? 🔥";
            Store.WriteAllText("t.txt", text);
            var bytes = Store.ReadAllBytes("t.txt");
            Assert.Equal(new UTF8Encoding(false).GetBytes(text), bytes);
            Assert.Equal(text, Store.ReadAllText("t.txt"));
            Assert.Equal(bytes.LongLength, Store.GetFileSize("t.txt"));
        }

        [Fact]
        public void WritesCreateParentDirectories()
        {
            Store.WriteAllBytes("a/b/c/file.bin", new byte[] { 7 });
            Assert.True(Store.DirectoryExists("a"));
            Assert.True(Store.DirectoryExists("a/b/c"));
            Assert.Equal(new[] { "b" }, Store.ListDirectories("a"));
        }

        [Fact]
        public void ListingsAreNamesSortedOrdinallyAndEmptyWhenMissing()
        {
            foreach (var n in new[] { "b", "B", "a", "a1", "_" }) Store.WriteAllText("l/" + n + ".txt", n);
            foreach (var n in new[] { "z", "Z", "m" }) Store.CreateDirectory("l/" + n);
            Assert.Equal(new[] { "B.txt", "_.txt", "a.txt", "a1.txt", "b.txt" }, Store.ListFiles("l"));
            Assert.Equal(new[] { "Z", "m", "z" }, Store.ListDirectories("l"));
            Assert.Empty(Store.ListFiles("missing"));
            Assert.Empty(Store.ListDirectories("missing"));
        }

        [Fact]
        public void DeletesAreRecursiveAndNoOpWhenMissing()
        {
            Store.WriteAllText("p/x/1.txt", "1");
            Store.WriteAllText("p/x/y/2.txt", "2");
            Store.WriteAllText("p/keep.txt", "k");
            Store.DeleteDirectory("p/x");
            Assert.False(Store.DirectoryExists("p/x"));
            Assert.False(Store.FileExists("p/x/y/2.txt"));
            Assert.True(Store.FileExists("p/keep.txt"));
            Store.DeleteDirectory("p/x");
            Store.DeleteFile("p/none.txt");
            Store.DeleteFile("p/keep.txt");
            Assert.False(Store.FileExists("p/keep.txt"));
        }

        [Fact]
        public void MissingFilesThrowFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => Store.ReadAllText("nope"));
            Assert.Throws<FileNotFoundException>(() => Store.ReadAllBytes("nope"));
            Assert.Throws<FileNotFoundException>(() => Store.GetFileSize("nope"));
            Assert.False(Store.FileExists("nope"));
            Assert.False(Store.DirectoryExists("nope"));
        }

        [Fact]
        public void RepositoryWorksOnTheStore()
        {
            var repo = new ProjectRepository(Store);
            var doc = ProjectTestUtil.Doc("p1", "v1");
            repo.Save(doc); // Replace onto a missing project.json
            doc.Name = "v2";
            repo.Save(doc); // Replace onto a missing project.json.bak
            doc.Name = "v3";
            repo.Save(doc); // both replaces onto existing files
            Assert.Equal("v3", repo.Load("p1").Document.Name);
            Assert.Equal(new[] { "project.json", "project.json.bak" }, Store.ListFiles("projects/p1"));
            Assert.Equal("v2", ProjectJson.Deserialize(Store.ReadAllText("projects/p1/project.json.bak")).Name);
            Assert.Equal("p2", repo.Duplicate("p1", "p2", ProjectTestUtil.Later).Id);
            Assert.True(repo.Delete("p1"));
            Assert.Equal(new[] { "p2" }, repo.List().Projects.Select(p => p.Id));
        }
    }

    public sealed class InMemoryFileStoreContractTests : FileStoreContractTests
    {
        readonly InMemoryFileStore _store = new InMemoryFileStore();

        protected override IFileStore Store => _store;
    }

    public sealed class DiskFileStoreContractTests : FileStoreContractTests
    {
        readonly string _root;
        readonly DiskFileStore _store;

        public DiskFileStoreContractTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "ballverse-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _store = new DiskFileStore(_root);
        }

        protected override IFileStore Store => _store;

        public override void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Test-only reference <see cref="IFileStore"/> on the real file system, written against netstandard2.1-era APIs only
    /// (no <c>File.Move(src, dst, overwrite)</c>), following the recipe on <see cref="IFileStore.Replace"/>.
    /// Directory fsync after a rename is left to the platform store.
    /// </summary>
    sealed class DiskFileStore : IFileStore
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false, true);
        readonly string _root;

        public DiskFileStore(string root)
        {
            _root = root;
        }

        string Full(string path)
        {
            return path.Length == 0 ? _root : Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        }

        public bool FileExists(string path) => File.Exists(Full(path));

        public bool DirectoryExists(string path) => Directory.Exists(Full(path));

        public string ReadAllText(string path) => s_utf8.GetString(File.ReadAllBytes(Full(path)));

        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(Full(path));

        public void WriteAllText(string path, string text) => WriteAllBytes(path, s_utf8.GetBytes(text));

        public void WriteAllBytes(string path, byte[] data)
        {
            string full = Full(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, data);
        }

        public void Flush(string path)
        {
            using (var fs = new FileStream(Full(path), FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Flush(true);
            }
        }

        public void Replace(string source, string destination)
        {
            string src = Full(source), dst = Full(destination);
            if (!File.Exists(src)) throw new FileNotFoundException("File not found: " + source, source);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst)) File.Replace(src, dst, null);
            else File.Move(src, dst);
        }

        public void Move(string source, string destination)
        {
            string dst = Full(destination);
            if (File.Exists(dst)) throw new IOException("Destination exists: " + destination);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Move(Full(source), dst);
        }

        public void Copy(string source, string destination)
        {
            string dst = Full(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(Full(source), dst, true);
        }

        public void DeleteFile(string path)
        {
            if (File.Exists(Full(path))) File.Delete(Full(path));
        }

        public void CreateDirectory(string path) => Directory.CreateDirectory(Full(path));

        public void DeleteDirectory(string path)
        {
            if (Directory.Exists(Full(path))) Directory.Delete(Full(path), true);
        }

        public long GetFileSize(string path)
        {
            var info = new FileInfo(Full(path));
            if (!info.Exists) throw new FileNotFoundException("File not found: " + path, path);
            return info.Length;
        }

        public IReadOnlyList<string> ListDirectories(string path) => Names(Full(path), true);

        public IReadOnlyList<string> ListFiles(string path) => Names(Full(path), false);

        static IReadOnlyList<string> Names(string dir, bool directories)
        {
            if (!Directory.Exists(dir)) return new string[0];
            var names = (directories ? Directory.GetDirectories(dir) : Directory.GetFiles(dir)).Select(Path.GetFileName).ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }
}
