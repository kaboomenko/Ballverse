using System;
using System.IO;
using System.Threading;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Projects;
using Ballverse.Core.Audio;
using Ballverse.Core.Config;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Studio;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.ViewModels;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>Ballverse.Sim live runtime and the App's pure pieces, without the app host.</summary>
    public sealed class SimRuntimeTests
    {
        string _root;
        ClipLibrary _clips;
        IModeDefinition _mode;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "bv-sim-tests-" + Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(Path.Combine(_root, "data"), Path.Combine(_root, "cache"));
            paths.EnsureStandardDirs();
            _clips = new ClipLibrary(StudioStorage.FromAppPaths(paths));
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out _mode));
        }

        [TearDown]
        public void TearDown() => TestApp.DeleteRoot(_root);

        LiveSimController RunToEnd(LiveSimController live, int maxFrames = 6000)
        {
            double now = 0;
            for (int i = 0; i < maxFrames && !live.IsCompleted; i++)
            {
                now += 1.0 / 60.0;
                live.AdvanceDisplayFrame(60, now);
            }
            Assert.IsTrue(live.IsCompleted);
            return live;
        }

        [Test]
        public void LiveRunRecordsAReplayThatResimulatesWithVerifiedHashes()
        {
            ParamSet values = TestApp.QuickRingEscape(_mode, DeviceTier.B);
            var live = new LiveSimController(new LiveSimOptions { Tier = DeviceTier.B, AppVersion = "test" });
            LiveRunOutcome finished = null;
            live.RunFinished += o => finished = o;
            live.Begin(_mode, values, 2026UL, new RunRecordingAdapter(_clips.BeginRun()));
            RunToEnd(live);

            Assert.IsNotNull(finished);
            Assert.IsTrue(finished.Finished);
            Assert.IsNotNull(finished.RunId);
            Assert.IsTrue(GameCompletedRule.IsGameCompleted(finished.Run));
            ClipEntry entry = _clips.GetRun(finished.RunId);
            Assert.IsNotNull(entry);
            Assert.IsTrue(entry.HasReplay && entry.HasRenderStream && entry.HasEvents);

            string dir = _clips.FullDirectory(entry);
            byte[] bvr = File.ReadAllBytes(Path.Combine(dir, ProjectPaths.ClipReplayFile));
            ModeReplayResult replay = ModeReplay.Resimulate(_mode, bvr);
            Assert.IsTrue(replay.IsVerified, replay.Status + " " + replay.Message);

            // The App's stream rebuilder (Studio seam) regenerates an evicted stream of the same length.
            string rebuilt = Path.Combine(_root, "rebuilt.bvs");
            string events = Path.Combine(_root, "rebuilt.bve");
            new ModeStreamRebuilder(ReleaseModes.CreateRegistry()).Rebuild(Path.Combine(dir, ProjectPaths.ClipReplayFile), rebuilt, events, null, CancellationToken.None);
            using (var a = new RenderStreamReader(File.OpenRead(Path.Combine(dir, ProjectPaths.ClipRenderFile))))
            using (var b = new RenderStreamReader(File.OpenRead(rebuilt)))
            {
                Assert.AreEqual(a.TickCount, b.TickCount);
            }
            Assert.IsTrue(File.Exists(events));
        }

        [Test]
        public void StreamRebuilderRefusesACorruptReplay()
        {
            string bad = Path.Combine(_root, "bad.bvr");
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4, 5 });
            var rebuilder = new ModeStreamRebuilder(ReleaseModes.CreateRegistry());
            Assert.Throws<InvalidDataException>(() => rebuilder.Rebuild(bad, Path.Combine(_root, "x.bvs"), Path.Combine(_root, "x.bve"), null, CancellationToken.None));
        }

        [Test]
        public void ShortAbandonedRunIsNotKeptButAKeptOneIs()
        {
            ParamSet values = TestApp.QuickRingEscape(_mode, DeviceTier.B);
            var live = new LiveSimController();
            live.Begin(_mode, values, 7UL, new RunRecordingAdapter(_clips.BeginRun()));
            for (int i = 0; i < 60; i++) live.AdvanceDisplayFrame(60, i / 60.0);
            LiveRunOutcome dropped = live.End(GameRunEnd.Abandoned, false);
            Assert.IsNull(dropped.RunId);
            Assert.AreEqual(0, _clips.ListRuns().Count);
            Assert.IsFalse(GameCompletedRule.IsGameCompleted(dropped.Run));

            live.Begin(_mode, values, 8UL, new RunRecordingAdapter(_clips.BeginRun()));
            for (int i = 0; i < 60; i++) live.AdvanceDisplayFrame(60, i / 60.0);
            LiveRunOutcome kept = live.End(GameRunEnd.Abandoned, true);
            Assert.IsNotNull(kept.RunId, "«В видео» keeps a short run");
            Assert.AreEqual(1, _clips.ListRuns().Count);
            live.Dispose();
        }

        [Test]
        public void PacingFollowsTheDisplayRateAndTimeScale()
        {
            ParamSet values = TestApp.QuickRingEscape(_mode, DeviceTier.B);
            var live = new LiveSimController();
            live.Begin(_mode, values, 9UL, null);
            for (int i = 0; i < 120; i++) live.AdvanceDisplayFrame(120, i / 120.0);
            Assert.AreEqual(60, live.Tick, "120 Hz display: one tick every second frame");
            live.SetPacing(2f);
            int before = live.Tick;
            for (int i = 0; i < 30; i++) live.AdvanceDisplayFrame(60, 1 + i / 60.0);
            Assert.AreEqual(60, live.Tick - before, "×2 at 60 Hz: two ticks per frame");
            live.Dispose();
        }

        [Test]
        public void GestureTrackerTurnsTouchesIntoTickCommands()
        {
            var g = new ArenaGestureTracker();
            g.OnTouch(ArenaTouchPhase.Down, 0.5f, 0.5f, 1, 0.0);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand down));
            Assert.AreEqual(ArenaInputTypes.HorizontalDrag, down.Type);
            Assert.AreEqual(1, down.B);
            Assert.AreEqual(5000, down.C);

            g.OnTouch(ArenaTouchPhase.Up, 0.5f, 0.5f, 1, 0.1);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand release));
            Assert.AreEqual(0, release.B);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand tap));
            Assert.AreEqual(ArenaInputTypes.Tap, tap.Type);
            Assert.IsFalse(g.TryDequeue(out _));

            g.OnTouch(ArenaTouchPhase.Down, 0.2f, 0.5f, 2, 1.0);
            g.TryDequeue(out _);
            g.OnTouch(ArenaTouchPhase.Move, 0.3f, 0.5f, 2, 1.05);
            g.Update(1.05);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand drag));
            Assert.Greater(drag.A, 0, "rightward drag velocity");
            g.Update(1.5);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand still));
            Assert.AreEqual(0, still.A, "a still finger decays to zero velocity");
            g.OnTouch(ArenaTouchPhase.Up, 0.3f, 0.5f, 2, 1.6);
            Assert.IsTrue(g.TryDequeue(out ArenaCommand up));
            Assert.AreEqual(0, up.B);
            Assert.IsFalse(g.TryDequeue(out _), "a moved press is not a tap");
        }

        [TestCase(48000)]
        [TestCase(44100)]
        public void AudioEngineMixesQueuedHitsAtTheDeviceRate(int rate)
        {
            var engine = new LiveAudioEngine(256);
            var buffer = new float[1024 * 2];
            engine.Render(buffer, 2, rate);
            Assert.That(buffer, Has.All.EqualTo(0f), "silent without a pack");

            engine.SetPack(SoundPackCatalog.Get(SoundPackId.Pop), LiveSoundSetup.BankFor(SoundPackId.Pop), null);
            engine.Render(buffer, 2, rate); // the audio thread adopts the new mixer
            engine.BeginRun();
            engine.OnSound(SoundEvent.Hit(0, 1, 5f), 0);
            float peak = 0f;
            for (int block = 0; block < 8; block++)
            {
                engine.Render(buffer, 2, rate);
                foreach (float s in buffer)
                {
                    Assert.IsFalse(float.IsNaN(s));
                    peak = Math.Max(peak, Math.Abs(s));
                }
            }
            Assert.Greater(peak, 0.001f);

            engine.Muted = true;
            engine.OnSound(SoundEvent.Hit(10, 1, 5f), 600);
            for (int block = 0; block < 4; block++) engine.Render(buffer, 2, rate);
            Assert.That(buffer, Has.All.EqualTo(0f));
        }

        [Test]
        public void ProceduralMusicTracksHaveTheirLength()
        {
            float[] pcm = ProceduralMusic.Render("calm_waves");
            Assert.IsNotNull(pcm);
            Assert.AreEqual(ProceduralMusic.LengthUs * AudioConstants.SampleRate / 1_000_000L * 2, pcm.Length);
            Assert.IsNull(ProceduralMusic.Render("no_such_track"));
        }

        [TestCase("android", "SM-S918B", "Adreno (TM) 740", 8192, DeviceTier.A)]
        [TestCase("android", "x", "Adreno (TM) 642L", 8192, DeviceTier.B)]
        [TestCase("android", "x", "Adreno (TM) 610", 4096, DeviceTier.C)]
        [TestCase("android", "x", "Mali-G710 MC10", 4096, DeviceTier.B)]
        [TestCase("android", "x", "Mali-G57 MC2", 6144, DeviceTier.C)]
        [TestCase("ios", "iPhone15,2", "Apple A16 GPU", 6144, DeviceTier.A)]
        [TestCase("ios", "iPhone12,1", "Apple A13 GPU", 4096, DeviceTier.B)]
        [TestCase("ios", "iPhone10,4", "Apple A11 GPU", 2048, DeviceTier.C)]
        public void DeviceTierFromChipAndMemory(string platform, string model, string gpu, int memoryMb, DeviceTier expected)
        {
            var device = new FakeDevice { Platform = platform, Model = model, GpuName = gpu, SystemMemoryMb = memoryMb };
            Assert.AreEqual(expected, DeviceTierDetector.Detect(device, RemoteConfigValues.Defaults));
        }

        [Test]
        public void QualityPreferenceAndPowerSavingAdjustTheTier()
        {
            Assert.AreEqual(DeviceTier.A, DeviceTierDetector.Effective(DeviceTier.C, QualityPreference.High, false));
            Assert.AreEqual(DeviceTier.C, DeviceTierDetector.Effective(DeviceTier.A, QualityPreference.Economy, false));
            Assert.AreEqual(DeviceTier.B, DeviceTierDetector.Effective(DeviceTier.A, QualityPreference.Auto, true));
            Assert.AreEqual(DeviceTier.C, DeviceTierDetector.Effective(DeviceTier.C, QualityPreference.Auto, true));
        }

        [Test]
        public void SettingsRoundTripAndCorruptFileFallsBackToDefaults()
        {
            string dir = Path.Combine(_root, "settings-test");
            var store = new AppSettingsStore(dir, (c, e) => { });
            Assert.IsTrue(store.IsNew);
            store.Update(d =>
            {
                d.FirstRunDone = true;
                d.ExportCount = 4;
                d.LastParams["ring_escape"] = "{}";
            });
            Assert.IsTrue(store.Save());
            var again = new AppSettingsStore(dir, (c, e) => { });
            Assert.IsFalse(again.IsNew);
            Assert.IsTrue(again.Data.FirstRunDone);
            Assert.AreEqual(4, again.Data.ExportCount);
            Assert.AreEqual("{}", again.Data.LastParams["ring_escape"]);

            File.WriteAllText(Path.Combine(dir, AppSettingsStore.FileName), "{ not json");
            int errors = 0;
            var broken = new AppSettingsStore(dir, (c, e) => errors++);
            Assert.IsFalse(broken.Data.FirstRunDone);
            Assert.AreEqual(1, errors);
        }

        [Test]
        public void RingEscapeIsAutomaticByDefault()
        {
            ParamSet defaults = ModeParams.Defaults(_mode, DeviceTier.B);
            Assert.IsFalse(ModeInteraction.IsPlayerControlled(_mode, defaults));
        }
    }
}
