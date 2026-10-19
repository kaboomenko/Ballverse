using System;
using System.Diagnostics;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>Timing-sensitive tests run alone, not in parallel with the rest of the suite.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class SimPerformanceCollection
    {
        public const string Name = "Sim performance";
    }

    [Collection(SimPerformanceCollection.Name)]
    public class DeterminismPerformanceTests
    {
        readonly ITestOutputHelper _out;

        public DeterminismPerformanceTests(ITestOutputHelper output)
        {
            _out = output;
        }

        static ulong[] HashRun(int balls, ulong seed, int ticks)
        {
            var w = SimTestUtil.HexagonScene(balls, seed);
            var sensorBody = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, -1f));
            w.AddRing(sensorBody, Vec2.Zero, 2f, 0.05f, new[] { AngularGap.Centered(0f, 1f) }, ColliderMaterial.Sensor());
            var hashes = new ulong[ticks];
            for (int t = 0; t < ticks; t++)
            {
                w.Step();
                hashes[t] = w.ComputeStateHash();
            }
            return hashes;
        }

        [Fact]
        public void TwoRunsOfSameSceneProduceIdenticalHashesEveryTick()
        {
            ulong[] a = HashRun(500, 2024UL, 600);
            ulong[] b = HashRun(500, 2024UL, 600);
            Assert.Equal(a, b);
            _out.WriteLine("hash@600 = 0x" + a[599].ToString("X16"));
        }

        [Fact]
        public void DifferentSeedsDiverge()
        {
            ulong[] a = HashRun(100, 1UL, 30);
            ulong[] b = HashRun(100, 2UL, 30);
            Assert.NotEqual(a[29], b[29]);
        }

        [Fact]
        public void HashReflectsStateChanges()
        {
            var w = new PhysicsWorld();
            int b = w.AddBall(BallDef.Create(Vec2.Zero, 0.2f));
            ulong h0 = w.ComputeStateHash();
            Assert.Equal(h0, w.ComputeStateHash());
            w.SetBallVelocity(b, new Vec2(0.001f, 0f));
            Assert.NotEqual(h0, w.ComputeStateHash());
            w.SetBallVelocity(b, Vec2.Zero);
            Assert.Equal(h0, w.ComputeStateHash());
            // Below the 2^-16 quantum the hash is stable (−0 / +0 and float noise do not matter).
            w.SetBallVelocity(b, new Vec2(-0f, 1e-7f));
            Assert.Equal(h0, w.ComputeStateHash());
            w.KillBall(b);
            Assert.NotEqual(h0, w.ComputeStateHash());
        }

        [Fact]
        public void QuantizeForHashRoundsHalfAwayAndSaturates()
        {
            Assert.Equal(65536L, PhysicsWorld.QuantizeForHash(1f));
            Assert.Equal(-1L, PhysicsWorld.QuantizeForHash(-1f / 65536f));
            Assert.Equal(1L, PhysicsWorld.QuantizeForHash(0.5f / 65536f));
            Assert.Equal(long.MaxValue, PhysicsWorld.QuantizeForHash(float.PositiveInfinity));
            Assert.Equal(long.MinValue, PhysicsWorld.QuantizeForHash(float.NaN));
        }

        /// <summary>
        /// Edge values for the hash quantizer: ±0, ±1, the half-unit rounding boundary and its neighbouring floats (half away
        /// from zero), sub-resolution and denormal values, the ±9.2·10¹⁸ saturation boundary and its neighbours, ±MaxValue, ±∞, NaN.
        /// </summary>
        static readonly float[] s_quantizerEdgeValues =
        {
            0f, -0f, 1f, -1f, 0.5f / 65536f, -0.5f / 65536f, MathF.BitDecrement(0.5f / 65536f), MathF.BitIncrement(0.5f / 65536f),
            -MathF.BitDecrement(0.5f / 65536f), -MathF.BitIncrement(0.5f / 65536f), 1.5f / 65536f, -1.5f / 65536f, 2.5f / 65536f,
            123.456f, -98765.4321f, 1e-30f, float.Epsilon, -float.Epsilon,
            9.2e18f / 65536f, -9.2e18f / 65536f, MathF.BitDecrement(9.2e18f / 65536f), MathF.BitIncrement(-9.2e18f / 65536f),
            float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN,
        };

        /// <summary>
        /// The world hash must match the replay HASH record (spec §7.4), whose quantizer is
        /// <c>Ballverse.Core.Replay.StateHashBuilder.AddQuantized</c> — a separate module this track cannot reference or edit.
        /// The digest below was produced by <c>StateHashBuilder.AddQuantized</c> (main @ c7f0649, default 2^16 units) over the same
        /// values; any change to either quantizer breaks one side of this pin. (Sharing one quantizer from Foundation is an
        /// integration step outside this track's files.)
        /// </summary>
        [Fact]
        public void QuantizeForHashMatchesReplayStateHashBuilder()
        {
            var hash = new XxHash64();
            foreach (float v in s_quantizerEdgeValues) hash.AppendULong((ulong)PhysicsWorld.QuantizeForHash(v));
            Assert.Equal(0xD700C4F90D47426AUL, hash.Digest());
            Assert.Equal(1L, PhysicsWorld.QuantizeForHash(MathF.BitIncrement(0.5f / 65536f)));
            Assert.Equal(0L, PhysicsWorld.QuantizeForHash(MathF.BitDecrement(0.5f / 65536f)));
            Assert.Equal(-1L, PhysicsWorld.QuantizeForHash(-0.5f / 65536f));
            Assert.Equal(3L, PhysicsWorld.QuantizeForHash(2.5f / 65536f));
            Assert.Equal(0L, PhysicsWorld.QuantizeForHash(-0f));
            Assert.Equal(long.MaxValue, PhysicsWorld.QuantizeForHash(9.2e18f / 65536f));
            Assert.Equal(long.MinValue + 1, PhysicsWorld.QuantizeForHash(float.NegativeInfinity));
        }

        [Fact]
        public void PerformanceSmoke500BallsInRotatingHexagon()
        {
            var w = SimTestUtil.HexagonScene(500, 99UL);
            for (int t = 0; t < 60; t++) w.Step(); // warm-up (JIT, buffer growth)
            const int ticks = 600;
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < ticks; t++) w.Step();
            sw.Stop();
            double avgMs = sw.Elapsed.TotalMilliseconds / ticks;
            _out.WriteLine("500 balls, rotating hexagon, 8 substeps: avg tick " + avgMs.ToString("F3") + " ms over " + ticks
                + " ticks (" + (1000.0 / avgMs).ToString("F0") + " ticks/s, " + (1000.0 / avgMs / 60.0).ToString("F1") + "x real time)");
            Assert.Equal(500, w.AliveBallCount);
            // Lenient bounds for shared CI machines (a desktop Release build runs this scene at ~2.5 ms per tick).
#if DEBUG
            const double limitMs = 120.0;
#else
            const double limitMs = 25.0;
#endif
            Assert.True(avgMs < limitMs, "average tick " + avgMs + " ms (limit " + limitMs + ")");
        }

        [Fact]
        public void SteadyStateStepsDoNotAllocate()
        {
            var w = SimTestUtil.HexagonScene(500, 5UL);
            var sensorBody = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddCircle(sensorBody, Vec2.Zero, 1.5f, ColliderMaterial.Sensor());
            w.AddSegment(sensorBody, new Vec2(-4f, 0f), new Vec2(4f, 0f), 0f, ColliderMaterial.Sensor());
            var ring = w.AddBody(BodyDef.Kinematic(new Vec2(0f, 0f), 0f, 2f));
            w.AddRing(ring, Vec2.Zero, 1f, 0.05f, new[] { AngularGap.Centered(0f, 0.6f) }, SimTestUtil.Wall(0.5f, 0.2f));
            var platformBody = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -2f), Scale = 1f, SpinVelocity = 2f });
            w.AddSpinPlatform(platformBody, Vec2.Zero, 1.5f, 0.1f, new[] { AngularGap.Centered(0f, 1f) }, SimTestUtil.Wall());
            w.ContactHook = new CountingHook();
            var snapshot = new SimSnapshot();
            ulong sink = 0;
            // Warm-up: every pooled buffer reaches its working size.
            for (int t = 0; t < 240; t++)
            {
                w.Step();
                sink ^= w.ComputeStateHash();
                w.WriteSnapshot(snapshot);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 600; t++)
            {
                w.Step();
                sink ^= w.ComputeStateHash();
                w.WriteSnapshot(snapshot);
                foreach (var e in w.Events) sink += (ulong)e.A;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            _out.WriteLine("allocated over 600 ticks: " + allocated + " bytes (sink " + sink + ")");
            Assert.Equal(0L, allocated);
        }

        [Fact]
        public void HeadlessRunnerDoesNotAllocateInSteadyState()
        {
            var w = SimTestUtil.HexagonScene(300, 8UL);
            var rules = new ScriptedRules { RecordInputs = false };
            var runner = new SimRunner(w, rules);
            var observer = new HashObserver();
            for (int t = 0; t < 120; t++)
            {
                runner.EnqueueInput(new SimInput(t, 1, t));
            }
            runner.RunHeadless(120, observer);
            for (int t = 120; t < 600; t++) runner.EnqueueInput(new SimInput(t, 1, t));
            long before = GC.GetAllocatedBytesForCurrentThread();
            runner.RunHeadless(480, observer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
            Assert.Equal(600, runner.Tick);
            Assert.Equal(600, observer.Ticks);
        }

        sealed class CountingHook : ISimContactHook
        {
            public long Calls;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                Calls++;
                return true;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact)
            {
                Calls++;
                return true;
            }
        }

        sealed class HashObserver : ISimObserver
        {
            public int Ticks;
            public ulong Last;

            public void OnTick(SimRunner runner)
            {
                Ticks++;
                Last = runner.ComputeStateHash();
            }
        }
    }
}
