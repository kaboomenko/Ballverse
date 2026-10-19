using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    public class RingEscapeSimulationTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet Prepared(params (string id, ParamValue v)[] values)
        {
            ParamSet s = Mode.Schema.CreateDefaults();
            foreach (var (id, v) in values) s = s.With(id, v);
            return ModeParams.Prepare(Mode, s, DeviceTier.A).Values;
        }

        static ParamSet Preset(string id)
        {
            foreach (var p in Mode.BuiltInPresets)
                if (p.Id == id) return ModeParams.ApplyPreset(Mode, p, DeviceTier.A).Values;
            throw new KeyNotFoundException(id);
        }

        [Fact]
        public void DefaultsEscapeAndRecordDeterministically()
        {
            ModeConformance.Recording rec = ModeConformance.AssertDeterministic(Mode, Prepared(), 2026, SimConstants.MaxDurationTicks);
            var scene = (RingEscapeScene)rec.Session.Scene;
            Assert.True(scene.EscapeTick > 0);
            Assert.Equal(12, scene.RingsPassed);
            Assert.Equal(SimOutcome.Completed, rec.Outcome.Result.Outcome);
            Assert.Equal(scene.EscapeTick + RingEscapeScene.FinaleTicks, rec.Session.Tick);
            Assert.True(rec.Outcome.IsGameCompleted);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            Assert.Equal(12, CountEvents(file, ReplayEventType.Break));
            Assert.Equal(1, CountEvents(file, ReplayEventType.Finish));
            Assert.Equal(new[] { 12 }, file.Result.Scores);
            Assert.Equal((uint)scene.EscapeTick, file.Result.FinishTick);
            Assert.True(CountRender(rec.RenderEvents, RenderEventType.Break) == 12);
            Assert.Equal(1, CountRender(rec.RenderEvents, RenderEventType.Escape));
            Assert.True(CountRender(rec.RenderEvents, RenderEventType.Hit) > 0);
            ModeConformance.AssertReplayRoundTrip(Mode, rec);
            ModeConformance.AssertRenderStreamReadable(Mode, rec);

            // Another seed gives another run.
            ModeConformance.Recording other = ModeConformance.Record(Mode, Prepared(), 2027, 600);
            Assert.NotEqual(rec.Hashes[0], other.Hashes[0]);
        }

        [Fact]
        public void CompetitionWithLiveRotationEditsReplaysExactly()
        {
            ParamSet values = Preset(RingEscapePresets.ColorRace);
            Action<ModeSession> script = s =>
            {
                if (s.Tick == 90) s.SetLiveParam(RingEscapeParams.RotationSpeed, ParamValue.Float(140));
                if (s.Tick == 200) s.SetLiveParam(RingEscapeParams.RingPalette, ParamValue.String(RingEscapeParams.PaletteRainbow));
                if (s.Tick == 240) s.SetLiveParam(CommonParams.Labels, ParamValue.String("numbers"));
            };
            ModeConformance.Recording rec = ModeConformance.AssertDeterministic(Mode, values, 31, SimConstants.MaxDurationTicks, script);
            var scene = (RingEscapeScene)rec.Session.Scene;
            Assert.Equal(6, scene.ParticipantCount);
            Assert.Equal(SimOutcome.Win, rec.Outcome.Result.Outcome);
            int winner = rec.Outcome.Result.Winner;
            Assert.InRange(winner, 0, 5);
            Assert.Equal(winner, rec.Outcome.ReplayResult.WinnerIndex);
            Assert.Equal("color" + winner, rec.Outcome.ReplayResult.WinnerId);
            Assert.Equal(6, rec.Outcome.ReplayResult.Scores.Count);
            int sum = 0;
            foreach (int x in rec.Outcome.ReplayResult.Scores) sum += x;
            Assert.Equal(scene.RingsPassed, sum);
            Assert.Equal(140f, scene.RotationSpeedDeg);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            Assert.Equal(3, file.ParamChanges.Count);
            ModeConformance.AssertReplayRoundTrip(Mode, rec);
        }

        [Fact]
        public void RenderStreamCarriesRingsBallsLabelsAndHud()
        {
            ParamSet values = Prepared((CommonParams.BallCount, ParamValue.Int(3)), (CommonParams.Labels, ParamValue.String("numbers")),
                (RingEscapeParams.GapCount, ParamValue.Int(2)));
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 4, 240);
            RenderStreamReader reader = ModeConformance.AssertRenderStreamReadable(Mode, rec);
            Assert.Equal(16f, reader.Header.Quantization.PositionRange);
            Assert.Equal(16f, reader.Header.Quantization.ScaleMax);
            Assert.Equal(ShapeKind.Circle, reader.Shapes[0].Kind);
            // Without gap shrink every ring has the same geometry (gap positions are body angles): one shared ring shape.
            Assert.Equal(2, reader.Shapes.Count);
            Assert.Equal(ShapeKind.Ring, reader.Shapes[1].Kind);
            Assert.Equal(2, reader.Shapes[1].Gaps.Length);
            Assert.True(reader.Shapes[1].FixedThickness);
            ModeConformance.Recording shrink = ModeConformance.Record(Mode, values.With(RingEscapeParams.GapShrink, ParamValue.Int(20)), 4, 10);
            Assert.Equal(1 + 12, new RenderStreamReader(new MemoryStream(shrink.RenderStream)).Shapes.Count);
            RenderFrame f0 = reader.ReadFrame(0);
            int ringBodies = 0, balls = 0;
            foreach (BodyState b in f0.Bodies)
            {
                if (b.Id >= RingEscapeScene.RingIdBase)
                {
                    ringBodies++;
                    Assert.True((b.Flags & RenderBodyFlags.FadeOutsideSafeZone) != 0);
                }
                else
                {
                    balls++;
                    Assert.Equal(b.Id + 1, int.Parse(reader.GetLabel(b.LabelId)));
                }
            }
            Assert.Equal(12, ringBodies);
            Assert.Equal(3, balls);
            Assert.True(f0.TryGetHud(HudKeys.ProgressTotal, out int total) && total == 12);
            Assert.True(f0.TryGetHud(HudKeys.Progress, out int passed) && passed == 0);
        }

        [Theory]
        [InlineData(RingEscapeParams.ProgressionCameraZoom, 100)]
        [InlineData(RingEscapeParams.ProgressionCompress, 100)]
        public void RenderStreamAtExtremeRingRadiiIsNeverClamped(string progression, int rings)
        {
            // Card «Поток рендера» / §8.5: rings up to 12 m with PositionRange 16 m, ScaleMax 16 m round-trip exactly.
            ParamSet values = Prepared((RingEscapeParams.Progression, ParamValue.String(progression)), (RingEscapeParams.InnerRadius, ParamValue.Float(0.6)),
                (RingEscapeParams.RingSpacing, ParamValue.Float(0.3)), (RingEscapeParams.RingThickness, ParamValue.Float(0.04)),
                (CommonParams.BallSize, ParamValue.Float(0.2)), (RingEscapeParams.RingCount, ParamValue.Int(rings)),
                (RingEscapeParams.GapSize, ParamValue.Float(60)));
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 8, 120);
            var reader = new RenderStreamReader(new MemoryStream(rec.RenderStream));
            RenderQuantization q = reader.Header.Quantization;
            var scene = (RingEscapeScene)rec.Session.Scene;
            RenderFrame f = reader.ReadFrame(0);
            float maxRadius = 0f;
            foreach (BodyState b in f.Bodies)
            {
                if (b.Id < RingEscapeScene.RingIdBase) continue;
                int j = (b.Id - RingEscapeScene.RingIdBase) % 128;
                float radius = q.DequantizeScale(b.Scale) * RingEscapeScene.ReferenceRadius;
                Assert.Equal(RingEscapeGeometry.RingRadius(scene.Config, j), radius, 2);
                Assert.True(b.Scale < ushort.MaxValue);
                maxRadius = MathF.Max(maxRadius, radius);
            }
            Assert.InRange(maxRadius, 11.9f, RingEscapeGeometry.MaxRingRadius + 0.01f);
            // One position step is far below a pixel of a 1080p frame covering the 16 m arena height at the widest camera.
            Assert.True(q.PositionStep < 16.0 / 1920.0);
        }

        [Fact]
        public void CompressShiftsTheNextRingInWithinTheCardsSpeed()
        {
            ParamSet values = Prepared();
            var world = new PhysicsWorld();
            var scene = (RingEscapeScene)Mode.BuildScene(world, new ModeSceneContext(Mode, values, 11));
            var runner = new SimRunner(world, scene);
            while (scene.RingsPassed == 0) Assert.True(runner.Step());
            int breakTick = runner.Tick;
            Assert.True(scene.IsRingDestroyed(0));
            float start = scene.RingRadius(1);
            Assert.Equal(1.8f, start, 2);
            while (scene.RingRadius(1) > 1.2001f && scene.RingsPassed == 1) runner.Step();
            if (scene.RingsPassed == 1)
            {
                int took = runner.Tick - breakTick;
                Assert.InRange(took, 20, 27); // 0.4 s ± a couple of ticks
                Assert.Equal(1.2f, scene.RingRadius(1), 3);
            }
        }

        [Fact]
        public void CountdownHoldsTheBallsForThreeSeconds()
        {
            ParamSet values = Prepared((CommonParams.Countdown, ParamValue.Bool(true)));
            var s = new ModeSession(Mode, values, 5);
            s.Start();
            Assert.True(s.Frame.TryGetHud(HudKeys.Countdown, out int c0) && c0 == 3);
            s.RunHeadless(RingEscapeScene.CountdownTicks - 1);
            Assert.Equal(Vec2.Zero, s.World.GetBall(0).Position);
            Assert.True(s.Frame.TryGetHud(HudKeys.Countdown, out int c1) && c1 == 1);
            s.RunHeadless(10);
            Assert.NotEqual(Vec2.Zero, s.World.GetBall(0).Position);
            Assert.True(s.Frame.TryGetHud(HudKeys.Countdown, out int c2) && c2 == 0);
        }

        [Fact]
        public void TargetDurationEndsTheRunByScoreAtTheGeneratorsLimit()
        {
            ParamSet values = Prepared((CommonParams.TargetDuration, ParamValue.Int(10)), (RingEscapeParams.RingCount, ParamValue.Int(100)),
                (RingEscapeParams.GapSize, ParamValue.Float(26)));
            var s = new ModeSession(Mode, values, 3, new ModeSessionOptions { BuildFrames = false });
            s.RunToEnd();
            var scene = (RingEscapeScene)s.Scene;
            Assert.Equal(780, s.Runner.FinaleTick); // ⌈10 s × 1.3⌉
            Assert.Equal(SimOutcome.TimeLimit, s.Runner.Result.Outcome);
            Assert.Equal(scene.RingsPassed, s.Runner.Result.Score);
            Assert.True(scene.RingsPassed < 100);
        }

        [Fact]
        public void NewRoundAndNewBallKeepPlayingUntilTheTimeLimit()
        {
            ParamSet round = Prepared((RingEscapeParams.RingCount, ParamValue.Int(3)), (RingEscapeParams.GapSize, ParamValue.Float(60)),
                (RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterNewRound)), (CommonParams.TargetDuration, ParamValue.Int(30)));
            RingEscapeProbe.Result r = RingEscapeProbe.RunChecked(round, 21);
            Assert.True(r.Scene.Escapes >= 2, "escapes " + r.Scene.Escapes);
            Assert.Equal(r.Scene.Escapes, r.Scene.Round);
            Assert.Equal(SimOutcome.TimeLimit, r.Scene.Result.Outcome);
            Assert.Equal(r.Scene.Escapes, r.Scene.Result.Detail);

            ParamSet ball = round.With(RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterNewBall));
            RingEscapeProbe.Result b = RingEscapeProbe.RunChecked(ball, 21);
            Assert.True(b.Scene.Round >= 1);
            Assert.Equal(Math.Min(1 + b.Scene.Round, RingEscapeParams.MaxBalls), b.Scene.RoundBallCount);
        }

        [Theory]
        [InlineData(RingEscapeParams.ProgressionStatic)]
        [InlineData(RingEscapeParams.ProgressionCameraZoom)]
        public void KeptRingsCountOnceAndStayAsWalls(string progression)
        {
            // §8.5: «Статично» / «Отдаление камеры» with destroyPassed off — every ring counted once.
            ParamSet values = Prepared((RingEscapeParams.Progression, ParamValue.String(progression)),
                (RingEscapeParams.DestroyPassed, ParamValue.Bool(false)), (CommonParams.BallCount, ParamValue.Int(3)), (RingEscapeParams.GapSize, ParamValue.Float(50)));
            for (ulong seed = 0; seed < 4; seed++)
            {
                RingEscapeProbe.Result r = RingEscapeProbe.RunChecked(values, seed);
                Assert.True(r.Escaped);
                RingEscapeConfig c = r.Scene.Config;
                for (int j = 0; j < c.RingCount; j++)
                {
                    Assert.True(r.Scene.IsRingPassed(j));
                    Assert.False(r.Scene.IsRingDestroyed(j));
                }
            }
        }

        [Fact]
        public void GrownBallStillPassesTheNarrowestRing()
        {
            // Card test (г): gapShrink at the rule-3 limit and growOnHit 10 % — the grown ball passes the last ring.
            ParamSet values = Prepared((RingEscapeParams.GapShrink, ParamValue.Int(28)), (RingEscapeParams.GrowOnHit, ParamValue.Int(10)));
            Assert.Equal(28, values.GetInt(RingEscapeParams.GapShrink));
            int escaped = 0;
            float grown = 0f;
            for (ulong seed = 0; seed < 12; seed++)
            {
                RingEscapeProbe.Result r = RingEscapeProbe.RunChecked(values, seed);
                if (r.Escaped) escaped++;
                grown = MathF.Max(grown, r.Runner.World.GetBall(0).Radius * 2f);
            }
            Assert.Equal(12, escaped);
            RingEscapeConfig c = RingEscapeConfig.From(values);
            Assert.True(grown > c.BallSize, "the ball never grew");
            Assert.True(grown <= RingEscapeGeometry.GrowLimit(c, 0) + 1e-4f);
            Assert.Equal(0.354f, RingEscapeGeometry.GrowLimit(c, 0), 2); // the card's d_grow example
        }

        [Fact]
        public void ZeroGravityElasticBallKeepsMovingAndEscapes()
        {
            ParamSet values = Prepared((CommonParams.Gravity, ParamValue.Float(0)), (RingEscapeParams.LaunchSpeed, ParamValue.Float(6)),
                (CommonParams.Restitution, ParamValue.Float(1.0)), (RingEscapeParams.RingCount, ParamValue.Int(6)));
            RingEscapeProbe.Result r = RingEscapeProbe.RunChecked(values, 7);
            Assert.True(r.Escaped);
        }

        static int CountEvents(ReplayFile f, ReplayEventType type)
        {
            int n = 0;
            foreach (ReplayEvent e in f.Events) if (e.Type == type) n++;
            return n;
        }

        static int CountRender(RenderEventLog log, RenderEventType type)
        {
            int n = 0;
            foreach (RenderEvent e in log.Events) if (e.Type == type) n++;
            return n;
        }
    }
}
