using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.App;
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
            var insets = SafeAreaInsets.FromUnityRect(new Rect(0f, 60f, 1080f, 2220f), 1080, 2400);
            Assert.AreEqual(0, insets.Left);
            Assert.AreEqual(0, insets.Right);
            Assert.AreEqual(120, insets.Top);
            Assert.AreEqual(60, insets.Bottom);
            Assert.AreEqual(0.05f, insets.TopNormalized, 1e-6f);
            Assert.AreEqual(0.025f, insets.BottomNormalized, 1e-6f);

            var clipped = SafeAreaInsets.FromUnityRect(new Rect(-10f, -10f, 2000f, 3000f), 1080, 2400);
            Assert.AreEqual(new SafeAreaInsets(1080, 2400, 0, 0, 0, 0), clipped);
            Assert.IsTrue(SafeAreaInsets.FromUnityRect(new Rect(0f, 0f, 10f, 10f), 0, 0).IsEmpty);
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
