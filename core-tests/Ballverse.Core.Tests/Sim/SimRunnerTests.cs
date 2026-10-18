using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    public class SimRunnerTests
    {
        static SimRunner NewRunner(ScriptedRules rules, SimRunnerOptions options = null)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(1f, 0f), 0.1f));
            return new SimRunner(w, rules, options);
        }

        [Fact]
        public void StepCallsRulesAroundPhysicsInOrder()
        {
            var order = new List<string>();
            var rules = new ScriptedRules();
            var runner = NewRunner(rules);
            rules.OnTick = (r, n) => order.Add("tick@" + r.Tick);
            rules.OnAfter = r => order.Add("after@" + r.Tick);
            Assert.True(runner.Step());
            Assert.True(runner.Step());
            Assert.Equal(1, rules.StartCalls);
            Assert.Equal(new[] { "tick@0", "after@1", "tick@1", "after@2" }, order);
            Assert.Equal(2, runner.Tick);
            Assert.Equal(2f / 60f, runner.ElapsedSeconds, 5);
        }

        [Fact]
        public void InputsAreAppliedAtTheirTickBoundaryInQueueOrder()
        {
            var rules = new ScriptedRules();
            var runner = NewRunner(rules);
            runner.EnqueueInput(new SimInput(3, 2, 30));
            runner.EnqueueInput(new SimInput(1, 1, 10));
            runner.EnqueueInput(new SimInput(3, 3, 31));
            runner.EnqueueInput(new SimInput(1, 4, 11));
            Assert.Equal(4, runner.PendingInputCount);
            runner.RunHeadless(5);
            Assert.Equal(4, rules.Applied.Count);
            Assert.Equal((1, 10), (rules.Applied[0].tick, rules.Applied[0].input.A));
            Assert.Equal((1, 11), (rules.Applied[1].tick, rules.Applied[1].input.A));
            Assert.Equal((3, 30), (rules.Applied[2].tick, rules.Applied[2].input.A));
            Assert.Equal((3, 31), (rules.Applied[3].tick, rules.Applied[3].input.A));
            Assert.Equal(0, runner.PendingInputCount);
        }

        [Fact]
        public void InputForConsumedTickIsRejected()
        {
            var runner = NewRunner(new ScriptedRules());
            runner.RunHeadless(3);
            Assert.Throws<ArgumentException>(() => runner.EnqueueInput(new SimInput(2, 0)));
            int tick = runner.EnqueueInputNow(7, 1);
            Assert.Equal(3, tick);
        }

        [Fact]
        public void RunnerOnPreSteppedWorldRejectsPastTicksAndDeliversLaterInputs()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            for (int t = 0; t < 10; t++) w.Step(); // settle / pre-roll before the run
            var rules = new ScriptedRules();
            var runner = new SimRunner(w, rules);
            Assert.Throws<ArgumentException>(() => runner.EnqueueInput(new SimInput(5, 1, 5)));
            runner.EnqueueInput(new SimInput(10, 1, 10));
            runner.EnqueueInput(new SimInput(20, 1, 20));
            runner.EnqueueInput(new SimInput(30, 1, 30));
            runner.RunHeadless(30);
            Assert.Equal(3, rules.Applied.Count);
            Assert.Equal((10, 10), (rules.Applied[0].tick, rules.Applied[0].input.A));
            Assert.Equal((20, 20), (rules.Applied[1].tick, rules.Applied[1].input.A));
            Assert.Equal((30, 30), (rules.Applied[2].tick, rules.Applied[2].input.A));
            Assert.Equal(0, runner.PendingInputCount);
            Assert.Equal(0, runner.DroppedInputCount);
            Assert.Equal(40, runner.EnqueueInputNow(1));
        }

        [Fact]
        public void InputsOfTicksSteppedOutsideTheRunnerAreDroppedAndCountedNotBlocking()
        {
            var rules = new ScriptedRules();
            var runner = NewRunner(rules);
            runner.EnqueueInput(new SimInput(1, 1, 1));
            runner.EnqueueInput(new SimInput(2, 1, 2));
            runner.EnqueueInput(new SimInput(5, 1, 5));
            runner.Step();                 // tick 0
            runner.World.Step();           // tick 1 runs without the runner: its input is lost
            runner.World.Step();           // tick 2 as well
            runner.RunHeadless(5);         // ticks 3..7
            Assert.Single(rules.Applied);
            Assert.Equal((5, 5), (rules.Applied[0].tick, rules.Applied[0].input.A));
            Assert.Equal(2, runner.DroppedInputCount);
            Assert.Equal(0, runner.PendingInputCount);
            Assert.Throws<ArgumentException>(() => runner.EnqueueInput(new SimInput(6, 0)));
        }

        [Fact]
        public void RulesMayQueueInputsForLaterTicksDuringTick()
        {
            var rules = new ScriptedRules();
            var runner = NewRunner(rules);
            rules.OnTick = (r, n) =>
            {
                if (r.Tick < 40) r.EnqueueInput(new SimInput(r.Tick + 1, 9, r.Tick + 1));
                Assert.Throws<ArgumentException>(() => r.EnqueueInput(new SimInput(r.Tick, 9)));
            };
            runner.EnqueueInput(new SimInput(0, 9, 0));
            runner.RunHeadless(50);
            Assert.Equal(41, rules.Applied.Count);
            for (int i = 0; i < 41; i++)
            {
                Assert.Equal(i, rules.Applied[i].tick);
                Assert.Equal(i, rules.Applied[i].input.A);
            }
        }

        [Fact]
        public void TimeLimitNoticeLeavesRoomForFinaleAndHardCapStops()
        {
            var rules = new ScriptedRules();
            var runner = NewRunner(rules);
            int ran = runner.RunHeadless(100000);
            Assert.Equal(SimConstants.MaxDurationTicks, ran);
            Assert.Equal(SimConstants.MaxDurationTicks, runner.Tick);
            Assert.Equal(1, rules.TimeLimitCalls);
            Assert.Equal(SimConstants.MaxDurationTicks - 3 * SimConstants.TickRate, rules.TimeLimitAtTick);
            Assert.True(runner.TimeLimitReached);
            Assert.Equal(rules.TimeLimitAtTick, runner.FinaleTick);
            Assert.True(runner.IsFinished);
            Assert.False(runner.Step());
            Assert.Equal(SimOutcome.TimeLimit, runner.Result.Outcome);
        }

        [Fact]
        public void FinishedRulesStopTheRunUnlessContinuing()
        {
            var rules = new ScriptedRules();
            rules.OnAfter = r =>
            {
                if (r.Tick == 30) rules.PhaseValue = SimPhase.Finale;
                if (r.Tick == 50) rules.PhaseValue = SimPhase.Finished;
            };
            var runner = NewRunner(rules);
            Assert.Equal(50, runner.RunHeadless(1000));
            Assert.Equal(30, runner.FinaleTick);
            Assert.Equal(50, runner.FinishedTick);
            Assert.True(runner.IsFinished);

            var rules2 = new ScriptedRules();
            rules2.OnAfter = r =>
            {
                if (r.Tick == 50) rules2.PhaseValue = SimPhase.Finished;
            };
            var runner2 = NewRunner(rules2, new SimRunnerOptions { ContinueAfterFinish = true, MaxTicks = 200, FinaleReserveTicks = 0 });
            Assert.Equal(200, runner2.RunHeadless(1000));
            Assert.Equal(50, runner2.FinishedTick);
        }

        [Fact]
        public void PhaseGoingBackwardsIsAnError()
        {
            var rules = new ScriptedRules();
            rules.OnAfter = r =>
            {
                if (r.Tick == 2) rules.PhaseValue = SimPhase.Finale;
                if (r.Tick == 3) rules.PhaseValue = SimPhase.Running;
            };
            var runner = NewRunner(rules);
            runner.Step();
            runner.Step();
            Assert.Throws<InvalidOperationException>(() => runner.Step());
        }

        [Theory]
        [InlineData(1f, 60, 60, 60)]
        [InlineData(0.25f, 60, 60, 15)]
        [InlineData(3f, 60, 60, 180)]
        [InlineData(1f, 30, 30, 60)]
        [InlineData(0.5f, 120, 120, 30)]
        [InlineData(2f, 30, 30, 120)]
        public void TimeScaleChangesStepsPerDisplayFrameNotStepSize(float scale, int fps, int frames, int expectedTicks)
        {
            var runner = NewRunner(new ScriptedRules());
            runner.TimeScale = scale;
            int total = 0;
            for (int f = 0; f < frames; f++) total += runner.AdvanceDisplayFrame(fps);
            Assert.Equal(expectedTicks, total);
            Assert.Equal(expectedTicks, runner.Tick);
        }

        [Fact]
        public void TimeScaleIsClampedAndPauseStopsPacing()
        {
            var runner = NewRunner(new ScriptedRules());
            runner.TimeScale = 10f;
            Assert.Equal(SimRunner.MaxTimeScale, runner.TimeScale);
            runner.TimeScale = 0.01f;
            Assert.Equal(SimRunner.MinTimeScale, runner.TimeScale);
            runner.Paused = true;
            Assert.Equal(0, runner.AdvanceDisplayFrame());
            runner.Paused = false;
            runner.TimeScale = 3f;
            Assert.Equal(3, runner.AdvanceDisplayFrame());
            // 3× at 10 Hz wants 18 ticks per frame; the catch-up cap (12) drops the rest.
            Assert.Equal(12, runner.AdvanceDisplayFrame(10));
        }

        [Fact]
        public void PacingDoesNotChangeTheResult()
        {
            ulong Run(float scale, int fps)
            {
                var w = SimTestUtil.HexagonScene(60, 3UL);
                var rules = new ScriptedRules { RecordInputs = false };
                var runner = new SimRunner(w, rules);
                for (int t = 0; t < 300; t += 7) runner.EnqueueInput(new SimInput(t, 1, t));
                runner.TimeScale = scale;
                while (runner.Tick < 300)
                {
                    int before = runner.Tick;
                    runner.AdvanceDisplayFrame(fps);
                    if (runner.Tick > 300) throw new InvalidOperationException("overshoot");
                    if (runner.Tick == before && scale >= 1f) throw new InvalidOperationException("no progress");
                }
                return runner.ComputeStateHash();
            }
            ulong reference = Run(1f, 60);
            Assert.Equal(reference, Run(0.25f, 60));
            Assert.Equal(reference, Run(3f, 60));
            Assert.Equal(reference, Run(1f, 30));
        }

        [Fact]
        public void StateHashIncludesRuleState()
        {
            var rules = new ScriptedRules { RecordInputs = false };
            var runner = NewRunner(rules);
            runner.Step();
            ulong h = runner.ComputeStateHash();
            rules.Counter = 5;
            Assert.NotEqual(h, runner.ComputeStateHash());
        }

        [Fact]
        public void InvalidOptionsAreRejected()
        {
            var w = new PhysicsWorld();
            Assert.Throws<ArgumentException>(() => new SimRunner(w, new ScriptedRules(), new SimRunnerOptions { MaxTicks = SimConstants.MaxDurationTicks + 1 }));
            Assert.Throws<ArgumentException>(() => new SimRunner(w, new ScriptedRules(), new SimRunnerOptions { MaxTicks = 100, FinaleReserveTicks = 100 }));
            Assert.Throws<ArgumentNullException>(() => new SimRunner(null, new ScriptedRules()));
            Assert.Throws<ArgumentNullException>(() => new SimRunner(w, null));
        }

        [Fact]
        public void RingEscapeStyleRulesBreakRingsOnPassageAndFinish()
        {
            // A miniature Ring Escape built only on the public API: 3 counter-rotating rings, a gap each; a ring breaks
            // (collider disabled) once the ball is outside it; the run finishes when all rings are broken.
            var w = new PhysicsWorld(SimTestUtil.NoGravity(maxSpeed: 15f));
            var rings = new int[3];
            var radii = new[] { 1f, 1.6f, 2.2f };
            for (int i = 0; i < 3; i++)
            {
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, i * 2f, i % 2 == 0 ? 1.5f : -1.5f));
                var mat = SimTestUtil.Wall(1f, 0f);
                mat.Tag = i;
                rings[i] = w.AddRing(body, Vec2.Zero, radii[i], 0.06f, new[] { AngularGap.Centered(0f, 0.9f) }, mat);
            }
            int ball = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(4f, 3f), 0.12f));
            var rules = new MiniRingEscape(rings, radii, ball);
            var runner = new SimRunner(w, rules);
            runner.RunHeadless(SimConstants.MaxDurationTicks);
            Assert.Equal(SimOutcome.Completed, runner.Result.Outcome);
            Assert.Equal(3, runner.Result.Detail);
            Assert.True(runner.FinishedTick > runner.FinaleTick);
            for (int i = 0; i < 3; i++) Assert.False(w.IsColliderEnabled(rings[i]));
        }

        sealed class MiniRingEscape : IModeRules
        {
            readonly int[] _rings;
            readonly float[] _radii;
            readonly int _ball;
            int _broken;
            int _finaleUntil = -1;
            int _hits;

            public MiniRingEscape(int[] rings, float[] radii, int ball)
            {
                _rings = rings;
                _radii = radii;
                _ball = ball;
                Result = SimResult.None;
            }

            public SimPhase Phase { get; private set; } = SimPhase.Running;

            public SimResult Result { get; private set; }

            public void Start(SimRunner runner)
            {
            }

            public void Tick(SimRunner runner, ReadOnlySpan<SimInput> inputs)
            {
            }

            public void AfterPhysics(SimRunner runner)
            {
                foreach (var e in runner.World.Events) if (e.Kind == SimEventKind.Hit) _hits++;
                if (Phase == SimPhase.Running)
                {
                    float r = runner.World.GetBall(_ball).Position.Length;
                    while (_broken < _rings.Length && r > _radii[_broken] + 0.1f)
                    {
                        runner.World.SetColliderEnabled(_rings[_broken], false);
                        _broken++;
                    }
                    if (_broken == _rings.Length)
                    {
                        Phase = SimPhase.Finale;
                        _finaleUntil = runner.Tick + 60;
                        Result = new SimResult { Outcome = SimOutcome.Completed, Winner = -1, Detail = _broken, Tick = runner.Tick };
                    }
                }
                else if (Phase == SimPhase.Finale && runner.Tick >= _finaleUntil)
                {
                    Phase = SimPhase.Finished;
                }
            }

            public void OnTimeLimit(SimRunner runner)
            {
                Phase = SimPhase.Finale;
                _finaleUntil = runner.Tick + 60;
                Result = new SimResult { Outcome = SimOutcome.TimeLimit, Winner = -1, Detail = _broken, Tick = runner.Tick };
            }

            public void AppendStateHash(XxHash64 hash)
            {
                hash.AppendInt(_broken);
                hash.AppendInt(_hits);
            }
        }
    }
}
