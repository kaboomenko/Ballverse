using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes
{
    public class ModeFrameworkTests
    {
        static ParamSet FakeDefaults => FakeBoxMode.Instance.Schema.CreateDefaults();

        // ------------------------------------------------------------------ registry

        [Fact]
        public void ReleaseRegistryHoldsRingEscapeOnlyUntilIntegration()
        {
            ModeRegistry r = ReleaseModes.CreateRegistry();
            Assert.Equal(1, r.Count);
            Assert.Same(RingEscapeMode.Instance, r.Get(ModeCode.M01));
            Assert.True(r.TryGet("ring_escape", out IModeDefinition byId));
            Assert.Same(RingEscapeMode.Instance, byId);
            Assert.False(r.TryGet(ModeCode.M02, out _));
            Assert.Empty(r.Available(new[] { ModeCode.M01 }));
            Assert.Single(r.Available(null));
        }

        [Fact]
        public void RegistryRejectsDuplicatesAndUnreleasedModesAndKeepsCodeOrder()
        {
            var r = new ModeRegistry();
            r.Register(FakeBoxMode.Instance);   // M05 info
            r.Register(RingEscapeMode.Instance); // M01 goes first
            Assert.Equal(ModeCode.M01, r.All[0].Info.Code);
            Assert.Equal(ModeCode.M05, r.All[1].Info.Code);
            Assert.Throws<InvalidOperationException>(() => r.Register(RingEscapeMode.Instance));
            Assert.Throws<ArgumentException>(() => r.Register(new UnreleasedMode()));
        }

        sealed class UnreleasedMode : IModeDefinition
        {
            public ModeInfo Info => ModeCatalog.Get(ModeCode.M03);
            public ushort SimVersion => 1;
            public ParamSchema Schema => CommonParams.Schema;
            public IReadOnlyList<Ballverse.Core.Params.Presets.Preset> BuiltInPresets => Array.Empty<Ballverse.Core.Params.Presets.Preset>();
            public ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues) => sanitized;
            public bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max) { min = max = 0; return false; }
            public int GetParticipantCount(ParamSet values) => 0;
            public ParamSet GenerateCandidate(in ModeCandidateRequest request) => request.BaseValues;
            public IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context) => throw new NotSupportedException();
        }

        // ------------------------------------------------------------------ building blocks

        [Fact]
        public void VisualTableMatchesTheRenderStreamWriterIds()
        {
            var table = new ModeVisualTable();
            var writer = new RenderStreamWriter(new MemoryStream(), new RenderStreamHeader("x"));
            var ring = ShapeDefinition.Ring(1000, 100, new[] { new RingGap(0, 5000) });
            Assert.Equal(0, table.DefineShape(ShapeDefinition.Circle));
            Assert.Equal(1, table.DefineShape(ring));
            Assert.Equal(0, table.DefineShape(ShapeDefinition.Circle));
            Assert.Equal(1, table.DefineShape(ShapeDefinition.Ring(1000, 100, new[] { new RingGap(0, 5000) })));
            Assert.Equal(0, writer.DefineShape(ShapeDefinition.Circle));
            Assert.Equal(1, writer.DefineShape(ring));
            Assert.Equal(1, table.DefineLabel("1"));
            Assert.Equal(2, table.DefineLabel("Anna"));
            Assert.Equal(1, table.DefineLabel("1"));
            Assert.Equal("Anna", table.GetLabel(2));
            Assert.Throws<ArgumentException>(() => table.DefineLabel(""));
        }

        [Fact]
        public void EventBufferStampsTicksAndIdsAndHonoursWants()
        {
            var b = new ModeEventBuffer { Tick = 42 };
            b.AddRender(RenderEvent.Create(0, RenderEventType.Hit, 99, 1f, 2f, 0, 1f));
            b.AddRender(RenderEvent.Create(0, RenderEventType.Break, 99, 1f, 2f, 0, 1f));
            b.AddSound(SoundEvent.Hit(0, 3, 1f));
            b.AddReplay(ReplayEventType.Hit, 3, 10, -2);
            Assert.Equal(42, b.RenderEvents[0].Tick);
            Assert.Equal(0, b.RenderEvents[0].Id);
            Assert.Equal(1, b.RenderEvents[1].Id);
            Assert.Equal(42, b.SoundEvents[0].Tick);
            Assert.Equal(42u, b.ReplayEvents[0].Tick);
            b.Clear();
            Assert.Equal(0, b.RenderEvents.Length);
            b.WantsRender = b.WantsSound = b.WantsReplay = false;
            b.AddRender(default);
            b.AddSound(default);
            b.AddReplay(ReplayEventType.Hit, 0, 0, 0);
            Assert.Equal(0, b.RenderEvents.Length + b.SoundEvents.Length + b.ReplayEvents.Length);
        }

        [Fact]
        public void MetricsBuildAValidSummary()
        {
            var world = new PhysicsWorld();
            var scene = FakeBoxMode.Instance.BuildScene(world, new ModeSceneContext(FakeBoxMode.Instance, FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(120)), 1));
            var runner = new SimRunner(world, scene);
            runner.RunHeadless(1000);
            var m = new ModeMetrics(2);
            m.AddCollisions(5);
            m.AddSignificant(10);
            m.AddSignificant(10);
            m.AddSignificant(130);
            Assert.Throws<ArgumentException>(() => m.AddSignificant(5));
            m.SampleStandings(60, 1, 0);
            m.SampleStandings(120, 0, 1);
            CandidateRunSummary s = m.ToSummary(runner, false, 0.5, 2.0, 1, false);
            Assert.True(s.Finished);
            Assert.Equal(120, s.DurationTicks);
            Assert.Equal(new[] { 10 }, s.SignificantEventTicks); // 130 lies after the finale
            Assert.Equal(new[] { 1, 0 }, s.LeaderBySecond);
            Assert.Equal(1.0, s.FinishTension);
            Assert.Equal(5, s.CollisionCount);
        }

        [Fact]
        public void ModeSoundUsesQuantizedSizeAndPan()
        {
            var q = new RenderQuantization(16f, 16f);
            SoundEvent e = ModeSound.ForBall(SoundEventType.Hit, 4, 1.5f, q, 0.175f, 9f);
            Assert.Equal(1f, e.Pan);
            Assert.Equal(2f * q.DequantizeScale(q.QuantizeScale(0.175f)), e.BodySize);
            Assert.Equal(-1, ModeSound.Bodiless(SoundEventType.Finish, 1f, 0.3f).BodyId);
        }

        // ------------------------------------------------------------------ session

        [Fact]
        public void SessionRejectsUnsanitizedValues()
        {
            ParamSet bad = FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(5));
            Assert.Throws<ArgumentException>(() => new ModeSession(FakeBoxMode.Instance, bad, 1));
        }

        [Fact]
        public void SessionRecordsEverySinkAndReplaysWithInputsAndLiveChanges()
        {
            ParamSet values = FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(400));
            Action<ModeSession> script = s =>
            {
                if (s.Tick == 30) s.EnqueueInput(FakeBoxMode.KickInput, 3000, 5000);
                if (s.Tick == 100) Assert.Equal(0.5, s.SetLiveParam(FakeBoxMode.Kick, ParamValue.Float(0.1)).AsFloat, 5); // clamped
                if (s.Tick == 150) s.EnqueueInput(FakeBoxMode.KickInput, -6000, 1000);
                if (s.Tick == 200) s.SetLiveParam(FakeBoxMode.Kick, ParamValue.Float(3.0));
                if (s.Tick == 210) s.EnqueueInput(FakeBoxMode.KickInput, 2000, -9000);
            };
            ModeConformance.Recording rec = ModeConformance.AssertDeterministic(FakeBoxMode.Instance, values, 77, 10000, script);
            Assert.Equal(430, rec.Session.Tick); // finale at 400 + 30
            Assert.True(rec.Outcome.IsGameCompleted);
            Assert.Equal(GameRunEnd.Finished, rec.Outcome.Run.End);
            Assert.Equal(430, rec.Outcome.Run.DurationTicks);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            Assert.Equal(3, file.Inputs.Count);
            Assert.Equal(30u, file.Inputs[0].Tick);
            Assert.Equal(new[] { 3000, 5000, 0, 0 }, file.Inputs[0].Values);
            Assert.Equal(2, file.ParamChanges.Count);
            Assert.Equal(100u, file.ParamChanges[0].Tick);
            Assert.Equal(7, file.Hashes.Count); // ticks 60…420
            Assert.NotNull(file.Result);
            Assert.Equal((uint)rec.Session.Tick, file.Header.DurationTicks);
            Assert.True(rec.SoundEvents.Count > 0);
            Assert.Equal(rec.RenderEvents.Count, rec.SoundEvents.Count);
            ModeConformance.AssertReplayRoundTrip(FakeBoxMode.Instance, rec);
            ModeConformance.AssertRenderStreamReadable(FakeBoxMode.Instance, rec);
        }

        [Fact]
        public void TamperedReplayFailsVerification()
        {
            ParamSet values = FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(300));
            ModeConformance.Recording rec = ModeConformance.Record(FakeBoxMode.Instance, values, 5, 10000,
                s => { if (s.Tick == 40) s.EnqueueInput(FakeBoxMode.KickInput, 4000, 0); });
            ReplayFile file = ReplayReader.Read(rec.Replay);
            var noInputs = new ReplayFile(file.Header, null, file.ParamChanges, file.Hashes, file.Events, file.Result);
            ModeReplayResult r = ModeReplay.Resimulate(FakeBoxMode.Instance, noInputs);
            Assert.Equal(ModeReplayStatus.HashMismatch, r.Status);
            Assert.Equal(60u, r.Verification.Tick);

            var other = new ReplayFile(new ReplayHeader("ring_escape", 1, "", PhysicsBackend.CoreSim, 1, file.Header.ParamsJson), null, null, null, null, null);
            Assert.Equal(ModeReplayStatus.WrongMode, ModeReplay.Resimulate(FakeBoxMode.Instance, other).Status);
            var old = new ReplayFile(new ReplayHeader(file.Header.ModeId, 6, "", PhysicsBackend.CoreSim, 1, file.Header.ParamsJson), null, null, null, null, null);
            Assert.Equal(ModeReplayStatus.SimVersionMismatch, ModeReplay.Resimulate(FakeBoxMode.Instance, old).Status);
            var box2d = new ReplayFile(new ReplayHeader(file.Header.ModeId, 7, "", PhysicsBackend.Box2DV3, 1, file.Header.ParamsJson), null, null, null, null, null);
            Assert.Equal(ModeReplayStatus.UnsupportedBackend, ModeReplay.Resimulate(FakeBoxMode.Instance, box2d).Status);
        }

        [Fact]
        public void LivePacingFollowsTheTimeScaleAndMatchesHeadless()
        {
            ParamSet values = FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(600));
            var live = new ModeSession(FakeBoxMode.Instance, values, 3, new ModeSessionOptions { BuildFrames = false });
            live.TimeScale = 0.25f;
            int[] steps = new int[8];
            for (int i = 0; i < 8; i++) steps[i] = live.AdvanceDisplayFrame(60);
            Assert.Equal(new[] { 0, 0, 0, 1, 0, 0, 0, 1 }, steps);
            live.SetLiveParam(CommonParams.TimeScale, ParamValue.Float(3.0));
            Assert.Equal(3f, live.TimeScale);
            Assert.Equal(3, live.AdvanceDisplayFrame(60));
            Assert.Equal(6, live.AdvanceDisplayFrame(30)); // 30 Hz display: twice the ticks per frame
            live.Paused = true;
            Assert.Equal(0, live.AdvanceDisplayFrame(60));
            live.Paused = false;
            while (live.Tick < 300) live.AdvanceDisplayFrame(60);

            var headless = new ModeSession(FakeBoxMode.Instance, values, 3, new ModeSessionOptions { BuildFrames = false });
            headless.RunHeadless(live.Tick);
            Assert.Equal(headless.Runner.ComputeStateHash(), live.Runner.ComputeStateHash());
        }

        [Fact]
        public void AbandonedRunIsNotAGameCompleted()
        {
            var s = new ModeSession(FakeBoxMode.Instance, FakeDefaults, 9, new ModeSessionOptions { Replay = new MemoryStream() });
            s.RunHeadless(400);
            ModeSessionOutcome o = s.Complete();
            Assert.Equal(GameRunEnd.Abandoned, o.Run.End);
            Assert.False(o.IsGameCompleted);
            Assert.Null(o.ReplayResult);
            Assert.Same(o, s.Complete());
            Assert.Throws<InvalidOperationException>(() => s.Step());
            ModeSessionOutcome restart = new ModeSession(FakeBoxMode.Instance, FakeDefaults, 9).Complete(GameRunEnd.RestartedBeforeFinale);
            Assert.False(restart.IsGameCompleted);
        }

        [Fact]
        public void StructuralParametersCannotChangeWhileRunning()
        {
            var s = new ModeSession(FakeBoxMode.Instance, FakeDefaults, 1);
            Assert.Throws<ArgumentException>(() => s.SetLiveParam(FakeBoxMode.Lifetime, ParamValue.Int(100)));
            Assert.Throws<ArgumentException>(() => s.SetLiveParam("nope", ParamValue.Int(1)));
        }

        // ------------------------------------------------------------------ generation driver

        [Fact]
        public void GeneratorSelectsACandidateThatReplaysIdentically()
        {
            ParamSet values = FakeDefaults.With(CommonParams.TargetDuration, ParamValue.Int(10));
            ModeGenerationResult g = ModeGenerator.Generate(FakeBoxMode.Instance, new ModeGenerationRequest
            {
                BaseValues = values,
                SessionSeed = 1234,
                Budget = new GenerationBudget(6, 1000),
            });
            Assert.True(g.HasSelection);
            Assert.Contains(g.Result.CandidatesEvaluated, new[] { 6, 12 }); // 12 when nothing passed and the budget doubled
            CandidateEvaluation sel = g.Result.Selected;
            Assert.Equal(sel.Seed, g.Seed);
            // The chosen candidate is the run the user gets: same parameters and seed → same finale tick.
            var live = new ModeSession(FakeBoxMode.Instance, g.Values, g.Seed);
            while (live.Phase == SimPhase.Running && live.Step()) { }
            Assert.Equal(sel.Summary.DurationTicks, live.Runner.FinaleTick);
            // Deterministic: the same press gives the same choice.
            ModeGenerationResult again = ModeGenerator.Generate(FakeBoxMode.Instance, new ModeGenerationRequest
            {
                BaseValues = values,
                SessionSeed = 1234,
                Budget = new GenerationBudget(6, 1000),
            });
            Assert.Equal(g.Seed, again.Seed);
            Assert.Equal(g.Values, again.Values);
        }

        [Fact]
        public void GeneratorHonoursCancellationAndKeepParams()
        {
            ParamSet values = FakeDefaults.With(FakeBoxMode.Lifetime, ParamValue.Int(200));
            int polls = 0;
            ModeGenerationResult cancelled = ModeGenerator.Generate(FakeBoxMode.Instance, new ModeGenerationRequest
            {
                BaseValues = values,
                IsCancelled = () => ++polls > 2,
            });
            Assert.Equal(GenerationOutcome.Cancelled, cancelled.Result.Outcome);
            Assert.False(cancelled.HasSelection);

            ModeGenerationResult kept = ModeGenerator.Generate(FakeBoxMode.Instance, new ModeGenerationRequest
            {
                BaseValues = values,
                KeepParams = true,
                Budget = new GenerationBudget(3, 1000),
            });
            Assert.True(kept.HasSelection);
            Assert.Equal(values, kept.Values);
        }

        [Collection(SerialMeasurementCollection.Name)]
        public class Allocation
        {
            [Fact]
            public void FakeModeSteadyStateDoesNotAllocate()
            {
                long bytes = ModeConformance.MeasureSteadyStateAllocation(FakeBoxMode.Instance,
                    FakeBoxMode.Instance.Schema.CreateDefaults().With(FakeBoxMode.Lifetime, ParamValue.Int(3000)), 4, 120, 600);
                Assert.Equal(0, bytes);
            }
        }
    }
}
