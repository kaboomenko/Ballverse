using System;
using System.Diagnostics;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Step cost of the release-1 body-budget scenes (spec §2.9 budgets, §6.5 CI target: tier-A scene at 8 substeps ≤ 0.5 ms
    /// per tick on an x64 runner), as a lenient wall-clock backstop. Measured times and the ratio to the 0.5 ms target are
    /// written to the test output; the absolute target is tracked, not asserted.
    /// <para>
    /// Regression detection is split. The 2× guard is machine-independent: <see cref="BudgetSceneWorkTests"/> counts the
    /// solver's work on these very scenes and ticks (substeps, bounds tests, narrow-phase candidates, sensor tests, ball
    /// pairs) and fails deterministically on any runner when a count grows past 1.25× its reference. These timing tests only
    /// catch a gross slowdown that the counters cannot see (a much costlier narrow phase per candidate), because wall-clock
    /// ratios on shared machines do not hold a 2× threshold: on the development container the identical, deterministic
    /// work of the Ring Escape and Helix Drop scenes measured 2× faster in some full-suite runs than in others (0.08 vs
    /// 0.12–0.14 ms/tick, 0.006 vs 0.013 ms/tick — code generation, not load), memory-heavy neighbour load slowed the
    /// Geometry Bounce scenes up to 1.8× while the ALU-bound calibration loop barely moved, and on a GitHub runner the
    /// pre-broadphase Ring Escape test measured 1.05–1.07× calibration (1.88–1.94 ms/tick) where the same test here measures
    /// 0.13–0.15× — a ~7× difference between the machines on that scene whose cause is not known (not the warm-up state: a
    /// probe of that code over a full 180 s run here never exceeds 0.32×).
    /// </para>
    /// <para>
    /// Measurement: the scene is built afresh in several rounds, warmed for a fixed number of ticks and timed over the same
    /// 100-tick window in 10-tick batches. Every batch is paired with a slice of the calibration loop run right before it,
    /// so both are sampled over the same seconds; the ratio is the best batch over the best slice. An attempt over the
    /// limit is repeated (up to three, best counts).
    /// </para>
    /// <para>
    /// Limits: the Geometry Bounce limits (2.5 / 2.0 / 1.0 / 0.4 / 3.0) are the ones the same scenes passed on the GitHub
    /// runner in every core-tests.yml run that reached the tests (the failing runs failed only the Ring Escape test; the
    /// step has only become cheaper since). Ring Escape 0.8 (the old 1.0 failed there at 1.05–1.07× in three of the runs,
    /// before the broadphase cut the scene's cost 2–5×) and the Helix Drop limit (0.1, unchanged, but the scene is now
    /// measured with the ball inside the tower) have not run on a GitHub runner in this form yet. Each scene quotes its lowest and highest ratio on the development container (x64,
    /// .NET 8 Release, a shared 4-core machine) over all runs of the earlier rounds and QA's (perf class alone at load 2–18,
    /// nine full-suite runs at load 3–20) and of this round (perf class alone six times and the full suite
    /// — <c>dotnet test core-tests/Ballverse.Core.sln -c Release</c>, the CI configuration — four times, all at load
    /// 21–24), and its headroom (limit ÷ highest ratio, ≥ 4.2× for every scene) and detection threshold (limit ÷ lowest
    /// ratio: the slowdown that fails even on the fastest run seen, 8.5–32×).
    /// </para>
    /// <para>
    /// Tier A at the default 8 substeps measures 0.47–0.78 ms/tick on the development container (≈ 1.0–1.6× the target),
    /// ~60 % of it in ball–ball contacts (sequential impulses whose order is part of the determinism contract); §6.5's
    /// fallback (fewer substeps inside the 4–16 envelope covered by the tunnelling property tests) stays available to the
    /// Geometry Bounce tier-A budget: 6 substeps (timed here too) ≈ 0.38–0.60 ms/tick.
    /// </para>
    /// </summary>
    [Collection(SimPerformanceCollection.Name)]
    public class BudgetScenePerformanceTests
    {
        readonly ITestOutputHelper _out;
        static float s_sink;

        public BudgetScenePerformanceTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>
        /// Calibration: 256 K iterations of a dependent float multiply-add chain with a square root every 8th step (a
        /// solver-like mix), about 2 ms on the development container. <see cref="CalibrationChunkMs"/> runs a slice of it.
        /// </summary>
        const int CalibrationReps = 256;
        /// <summary>Calibration slice run next to every timed batch (a quarter of the loop, ≈ 0.5 ms).</summary>
        const int ChunkReps = 64;

        static readonly float[] s_xs = CalibrationInput(0.001f, 0f);
        static readonly float[] s_ys = CalibrationInput(-0.0005f, 1f);

        static float[] CalibrationInput(float step, float start)
        {
            var a = new float[1024];
            for (int i = 0; i < a.Length; i++) a[i] = start + step * i;
            return a;
        }

        /// <summary>Time (ms) of <paramref name="reps"/> × 1024 iterations of the calibration chain.</summary>
        static double CalibrationChunkMs(int reps)
        {
            float[] xs = s_xs, ys = s_ys;
            var sw = Stopwatch.StartNew();
            float acc = 0f;
            for (int rep = 0; rep < reps; rep++)
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    float x = xs[i] + acc * 1e-9f, y = ys[i];
                    float d = x * x + y * y;
                    acc += d * 0.5f - x * y;
                    if ((i & 7) == 0) acc += MathF.Sqrt(d);
                }
            }
            sw.Stop();
            s_sink += acc;
            return sw.Elapsed.TotalMilliseconds;
        }

        static bool s_warm;

        /// <summary>
        /// Once per process: steps a scene that uses every collider kind, sensors and ball pairs for 2 s, so the tiered JIT has
        /// promoted the solver's hot paths before anything is timed (measured: without it, a scene whose code paths were not
        /// used before ran ~5× slower for its first second — tier-0 code, with promotion delayed while other code is jitted),
        /// and runs the calibration loop a few times for the same reason.
        /// </summary>
        static void EnsureJitWarm()
        {
            if (s_warm) return;
            for (int k = 0; k < 8; k++) CalibrationChunkMs(CalibrationReps);
            var w = GeometryBounce(80);
            int ringBody = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 1f));
            w.AddRing(ringBody, Vec2.Zero, 1.5f, 0.04f, new[] { AngularGap.Centered(0f, 0.6f) }, SimTestUtil.Wall(0.9f, 0.1f));
            w.AddRing(ringBody, Vec2.Zero, 1.0f, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor());
            w.AddCircle(ringBody, new Vec2(0f, 2.5f), 0.3f, SimTestUtil.Wall(0.9f, 0.1f));
            w.AddSegment(ringBody, new Vec2(-1f, -2.5f), new Vec2(1f, -2.5f), 0.05f, SimTestUtil.Wall(0.9f, 0.1f));
            int platform = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -2f), Scale = 1f, SpinVelocity = 2f });
            w.AddSpinPlatform(platform, Vec2.Zero, 1.2f, 0.1f, new[] { AngularGap.Centered(0f, 1f) }, SimTestUtil.Wall(0.6f, 0.3f));
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 2000) w.Step();
            s_warm = true;
        }

        /// <summary>One timing: the best batch's ms/tick and the best adjacent calibration slice, scaled to the full loop (ms).</summary>
        readonly struct Timing
        {
            public readonly double TickMs, CalibrationMs;

            public Timing(double tickMs, double calibrationMs)
            {
                TickMs = tickMs;
                CalibrationMs = calibrationMs;
            }

            public double Ratio => TickMs / CalibrationMs;
        }

        /// <summary>
        /// Times <paramref name="build"/>'s scene after the process-wide JIT warm-up: <paramref name="rounds"/> fresh worlds,
        /// each warmed for <paramref name="warmTicks"/> ticks and then timed over <paramref name="batches"/> batches of 10
        /// ticks — the same ticks in every round (the simulation is deterministic). Every batch is paired with a calibration
        /// slice run right before it, so the scene and the calibration loop are sampled over the same seconds and see the same
        /// machine load; the result is the best batch and the best slice (preemption only ever adds time).
        /// </summary>
        static Timing TimeTicks(Func<PhysicsWorld> build, Action<PhysicsWorld, int> beforeStep = null, int rounds = 4, int batches = 10, int warmTicks = 120)
        {
            const int ticksPerBatch = 10;
            EnsureJitWarm();
            double best = double.MaxValue, bestChunk = double.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                PhysicsWorld w = build();
                int tick = 0;
                for (; tick < warmTicks; tick++)
                {
                    beforeStep?.Invoke(w, tick);
                    w.Step();
                }
                for (int b = 0; b < batches; b++)
                {
                    bestChunk = Math.Min(bestChunk, CalibrationChunkMs(ChunkReps));
                    var sw = Stopwatch.StartNew();
                    for (int t = 0; t < ticksPerBatch; t++, tick++)
                    {
                        beforeStep?.Invoke(w, tick);
                        w.Step();
                    }
                    sw.Stop();
                    best = Math.Min(best, sw.Elapsed.TotalMilliseconds / ticksPerBatch);
                }
            }
            return new Timing(best, bestChunk * ((double)CalibrationReps / ChunkReps));
        }

        /// <summary>Measurement attempts per scene; the best ratio counts (a passing attempt ends the measurement).</summary>
        const int Attempts = 3;

        /// <summary>
        /// Times the scene (<paramref name="measure"/>) and asserts its ratio to the paired calibration. An attempt over the
        /// limit is repeated (up to <see cref="Attempts"/>, best ratio counts).
        /// </summary>
        double Report(string scene, Func<Timing> measure, double ratioLimit)
        {
            Timing best = default;
            double bestRatio = double.MaxValue;
            int attempts = 0;
            while (attempts < Attempts && bestRatio >= ratioLimit)
            {
                attempts++;
                Timing t = measure();
                if (t.Ratio < bestRatio)
                {
                    bestRatio = t.Ratio;
                    best = t;
                }
            }
            _out.WriteLine(scene + ": " + best.TickMs.ToString("F3") + " ms/tick (" + (best.TickMs / 0.5).ToString("F2") + "× the 0.5 ms target), "
                + bestRatio.ToString("F4") + "× calibration (" + best.CalibrationMs.ToString("F3") + " ms), limit " + ratioLimit + "×, attempts " + attempts);
            Assert.True(bestRatio < ratioLimit, scene + ": " + best.TickMs + " ms/tick is " + bestRatio + "× the calibration loop (" + best.CalibrationMs
                + " ms; limit " + ratioLimit + "×, best of " + attempts + ")");
            return bestRatio;
        }

#if DEBUG
        // Unoptimised Core code is 10–13× slower relative to the (also unoptimised) calibration loop.
        const double Slack = 20.0;
#else
        const double Slack = 1.0;
#endif

        /// <summary>Geometry Bounce (M02): balls of 0.34 m in a rotating hexagon (R 3.6 m, wall 0.12 m), 1 g, 8 substeps.</summary>
        internal static PhysicsWorld GeometryBounce(int balls, float diameterSpread = 0f, float bigRadius = 0f, float radius = 0.17f, int substeps = SimSettings.DefaultSubsteps)
        {
            var settings = SimSettings.Default;
            settings.Substeps = substeps;
            var w = new PhysicsWorld(settings);
            SimTestUtil.AddPolygonArena(w, 6, 3.6f, 0.12f, 45f * SimMath.DegToRad, 0.9f, 0.2f);
            w.ReserveBalls(balls + 1);
            var rng = Rng.FromSeed(11UL, 1);
            float inr = ShapeBuilder.Inradius(6, 3.6f) - 0.12f - radius * 1.5f - 0.05f;
            if (bigRadius > 0f) w.AddBall(SimTestUtil.Ball(Vec2.Zero, Vec2.Zero, bigRadius, 0.9f, 0.2f));
            for (int i = 0; i < balls; i++)
            {
                Vec2 p;
                do
                {
                    p = new Vec2(rng.NextFloat(-inr, inr), rng.NextFloat(-inr, inr));
                } while (p.Length > inr || (bigRadius > 0f && p.Length < bigRadius + radius));
                float r = radius * (1f + diameterSpread * rng.NextFloat(-0.5f, 0.5f));
                w.AddBall(SimTestUtil.Ball(p, new Vec2(rng.NextFloat(-5f, 5f), rng.NextFloat(-5f, 5f)), r, 0.9f, 0.2f));
            }
            return w;
        }

        /// <summary>
        /// Ratio lowest–highest / limit / headroom / detection threshold: tier A 8 substeps 0.248–0.492 / 2.5 / 5.1× / 10×;
        /// tier A 6 substeps 0.193–0.366 / 2.0 / 5.5× / 10×; tier B 0.117–0.236 / 1.0 / 4.2× / 8.5×; tier C 0.047–0.094 /
        /// 0.4 / 4.3× / 8.5×.
        /// </summary>
        [Theory]
        [InlineData("A", 200, 2.5, 8)]
        [InlineData("A", 200, 2.0, 6)]
        [InlineData("B", 120, 1.0, 8)]
        [InlineData("C", 60, 0.4, 8)]
        public void GeometryBounceBudgetScenes(string tier, int balls, double ratioLimit, int substeps)
        {
            Report("M02 tier " + tier + " (" + balls + " balls Ø0.34 m, rotating hexagon, " + substeps + " substeps)",
                () => TimeTicks(() => GeometryBounce(balls, substeps: substeps)), ratioLimit * Slack);
        }

        /// <summary>Ratio 0.355–0.583; limit 3.0, headroom 5.1×, detection threshold 8.5×.</summary>
        [Fact]
        public void GeometryBounceWithFullSizeSpreadStaysCheap()
        {
            Report("M02 tier A, sizeSpread 100%", () => TimeTicks(() => GeometryBounce(200, diameterSpread: 1f)), 3.0 * Slack);
        }

        /// <summary>
        /// One grown ball among small ones (M02 «Рост», M01 growOnHit) must not inflate the broadphase: before the grid used the
        /// median radius, one r = 1.2 m ball among 120 r = 0.12 m balls made every tick ~7× slower.
        /// </summary>
        [Fact]
        public void OneGrownBallDoesNotSlowTheBroadphaseDown()
        {
            double uniform = double.MaxValue, mixed = double.MaxValue;
            for (int rep = 0; rep < 2; rep++)
            {
                uniform = Math.Min(uniform, TimeTicks(() => GeometryBounce(120, radius: 0.12f)).TickMs);
                mixed = Math.Min(mixed, TimeTicks(() => GeometryBounce(120, bigRadius: 1.2f, radius: 0.12f)).TickMs);
            }
            double ratio = mixed / uniform;
            _out.WriteLine("120 balls r 0.12: " + uniform.ToString("F3") + " ms/tick; + one r 1.2 ball: " + mixed.ToString("F3") + " ms/tick ("
                + ratio.ToString("F2") + "×)");
            Assert.True(ratio < 2.5, "one grown ball made the tick " + ratio + "× slower");
        }

        /// <summary>
        /// Ring Escape (M01) test scene: <paramref name="rings"/> rotating rings 0.2 m apart (radius 0.8 m + 0.2 m·k), each with a
        /// 0.6 rad gap on its own body and a passage sensor ring 0.08 m outside it, 20 balls (r 0.05 m) launched at 8 m/s from
        /// the centre under 1 g. No floor: escaped balls fall away.
        /// </summary>
        internal static PhysicsWorld RingEscape(int rings, int balls = 20)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(3UL);
            for (int k = 0; k < rings; k++)
            {
                float radius = 0.8f + 0.2f * k;
                float omega = (k % 2 == 0 ? 1f : -1f) * rng.NextFloat(0.5f, 2f);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, rng.NextFloat(0f, SimMath.TwoPi), omega));
                w.AddRing(body, Vec2.Zero, radius, 0.04f, new[] { AngularGap.Centered(0f, 0.6f) }, SimTestUtil.Wall(0.9f, 0.1f));
                w.AddRing(body, Vec2.Zero, radius + 0.08f, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor());
            }
            for (int i = 0; i < balls; i++)
            {
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-0.3f, 0.3f), rng.NextFloat(-0.3f, 0.3f)),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 8f, 0.05f, 0.9f, 0.1f);
                w.AddBall(d);
            }
            return w;
        }

        /// <summary>
        /// Ring Escape (M01) at its budget: 100 rotating rings with a gap each (own bodies), a passage sensor per ring, 20 balls.
        /// Ratio 0.037–0.080 (0.071–0.136 ms/tick; bimodal across full-suite runs, see the class notes); limit 0.8: headroom
        /// 10×, detection threshold 21×. The 2× guard of this scene is <see cref="BudgetSceneWorkTests"/> (QA's check, the
        /// scene at 16 instead of 8 substeps, fails it on any machine). Eight rounds (the scene is cheap) steady the best batch.
        /// </summary>
        [Fact]
        public void RingEscapeHundredRingsTwentyBalls()
        {
            Report("M01 (100 rings + 100 sensors, 20 balls)", () => TimeTicks(() => RingEscape(100), rounds: 8), 0.8 * Slack);
        }

        /// <summary>
        /// Machine-independent guard of the collider broadphase: the same 100-ring scene with the radial index, the sensor
        /// pre-check and the sparse ball grid must run at least 1.3× faster than with the reference exhaustive paths, timed
        /// back to back in this process (measured 1.63–4.31× at this early state, balls still near the centre; 3× over a full
        /// 180 s run, see the headless test). The deterministic form of this guard is
        /// <see cref="BudgetSceneWorkTests.BroadphaseCullsBoundsTestsAndKeepsTheNarrowPhaseWork"/>.
        /// </summary>
        [Fact]
        public void BroadphaseSpeedsUpTheHundredRingScene()
        {
            double fast = double.MaxValue, slow = double.MaxValue;
            for (int rep = 0; rep < 2 || (rep < 2 * Attempts && slow / fast <= 1.3); rep++)
            {
                fast = Math.Min(fast, TimeTicks(() => RingEscape(100)).TickMs);
                slow = Math.Min(slow, TimeTicks(() =>
                {
                    var w = RingEscape(100);
                    w.ExhaustiveBroadphase = true;
                    return w;
                }).TickMs);
            }
            double speedup = slow / fast;
            _out.WriteLine("M01 100 rings: broadphase " + fast.ToString("F3") + " ms/tick, exhaustive " + slow.ToString("F3") + " ms/tick (" + speedup.ToString("F2") + "×)");
            Assert.True(speedup > 1.3, "broadphase speedup only " + speedup + "×");
        }

        /// <summary>
        /// Helix Drop (M05): one ball falling through a tower of 30 spinning platforms with gaps, swiped tower spin. Ratio
        /// 0.0031–0.0080 (0.006–0.015 ms/tick); limit 0.1: headroom 12×, detection threshold 32×. Eight rounds (the scene is
        /// cheap). (Before this track the scene was measured after a 0.2 s warm-up that had run the ball out of the tower.)
        /// </summary>
        [Fact]
        public void HelixDropThirtyPlatforms()
        {
            Report("M05 (30 spin platforms, 1 ball)", () => TimeTicks(HelixDrop, HelixDropInput, rounds: 8), 0.1 * Slack);
        }

        /// <summary>
        /// Helix Drop (M05) test scene: a tower of 30 spin platforms (bodies 0–29, 1.2 m apart, radius 1.5 m, a 0.9 rad gap each)
        /// and one ball (r 0.15 m) dropped onto the top; drive it with <see cref="HelixDropInput"/>.
        /// </summary>
        internal static PhysicsWorld HelixDrop()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(5UL);
            for (int k = 0; k < HelixPlatforms; k++)
            {
                int body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -1.2f * k), Scale = 1f, Spin = rng.NextFloat(0f, SimMath.TwoPi) });
                float gap = rng.NextFloat(0f, SimMath.TwoPi);
                w.AddSpinPlatform(body, Vec2.Zero, 1.5f, 0.15f, new[] { AngularGap.Centered(gap, 0.9f) }, SimTestUtil.Wall(0.6f, 0.3f));
            }
            w.AddBall(SimTestUtil.Ball(new Vec2(0.3f, 1f), Vec2.Zero, 0.15f, 0.6f, 0.3f));
            return w;
        }

        const int HelixPlatforms = 30;

        /// <summary>Helix Drop swipe input: the whole tower spins at ±3 rad/s, reversing every 30 ticks.</summary>
        internal static void HelixDropInput(PhysicsWorld world, int tick)
        {
            float spin = (tick / 30) % 2 == 0 ? 3f : -3f;
            for (int k = 0; k < HelixPlatforms; k++) world.SetBodySpinVelocity(k, spin);
        }

        /// <summary>
        /// Headless generation cost (spec §2.4: candidates run without rendering up to 180 s of game time = 10 800 ticks): one
        /// full 180 s run of the Ring Escape test scene at the 100-ring maximum and one at the default 12 rings, wall time
        /// reported. Only a lenient bound is asserted (a run must stay far below a minute on any runner); the numbers feed the
        /// generation budget discussion (tier A: up to 24 candidates in ≤ 1.5 s).
        /// </summary>
        [Theory]
        [InlineData(100)]
        [InlineData(12)]
        public void HeadlessRingEscapeRunOf180Seconds(int rings)
        {
            EnsureJitWarm();
            var w = RingEscape(rings);
            const int ticks = 180 * SimConstants.TickRate;
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < ticks; t++) w.Step();
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            _out.WriteLine("M01 headless 180 s, " + rings + " rings, 20 balls: " + (ms / 1000.0).ToString("F2") + " s wall (" + (ms / ticks).ToString("F3")
                + " ms/tick, " + (ticks * TickDurationMs / ms).ToString("F0") + "× real time), " + w.AliveBallCount + " balls alive");
            Assert.True(ms < 20000.0 * Slack, "180 s headless run took " + ms + " ms");
        }

        const double TickDurationMs = 1000.0 / SimConstants.TickRate;
    }
}
