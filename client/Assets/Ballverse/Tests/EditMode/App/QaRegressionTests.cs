using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Config;
using Ballverse.Core.Modes;
using Ballverse.Core.Monetization;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Projects;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Ballverse.Media;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.Layout;
using Ballverse.UI.ViewModels;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>
    /// Regression tests for the QA round-1 findings of the app wiring (live audio, mode-owned input, live look in the studio,
    /// real-rate pacing, IN2 / IN3 interstitial breaks, studio preview races, the modes_disabled kill switch, the degraded
    /// studio, power-saver haptics, localization fallbacks, the Settings watermark, generation, the ATT bridge). Public API
    /// only (the Unity test assembly is separate from Ballverse.App).
    /// </summary>
    public sealed class QaRegressionTests
    {
        TestApp _app;

        [TearDown]
        public void TearDown()
        {
            _app?.Dispose();
            _app = null;
        }

        TestApp Boot(Action<TestApp> setup = null)
        {
            _app = new TestApp();
            setup?.Invoke(_app);
            return _app.Boot();
        }

        static string Errors(TestApp app) => string.Join("; ", app.Errors);

        // ------------------------------------------------------------------ live audio (blocker)

        [Test]
        public void LiveAudioOutputKeepsItsOwnFilterAudibleAndAnAppWideListener()
        {
            string path = FindSource(Path.Combine("Sim", "Unity", "LiveAudioOutput.cs"));
            if (path == null) Assert.Ignore("LiveAudioOutput.cs source not found from the test directory");
            string code = File.ReadAllText(path);
            // bypassEffects also bypasses the component's own OnAudioFilterRead (the only thing producing sound).
            Assert.IsFalse(Regex.IsMatch(code, @"bypassEffects\s*=\s*true"), "the source must not bypass effects");
            StringAssert.Contains("OnAudioFilterRead", code);
            // Unity mixes nothing without an enabled AudioListener; the boot scene has no camera.
            StringAssert.Contains("AddComponent<AudioListener>()", code);
            StringAssert.Contains("EnsureListener();", code);
        }

        // ------------------------------------------------------------------ mode-owned input (blocker)

        [Test]
        public void HelixDropMapperSendsSpinWhileDraggingReleaseOnLiftAndNothingForTaps()
        {
            var mapper = new HelixDropInputMapper();
            mapper.BeginRun();
            ParamSet player = ParamSet.Empty.With(ArenaControl.Param, ParamValue.String("player"));
            Assert.IsTrue(mapper.AcceptsInput(player));
            Assert.IsFalse(mapper.AcceptsInput(ParamSet.Empty.With(ArenaControl.Param, ParamValue.String("auto"))), "«Авто-ролик»");
            Assert.IsFalse(mapper.AcceptsInput(ParamSet.Empty.With(ArenaControl.Param, ParamValue.String("bot"))), "«Бот» records no touches");

            Assert.IsTrue(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Press, 0f, 0.5f, 0.5f), player, 390f, out ArenaCommand press));
            Assert.AreEqual(HelixDropInputMapper.Spin, press.Type);
            Assert.AreEqual(0, press.A, "the finger holds the tower");

            // A slow right drag at 0.25 arena widths/s on a 390 pt arena = 97.5 pt/s → M05 FromSwipe = −1021 mrad/s.
            Assert.IsTrue(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Drag, 0.25f, 0.6f, 0.5f), player, 390f, out ArenaCommand drag));
            Assert.AreEqual(HelixDropInputMapper.Spin, drag.Type);
            Assert.AreEqual(-1021, drag.A, "M05 HelixDropInput.FromSwipe(97.5, 1) — right drag turns the front right (negative)");
            Assert.IsFalse(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Drag, 0.25f, 0.6f, 0.5f), player, 390f, out _), "unchanged speed is not re-sent");

            Assert.IsTrue(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Release, 0.25f, 0.6f, 0.5f), player, 390f, out ArenaCommand release));
            Assert.AreEqual(HelixDropInputMapper.Release, release.Type, "finger up = Release (the mode applies 9-tick inertia)");
            Assert.AreEqual(-1021, release.A);

            Assert.IsFalse(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Tap, 0f, 0.5f, 0.5f), player, 390f, out _), "M05 has no tap");

            ParamSet sensitive = player.With(HelixDropInputMapper.SwipeSensitivityParam, ParamValue.Float(2.0));
            Assert.AreEqual(-2042, HelixDropInputMapper.FromSwipe(97.5f, 2f));
            Assert.IsTrue(mapper.TryMap(new ArenaGesture(ArenaGestureKind.Drag, 0.25f, 0.6f, 0.5f), sensitive, 390f, out ArenaCommand fast));
            Assert.AreEqual(-2042, fast.A, "live swipeSensitivity applies");
            Assert.AreEqual(-6283, HelixDropInputMapper.FromSwipe(1e6f, 1f), "clamped to 360 °/s");
        }

        [Test]
        public void LiveRunRecordsTheModesOwnInputEncodingAndModesWithoutAMapperGetNone()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition ring));
            IModeDefinition helix = new InteractiveAs(ring, ModeCatalog.Get(ModeCode.M05));
            ParamSet values = TestApp.QuickRingEscape(ring, DeviceTier.B);
            string dir = Path.Combine(Path.GetTempPath(), "bv-input-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var live = new LiveSimController();
                Assert.IsFalse(live.TakesPlayerInput(ring, values), "Ring Escape is automatic");
                Assert.IsTrue(live.TakesPlayerInput(helix, values));
                live.PointsPerArenaWidth = 390f;
                var recording = new TempRecording(dir);
                live.Begin(helix, values, 3UL, recording);
                Assert.IsTrue(live.AcceptsInput);

                double t = 0;
                live.OnTouch(ArenaTouchPhase.Down, 0.5f, 0.5f, 1, t);
                live.AdvanceDisplayFrame(60, t);
                t = 0.1;
                live.OnTouch(ArenaTouchPhase.Move, 0.55f, 0.5f, 1, t);
                live.AdvanceDisplayFrame(60, t);
                t = 0.12;
                live.OnTouch(ArenaTouchPhase.Up, 0.55f, 0.5f, 1, t);
                live.AdvanceDisplayFrame(60, t);
                // A tap: press + release, no tap input.
                t = 0.5;
                live.OnTouch(ArenaTouchPhase.Down, 0.5f, 0.5f, 2, t);
                live.AdvanceDisplayFrame(60, t);
                t = 0.55;
                live.OnTouch(ArenaTouchPhase.Up, 0.5f, 0.5f, 2, t);
                live.AdvanceDisplayFrame(60, t);
                for (int i = 0; i < 30; i++) live.AdvanceDisplayFrame(60, 1 + i / 60.0);
                LiveRunOutcome outcome = live.End(GameRunEnd.Abandoned, true);
                Assert.IsNotNull(outcome.RunId, "kept");
                live.Dispose();

                ReplayFile replay = ReplayReader.Read(File.ReadAllBytes(recording.ReplayPath));
                string got = string.Join(" ", replay.Inputs.Select(i => i.Type + ":" + i.Values[0]));
                Assert.AreEqual("1:0 1:-1021 2:-1021 1:0 2:0", got, "Spin 0 on press, Spin while dragging, Release on lift; taps add nothing");

                // Without a mapper for the mode (empty registry), touches never reach the run.
                var noMapper = new LiveSimController(new LiveSimOptions { InputMappers = new ArenaInputMapperRegistry() });
                noMapper.Begin(helix, values, 3UL, null);
                Assert.IsFalse(noMapper.AcceptsInput);
                Assert.IsFalse(noMapper.TakesPlayerInput(helix, values));
                noMapper.Dispose();
            }
            finally
            {
                TestApp.DeleteRoot(dir);
            }
        }

        [Test]
        public void ModeInteractionTreatsOnlyControlPlayerAsPlayerControlled()
        {
            IModeDefinition helix = new InteractiveAs(RingEscape(), ModeCatalog.Get(ModeCode.M05));
            Assert.IsTrue(ModeInteraction.IsPlayerControlled(helix, ParamSet.Empty), "no control parameter: the player drives");
            Assert.IsTrue(ModeInteraction.IsPlayerControlled(helix, ParamSet.Empty.With("control", ParamValue.String("player"))));
            Assert.IsFalse(ModeInteraction.IsPlayerControlled(helix, ParamSet.Empty.With("control", ParamValue.String("bot"))), "bot is not the player");
            Assert.IsFalse(ModeInteraction.IsPlayerControlled(helix, ParamSet.Empty.With("control", ParamValue.String("auto"))));
        }

        // ------------------------------------------------------------------ live look in the studio (major)

        [Test]
        public void ALiveThemeChangeReachesTheStudioLookAtItsTick()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            for (int i = 0; i < 120; i++) app.Frame();
            string startTheme = app.Sim.CurrentValues.TryGet(CommonParams.Theme, out ParamValue t0) ? t0.AsString : null;
            string newTheme = startTheme == "Space" ? "Ocean" : "Space";
            app.Sim.Params.Apply(CommonParams.Theme, ParamValue.String(newTheme));
            app.RunToEnd();
            string dir = app.Studio.Clips.FullDirectory(app.Studio.Clips.GetRun(app.Context.Live.Outcome.RunId));
            ReplayFile replay = ReplayReader.Read(File.ReadAllBytes(Path.Combine(dir, ProjectPaths.ClipReplayFile)));
            ReplayParamChange change = replay.ParamChanges.First(c => c.ParamId == CommonParams.Theme);

            Assert.IsTrue(ClipLookTimeline.TryRead(dir, app.Context.Modes, out ClipLookTimeline look));
            Assert.AreEqual(1, look.ChangeCount);
            Assert.IsTrue(look.Advance(0, out ParamSet atStart));
            Assert.AreEqual(startTheme, atStart.Get(CommonParams.Theme).AsString);
            Assert.IsFalse(look.Advance((int)change.Tick - 1, out _), "no change before the recorded tick");
            Assert.IsTrue(look.Advance((int)change.Tick, out ParamSet after));
            Assert.AreEqual(newTheme, after.Get(CommonParams.Theme).AsString, "the export repeats the live change (spec §2.2)");
            Assert.IsFalse(look.Advance((int)change.Tick + 100, out _));
            Assert.IsTrue(look.Advance(0, out ParamSet back), "seeking back restores the start look");
            Assert.AreEqual(startTheme, back.Get(CommonParams.Theme).AsString);
        }

        [Test]
        public void ExportRequestMapsOntoStudioExportOptions()
        {
            var masks = new[] { new Core.Layout.FloatRect(0f, 0f, 1f, 0.1f) };
            StudioExportOptions o = StudioBackend.ToStudioOptions(new ExportRequest
            {
                Resolution = ExportResolution.P720,
                Fps = 30,
                ShowWatermark = false,
                PlatformMasks = masks,
                SaveToGallery = false,
            });
            Assert.AreEqual(ExportResolution.P720, o.Resolution);
            Assert.AreEqual(30, o.Fps);
            Assert.IsFalse(o.ShowWatermark);
            Assert.AreSame(masks, o.PlatformMasks);
            Assert.IsFalse(o.SaveToGallery);
            Assert.IsTrue(StudioBackend.ToStudioOptions(null).ShowWatermark, "defaults keep the watermark");
        }

        // ------------------------------------------------------------------ pacing at the real display rate (major)

        [TestCase(30)]
        [TestCase(45)]
        [TestCase(60)]
        public void LiveRunPlaysInRealTimeAtTheActualDisplayRate(int fps)
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            float dt = 1f / fps;
            for (int i = 0; i < fps; i++) app.Frame(dt); // 1 s warm-up: the estimate converges
            int t0 = app.Context.Live.Tick;
            for (int i = 0; i < 2 * fps; i++) app.Frame(dt); // 2 s of wall-clock time
            int ticks = app.Context.Live.Tick - t0;
            Assert.That(ticks, Is.InRange(114, 126), "2 s at " + fps + " FPS plays 2 s of simulation (1×)");
        }

        [Test]
        public void DisplayRateEstimatorFollowsTheMeasuredRateAndIgnoresHitches()
        {
            var e = new DisplayRateEstimator(60);
            for (int i = 0; i < 60; i++) e.Sample(1f / 30f);
            Assert.AreEqual(30, e.Rate);
            e.Sample(2f);
            Assert.AreEqual(30, e.Rate, "a 2 s hitch (pause) is ignored");
            for (int i = 0; i < 60; i++) e.Sample(1f / 58.8f);
            Assert.AreEqual(60, e.Rate, "snapped to the common refresh rate");
            for (int i = 0; i < 60; i++) e.Sample(1f / 55f);
            Assert.AreEqual(55, e.Rate, "an uncommon rate is used as measured");
            for (int i = 0; i < 80; i++) e.Sample(1f / 120f);
            Assert.AreEqual(120, e.Rate);
        }

        [Test]
        public void LiveControllerPacingMatchesModeSessionAdvanceDisplayFrame()
        {
            IModeDefinition mode = RingEscape();
            ParamSet values = TestApp.QuickRingEscape(mode, DeviceTier.B);
            var session = new ModeSession(mode, values, 5UL);
            var live = new LiveSimController();
            live.Begin(mode, values, 5UL, null);
            var rng = new Random(7);
            int[] rates = { 30, 45, 60, 90, 120, 144 };
            float[] scales = { 0.5f, 1f, 2f };
            for (int i = 0; i < 300; i++)
            {
                if (i % 50 == 0)
                {
                    float s = scales[rng.Next(scales.Length)];
                    session.TimeScale = s;
                    live.SetPacing(s);
                }
                int rate = rates[rng.Next(rates.Length)];
                session.AdvanceDisplayFrame(rate);
                live.AdvanceDisplayFrame(rate, i / 60.0);
                Assert.AreEqual(session.Tick, live.Tick, "frame " + i + " at " + rate + " Hz");
            }
            live.Dispose();
        }

        // ------------------------------------------------------------------ IN2 / IN3 (majors)

        [Test]
        public void MidRunRetryAndGenerateShowNoInterstitialButTheResultCardDoes()
        {
            TestApp app = Boot();
            app.StartAds();
            app.OpenQuickRun();
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            for (int i = 0; i < 60; i++) app.Frame();
            Assert.AreEqual(SimPhaseView.Running, app.Sim.Phase);

            app.Sim.Generate();
            Assert.AreEqual(0, app.Ui.AdNotices, "mid-run 🎲 is not a natural pause (IN2)");
            app.Sim.CancelGeneration();
            app.Sim.Retry();
            Assert.AreEqual(0, app.Ui.AdNotices, "mid-run ↻ is not a natural pause (IN2)");
            Assert.IsTrue(app.Context.Live.HasRun && !app.Context.Live.IsCompleted, "the run restarted at once");

            app.RunToEnd();
            Assert.IsNotNull(app.Sim.Result);
            app.Sim.Retry();
            Assert.AreEqual(1, app.Ui.AdNotices, "«Ещё раз» on the result card is a natural pause");
        }

        [Test]
        public void NoInterstitialRightAfterAnExportButAtTheNextRealBreak()
        {
            TestApp app = Boot(a =>
            {
                a.StoreConfig = new FakeStoreConfig();
                a.StoreConfig.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            });
            app.StartAds();
            app.OpenQuickRun();
            for (int i = 0; i < 400; i++) app.Frame();
            app.Sim.ToVideo();
            app.StudioModel.SetScreenActive(true);
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            app.Export.Begin();
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage, Errors(app));
            app.StudioModel.SetScreenActive(false); // the UI pushes ExportProgress / ExportDone over the studio screen
            app.Export.NewVideo();
            Assert.AreEqual(0, app.Ui.AdNotices, "IN3: never right after an export");

            app.Sim.Back(); // exit to the gallery: a natural pause, the pending interstitial shows now
            Assert.AreEqual(1, app.Ui.AdNotices);
        }

        [Test]
        public void BackToTheGalleryIsANaturalBreak()
        {
            TestApp app = Boot();
            app.StartAds();
            app.OpenQuickRun();
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            app.Sim.Back();
            Assert.AreEqual(1, app.Ui.AdNotices);
        }

        // ------------------------------------------------------------------ studio preview sequencing (major)

        [Test]
        public void ASecondProjectGetsItsPreviewWhileTheFirstIsStillOpening()
        {
            TestApp app = Boot();
            ProjectDocument a = NewProject(app);
            ProjectDocument b = NewProject(app);
            app.Studio.HoldPreviews = true;
            Assert.IsTrue(app.StudioModel.OpenProject(a.Id));
            Assert.IsTrue(app.StudioModel.OpenProject(b.Id));
            Assert.AreEqual(2, app.Studio.PendingPreviews.Count, "B's opening was not skipped");

            app.Studio.PendingPreviews[0].SetResult(true); // A's opening finishes late
            app.Frame();
            app.Studio.PendingPreviews[1].SetResult(true);
            app.Frame();

            Assert.IsTrue(app.Studio.OpenedPreviews.Where(p => p.Key == a.Id).All(p => p.Value.Disposed), "A's late preview is released");
            Assert.AreEqual(1, app.Studio.OpenedPreviews.Count(p => p.Key == b.Id && !p.Value.Disposed), "B has its preview");
            app.StudioModel.TogglePlay();
            Assert.IsTrue(app.StudioModel.IsPlaying);
        }

        [Test]
        public void SuspendingForAnExportWhileThePreviewOpensKeepsItReleased()
        {
            TestApp app = Boot();
            ProjectDocument a = NewProject(app);
            app.Studio.HoldPreviews = true;
            Assert.IsTrue(app.StudioModel.OpenProject(a.Id));
            app.StudioModel.SuspendPreview(); // what the export start does
            app.Studio.PendingPreviews[0].SetResult(true);
            app.Frame();
            Assert.IsTrue(app.Studio.OpenedPreviews.All(p => p.Value.Disposed), "released during the export (spec §7.11)");
            app.StudioModel.TogglePlay();
            Assert.IsFalse(app.StudioModel.IsPlaying);

            app.StudioModel.ResumePreview();
            Assert.AreEqual(2, app.Studio.PendingPreviews.Count);
            app.Studio.PendingPreviews[1].SetResult(true);
            app.Frame();
            Assert.AreEqual(1, app.Studio.OpenedPreviews.Count(p => !p.Value.Disposed), "reopened after the export");
        }

        [Test]
        public void AnyPreviewFailureIsReportedAndDoesNotBlockTheNextOpening()
        {
            TestApp app = Boot();
            ProjectDocument a = NewProject(app);
            app.Studio.FailNextPreviewWith = new NotSupportedException("codec");
            Assert.IsTrue(app.StudioModel.OpenProject(a.Id));
            app.Frame();
            Assert.IsTrue(app.Ui.Toasts.Any(t => t.Key == AppStrings.PreviewFailed || t.Literal != null), "toast");
            Assert.IsTrue(app.StudioModel.OpenProject(a.Id));
            app.Frame();
            Assert.AreEqual(1, app.Studio.OpenedPreviews.Count(p => !p.Value.Disposed), "the next opening works");
        }

        static IModeDefinition RingEscape()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition mode));
            return mode;
        }

        static ProjectDocument NewProject(TestApp app)
        {
            app.OpenQuickRun();
            app.RunToEnd();
            string run = app.Context.Live.Outcome.RunId;
            Assert.IsNotNull(run);
            return app.Studio.CreateFromRun(run);
        }

        // ------------------------------------------------------------------ kill switch (major)

        [Test]
        public void AModeSwitchedOffByRemoteConfigDoesNotRunFromAnyEntry()
        {
            TestApp app = Boot();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            app.OpenQuickRun();
            app.RunToEnd();
            app.Sim.SaveClip();
            string clipId = app.Studio.Clips.ListClips().First().Id;
            app.Sim.Back();

            var b = new RemoteConfigBuilder();
            b.ModesDisabled.Add(ModeCode.M01);
            app.Context.Remote.Apply(b.Build());
            Assert.IsEmpty(app.Context.Remote.GalleryModes());

            Assert.IsFalse(app.Sim.Open("ring_escape"));
            Assert.IsFalse(app.Sim.OpenWith(mode, TestApp.QuickRingEscape(mode, app.Context.Tier)));
            Assert.AreEqual(OpenTarget.None, app.ViewModels.ProjectsModel.Open(ProjectsTab.Clips, clipId), "not from a saved clip either");
            Assert.IsFalse(app.Context.Live.HasRun && !app.Context.Live.IsCompleted, "nothing runs");
            Assert.IsTrue(app.Ui.Toasts.Any(t => t.Literal != null && t.Literal.Length > 0), "the user is told");
        }

        // ------------------------------------------------------------------ degraded studio (major)

        [Test]
        public void AStudioThatFailsToStartLeavesAWorkingAppWithoutTheStudio()
        {
            TestApp app = Boot(a => a.StudioFactory = s => throw new IOException("No space left on device"));
            Assert.IsNotNull(app.Ui.ViewModels, "the UI comes up");
            Assert.IsInstanceOf<UnavailableStudioBackend>(app.Context.Studio);
            Assert.IsTrue(app.Errors.Any(e => e.Contains("Studio: start")), Errors(app));
            Assert.AreEqual(1, app.Ui.Toasts.Count(t => t.Literal != null), "one notice about the studio");

            app.OpenQuickRun();
            app.RunToEnd();
            Assert.IsNull(app.Context.Live.Outcome.RunId, "the run plays, unrecorded");
            app.Sim.ToVideo();
            app.Sim.SaveClip();
            Assert.IsFalse(app.StudioModel.HasProject);
            for (int i = 0; i < 5; i++) app.Frame();
            Assert.IsEmpty(app.ViewModels.ProjectsModel.Projects);
        }

        // ------------------------------------------------------------------ power saver (major)

        [Test]
        public void ThePowerSaverSwitchesHapticsOff()
        {
            var media = new FakeMediaOptions { LowPowerMode = true };
            TestApp app = Boot(a => a.MediaOptions = media);
            Assert.IsTrue(app.Context.Haptics.PowerSaving);
            Assert.IsFalse(app.Context.Haptics.Active);

            media.LowPowerMode = false;
            app.Lifecycle.NotifyPause(true);
            app.Lifecycle.NotifyPause(false);
            Assert.IsFalse(app.Context.Haptics.PowerSaving, "re-read on resume");
        }

        // ------------------------------------------------------------------ localization (major)

        [Test]
        public void EveryTextTheAppRaisesResolvesInEnglishAndRussian()
        {
            string enPath = FindSource(Path.Combine("UI", "Resources", "BallverseUI", "Localization", "en.json"));
            if (enPath == null) Assert.Ignore("UI string tables not found from the test directory");
            HashSet<string> en = Flatten(File.ReadAllText(enPath));
            HashSet<string> ru = Flatten(File.ReadAllText(Path.Combine(Path.GetDirectoryName(enPath), "ru.json")));

            var keys = new List<string>();
            foreach (FieldInfo f in typeof(AppStrings).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.IsLiteral) keys.Add((string)f.GetValue(null));
            }
            foreach (IModeDefinition m in ReleaseModes.CreateRegistry().All)
            {
                foreach (Preset p in m.BuiltInPresets)
                {
                    if (!string.IsNullOrEmpty(p.NameKey)) keys.Add(p.NameKey);
                }
            }
            // Result-card stat labels are keys the UI resolves itself (ResultStat.LabelKey): they need the UI tables (open issue).
            var labelKeys = new HashSet<string> { AppStrings.ResultStatScore, AppStrings.ResultStatProgress };
            var missing = new List<string>();
            foreach (string key in keys.Distinct())
            {
                if (labelKeys.Contains(key)) continue;
                bool inTables = en.Contains(key) && ru.Contains(key);
                bool fallback = AppText.TryGetFallback(key, "en", out string e) && AppText.TryGetFallback(key, "ru", out string r) && e != r;
                if (!inTables && !fallback) missing.Add(key);
            }
            Assert.IsEmpty(missing, "keys with neither a UI table entry (en + ru) nor an App fallback");
        }

        [Test]
        public void ARussianDeviceSeesRussianPresetNamesAndToasts()
        {
            TestApp app = Boot(a => a.Device.LanguageTag = "ru-RU");
            Assert.AreEqual("Классика", app.Sim.Presets.CurrentName.Literal, "first run: Ring Escape «Классика»");
            app.Context.Notices.Toast(AppStrings.RunTooShort);
            Assert.AreEqual("Запуск слишком короткий для видео", app.Ui.Toasts.Last().Literal);
            app.Context.Notices.Toast("sim.clip_saved");
            Assert.AreEqual("sim.clip_saved", app.Ui.Toasts.Last().Key, "keys the UI tables have stay keys");
        }

        // ------------------------------------------------------------------ watermark (minor)

        [Test]
        public void TheSettingsWatermarkSwitchIsTheDefaultOfNewVideos()
        {
            TestApp app = Boot(a =>
            {
                a.StoreConfig = new FakeStoreConfig();
                a.StoreConfig.InitiallyOwned.Add(ProductIds.NoAds);
            });
            app.StartAds();
            Assert.IsTrue(app.Services.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.IsFalse(app.ViewModels.SettingsModel.WatermarkEnabled, "owners export without it by default");
            app.ViewModels.SettingsModel.SetWatermarkEnabled(true);
            Assert.IsTrue(app.ViewModels.SettingsModel.WatermarkEnabled);
            app.OpenQuickRun();
            for (int i = 0; i < 400; i++) app.Frame();
            app.Sim.ToVideo();
            Assert.IsFalse(app.StudioModel.CanUndo, "the default is not an undo step");
            app.Export.Begin();
            Assert.IsTrue(app.Export.WatermarkEnabled);
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage, Errors(app));
            Assert.IsTrue(app.Studio.LastRequest.ShowWatermark, "the user chose to keep the watermark in Settings");
        }

        // ------------------------------------------------------------------ generation (coverage)

        [Test]
        public void GenerationFinishesOnAWorkerAndStartsTheGeneratedRun()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            for (int i = 0; i < 30; i++) app.Frame();
            ulong before = app.Context.Live.Seed;
            int evaluated = 0;
            app.Context.Ads.Evaluated += (k, d) => evaluated++;
            app.Sim.Generate();
            Assert.IsTrue(app.Sim.IsGenerating);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (app.Sim.IsGenerating && watch.ElapsedMilliseconds < 60000)
            {
                System.Threading.Thread.Sleep(5);
                app.Frame();
            }
            Assert.IsFalse(app.Sim.IsGenerating, "finished");
            Assert.AreNotEqual(before, app.Context.Live.Seed, "the generated run started");
            Assert.IsTrue(app.Context.Settings.Data.QuestGenerated);
            Assert.AreEqual(0, evaluated, "mid-run 🎲 evaluates no interstitial break");
            Assert.AreEqual(PresetsViewModel.CustomId, app.Sim.Presets.SelectedId);
        }

        [Test]
        public void CancelledGenerationKeepsTheCurrentRun()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            ulong before = app.Context.Live.Seed;
            app.Sim.Generate();
            app.Sim.CancelGeneration();
            Assert.IsFalse(app.Sim.IsGenerating);
            for (int i = 0; i < 120; i++)
            {
                System.Threading.Thread.Sleep(1);
                app.Frame();
            }
            Assert.AreEqual(before, app.Context.Live.Seed);
            Assert.IsFalse(app.Context.Settings.Data.QuestGenerated);
        }

        // ------------------------------------------------------------------ ATT bridge (coverage)

        [Test]
        public void TheAttPrePromptBridgeShowsTheUiScreenAndContinuesToTheSystemPrompt()
        {
            TestApp app = Boot();
            Assert.AreSame(app.ViewModels.TrackingModel, app.Services.Consent.AttPrePrompt);
            bool? answer = null;
            app.ViewModels.TrackingModel.Show(ok => answer = ok);
            Assert.AreEqual(1, app.Ui.TrackingPrompts);
            Assert.IsNull(answer);
            app.ViewModels.TrackingModel.ContinueToSystemPrompt();
            Assert.AreEqual(true, answer);

            answer = null;
            app.ViewModels.TrackingModel.Show(ok => answer = ok);
            app.ViewModels.TrackingModel.Dismiss();
            Assert.AreEqual(1, app.Ui.TrackingDismissals);
            app.ViewModels.TrackingModel.ContinueToSystemPrompt();
            Assert.IsNull(answer, "an abandoned screen answers nothing");
        }

        // ------------------------------------------------------------------ helpers

        static HashSet<string> Flatten(string json)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            void Walk(JToken t, string prefix)
            {
                if (t is JObject o)
                {
                    bool plural = o.Properties().Any(p => p.Name == "other" || p.Name == "one");
                    if (plural && prefix.Length > 0)
                    {
                        keys.Add(prefix);
                        return;
                    }
                    foreach (JProperty p in o.Properties()) Walk(p.Value, prefix.Length == 0 ? p.Name : prefix + "." + p.Name);
                }
                else
                {
                    keys.Add(prefix);
                }
            }
            Walk(JToken.Parse(json), "");
            return keys;
        }

        /// <summary>Finds a source under Assets/Ballverse from the test run's folders (Unity: project root; .NET: the harness bin).</summary>
        internal static string FindSource(string relativeToBallverse)
        {
            foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
            {
                if (string.IsNullOrEmpty(start)) continue;
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    foreach (string prefix in new[] { "", "client" })
                    {
                        string p = Path.Combine(dir.FullName, prefix, "Assets", "Ballverse", relativeToBallverse);
                        if (File.Exists(p)) return p;
                    }
                    dir = dir.Parent;
                }
            }
            return null;
        }

        /// <summary>A mode with another catalog entry (here: M05's, interactive) over Ring Escape's rules and scene.</summary>
        sealed class InteractiveAs : IModeDefinition
        {
            readonly IModeDefinition _inner;

            public InteractiveAs(IModeDefinition inner, ModeInfo info)
            {
                _inner = inner;
                Info = info;
            }

            public ModeInfo Info { get; }
            public ushort SimVersion => _inner.SimVersion;
            public ParamSchema Schema => _inner.Schema;
            public IReadOnlyList<Preset> BuiltInPresets => _inner.BuiltInPresets;
            public ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues) => _inner.Constrain(sanitized, tier, issues);
            public bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max) => _inner.TryGetDynamicRange(paramId, values, tier, out min, out max);
            public int GetParticipantCount(ParamSet values) => _inner.GetParticipantCount(values);
            public ParamSet GenerateCandidate(in ModeCandidateRequest request) => _inner.GenerateCandidate(in request);
            public IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context) => _inner.BuildScene(world, context);
        }

        sealed class TempRecording : ILiveRunRecording
        {
            public TempRecording(string dir)
            {
                ReplayPath = Path.Combine(dir, "clip.bvr");
                RenderStreamPath = Path.Combine(dir, "clip.bvs");
                EventsPath = Path.Combine(dir, "clip.bve");
            }

            public string ReplayPath { get; }
            public string RenderStreamPath { get; }
            public string EventsPath { get; }
            public string Commit(string modeId, int durationTicks, string winnerId) => "run1";

            public void Abandon()
            {
            }
        }
    }
}
