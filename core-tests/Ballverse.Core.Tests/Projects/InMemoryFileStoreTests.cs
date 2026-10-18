using System;
using System.IO;
using System.Text;
using Ballverse.Core.Projects;
using Xunit;

namespace Ballverse.Core.Tests.Projects
{
    public class InMemoryFileStoreTests
    {
        [Fact]
        public void WriteReadAndParents()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a/b/c.txt", "привет");
            Assert.True(s.FileExists("a/b/c.txt"));
            Assert.True(s.DirectoryExists("a"));
            Assert.True(s.DirectoryExists("a/b"));
            Assert.True(s.DirectoryExists(""));
            Assert.Equal("привет", s.ReadAllText("a/b/c.txt"));
            Assert.Equal(Encoding.UTF8.GetBytes("привет"), s.ReadAllBytes("a/b/c.txt"));
            s.WriteAllBytes("a/d.bin", new byte[] { 1, 2, 3 });
            Assert.Equal(new byte[] { 1, 2, 3 }, s.ReadAllBytes("a/d.bin"));
        }

        [Fact]
        public void ListingIsSortedAndImmediate()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("r/z.txt", "");
            s.WriteAllText("r/a.txt", "");
            s.WriteAllText("r/sub/x.txt", "");
            s.CreateDirectory("r/empty");
            s.CreateDirectory("r/B");
            Assert.Equal(new[] { "a.txt", "z.txt" }, s.ListFiles("r"));
            Assert.Equal(new[] { "B", "empty", "sub" }, s.ListDirectories("r"));
            Assert.Equal(new[] { "r" }, s.ListDirectories(""));
            Assert.Empty(s.ListFiles("missing"));
        }

        [Fact]
        public void ReplaceMoveCopyDelete()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "1");
            s.WriteAllText("b", "2");
            s.Replace("a", "b");
            Assert.False(s.FileExists("a"));
            Assert.Equal("1", s.ReadAllText("b"));
            s.Copy("b", "c");
            Assert.Equal("1", s.ReadAllText("c"));
            Assert.Throws<IOException>(() => s.Move("b", "c"));
            s.Move("b", "d/e");
            Assert.Equal("1", s.ReadAllText("d/e"));
            s.DeleteFile("c");
            s.DeleteFile("c");
            Assert.False(s.FileExists("c"));
            Assert.Throws<FileNotFoundException>(() => s.ReadAllText("c"));
            Assert.Throws<FileNotFoundException>(() => s.Replace("nope", "x"));
            s.DeleteDirectory("d");
            Assert.False(s.FileExists("d/e"));
            Assert.False(s.DirectoryExists("d"));
            s.DeleteDirectory("d");
        }

        [Fact]
        public void DeleteDirectoryDoesNotTouchSiblingsWithSamePrefix()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("p/a1/x", "");
            s.WriteAllText("p/a10/x", "");
            s.DeleteDirectory("p/a1");
            Assert.True(s.FileExists("p/a10/x"));
            Assert.Equal(new[] { "a10" }, s.ListDirectories("p"));
        }

        [Theory]
        [InlineData("../x")]
        [InlineData("a/../b")]
        [InlineData("/abs")]
        [InlineData("a\\b")]
        [InlineData("a//b")]
        [InlineData("C:/x")]
        [InlineData("")]
        public void RejectsTraversalPaths(string path)
        {
            var s = new InMemoryFileStore();
            Assert.Throws<ArgumentException>(() => s.WriteAllText(path, "x"));
        }

        [Fact]
        public void CrashLosesUnflushedWrites()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "durable");
            s.Flush("a");
            s.WriteAllText("b", "volatile");
            s.WriteAllText("a", "overwritten");
            s.SimulateCrash();
            Assert.Equal("", s.ReadAllText("a"));
            Assert.Equal("", s.ReadAllText("b"));
        }

        [Fact]
        public void RenameKeepsDurabilityOfSource()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("t1", "flushed");
            s.Flush("t1");
            s.Replace("t1", "f1");
            s.WriteAllText("t2", "not flushed");
            s.Replace("t2", "f2");
            s.SimulateCrash();
            Assert.Equal("flushed", s.ReadAllText("f1"));
            Assert.Equal("", s.ReadAllText("f2"));
        }

        [Fact]
        public void FailAtMutationCountsAndFailsOnce()
        {
            var s = new InMemoryFileStore();
            s.FailAtMutation(1);
            s.WriteAllText("a", "1");
            var ex = Assert.Throws<InjectedFailureException>(() => s.WriteAllText("b", "2"));
            Assert.Equal(FileOp.WriteText, ex.Operation);
            Assert.False(s.FileExists("b"));
            s.WriteAllText("b", "2");
            Assert.Equal(3, s.MutationCount);
            Assert.Equal(new[] { "WriteText a", "WriteText b", "WriteText b" }, s.MutationLog);
        }

        [Fact]
        public void TornWriteLeavesHalfFile()
        {
            var s = new InMemoryFileStore();
            s.FailAtMutation(0, FailureKind.TornWrite);
            Assert.Throws<InjectedFailureException>(() => s.WriteAllText("a", "0123456789"));
            Assert.Equal("01234", s.ReadAllText("a"));
            s.WriteAllText("src", "abcdef");
            s.FailAtMutation(s.MutationCount, FailureKind.TornWrite);
            Assert.Throws<InjectedFailureException>(() => s.Copy("src", "dst"));
            Assert.Equal("abc", s.ReadAllText("dst"));
        }

        [Fact]
        public void TornRenameBehavesLikeThrow()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "1");
            s.FailAtMutation(s.MutationCount, FailureKind.TornWrite);
            Assert.Throws<InjectedFailureException>(() => s.Replace("a", "b"));
            Assert.True(s.FileExists("a"));
            Assert.False(s.FileExists("b"));
        }

        [Fact]
        public void CrashFailureDropsCaches()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "data");
            s.FailAtMutation(s.MutationCount, FailureKind.Crash);
            Assert.Throws<InjectedFailureException>(() => s.Flush("a"));
            Assert.Equal("", s.ReadAllText("a"));
        }

        [Fact]
        public void FailWhenAppliesToReadsUntilCleared()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "1");
            s.FailWhen((op, path) => op == FileOp.ReadText && path == "a");
            Assert.Throws<InjectedFailureException>(() => s.ReadAllText("a"));
            Assert.Throws<InjectedFailureException>(() => s.ReadAllText("a"));
            Assert.Equal(new byte[] { (byte)'1' }, s.ReadAllBytes("a"));
            s.ClearFailures();
            Assert.Equal("1", s.ReadAllText("a"));
        }

        [Fact]
        public void InvalidUtf8ThrowsOnTextRead()
        {
            var s = new InMemoryFileStore();
            s.WriteAllBytes("a", new byte[] { 0xFF, 0xFE, 0x00 });
            Assert.ThrowsAny<ArgumentException>(() => s.ReadAllText("a"));
        }

        [Fact]
        public void CloneIsIndependent()
        {
            var s = new InMemoryFileStore();
            s.WriteAllText("a", "1");
            var c = s.Clone();
            c.WriteAllText("a", "2");
            c.WriteAllText("b", "3");
            Assert.Equal("1", s.ReadAllText("a"));
            Assert.False(s.FileExists("b"));
            Assert.Equal(new[] { "a", "b" }, c.AllFiles());
        }

        [Fact]
        public void FileAndDirectoryCollide()
        {
            var s = new InMemoryFileStore();
            s.CreateDirectory("d");
            Assert.Throws<IOException>(() => s.WriteAllText("d", "x"));
            s.WriteAllText("f", "x");
            Assert.Throws<IOException>(() => s.CreateDirectory("f"));
        }
    }
}
