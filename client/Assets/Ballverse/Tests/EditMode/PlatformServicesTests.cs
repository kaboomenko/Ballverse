using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.App;
using Ballverse.Platform;
using NUnit.Framework;
using UnityEngine;

namespace Ballverse.Tests
{
    public sealed class PlatformServicesTests
    {
        readonly List<string> _errors = new List<string>();

        void Report(string context, Exception e) => _errors.Add(context);

        [SetUp]
        public void SetUp() => _errors.Clear();

        [Test]
        public void LifecycleRaisesOnStateChangesOnly()
        {
            var lifecycle = new AppLifecycle(Report);
            var log = new List<string>();
            lifecycle.Paused += () => log.Add("paused");
            lifecycle.Resumed += () => log.Add("resumed");
            lifecycle.FocusChanged += f => log.Add("focus:" + f);
            lifecycle.Quitting += () => log.Add("quit");

            lifecycle.NotifyPause(false); // already running
            lifecycle.NotifyFocus(true);  // already focused
            lifecycle.NotifyFocus(false);
            lifecycle.NotifyPause(true);
            lifecycle.NotifyPause(true);
            lifecycle.NotifyPause(false);
            lifecycle.NotifyFocus(true);
            lifecycle.NotifyQuitting();
            lifecycle.NotifyQuitting();
            lifecycle.NotifyPause(true); // ignored after quit

            CollectionAssert.AreEqual(new[] { "focus:False", "paused", "resumed", "focus:True", "quit" }, log);
            Assert.IsTrue(lifecycle.IsQuitting);
            Assert.IsFalse(lifecycle.IsPaused);
        }

        [Test]
        public void ThrowingSubscriberDoesNotBlockOthers()
        {
            var lifecycle = new AppLifecycle(Report);
            bool secondCalled = false;
            lifecycle.Paused += () => throw new InvalidOperationException("boom");
            lifecycle.Paused += () => secondCalled = true;
            lifecycle.NotifyPause(true);
            Assert.IsTrue(secondCalled);
            CollectionAssert.AreEqual(new[] { "AppLifecycle.Paused" }, _errors);
        }

        [Test]
        public void FrameRateIsClampedAndAppliedOnChange()
        {
            var applied = new List<int>();
            var policy = new FrameRatePolicy(applied.Add);
            Assert.AreEqual(60, policy.DefaultFps);
            policy.ResetToDefault();
            policy.SetTarget(60);
            policy.SetTarget(30);
            policy.SetTarget(120);
            policy.SetTarget(1);
            CollectionAssert.AreEqual(new[] { 60, 30, 60, FrameRatePolicy.MinFps }, applied);
            Assert.AreEqual(FrameRatePolicy.MinFps, policy.TargetFps);
        }

        [Test]
        public void ScreenStaysAwakeWhileAnyTokenIsHeld()
        {
            var applied = new List<bool>();
            var awake = new ScreenAwake(applied.Add);
            IDisposable sim = awake.Acquire("simulation");
            IDisposable export = awake.Acquire("export");
            Assert.IsTrue(awake.IsKeptAwake);
            CollectionAssert.AreEquivalent(new[] { "simulation", "export" }, awake.ActiveReasons);

            sim.Dispose();
            sim.Dispose(); // idempotent
            Assert.IsTrue(awake.IsKeptAwake);
            export.Dispose();
            Assert.IsFalse(awake.IsKeptAwake);
            CollectionAssert.AreEqual(new[] { true, false }, applied);
            Assert.Throws<ArgumentException>(() => awake.Acquire(""));
        }

        [Test]
        public void SafeAreaConvertsUnityBottomLeftRectToEdgeInsets()
        {
            // 1080x2400 portrait phone: 120 px status bar/notch at the top, 60 px home indicator at the bottom.
            var insets = SafeAreaProvider.FromUnityRect(new Rect(0f, 60f, 1080f, 2220f), 1080, 2400);
            Assert.AreEqual(0, insets.Left);
            Assert.AreEqual(0, insets.Right);
            Assert.AreEqual(120, insets.Top);
            Assert.AreEqual(60, insets.Bottom);
            Assert.AreEqual(0.05f, insets.TopNormalized, 1e-6f);
            Assert.AreEqual(0.025f, insets.BottomNormalized, 1e-6f);

            var clipped = SafeAreaProvider.FromUnityRect(new Rect(-10f, -10f, 2000f, 3000f), 1080, 2400);
            Assert.AreEqual(new SafeAreaInsets(1080, 2400, 0, 0, 0, 0), clipped);
            Assert.IsTrue(SafeAreaProvider.FromUnityRect(new Rect(0f, 0f, 10f, 10f), 0, 0).IsEmpty);
        }

        [Test]
        public void SafeAreaRoundsLikeUnityAndClipsInvertedRects()
        {
            // Mathf.RoundToInt rounds .5 to even: 60.5 -> 60, 61.5 -> 62.
            var insets = SafeAreaInsets.FromBottomLeftRect(0f, 60.5f, 1080f, 2338.5f, 1080, 2400);
            Assert.AreEqual(60, insets.Bottom);
            Assert.AreEqual(2400 - 2338, insets.Top);
            // xMax below xMin collapses to an empty safe width instead of negative insets.
            var inverted = SafeAreaInsets.FromBottomLeftRect(500f, 0f, 100f, 2400f, 1080, 2400);
            Assert.AreEqual(500, inverted.Left);
            Assert.AreEqual(580, inverted.Right);
            Assert.AreEqual(new SafeAreaInsets(0, 0, 0, 0, 0, 0), SafeAreaInsets.FromBottomLeftRect(float.NaN, 0f, 1f, 1f, -1, 10));
        }

        [Test]
        public void PlatformContractsLiveInAnEngineFreeAssemblyWithoutProjectReferences()
        {
            // Feature assemblies (UI, Services, Media, Studio, Sim, Audio) reference Ballverse.Platform; Ballverse.App
            // references them, so the contracts must not live in App (cyclic asmdef) and must not pull in the engine.
            System.Reflection.Assembly contracts = typeof(IScreenAwake).Assembly;
            Assert.AreEqual("Ballverse.Platform", contracts.GetName().Name);
            foreach (Type t in new[] { typeof(IAppLifecycle), typeof(IFrameRatePolicy), typeof(ISafeAreaProvider), typeof(SafeAreaInsets), typeof(IAppPaths) })
                Assert.AreSame(contracts, t.Assembly, t.Name);
            foreach (System.Reflection.AssemblyName reference in contracts.GetReferencedAssemblies())
            {
                StringAssert.DoesNotStartWith("UnityEngine", reference.Name);
                StringAssert.DoesNotStartWith("UnityEditor", reference.Name);
                StringAssert.DoesNotStartWith("Ballverse", reference.Name);
            }
        }

        [Test]
        public void ProjectsFolderMatchesTheCoreProjectStoreLayout()
        {
            // Core's project store (Ballverse.Core.Projects.ProjectPaths.ProjectsRoot) owns the layout; looked up by
            // reflection so this assembly also compiles against a Core without the Projects module.
            Type projectPaths = typeof(Ballverse.Core.Rng).Assembly.GetType("Ballverse.Core.Projects.ProjectPaths", false);
            if (projectPaths == null) Assert.Ignore("Ballverse.Core.Projects is not part of this Core yet.");
            System.Reflection.FieldInfo root = projectPaths.GetField("ProjectsRoot", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(root, "ProjectPaths.ProjectsRoot");
            Assert.AreEqual(root.GetValue(null), AppPaths.ProjectsDirName);
        }

        [Test]
        public void SafeAreaProviderRaisesOnlyOnChange()
        {
            var provider = new SafeAreaProvider(Report);
            var seen = new List<SafeAreaInsets>();
            provider.Changed += seen.Add;
            var a = new SafeAreaInsets(1080, 2400, 0, 120, 0, 60);
            Assert.IsTrue(provider.Update(a));
            Assert.IsFalse(provider.Update(a));
            Assert.IsTrue(provider.Update(new SafeAreaInsets(2400, 1080, 120, 0, 60, 0)));
            Assert.AreEqual(2, seen.Count);
            Assert.AreEqual(a, seen[0]);
        }

        [Test]
        public void AppPathsSplitPersistentAndCacheAndRejectUnsafeNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "ballverse-paths-" + Path.GetRandomFileName());
            try
            {
                var paths = new AppPaths(Path.Combine(root, "p"), Path.Combine(root, "c"));
                Assert.AreEqual(Path.Combine(paths.PersistentRoot, "projects"), paths.Projects);
                StringAssert.StartsWith(paths.CacheRoot, paths.RenderStreamCache);
                StringAssert.StartsWith(paths.CacheRoot, paths.ExportScratch);
                StringAssert.StartsWith(paths.PersistentRoot, paths.Settings);

                paths.EnsureStandardDirs();
                Assert.IsTrue(Directory.Exists(paths.Projects));
                Assert.IsTrue(Directory.Exists(paths.Thumbnails));
                Assert.IsTrue(Directory.Exists(paths.EnsurePersistentDir("presets")));

                foreach (string bad in new[] { "", "..", "a/b", "a\\b", "Upper", "dot.dot", new string('a', 65) })
                    Assert.Throws<ArgumentException>(() => paths.EnsureCacheDir(bad), bad);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
