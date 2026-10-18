using System;
using Ballverse.Core.Projects;
using Xunit;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectPathsTests
    {
        [Theory]
        [InlineData("6f1c2a9e-3b7d-4c11-9a0e-5d2f8b7c4e10")]
        [InlineData("t1")]
        [InlineData("A_b-9")]
        [InlineData("x")]
        public void SafeIds(string id)
        {
            Assert.True(ProjectPaths.IsSafeId(id));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("a.b")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("/abs")]
        [InlineData("C:")]
        [InlineData("a b")]
        [InlineData("6f1c…")]
        [InlineData("ä")]
        [InlineData("a\0")]
        public void UnsafeIds(string id)
        {
            Assert.False(ProjectPaths.IsSafeId(id));
        }

        [Fact]
        public void IdLengthLimit()
        {
            Assert.True(ProjectPaths.IsSafeId(new string('a', 64)));
            Assert.False(ProjectPaths.IsSafeId(new string('a', 65)));
        }

        [Theory]
        [InlineData("clip.bvr")]
        [InlineData("media/user.mp4")]
        [InlineData("thumbs/cover.jpg")]
        [InlineData("a/b/c/d.e.f")]
        [InlineData("..hidden")]
        [InlineData(".config")]
        [InlineData("file..mp4")]
        public void SafePaths(string path)
        {
            Assert.True(ProjectPaths.IsSafeRelativePath(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("..")]
        [InlineData("../secret")]
        [InlineData("media/../../etc/passwd")]
        [InlineData("media/..")]
        [InlineData("./clip.bvr")]
        [InlineData("media/./user.mp4")]
        [InlineData("/etc/passwd")]
        [InlineData("/")]
        [InlineData("media/")]
        [InlineData("media//user.mp4")]
        [InlineData("..\\..\\x")]
        [InlineData("media\\user.mp4")]
        [InlineData("C:/Windows/x")]
        [InlineData("C:x")]
        [InlineData("file:///etc/passwd")]
        [InlineData("content://media/1")]
        [InlineData("~/x")]
        [InlineData("media/us er.mp4")]
        [InlineData("media/user.mp4\0.txt")]
        [InlineData("%2e%2e/x")]
        [InlineData("медиа/x.mp4")]
        public void UnsafePaths(string path)
        {
            Assert.False(ProjectPaths.IsSafeRelativePath(path));
        }

        [Fact]
        public void PathLengthLimit()
        {
            Assert.True(ProjectPaths.IsSafeRelativePath(new string('a', 255)));
            Assert.False(ProjectPaths.IsSafeRelativePath(new string('a', 256)));
        }

        [Fact]
        public void ComposesStorePaths()
        {
            Assert.Equal("projects/p1", ProjectPaths.ProjectDir("p1"));
            Assert.Equal("projects/p1/media/user.mp4", ProjectPaths.InProject("p1", "media/user.mp4"));
            Assert.Equal("root/p1/project.json", ProjectPaths.InProject("p1", ProjectPaths.ProjectFile, "root"));
            Assert.Throws<ArgumentException>(() => ProjectPaths.ProjectDir(".."));
            Assert.Throws<ArgumentException>(() => ProjectPaths.InProject("p1", "../p2/project.json"));
        }
    }
}
