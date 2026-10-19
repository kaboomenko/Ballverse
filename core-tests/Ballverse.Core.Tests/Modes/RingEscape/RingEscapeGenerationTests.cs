using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    public class RingEscapeGenerationTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet Base(int? target)
        {
            ParamSet d = Mode.Schema.CreateDefaults();
            if (target.HasValue) d = d.With(CommonParams.TargetDuration, ParamValue.Int(target.Value));
            return ModeParams.Prepare(Mode, d, DeviceTier.B).Values;
        }

        [Fact]
        public void DurationFitMeetsTheSpecAcceptance()
        {
            // Spec §2.4 acceptance: targetDuration = 30 s → the finale falls in 30 ± 3 s in at least 80 % of generations (tier B budget).
            const int n = 40;
            int inWindow = 0;
            for (int i = 0; i < n; i++)
            {
                ModeGenerationResult g = ModeGenerator.Generate(Mode, new ModeGenerationRequest
                {
                    BaseValues = Base(30),
                    Tier = DeviceTier.B,
                    SessionSeed = (ulong)(1000 + i),
                });
                Assert.True(g.HasSelection);
                double seconds = g.Result.Selected.Summary.DurationTicks / 60.0;
                if (Math.Abs(seconds - 30.0) <= 3.0) inWindow++;
            }
            Assert.True(inWindow >= n * 8 / 10, "in window " + inWindow + " of " + n);
        }

        [Fact]
        public void ChosenCandidateIsTheRunTheUserGets()
        {
            ModeGenerationResult g = ModeGenerator.Generate(Mode, new ModeGenerationRequest { BaseValues = Base(20), SessionSeed = 77, Style = GenerationStyle.TenseFinale });
            Assert.True(g.HasSelection);
            Assert.True(ModeParams.IsRunnable(Mode, g.Values, out string problem), problem);
            var live = new ModeSession(Mode, g.Values, g.Seed, new ModeSessionOptions { BuildFrames = false });
            live.RunToEnd();
            var scene = (RingEscapeScene)live.Scene;
            Assert.Equal(g.Result.Selected.Summary.DurationTicks, scene.EscapeTick);
            Assert.True(g.Values.GetInt(RingEscapeParams.GapShrink) > 0);
        }

        [Fact]
        public void ForcedWinnerWinsTheLiveRun()
        {
            // Spec §2.5: the chosen colour wins; physics is not touched, only the seed (and parameters) are chosen.
            ParamSet values = ModeParams.Prepare(Mode, Base(25).With(CommonParams.BallCount, ParamValue.Int(4))
                .With(RingEscapeParams.Competition, ParamValue.Bool(true)).With(CommonParams.ForcedWinner, ParamValue.Int(3)), DeviceTier.B).Values;
            Assert.Equal(4, Mode.GetParticipantCount(values));
            ModeGenerationResult g = ModeGenerator.Generate(Mode, new ModeGenerationRequest
            {
                BaseValues = values,
                SessionSeed = 5,
                Style = GenerationStyle.Chaos,
                Goal = DirectorGoal.LastSecond,
                AutoSearchLonger = 1,
            });
            Assert.True(g.HasSelection);
            Assert.True(g.Result.WinnerMatched, g.Result.Outcome.ToString());
            Assert.Equal(4, g.Values.GetInt(CommonParams.BallCount)); // participants kept
            var live = new ModeSession(Mode, g.Values, g.Seed, new ModeSessionOptions { BuildFrames = false });
            live.RunToEnd();
            Assert.Equal(SimOutcome.Win, live.Runner.Result.Outcome);
            Assert.Equal(2, live.Runner.Result.Winner); // forcedWinner 3 = third colour = index 2
        }

        [Fact]
        public void EveryStyleBuildsValidCandidatesWithItsShape()
        {
            ParamSet b = Base(40);
            for (int i = 0; i < 60; i++)
            {
                var style = (GenerationStyle)(i % 6);
                ParamSet v = Mode.GenerateCandidate(new ModeCandidateRequest { BaseValues = b, Seed = (ulong)i, Style = style, Tier = DeviceTier.B });
                Assert.True(ModeParams.IsRunnable(Mode, v, out string problem), style + ": " + problem);
                RingEscapeValidationTests.AssertAllRules(v);
                Assert.Equal(v, Mode.GenerateCandidate(new ModeCandidateRequest { BaseValues = b, Seed = (ulong)i, Style = style, Tier = DeviceTier.B }));
                RingEscapeConfig c = RingEscapeConfig.From(v);
                Assert.Equal(RingProgression.Compress, c.Progression);
                switch (style)
                {
                    case GenerationStyle.Meditation:
                        Assert.Equal(1, c.BallCount);
                        Assert.Equal("Pastel", v.GetString(CommonParams.Theme));
                        break;
                    case GenerationStyle.Minimalism:
                        Assert.Equal(1, c.BallCount);
                        Assert.Equal("Mono", v.GetString(CommonParams.Theme));
                        Assert.InRange(c.RingCount, 6, 10);
                        break;
                    case GenerationStyle.ManyBalls:
                        Assert.True(c.CompetitionActive);
                        Assert.InRange(c.BallCount, 10, 12); // tier B cap
                        break;
                    case GenerationStyle.Chaos:
                        Assert.InRange(c.BallCount, 2, 12);
                        Assert.Equal(RingSpeedLaw.Reverse, c.SpeedLaw);
                        break;
                    case GenerationStyle.TenseFinale:
                        float last = RingEscapeGeometry.GapDeg(c, c.RingCount - 1);
                        float gapMin = RingEscapeGeometry.GapMinDeg(c.InnerRadius, c.BallSize, c.RingThickness);
                        Assert.InRange(last / gapMin, 1.0f, 1.45f);
                        break;
                }
            }
        }

        [Fact]
        public void GeneratorResultsEscapeBeforeTheCap()
        {
            // Card test (в): generator results (all styles, targetDuration 15–120 s) escape before 180 s in ≥ 99 %.
            int n = Environment.GetEnvironmentVariable("BALLVERSE_LONG_TESTS") == "1" ? 1000 : 36;
            int[] targets = { 15, 30, 45, 60, 90, 120 };
            int escaped = 0;
            for (int i = 0; i < n; i++)
            {
                ModeGenerationResult g = ModeGenerator.Generate(Mode, new ModeGenerationRequest
                {
                    BaseValues = Base(targets[i % targets.Length]),
                    Tier = DeviceTier.C,
                    SessionSeed = (ulong)(5000 + i),
                    Style = (GenerationStyle)(i % 6),
                });
                if (!g.HasSelection) continue;
                var s = new ModeSession(Mode, g.Values, g.Seed, new ModeSessionOptions { BuildFrames = false });
                s.RunToEnd();
                var scene = (RingEscapeScene)s.Scene;
                if (scene.EscapeTick >= 0 && scene.EscapeTick < SimConstants.MaxDurationTicks) escaped++;
            }
            Assert.True(escaped >= n * 99 / 100, "escaped " + escaped + " of " + n);
        }

        [Fact]
        public void EveryPresetEscapesOnAHundredSeeds()
        {
            // Card test (в): each built-in preset on 100 seeds escapes before 180 s in ≥ 99 %.
            foreach (var preset in Mode.BuiltInPresets)
            {
                ParamSet v = ModeParams.ApplyPreset(Mode, preset, DeviceTier.A).Values;
                int escaped = 0;
                for (ulong seed = 0; seed < 100; seed++)
                {
                    var s = new ModeSession(Mode, v, seed * 7919 + 1, new ModeSessionOptions { BuildFrames = false });
                    s.RunToEnd();
                    if (((RingEscapeScene)s.Scene).EscapeTick >= 0) escaped++;
                }
                Assert.True(escaped >= 99, preset.Id + ": escaped " + escaped + " of 100");
            }
        }

        [Fact]
        public void CandidateSummaryRejectsRunsWithoutEscape()
        {
            // 100 rings at gapMin cannot escape within 10 s × 1.3: the candidate is rejected (card «Отбраковка»).
            ParamSet hard = ModeParams.Prepare(Mode, Base(10).With(RingEscapeParams.RingCount, ParamValue.Int(100))
                .With(RingEscapeParams.GapSize, ParamValue.Float(26)), DeviceTier.B).Values;
            CandidateRunSummary s = ModeGenerator.RunCandidate(Mode, hard, 1, GenerationSession.RunLimitTicks(600));
            Assert.True(s.Stuck);
            Assert.True(SpectacleScorer.Score(s, 600, SpectacleWeights.Default, new DensityRange(1.5, 8)).IsRejected);
            ParamSet easy = Base(30);
            CandidateRunSummary ok = ModeGenerator.RunCandidate(Mode, easy, 1, GenerationSession.RunLimitTicks(1800));
            Assert.True(ok.Finished);
            Assert.False(ok.Stuck);
            Assert.True(ok.CollisionCount > 0);
            Assert.NotEmpty(ok.SignificantEventTicks);
            Assert.InRange(ok.Coverage, 0.5, 1.0);
        }

        [Fact]
        public void DurationHintShowsOnlyWhenTheEscapeCannotFit()
        {
            // Docs QA residual: the hint runs within the 🎲 budget and extrapolates when the budget ends first.
            // Budget: tier B 2.5 s at a conservative 10× real time = 25 s of game time over the three seeds.
            const int budget = 25 * SimConstants.TickRate;
            RingEscapeDurationEstimate defaults = RingEscapeDurationHint.Estimate(Base(null), 1, budget);
            Assert.False(defaults.ShowHint);
            Assert.True(defaults.TicksRun <= budget);
            Assert.True(defaults.ProjectedSeconds < 180f);

            ParamSet hard = ModeParams.Prepare(Mode, Base(null).With(RingEscapeParams.RingCount, ParamValue.Int(100))
                .With(RingEscapeParams.GapSize, ParamValue.Float(26)), DeviceTier.B).Values;
            RingEscapeDurationEstimate h = RingEscapeDurationHint.Estimate(hard, 1, budget);
            Assert.True(h.ShowHint);
            Assert.True(h.BudgetExhausted);
            Assert.Equal(0, h.SeedsEscaped);
            Assert.True(h.ProjectedSeconds > 180f);

            // An unlimited budget on a quick set finds an escape and stops early.
            ParamSet quick = ModeParams.Prepare(Mode, Base(null).With(RingEscapeParams.RingCount, ParamValue.Int(3)), DeviceTier.B).Values;
            RingEscapeDurationEstimate q = RingEscapeDurationHint.Estimate(quick, 1, int.MaxValue);
            Assert.True(q.SeedsEscaped >= 1);
            Assert.False(q.ShowHint);
            Assert.False(q.BudgetExhausted);
        }
    }
}
