using System;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using NUnit.Framework;
using UnityEngine;

namespace Ballverse.Studio.Tests
{
    /// <summary>
    /// Editor checks of the studio runtime that need the Unity player APIs but no GPU work (EditMode, batch mode). The pure logic
    /// is covered by core-tests (Core/Studio) and by the .NET harness in Tests/DotNet~ (storage, clips, export pipeline).
    /// </summary>
    public class StudioEditModeTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Application.temporaryCachePath, "studio-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void CompositorShaderIsInResources()
        {
            Assert.IsNotNull(Resources.Load<Shader>(StudioCompositor.ShaderResource), "Resources/" + StudioCompositor.ShaderResource);
        }

        [Test]
        public void FileStoreReplacesAtomicallyOnThePlayerRuntime()
        {
            var store = new UnityFileStore(Path.Combine(_root, "data"));
            store.WriteAllText("projects/p1/project.json.tmp", "v1");
            store.Flush("projects/p1/project.json.tmp");
            store.Replace("projects/p1/project.json.tmp", "projects/p1/project.json");
            store.WriteAllText("projects/p1/project.json.tmp", "v2");
            store.Flush("projects/p1/project.json.tmp");
            store.Replace("projects/p1/project.json.tmp", "projects/p1/project.json");
            Assert.AreEqual("v2", store.ReadAllText("projects/p1/project.json"));
            CollectionAssert.AreEqual(new[] { "project.json" }, store.ListFiles("projects/p1"));
        }

        [Test]
        public void ProjectsSurviveARoundTripThroughTheRepository()
        {
            var storage = new StudioStorage(Path.Combine(_root, "data"), Path.Combine(_root, "runs"), Path.Combine(_root, "export"));
            var clips = new ClipLibrary(storage);
            RunRecording run = clips.BeginRun();
            File.WriteAllBytes(run.ReplayPath, new byte[] { 1, 2, 3 });
            run.Commit("ring_escape", 600);
            var library = new ProjectLibrary(clips.Store, clips.Clock, clips.Ids);
            ProjectDocument doc = library.CreateFromRun(run.RunId);
            Assert.AreEqual("Ring Escape", doc.Name);
            Assert.IsTrue(library.Open(doc.Id).Success);
            Assert.That(storage.CleanupAtStartup(clips.Store), Does.Contain("runs: 0"));
        }

        [Test]
        public void ClockAndIdsAreUsableByCore()
        {
            Assert.IsTrue(ProjectValidator.IsTimestamp(StudioClock.Instance.NowIso()));
            Assert.IsTrue(ProjectPaths.IsSafeId(GuidIdSource.Instance.NewId()));
        }
    }
}
