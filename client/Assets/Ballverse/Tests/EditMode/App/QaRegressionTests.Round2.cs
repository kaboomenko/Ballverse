using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Config;
using Ballverse.Core.Generation;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Ballverse.Sim;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>
    /// Regression tests for the QA round-2 findings: 🎲 behind the result-card interstitial survives the ad's app pause, the
    /// incremental studio look timeline, the forced-winner «Искать дольше» / «Взять ближайший вариант» choice, the M01 Setup
    /// duration hint, the degraded studio on «В видео» / «Сохранить клип», and the touch lost mid-drag.
    /// </summary>
    public sealed partial class QaRegressionTests
    {
        const int WaitMs = 60000;

        static void WaitUntil(TestApp app, Func<bool> done, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!done() && watch.ElapsedMilliseconds < WaitMs)
            {
                System.Threading.Thread.Sleep(2);
                app.Frame();
            }
            Assert.IsTrue(done(), what);
        }

        static bool HasText(TestApp app, string key)
        {
            string literal = app.Context.Notices.TextOf(key).Literal;
            return app.Ui.Toasts.Any(t => (literal != null && t.Literal == literal) || t.Key == key);
        }

        // ------------------------------------------------------------------ 🎲 behind the interstitial (major)

        [Test]
        public void ResultCardGenerateSurvivesTheAppPauseOfItsInterstitial()
        {
            TestApp app = Boot();
            app.StartAds();
            // Unity pauses the app while a full-screen ad shows (Android; iOS with SetiOSAppPauseOnBackground(true)).
            int pausesDuringAd = 0;
            app.Services.Ads.FullScreenChanged += showing =>
            {
                if (showing) pausesDuringAd++;
                app.Lifecycle.NotifyPause(showing);
            };
            app.OpenQuickRun();
            app.RunToEnd();
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            Assert.IsNotNull(app.Sim.Result, "result card");
            ulong before = app.Context.Live.Seed;

            app.Sim.Generate();
            Assert.AreEqual(1, app.Ui.AdNotices, "IN2: 🎲 on the result card shows the pending interstitial");
            Assert.AreEqual(1, pausesDuringAd, "the app paused during the ad");
            Assert.IsFalse(app.Context.Ads.AdCoversScreen, "the break is over");
            Assert.IsTrue(app.Sim.IsGenerating, "the ad's pause did not cancel the generation");
            WaitUntil(app, () => !app.Sim.IsGenerating, "generation finished");
            Assert.AreNotEqual(before, app.Context.Live.Seed, "the generated run started after the ad");
            Assert.IsTrue(app.Context.Settings.Data.QuestGenerated);
        }

        [Test]
        public void LeavingTheAppStillCancelsAGeneration()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            ulong before = app.Context.Live.Seed;
            app.Sim.Generate();
            Assert.IsTrue(app.Sim.IsGenerating);
            app.Lifecycle.NotifyPause(true);
            Assert.IsFalse(app.Sim.IsGenerating, "a real pause (no ad on screen) is leaving the screen");
            app.Lifecycle.NotifyPause(false);
            for (int i = 0; i < 60; i++) app.Frame();
            Assert.AreEqual(before, app.Context.Live.Seed);
        }

        // ------------------------------------------------------------------ studio look timeline (major)

        [Test]
        public void LookTimelineIsIncrementalAndMatchesReplayParamsAtEveryTick()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition mode));
            ParamSchema schema = mode.Schema;
            ParamSet initial = ModeParams.Defaults(mode, DeviceTier.B);
            // One live glow edit per tick (a slider drag), plus a theme change inside the stretch.
            var changes = new List<ReplayParamChange>();
            const int first = 100, edits = 600;
            for (int i = 0; i < edits; i++)
            {
                ParamValue glow = schema.SanitizeValue(CommonParams.Glow, ParamValue.Float((i % 21) * 0.05), DeviceTier.A);
                changes.Add(ReplayParams.ParamChange((uint)(first + i), CommonParams.Glow, glow, schema));
            }
            string theme = initial.Get(CommonParams.Theme).AsString == "Space" ? "Ocean" : "Space";
            changes.Add(ReplayParams.ParamChange(first + 300, CommonParams.Theme, ParamValue.String(theme), schema));
            var timeline = new ClipLookTimeline(initial, changes, schema);
            Assert.AreEqual(edits + 1, timeline.ChangeCount);

            // Correctness against Core's reference at every tick, forward, then after seeking back.
            var sorted = changes.OrderBy(c => c.Tick).ToList();
            ParamSet last = null;
            for (int tick = 0; tick < first + edits + 50; tick++)
            {
                if (timeline.Advance(tick, out ParamSet values)) last = values;
                ParamSet expected = ReplayParams.ParamsAtTick(initial, sorted, (uint)tick, schema);
                foreach (string id in ClipLookTimeline.LookParams)
                {
                    Assert.AreEqual(expected.Get(id), last.Get(id), "tick " + tick + " " + id);
                }
            }
            Assert.IsTrue(timeline.Advance(first + 10, out ParamSet back), "seek back");
            Assert.AreEqual(ReplayParams.ParamsAtTick(initial, sorted, first + 10, schema).Get(CommonParams.Glow), back.Get(CommonParams.Glow));
            Assert.AreEqual(initial.Get(CommonParams.Theme), back.Get(CommonParams.Theme), "before the theme change");
            Assert.AreEqual(ReplayParams.ParamsAtTick(initial, sorted, 500, schema), timeline.ValuesAt(500), "whole set");

            // Cost per frame through the edited stretch (was 3.6–4.4 ms and 0.3–1.6 MB per frame with full recomputation).
            timeline.Advance(0, out _);
            for (int tick = 0; tick < first + edits; tick++) timeline.Advance(tick, out _); // warm up the JIT
            timeline.Advance(0, out _);
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            int frames = 0;
            for (int tick = first; tick < first + edits; tick++, frames++) timeline.Advance(tick, out _);
            watch.Stop();
            long bytesPerFrame = (GC.GetAllocatedBytesForCurrentThread() - bytesBefore) / frames;
            double msPerFrame = watch.Elapsed.TotalMilliseconds / frames;
            TestContext.WriteLine("look timeline: " + msPerFrame.ToString("F4") + " ms and " + bytesPerFrame + " bytes per frame");
            Assert.Less(bytesPerFrame, 4096, "one small look set per changed frame");
            Assert.Less(msPerFrame, 0.25, "no re-parsing of the recorded changes");

            // No change → nothing allocated per call (a few bytes of one-off runtime work are tolerated).
            timeline.Advance(first + edits + 10, out _);
            long still = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) timeline.Advance(first + edits + 10 + i, out _);
            Assert.Less(GC.GetAllocatedBytesForCurrentThread() - still, 256, "allocation-free while the look holds");
        }

        // ------------------------------------------------------------------ forced winner (major)

        /// <summary>RC budgets of two candidates and 10 s per tier (×4 with a forced winner): the time never limits, so a session seed decides the outcome.</summary>
        static void ApplyTinyBudgets(TestApp app)
        {
            var b = new RemoteConfigBuilder();
            var budget = new GenerationBudgetConfig(2, 10000);
            b.GenerationBudgetByTier = b.GenerationBudgetByTier.With(DeviceTier.A, budget).With(DeviceTier.B, budget).With(DeviceTier.C, budget);
            app.Context.Remote.Apply(b.Build());
            Assert.AreEqual(2, app.Context.Remote.Budgets.Get(app.Context.Tier).Candidates);
        }

        TestApp BootTinyBudgets()
        {
            TestApp app = Boot();
            ApplyTinyBudgets(app);
            return app;
        }

        /// <summary>«Гонка цветов» (6 colours, competition, 15 s) with winner 3 forced, on the simulation screen.</summary>
        static ParamSet ColorRaceForcingWinner3(IModeDefinition mode, DeviceTier tier)
        {
            Preset race = mode.BuiltInPresets.First(p => p.Id == RingEscapePresets.ColorRace);
            ParamSet values = ModeParams.ApplyPreset(mode, race, tier).Values
                .With(CommonParams.ForcedWinner, ParamValue.Int(3))
                .With(CommonParams.TargetDuration, ParamValue.Int(15));
            return ModeParams.Prepare(mode, values, tier).Values;
        }

        static IModeDefinition OpenColorRaceForcingWinner3(TestApp app)
        {
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRaceForcingWinner3(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            Assert.AreEqual(6, mode.GetParticipantCount(app.Sim.CurrentValues));
            return mode;
        }

        /// <summary>The request the App sends for the current values (style «Любой», duration «Авто»).</summary>
        static ModeGenerationRequest AppRequest(TestApp app, ulong sessionSeed)
        {
            return new ModeGenerationRequest
            {
                BaseValues = app.Sim.CurrentValues,
                Tier = app.Context.Tier,
                SessionSeed = sessionSeed,
                Style = GenerationStyle.Any,
                Budgets = app.Context.Remote.Budgets,
                Profiles = app.Context.Remote.Profiles,
                AutoTakeNearest = false,
            };
        }

        /// <summary>A session seed whose first budget does not find the forced winner (deterministic: candidate count only).</summary>
        static ulong SeedWithoutTheWinner(IModeDefinition mode, TestApp app)
        {
            for (ulong seed = 1; seed < 32; seed++)
            {
                ModeGenerationResult r = ModeGenerator.Generate(mode, AppRequest(app, seed));
                if (r.Result.Outcome == GenerationOutcome.WinnerNotFound) return seed;
            }
            Assert.Fail("no session seed misses the forced winner with two candidates");
            return 0;
        }

        [Test]
        public void ForcedWinnerMissOffersSearchLongerOrTakeNearestAndTakeNearestSaysSo()
        {
            TestApp app = BootTinyBudgets();
            IModeDefinition mode = OpenColorRaceForcingWinner3(app);
            ulong seed = SeedWithoutTheWinner(mode, app);
            ModeGenerationRequest nearestRequest = AppRequest(app, seed);
            nearestRequest.AutoTakeNearest = true;
            ModeGenerationResult nearest = ModeGenerator.Generate(mode, nearestRequest);
            Assert.AreEqual(GenerationOutcome.NearestWinner, nearest.Result.Outcome);

            ulong before = app.Context.Live.Seed;
            app.Sim.SessionSeedSource = () => seed;
            app.Sim.Generate();
            WaitUntil(app, () => !app.Sim.IsGenerating, "first budget done");
            Assert.IsTrue(app.Sim.AwaitingWinnerDecision, "spec §2.5: the user decides");
            Assert.IsNotNull(app.Ui.OpenChoice, "the choice is shown");
            Assert.AreEqual(2, app.Ui.OpenChoice.Options.Count);
            Assert.AreEqual(app.Context.Notices.TextOf(AppStrings.SearchLonger).Literal, app.Ui.OpenChoice.Options[0].Label.Literal);
            Assert.AreEqual(app.Context.Notices.TextOf(AppStrings.TakeNearest).Literal, app.Ui.OpenChoice.Options[1].Label.Literal);
            Assert.AreEqual(before, app.Context.Live.Seed, "no run with another winner starts silently");

            app.Ui.Choose(1); // «Взять ближайший вариант»
            Assert.IsFalse(app.Sim.AwaitingWinnerDecision);
            Assert.AreEqual(nearest.Seed, app.Context.Live.Seed, "the nearest candidate runs");
            Assert.AreEqual(ModeParams.Prepare(mode, nearest.Values.With(CommonParams.Seed, ParamValue.Seed(nearest.Seed)), app.Context.Tier).Values, app.Sim.CurrentValues,
                "rebuilt from its seed exactly as ModeGenerator built it");
            Assert.IsTrue(HasText(app, AppStrings.GenerateNearestWinner), "«Нужный победитель не найден — показан ближайший вариант»");
            Assert.IsFalse(HasText(app, AppStrings.GenerateNone));
        }

        [Test]
        public void SearchLongerContinuesTheSameSessionLikeCoreAutoSearchLonger()
        {
            TestApp app = BootTinyBudgets();
            IModeDefinition mode = OpenColorRaceForcingWinner3(app);
            ulong seed = SeedWithoutTheWinner(mode, app);
            ModeGenerationRequest longer = AppRequest(app, seed);
            longer.AutoSearchLonger = 1;
            ModeGenerationResult reference = ModeGenerator.Generate(mode, longer);

            app.Sim.SessionSeedSource = () => seed;
            app.Sim.Generate();
            WaitUntil(app, () => !app.Sim.IsGenerating, "first budget done");
            ulong before = app.Context.Live.Seed;
            app.Ui.Choose(0); // «Искать дольше»
            Assert.IsTrue(app.Sim.IsGenerating, "one more budget on a worker");
            WaitUntil(app, () => !app.Sim.IsGenerating, "second budget done");
            if (reference.Session.Status == GenerationStatus.AwaitingDecision)
            {
                Assert.IsTrue(app.Sim.AwaitingWinnerDecision, "still not found: asked again");
                Assert.IsNotNull(app.Ui.OpenChoice);
                Assert.AreEqual(before, app.Context.Live.Seed);
            }
            else
            {
                Assert.IsTrue(reference.HasSelection);
                Assert.AreEqual(reference.Seed, app.Context.Live.Seed, "same candidate as Core's AutoSearchLonger");
                Assert.AreEqual(ModeParams.Prepare(mode, reference.Values.With(CommonParams.Seed, ParamValue.Seed(reference.Seed)), app.Context.Tier).Values,
                    app.Sim.CurrentValues);
            }
        }

        [Test]
        public void GenerationDecisionRebuildsCandidatesExactlyAsModeGenerator()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition mode));
            ParamSet values = ColorRaceForcingWinner3(mode, DeviceTier.B);
            int checkedSeeds = 0;
            for (ulong seed = 1; seed < 32 && checkedSeeds < 3; seed++)
            {
                ModeGenerationRequest Request(int searchLonger, bool takeNearest) => new ModeGenerationRequest
                {
                    BaseValues = values,
                    Tier = DeviceTier.B,
                    SessionSeed = seed,
                    Budget = new GenerationBudget(2, 30000),
                    AutoSearchLonger = searchLonger,
                    AutoTakeNearest = takeNearest,
                };
                ModeGenerationResult first = ModeGenerator.Generate(mode, Request(0, false));
                if (first.Session.Status != GenerationStatus.AwaitingDecision) continue;
                checkedSeeds++;

                var decision = new GenerationDecision(mode, Request(0, false), first.Session);
                ParamSet nearestValues = decision.TakeNearest(out ulong nearestSeed);
                ModeGenerationResult nearest = ModeGenerator.Generate(mode, Request(0, true));
                Assert.AreEqual(nearest.Seed, nearestSeed);
                Assert.AreEqual(nearest.Values, nearestValues);

                ModeGenerationResult again = ModeGenerator.Generate(mode, Request(0, false));
                var longerDecision = new GenerationDecision(mode, Request(0, false), again.Session);
                ParamSet longerValues = longerDecision.SearchLonger(null, () => 0L, out ulong longerSeed);
                ModeGenerationResult longer = ModeGenerator.Generate(mode, Request(1, false));
                Assert.AreEqual(longer.Session.Status, longerDecision.Session.Status);
                Assert.AreEqual(longer.Result?.Outcome, longerDecision.Session.Result?.Outcome);
                Assert.AreEqual(longer.Seed, longerSeed);
                Assert.AreEqual(longer.Values, longerValues);
            }
            Assert.Greater(checkedSeeds, 0, "a seed that misses the winner");
        }

        [Test]
        public void DismissingOrLeavingDropsTheWinnerChoiceAndKeepsTheRun()
        {
            TestApp app = BootTinyBudgets();
            IModeDefinition mode = OpenColorRaceForcingWinner3(app);
            ulong seed = SeedWithoutTheWinner(mode, app);
            app.Sim.SessionSeedSource = () => seed;
            ulong before = app.Context.Live.Seed;

            app.Sim.Generate();
            WaitUntil(app, () => !app.Sim.IsGenerating, "first budget done");
            app.Ui.CancelChoice();
            Assert.IsFalse(app.Sim.AwaitingWinnerDecision);
            Assert.AreEqual(before, app.Context.Live.Seed, "dismissed: the run stays");

            app.Sim.Generate();
            WaitUntil(app, () => !app.Sim.IsGenerating, "first budget done again");
            Assert.IsNotNull(app.Ui.OpenChoice);
            app.Sim.SetScreenActive(false);
            Assert.IsNull(app.Ui.OpenChoice, "leaving the screen closes the stale choice");
            Assert.AreEqual(1, app.Ui.ChoicesClosedByApp);
            Assert.IsFalse(app.Sim.AwaitingWinnerDecision);
            Assert.AreEqual(before, app.Context.Live.Seed);
        }

        [Test]
        public void HeadlessCompositionTakesTheNearestWinnerWithItsNotice()
        {
            TestApp app = Boot(a => a.Headless = true);
            ApplyTinyBudgets(app);
            var notices = new List<LocalizedText>();
            app.Sim.Notice += notices.Add;
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRaceForcingWinner3(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            ulong seed = SeedWithoutTheWinner(mode, app);
            app.Sim.SessionSeedSource = () => seed;
            app.Sim.Generate();
            WaitUntil(app, () => !app.Sim.IsGenerating, "generation done");
            Assert.IsFalse(app.Sim.AwaitingWinnerDecision);
            string expected = app.Context.Notices.TextOf(AppStrings.GenerateNearestWinner).Literal;
            Assert.IsTrue(notices.Any(n => n.Literal == expected), "no silent run with another winner");
        }

        // ------------------------------------------------------------------ M01 Setup duration hint (major)

        [Test]
        public void RingEscapeDefaultsGiveNoDurationHint()
        {
            TestApp app = Boot();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ModeParams.Defaults(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            Assert.IsTrue(app.Sim.SetupHintPending, "probed in the background");
            WaitUntil(app, () => !app.Sim.SetupHintPending, "probe done");
            Assert.IsNull(app.Sim.SetupHint);
            Assert.IsFalse(HasText(app, AppStrings.HintRingEscapeDuration));
        }

        [Test]
        public void HundredRingsAtGapMinShowTheDurationHintWithItsActions()
        {
            TestApp app = Boot();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            ParamSet hard = ModeParams.Prepare(mode, ModeParams.Defaults(mode, app.Context.Tier)
                .With(RingEscapeParams.RingCount, ParamValue.Int(100))
                .With(RingEscapeParams.GapSize, ParamValue.Float(26)), app.Context.Tier).Values;
            Assert.IsTrue(app.Sim.OpenWith(mode, hard));
            app.Sim.SetScreenActive(true);
            WaitUntil(app, () => !app.Sim.SetupHintPending, "probe done");
            Assert.IsNotNull(app.Sim.SetupHint, "«Побег может не успеть за 180 с»");
            Assert.IsTrue(HasText(app, AppStrings.HintRingEscapeDuration), "raised as a notice");
            CollectionAssert.AreEqual(new[] { RingEscapeDurationHintProvider.WiderGapsAction, RingEscapeDurationHintProvider.FewerRingsAction },
                app.Sim.SetupHint.Actions.Select(a => a.Id).ToArray());

            // Retry with the same set probes nothing new.
            int toasts = app.Ui.Toasts.Count;
            app.Sim.Retry();
            Assert.IsFalse(app.Sim.SetupHintPending, "same structural set: no new probe");
            Assert.AreEqual(toasts, app.Ui.Toasts.Count);

            Assert.IsTrue(app.Sim.ApplySetupHintAction(RingEscapeDurationHintProvider.FewerRingsAction));
            Assert.AreEqual(75, app.Sim.CurrentValues.GetInt(RingEscapeParams.RingCount), "«Меньше колец»");
            Assert.IsNull(app.Sim.SetupHint, "a new set clears the hint");
            Assert.IsTrue(app.Sim.SetupHintPending, "and probes again");
            WaitUntil(app, () => !app.Sim.SetupHintPending, "second probe done");
            Assert.IsNotNull(app.Sim.SetupHint, "75 narrow rings still cannot escape in time");
            Assert.IsTrue(app.Sim.ApplySetupHintAction(RingEscapeDurationHintProvider.WiderGapsAction));
            Assert.AreEqual(36.0, app.Sim.CurrentValues.GetFloat(RingEscapeParams.GapSize), 1e-6, "«Шире прорези»");

            // Leaving the screen cancels a probe.
            Assert.IsTrue(app.Sim.SetupHintPending);
            app.Sim.SetScreenActive(false);
            Assert.IsFalse(app.Sim.SetupHintPending);
            Assert.IsEmpty(app.Errors.Where(e => e.Contains("setup_hint")).ToList(), Errors(app));
        }

        // ------------------------------------------------------------------ degraded studio (minor)

        [Test]
        public void ToVideoAndSaveClipWithoutTheStudioKeepTheRunAndSaySo()
        {
            TestApp app = Boot(a => a.StudioFactory = s => throw new IOException("No space left on device"));
            app.OpenQuickRun();
            for (int i = 0; i < 120; i++) app.Frame();
            Assert.IsTrue(app.Context.Live.HasRun && !app.Context.Live.IsCompleted);
            app.Ui.Toasts.Clear();
            app.Sim.ToVideo();
            Assert.IsTrue(app.Context.Live.HasRun && !app.Context.Live.IsCompleted, "«В видео» does not end the live run");
            Assert.IsTrue(HasText(app, AppStrings.StudioUnavailable));
            Assert.IsFalse(HasText(app, AppStrings.RunTooShort));
            app.Ui.Toasts.Clear();
            app.Sim.SaveClip();
            Assert.IsTrue(HasText(app, AppStrings.StudioUnavailable));
            Assert.IsFalse(HasText(app, AppStrings.RunTooShort));
        }

        // ------------------------------------------------------------------ touch lost mid-drag (minor)

        [Test]
        public void ATouchLostMidDragReleasesTheTowerAtTheNextTick()
        {
            var tracker = new ArenaGestureTracker();
            tracker.OnTouch(ArenaTouchPhase.Down, 0.5f, 0.5f, 1, 0);
            tracker.OnTouch(ArenaTouchPhase.Move, 0.6f, 0.5f, 1, 0.05);
            tracker.Update(0.05);
            Assert.IsTrue(tracker.IsTouching);
            tracker.CancelTouch();
            Assert.IsFalse(tracker.IsTouching);
            Assert.IsTrue(tracker.TryDequeue(out ArenaGesture g));
            Assert.AreEqual(ArenaGestureKind.Release, g.Kind, "pending press / drag dropped, one release");
            Assert.AreEqual(0f, g.Velocity);
            Assert.IsFalse(tracker.TryDequeue(out _));
            tracker.CancelTouch();
            Assert.IsFalse(tracker.TryDequeue(out _), "no finger, nothing to release");

            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition ring));
            IModeDefinition helix = new InteractiveAs(ring, ModeCatalog.Get(ModeCode.M05));
            ParamSet values = TestApp.QuickRingEscape(ring, DeviceTier.B);
            string dir = Path.Combine(Path.GetTempPath(), "bv-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var live = new LiveSimController();
                live.PointsPerArenaWidth = 390f;
                var recording = new TempRecording(dir);
                live.Begin(helix, values, 3UL, recording);
                live.OnTouch(ArenaTouchPhase.Down, 0.5f, 0.5f, 1, 0);
                live.AdvanceDisplayFrame(60, 0);
                live.OnTouch(ArenaTouchPhase.Move, 0.55f, 0.5f, 1, 0.1);
                live.AdvanceDisplayFrame(60, 0.1);
                live.ResetInput(); // screen hidden / app paused with the finger down
                for (int i = 0; i < 30; i++) live.AdvanceDisplayFrame(60, 1 + i / 60.0);
                LiveRunOutcome outcome = live.End(GameRunEnd.Abandoned, true);
                Assert.IsNotNull(outcome.RunId);
                live.Dispose();
                ReplayFile replay = ReplayReader.Read(File.ReadAllBytes(recording.ReplayPath));
                string got = string.Join(" ", replay.Inputs.Select(i => i.Type + ":" + i.Values[0]));
                Assert.AreEqual("1:0 1:-1021 2:0", got, "Release(0) after the lost touch: the tower does not keep spinning");
            }
            finally
            {
                TestApp.DeleteRoot(dir);
            }
        }
    }
}
