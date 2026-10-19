using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Services;
using Ballverse.UI.ViewModels;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>
    /// Regression tests for the QA round-3 findings: the merged Core duration estimator (no false hint from a probe cut
    /// short), the director goal «Победа в последнюю секунду» for M01 «Соревнование», the 🎲 queued behind the first
    /// interstitial on iOS (ATT pre-prompt screen), the stale Setup-hint key after a mode switch, the probe stopped by the
    /// tier's clock instead of a fixed game-time ratio, and the below-threshold «Попробуйте увеличить длительность» text.
    /// </summary>
    public sealed partial class QaRegressionTests
    {
        // ------------------------------------------------------------------ M1: merged Core estimator

        [Test]
        public void DurationHintProbeStoppedAfterTheFirstRoundGivesNoHintForTheDefaults()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition mode));
            Preset classic = mode.BuiltInPresets.First(p => p.Id == RingEscapePresets.Classic);
            foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
            {
                var sets = new Dictionary<string, ParamSet>
                {
                    { "defaults", ModeParams.Defaults(mode, tier) },
                    { "classic", ModeParams.ApplyPreset(mode, classic, tier).Values },
                };
                foreach (KeyValuePair<string, ParamSet> kv in sets)
                {
                    ParamSet values = ModeParams.Prepare(mode, kv.Value, tier).Values;
                    foreach (int rounds in new[] { 1, 2, 3 })
                    {
                        int calls = 0;
                        // The stop fires after the first round(s) of 1 s slices: a slow device or a busy worker.
                        SetupHint hint = new RingEscapeDurationHintProvider().Evaluate(values, SetupHintRegistry.MaxProbeTicks, () => ++calls >= rounds);
                        Assert.IsNull(hint, kv.Key + " on tier " + tier + ", stopped after " + rounds + " round(s): a short probe never shows the hint");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ M2: director goal «Победа в последнюю секунду»

        /// <summary>«Гонка цветов» (6 colours, «Соревнование») at 15 s, no forced winner.</summary>
        static ParamSet ColorRace(IModeDefinition mode, DeviceTier tier)
        {
            Preset race = mode.BuiltInPresets.First(p => p.Id == RingEscapePresets.ColorRace);
            ParamSet values = ModeParams.ApplyPreset(mode, race, tier).Values.With(CommonParams.TargetDuration, ParamValue.Int(15));
            return ModeParams.Prepare(mode, values, tier).Values;
        }

        /// <summary>The request the App sends for «Победа в последнюю секунду» (duration «Авто»).</summary>
        static ModeGenerationRequest LastSecondRequest(TestApp app, ulong sessionSeed)
        {
            return new ModeGenerationRequest
            {
                BaseValues = app.Sim.CurrentValues,
                Tier = app.Context.Tier,
                SessionSeed = sessionSeed,
                Style = DirectorGoals.GoalStyle,
                Goal = DirectorGoal.LastSecond,
                Budgets = app.Context.Remote.Budgets,
                Profiles = app.Context.Remote.Profiles,
                AutoTakeNearest = false,
            };
        }

        [Test]
        public void LastSecondGoalIsOfferedForRingEscapeCompetitionOnly()
        {
            TestApp app = Boot();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ModeParams.Defaults(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            Assert.AreEqual(0, mode.GetParticipantCount(app.Sim.CurrentValues), "one ball, no competition");
            Assert.IsFalse(app.Sim.LastSecondGoalAvailable);
            Assert.IsFalse(app.Sim.GenerationStyles.Any(s => s.Id == DirectorGoals.StyleId), "no goal without ≥ 2 colours");

            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRace(mode, app.Context.Tier)));
            Assert.IsTrue(app.Sim.LastSecondGoalAvailable);
            GenerationChoice goal = app.Sim.GenerationStyles.SingleOrDefault(s => s.Id == DirectorGoals.StyleId);
            Assert.IsNotNull(goal, "«Победа в последнюю секунду» is a choice of the 🎲 mini sheet");
            Assert.AreEqual("Last-second win", goal.Label.Literal);
            Assert.IsTrue(AppText.TryGetFallback(AppStrings.GenerateStyleLastSecond, "ru", out string ru));
            Assert.AreEqual("Победа в последнюю секунду", ru);

            // A mode Core has no goal for (M02 catalog entry over the same rules): never offered, never sent.
            IModeDefinition other = new InteractiveAs(mode, ModeCatalog.Get(ModeCode.M02));
            Assert.IsFalse(DirectorGoals.LastSecondAvailable(other, ColorRace(mode, app.Context.Tier)));
        }

        [Test]
        public void LastSecondChoiceSendsTheGoalKeepsTheColoursAndAMissRaisesGoalMissed()
        {
            TestApp app = BootTinyBudgets();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRace(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            int participants = mode.GetParticipantCount(app.Sim.CurrentValues);
            Assert.AreEqual(6, participants);

            // A session seed whose two candidates both miss the goal (deterministic: the time budget never limits).
            ModeGenerationResult reference = null;
            ulong seed = 0;
            for (ulong s = 1; s < 64 && reference == null; s++)
            {
                ModeGenerationResult r = ModeGenerator.Generate(new ParticipantsKeepingMode(mode), LastSecondRequest(app, s));
                if (r.HasSelection && r.Result.DirectorGoalMissed)
                {
                    reference = r;
                    seed = s;
                }
            }
            Assert.IsNotNull(reference, "a session seed that misses «Победа в последнюю секунду» with two candidates");
            Assert.AreEqual(participants, mode.GetParticipantCount(reference.Values), "every candidate keeps the user's colours");

            var notices = new List<string>();
            app.Sim.Notice += t => notices.Add(t.Literal ?? t.Key);
            app.Sim.SessionSeedSource = () => seed;
            app.Sim.Generate(DirectorGoals.StyleId, "auto");
            Assert.AreEqual(DirectorGoal.LastSecond, app.Sim.LastGenerationGoal, "ModeGenerationRequest.Goal = LastSecond");
            WaitUntil(app, () => !app.Sim.IsGenerating, "generation done");
            Assert.AreEqual(reference.Seed, app.Context.Live.Seed, "the candidate Core chose for the goal runs");
            Assert.AreEqual(participants, mode.GetParticipantCount(app.Sim.CurrentValues), "still «Соревнование» with 6 colours");
            Assert.Contains(app.Context.Notices.TextOf(AppStrings.GenerateGoalMissed).Literal, notices, "«Цель режиссуры не достигнута» fires");
            Assert.AreEqual(DirectorGoals.StyleId, app.Sim.SelectedStyleId, "the choice is remembered");
            Assert.IsEmpty(app.Errors, Errors(app));
        }

        [Test]
        public void LastSecondChoiceWithoutColoursFallsBackToTenseFinaleWithoutAGoal()
        {
            TestApp app = BootTinyBudgets();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRace(mode, app.Context.Tier)));
            app.Sim.SetScreenActive(true);
            app.Context.Settings.Update(d => d.GenerationStyle = DirectorGoals.StyleId);
            Assert.AreEqual(DirectorGoals.StyleId, app.Sim.SelectedStyleId);

            ParamSet oneBall = ModeParams.Prepare(mode, ModeParams.Defaults(mode, app.Context.Tier).With(CommonParams.TargetDuration, ParamValue.Int(15)), app.Context.Tier).Values;
            Assert.IsTrue(app.Sim.OpenWith(mode, oneBall));
            Assert.IsFalse(app.Sim.LastSecondGoalAvailable);
            Assert.AreEqual(DirectorGoals.FallbackStyleId, app.Sim.SelectedStyleId, "shown as «Напряжённый финал»");
            ulong before = app.Context.Live.Seed;
            app.Sim.Generate(); // short press with the remembered goal
            Assert.AreEqual(DirectorGoal.None, app.Sim.LastGenerationGoal, "Core rejects no goal it does not support");
            WaitUntil(app, () => !app.Sim.IsGenerating, "generation done");
            Assert.AreNotEqual(before, app.Context.Live.Seed, "the 🎲 still ran: " + string.Join(", ", app.Ui.Toasts.Select(t => t.Literal ?? t.Key)));
            Assert.IsEmpty(app.Errors, Errors(app));
        }

        // ------------------------------------------------------------------ m3: 🎲 behind the first interstitial on iOS

        TestApp BootAtResultCardWithAttPending(FakeTrackingAuthorization tracking)
        {
            TestApp app = Boot(a => a.Tracking = tracking);
            app.StartAds();
            app.OpenQuickRun();
            app.RunToEnd();
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            Assert.IsNotNull(app.Sim.Result, "result card");
            return app;
        }

        [Test]
        public void GenerateQueuedBehindTheFirstAdSurvivesTheAttPrePromptScreen()
        {
            var tracking = new FakeTrackingAuthorization { Answer = TrackingAuthorizationStatus.Denied };
            TestApp app = BootAtResultCardWithAttPending(tracking);
            ulong before = app.Context.Live.Seed;

            app.Sim.Generate();
            Assert.AreEqual(1, app.Ui.TrackingPrompts, "spec §5.6: the ATT pre-prompt comes right before the first ad");
            Assert.IsTrue(app.Sim.IsGenerating);
            // The real navigator pushes the pre-prompt screen: SimulationScreen.OnHide → SetScreenActive(false).
            app.Sim.SetScreenActive(false);
            Assert.IsTrue(app.Sim.IsGenerating, "the break covers the screen: the 🎲 behind it is not cancelled");
            app.Frame();
            app.Sim.SetScreenActive(true); // «Продолжить» pops the screen first
            app.ViewModels.TrackingModel.ContinueToSystemPrompt();
            WaitUntil(app, () => !app.Sim.IsGenerating && app.Context.Live.Seed != before, "the generated run started after the ATT and the ad");
            Assert.AreEqual(1, tracking.RequestCount, "the system dialog was asked");
            Assert.AreEqual(1, app.Ui.AdNotices, "then the ad");
            Assert.IsTrue(app.Context.Settings.Data.QuestGenerated);
        }

        [Test]
        public void GenerateBehindABreakIsDroppedWhenTheScreenIsStillHiddenAfterIt()
        {
            var tracking = new FakeTrackingAuthorization { Answer = TrackingAuthorizationStatus.Denied };
            TestApp app = BootAtResultCardWithAttPending(tracking);
            ulong before = app.Context.Live.Seed;

            app.Sim.Generate();
            Assert.AreEqual(1, app.Ui.TrackingPrompts);
            app.Sim.SetScreenActive(false);
            app.Frame();
            // The flow goes on, but the user ends up elsewhere (the screen never shows again before the continuation).
            app.ViewModels.TrackingModel.ContinueToSystemPrompt();
            for (int i = 0; i < 120 && app.Context.Ads.AdCoversScreen; i++) app.Frame();
            Assert.IsFalse(app.Context.Ads.AdCoversScreen, "the break is over");
            Assert.IsFalse(app.Sim.IsGenerating, "spec §2.4: the user left the screen — the 🎲 is cancelled");
            for (int i = 0; i < 60; i++) app.Frame();
            Assert.AreEqual(before, app.Context.Live.Seed);
        }

        // ------------------------------------------------------------------ m4: stale hint key after a mode switch

        [Test]
        public void DurationHintComesBackAfterVisitingAnotherModeWithTheSameSet()
        {
            TestApp app = Boot();
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition ring));
            IModeDefinition other = new InteractiveAs(ring, ModeCatalog.Get(ModeCode.M02)); // a mode without a Setup hint
            app.Context.Modes.Register(other);
            Assert.IsTrue(app.Context.Remote.IsAvailable(other.Info.Id, out _), "stand-in available");
            ParamSet hard = ModeParams.Prepare(ring, ModeParams.Defaults(ring, app.Context.Tier)
                .With(RingEscapeParams.RingCount, ParamValue.Int(100))
                .With(RingEscapeParams.GapSize, ParamValue.Float(26)), app.Context.Tier).Values;
            app.Sim.SetScreenActive(true);
            Assert.IsTrue(app.Sim.OpenWith(ring, hard));
            WaitUntil(app, () => !app.Sim.SetupHintPending, "first probe done");
            Assert.IsNotNull(app.Sim.SetupHint, "100 rings at gapMin: hint");

            Assert.IsTrue(app.Sim.Open(other.Info.Id));
            Assert.IsNull(app.Sim.SetupHint, "the other mode has no hint");

            Assert.IsTrue(app.Sim.Open("ring_escape"), "back with its last parameters");
            Assert.AreEqual(100, app.Sim.CurrentValues.GetInt(RingEscapeParams.RingCount));
            Assert.IsTrue(app.Sim.SetupHintPending, "the same set is probed again");
            WaitUntil(app, () => !app.Sim.SetupHintPending, "second probe done");
            Assert.IsNotNull(app.Sim.SetupHint, "the hint is back");
        }

        // ------------------------------------------------------------------ m5: probe stopped by the tier's clock

        [Test]
        public void DurationProbeRunsOnTheTierClockNotAFixedGameTimeRatio()
        {
            TestApp app = Boot();
            Assert.AreEqual(DeviceTier.A, app.Context.Tier, "test phone is tier A (1.5 s 🎲 budget)");
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            // 80 rings at 35°: one probe seed escapes in ≈ 154 s (QA S1) — no hint. The old cap of 10 game seconds per budget
            // second (900 ticks on tier A, ≈ 5 s per seed) extrapolated ≈ 200 s and showed a false hint.
            ParamSet values = ModeParams.Prepare(mode, ModeParams.Defaults(mode, DeviceTier.A)
                .With(RingEscapeParams.RingCount, ParamValue.Int(80))
                .With(RingEscapeParams.GapSize, ParamValue.Float(35)), DeviceTier.A).Values;
            RingEscapeDurationEstimate truth = RingEscapeDurationHint.Estimate(values, RingEscapeDurationHintProvider.ProbeSeed, SetupHintRegistry.MaxProbeTicks, null);
            Assert.IsFalse(truth.ShowHint, "full probe: a seed escapes within 180 s");
            Assert.Greater(SetupHintRegistry.MaxProbeTicks, truth.TicksRun, "the cap does not cut the full probe");

            var watch = Stopwatch.StartNew();
            Assert.IsTrue(app.Sim.OpenWith(mode, values));
            app.Sim.SetScreenActive(true);
            WaitUntil(app, () => !app.Sim.SetupHintPending, "probe done");
            Assert.IsNull(app.Sim.SetupHint, "tier A, 80 rings at 35°: no false hint");
            TestContext.WriteLine("probe took " + watch.ElapsedMilliseconds + " ms (" + truth.TicksRun + " ticks for the full probe)");
        }

        // ------------------------------------------------------------------ m6: below-threshold text

        [Test]
        public void BelowThresholdNoticeAdvisesALongerDuration()
        {
            Assert.IsTrue(AppText.TryGetFallback(AppStrings.GenerateWeak, "ru", out string ru));
            Assert.IsTrue(AppText.TryGetFallback(AppStrings.GenerateWeak, "en", out string en));
            StringAssert.Contains("Попробуйте увеличить длительность", ru, "spec §2.4 step 5");
            StringAssert.Contains("Try a longer duration", en);
        }
    }
}
