using System;
using System.Collections.Generic;
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
                Style = GenerationStyle.Any, // QA round 3 (r1): the goal ranks neutral candidates, not «Напряжённый финал» ones
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
            Assert.AreEqual(GenerationStyle.Any, app.Sim.LastGenerationStyle, "the goal does not force «Напряжённый финал»");
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
            Assert.AreEqual(GenerationStyle.TenseFinale, app.Sim.LastGenerationStyle, "generates as «Напряжённый финал»");
            WaitUntil(app, () => !app.Sim.IsGenerating, "generation done");
            Assert.AreNotEqual(before, app.Context.Live.Seed, "the 🎲 still ran: " + string.Join(", ", app.Ui.Toasts.Select(t => t.Literal ?? t.Key)));
            Assert.AreEqual(DirectorGoals.StyleId, app.Context.Settings.Data.GenerationStyle, "QA round 3 (r1): the short press keeps the goal remembered");

            // The sheet echoing the shown «Напряжённый финал» keeps it remembered too.
            app.Sim.Generate(DirectorGoals.FallbackStyleId, "auto");
            Assert.AreEqual(GenerationStyle.TenseFinale, app.Sim.LastGenerationStyle);
            WaitUntil(app, () => !app.Sim.IsGenerating, "second generation done");
            Assert.AreEqual(DirectorGoals.StyleId, app.Context.Settings.Data.GenerationStyle);

            // Back on «Гонка цветов»: the goal is selected again and a short 🎲 sends it.
            Assert.IsTrue(app.Sim.OpenWith(mode, ColorRace(mode, app.Context.Tier)));
            Assert.AreEqual(DirectorGoals.StyleId, app.Sim.SelectedStyleId, "«Победа в последнюю секунду» is still the choice");
            app.Sim.Generate();
            Assert.AreEqual(DirectorGoal.LastSecond, app.Sim.LastGenerationGoal);
            WaitUntil(app, () => !app.Sim.IsGenerating, "goal generation done");

            // A real choice of another style on the one-ball set replaces the goal.
            Assert.IsTrue(app.Sim.OpenWith(mode, oneBall));
            app.Sim.Generate("chaos", "auto");
            WaitUntil(app, () => !app.Sim.IsGenerating, "chaos generation done");
            Assert.AreEqual("chaos", app.Context.Settings.Data.GenerationStyle);
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
            app.Sim.ProbeClockMs = () => 0; // full probes on any machine
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

        /// <summary>Wraps the real M01 provider and records how the App drives it (on the worker thread).</summary>
        sealed class RecordingHintProvider : ISetupHintProvider
        {
            readonly ISetupHintProvider _inner = new RingEscapeDurationHintProvider();
            int _maxTicks = -1, _stops, _stopped;

            public int MaxTicks => System.Threading.Volatile.Read(ref _maxTicks);
            public int StopPolls => System.Threading.Volatile.Read(ref _stops);
            public bool Stopped => System.Threading.Volatile.Read(ref _stopped) != 0;

            public SetupHint Evaluate(ParamSet values, int maxTotalTicks, Func<bool> stop)
            {
                System.Threading.Volatile.Write(ref _maxTicks, maxTotalTicks);
                return _inner.Evaluate(values, maxTotalTicks, () =>
                {
                    System.Threading.Interlocked.Increment(ref _stops);
                    bool s = stop();
                    if (s) System.Threading.Volatile.Write(ref _stopped, 1);
                    return s;
                });
            }
        }

        static ParamSet RingSet(IModeDefinition mode, DeviceTier tier, int rings, double gap, string placement = null)
        {
            ParamSet values = ModeParams.Defaults(mode, tier)
                .With(RingEscapeParams.RingCount, ParamValue.Int(rings))
                .With(RingEscapeParams.GapSize, ParamValue.Float(gap));
            if (placement != null) values = values.With(RingEscapeParams.GapPlacement, ParamValue.String(placement));
            return ModeParams.Prepare(mode, values, tier).Values;
        }

        RecordingHintProvider BootWithRecordingProvider(out TestApp app, Func<long> clock)
        {
            app = Boot();
            var provider = new RecordingHintProvider();
            var registry = new SetupHintRegistry();
            registry.Register(RingEscapeDurationHintProvider.ModeId, provider);
            app.Sim.SetupHints = registry;
            app.Sim.ProbeClockMs = clock;
            return provider;
        }

        /// <summary>
        /// m5, made deterministic in QA round 3 (r1): the App gives the provider the full tick cap (not a fixed game-time
        /// ratio of the budget) and stops it only by its clock; with a clock that never runs out the probe of 80 rings at 35°
        /// runs to its end and gives no hint, on any machine. (The round-3 test read the wall clock and failed on slow runners.)
        /// </summary>
        [Test]
        public void DurationProbeRunsOnTheTierClockNotAFixedGameTimeRatio()
        {
            RecordingHintProvider provider = BootWithRecordingProvider(out TestApp app, () => 0);
            Assert.AreEqual(DeviceTier.A, app.Context.Tier, "test phone is tier A (1.5 s 🎲 budget)");
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            // 80 rings at 35°: one probe seed escapes in ≈ 154 s — no hint. The old cap of 10 game seconds per budget second
            // (900 ticks on tier A, ≈ 5 s per seed) extrapolated ≈ 200 s and showed a false hint.
            ParamSet values = RingSet(mode, DeviceTier.A, 80, 35);
            RingEscapeDurationEstimate truth = RingEscapeDurationHint.Estimate(values, RingEscapeDurationHintProvider.ProbeSeed, SetupHintRegistry.MaxProbeTicks, null);
            Assert.IsFalse(truth.ShowHint, "full probe: a seed escapes within 180 s");
            Assert.IsFalse(truth.BudgetExhausted);
            Assert.Greater(SetupHintRegistry.MaxProbeTicks, truth.TicksRun, "the cap does not cut the full probe");

            Assert.IsTrue(app.Sim.OpenWith(mode, values));
            app.Sim.SetScreenActive(true);
            WaitUntil(app, () => !app.Sim.SetupHintPending, "probe done");
            Assert.AreEqual(SetupHintRegistry.MaxProbeTicks, provider.MaxTicks, "the App passes the tick cap, not a game-time ratio");
            Assert.IsFalse(provider.Stopped, "the clock never ran out: the full probe ran");
            Assert.Greater(provider.StopPolls, 0, "the clock is polled between slices");
            Assert.IsNull(app.Sim.SetupHint, "tier A, 80 rings at 35°: no false hint");
            Assert.IsEmpty(app.Errors, Errors(app));
        }

        /// <summary>QA round 3 (r1): a clock that has run out at the first poll stops the probe after its first round — no hint.</summary>
        [Test]
        public void DurationProbeStopsWhenTheClockRunsOutAndShowsNoHint()
        {
            long now = 0;
            // Every read advances by a minute: the tier's budget has passed at the first poll.
            BootWithRecordingProvider(out TestApp app, () => System.Threading.Interlocked.Add(ref now, 60000));
            Assert.IsTrue(app.Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            foreach (ParamSet values in new[] { ModeParams.Defaults(mode, app.Context.Tier), RingSet(mode, app.Context.Tier, 100, 26) })
            {
                app.Sim.SetScreenActive(false); // no probe while the provider is swapped
                var provider = new RecordingHintProvider();
                var registry = new SetupHintRegistry();
                registry.Register(RingEscapeDurationHintProvider.ModeId, provider);
                app.Sim.SetupHints = registry;
                ParamSet prepared = ModeParams.Prepare(mode, values, app.Context.Tier).Values;
                Assert.IsTrue(app.Sim.OpenWith(mode, prepared));
                app.Sim.SetScreenActive(true);
                Assert.IsTrue(app.Sim.SetupHintPending, "probed");
                WaitUntil(app, () => !app.Sim.SetupHintPending, "probe done");
                Assert.IsTrue(provider.Stopped, "stopped by the clock");
                Assert.AreEqual(1, provider.StopPolls, "after the first round");
                Assert.IsNull(app.Sim.SetupHint, prepared.GetInt(RingEscapeParams.RingCount) + " rings: a probe cut short never shows the hint");
            }
            Assert.IsEmpty(app.Errors, Errors(app));
        }

        /// <summary>
        /// QA round 3 (r1): a probe stopped by the clock (a slow device) rests on Core's rhythm extrapolation, which overshoots
        /// after the first rings; Core alone showed a false hint for 80 rings at 35° when stopped after 3–5 or 40–122 rounds and
        /// for 60 rings at 30° after 4–116 rounds. The provider keeps such a verdict only when clear — and still keeps a clear
        /// one (100 rings at gapMin).
        /// </summary>
        [Test]
        public void ExtrapolatedDurationVerdictShowsTheHintOnlyWhenClear()
        {
            Assert.IsTrue(ReleaseModes.CreateRegistry().TryGet("ring_escape", out IModeDefinition mode));
            var provider = new RingEscapeDurationHintProvider();
            var cases = new[]
            {
                (rings: 80, gap: 35.0, placement: RingEscapeParams.PlacementRandom, rounds: new[] { 3, 4, 5, 40, 60, 100 }),
                (rings: 60, gap: 30.0, placement: RingEscapeParams.PlacementRandom, rounds: new[] { 4, 7, 40, 100 }),
                // QA round 3 (r2): the 1.5 × 180 s margin still showed these (each escapes within 180 s on the full probe).
                (rings: 20, gap: 26.0, placement: RingEscapeParams.PlacementSpiral, rounds: new[] { 35 }),
                (rings: 30, gap: 26.0, placement: RingEscapeParams.PlacementSpiral, rounds: new[] { 35, 40, 46 }),
                (rings: 30, gap: 29.0, placement: RingEscapeParams.PlacementRandom, rounds: new[] { 37, 45 }),
                (rings: 40, gap: 29.0, placement: RingEscapeParams.PlacementRandom, rounds: new[] { 37, 48 }),
            };
            foreach (var c in cases)
            {
                ParamSet values = RingSet(mode, DeviceTier.A, c.rings, c.gap, c.placement);
                foreach (int rounds in c.rounds)
                {
                    int calls = 0;
                    SetupHint hint = provider.Evaluate(values, SetupHintRegistry.MaxProbeTicks, () => ++calls >= rounds);
                    Assert.IsNull(hint, c.rings + " rings at " + c.gap + "° " + c.placement + ", stopped after " + rounds + " rounds: no false hint");
                }
            }

            ParamSet hard = RingSet(mode, DeviceTier.A, 100, 26);
            int n = 0;
            RingEscapeDurationEstimate cut = RingEscapeDurationHint.Estimate(hard, RingEscapeDurationHintProvider.ProbeSeed, SetupHintRegistry.MaxProbeTicks, () => ++n >= 60);
            Assert.IsTrue(cut.BudgetExhausted && cut.ShowHint, "stopped after 60 rounds: an extrapolated verdict");
            Assert.IsTrue(RingEscapeDurationHintProvider.ShowsHint(cut), "100 rings at gapMin, projected " + cut.ProjectedSeconds + " s: the clear hint is kept");
            n = 0;
            Assert.IsNotNull(provider.Evaluate(hard, SetupHintRegistry.MaxProbeTicks, () => ++n >= 60));
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
