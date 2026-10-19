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
    /// per tick on an x64 runner). Absolute times depend on the machine, so each scene is timed relative to a fixed
    /// float/sqrt calibration loop measured in the same process (best of several runs for both), and the bound is a ratio.
    /// <para>
    /// Measurement: every scene is built afresh in several rounds, warmed for a fixed number of ticks (not a wall time — the
    /// old 0.2 s warm-up measured a later, costlier state on faster machines) and timed over the same 100-tick window in
    /// 10-tick batches; the result is the best batch over all rounds, i.e. the best of several measurements of identical work,
    /// which keeps a busy machine's preemption out of it.
    /// </para>
    /// <para>
    /// Limits: from 15 full runs of this class on the development container (x64, .NET 8 Release, a shared 4-core machine;
    /// four runs at load average 8–18, three by QA, eight at load 4–8, two of them during bursts of other work that slowed the
    /// Geometry Bounce scenes 1.45–1.65× while the latency-bound calibration loop barely moved). Each scene quotes its lowest and highest
    /// ratio, its detection threshold (limit ÷ lowest ratio: the smallest slowdown that fails even on the luckiest run measured)
    /// and its margin (limit ÷ highest ratio, quiet runs / the loaded burst). Every detection threshold is 1.75–1.95×, so a 2×
    /// regression of the step fails; QA's check (M01 at 16 instead of 8 substeps: 2.0–2.1× the cost) now fails every attempt.
    /// The 1.75–1.95× band leaves the calibration loop's own run-to-run spread (1.9–2.2 ms, ±8 %) on the safe side. The
    /// calibration loop is re-measured around every scene and an attempt over the limit is repeated up to five times (best
    /// counts), so a burst of load on the runner does not fail a test while a real regression fails every attempt. GitHub
    /// runner ratios could not be fetched for these limits (CI artifact download refused); a runner whose memory system is
    /// relatively slower than its ALU than this container's would eat into the margins. Before the collider broadphase the Ring Escape 100-ring scene
    /// measured 1.07× calibration on a GitHub runner (1.94 ms/tick) against a 1.0× limit — the dense ball grid grew with the
    /// balls' spread, so the cost depended on how far the time-based warm-up had run. Measured times and the ratio to the
    /// 0.5 ms target are written to the test output; the absolute target is tracked, not asserted.
    /// </para>
    /// <para>
    /// Tier A at the default 8 substeps measures 0.48–0.62 ms/tick on the development container (≈ 1.0–1.2× the target),
    /// ~60 % of it in ball–ball contacts (sequential impulses whose order is part of the determinism contract); §6.5's
    /// fallback (fewer substeps inside the 4–16 envelope covered by the tunnelling property tests) stays available to the
    /// Geometry Bounce tier-A budget: 6 substeps (timed here too) ≈ 0.40–0.46 ms/tick.
    /// </para>
    /// </summary>
    [Collection(SimPerformanceCollection.Name)]
    public class BudgetScenePerformanceTests
    {
        readonly ITestOutputHelper _out;
        static double s_calibrationMs = -1;
        static float s_sink;

        public BudgetScenePerformanceTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>
        /// Calibration: 256 K iterations of a dependent float multiply-add chain with a square root every 8th step (a
        /// solver-like mix), about 1.7 ms on the development container.
        /// </summary>
        static double CalibrationMs()
        {
            if (s_calibrationMs > 0) return s_calibrationMs;
            return MeasureCalibrationMs();
        }

        /// <summary>Measures the calibration loop now (best of 7 runs); the process-wide value keeps the best seen.</summary>
        static double MeasureCalibrationMs()
        {
            var xs = new float[1024];
            var ys = new float[1024];
            for (int i = 0; i < xs.Length; i++)
            {
                xs[i] = 0.001f * i;
                ys[i] = 1f - 0.0005f * i;
            }
            double best = double.MaxValue;
            for (int run = 0; run < 7; run++)
            {
                var sw = Stopwatch.StartNew();
                float acc = 0f;
                for (int rep = 0; rep < 256; rep++)
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
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            if (s_calibrationMs < 0 || best < s_calibrationMs) s_calibrationMs = best;
            return best;
        }

        static bool s_warm;

        /// <summary>
        /// Once per process: steps a scene that uses every collider kind, sensors and ball pairs for 2 s, so the tiered JIT has
        /// promoted the solver's hot paths before anything is timed. (Measured: without it, a scene whose code paths were not
        /// used before ran ~5× slower for its first second — tier-0 code, with promotion delayed while other code is jitted.)
        /// </summary>
        static void EnsureJitWarm()
        {
            if (s_warm) return;
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

        /// <summary>
        /// Best average tick time (ms) of <paramref name="build"/>'s scene: <paramref name="rounds"/> fresh worlds, each warmed
        /// for <paramref name="warmTicks"/> ticks and then timed over <paramref name="batches"/> batches of 10 ticks — the same
        /// ticks in every round (the simulation is deterministic) — after the process-wide JIT warm-up.
        /// </summary>
        static double TimeTicks(Func<PhysicsWorld> build, Action<PhysicsWorld, int> beforeStep = null, int rounds = 4, int batches = 10, int warmTicks = 120)
        {
            const int ticksPerBatch = 10;
            EnsureJitWarm();
            double best = double.MaxValue;
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
            return best;
        }

        /// <summary>Measurement attempts per scene; the best ratio counts (a passing attempt ends the measurement).</summary>
        const int Attempts = 5;

        /// <summary>
        /// Times the scene (<paramref name="measure"/>, ms/tick) with the calibration loop measured right before and after
        /// it, so both see the same machine load, and asserts the ratio. An attempt over the limit is repeated (up to
        /// <see cref="Attempts"/>, best ratio counts): a busy machine passes on a quieter attempt, a real regression fails
        /// all of them. (On the shared development container a full-suite run beside other jobs, load average ~20 on 4
        /// cores, once pushed the memory-heavy Geometry Bounce scenes 1.5–2× above their quiet ratios while the ALU-bound
        /// calibration loop barely moved.)
        /// </summary>
        double Report(string scene, Func<double> measure, double ratioLimit)
        {
            double bestRatio = double.MaxValue, bestMs = 0, bestCalib = 0;
            int attempts = 0;
            while (attempts < Attempts && bestRatio >= ratioLimit)
            {
                attempts++;
                double calib = MeasureCalibrationMs();
                double ms = measure();
                calib = Math.Min(calib, MeasureCalibrationMs());
                double ratio = ms / calib;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestMs = ms;
                    bestCalib = calib;
                }
            }
            _out.WriteLine(scene + ": " + bestMs.ToString("F3") + " ms/tick (" + (bestMs / 0.5).ToString("F2") + "× the 0.5 ms target), "
                + bestRatio.ToString("F4") + "× calibration (" + bestCalib.ToString("F3") + " ms), limit " + ratioLimit + "×, attempts " + attempts);
            Assert.True(bestRatio < ratioLimit, scene + ": " + bestMs + " ms/tick is " + bestRatio + "× the calibration loop (limit " + ratioLimit + "×, best of " + attempts + ")");
            return bestRatio;
        }

#if DEBUG
        // Unoptimised Core code is 10–13× slower relative to the (also unoptimised) calibration loop.
        const double Slack = 20.0;
#else
        const double Slack = 1.0;
#endif

        /// <summary>Geometry Bounce (M02): balls of 0.34 m in a rotating hexagon (R 3.6 m, wall 0.12 m), 1 g, 8 substeps.</summary>
        static PhysicsWorld GeometryBounce(int balls, float diameterSpread = 0f, float bigRadius = 0f, float radius = 0.17f, int substeps = SimSettings.DefaultSubsteps)
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
        /// Ratio lowest – highest quiet (highest in the loaded burst) / limit / detection threshold / margin quiet (burst):
        /// tier A 8 substeps 0.233–0.370 (0.382) / 0.43 / 1.85× / 1.16× (1.13×); tier A 6 substeps 0.189–0.276 (0.298) / 0.36 /
        /// 1.90× / 1.30× (1.21×); tier B 0.117–0.158 (0.167) / 0.21 / 1.79× / 1.33× (1.26×); tier C 0.047–0.065 (0.087) /
        /// 0.092 / 1.95× / 1.42× (1.06×).
        /// </summary>
        [Theory]
        [InlineData("A", 200, 0.43, 8)]
        [InlineData("A", 200, 0.36, 6)]
        [InlineData("B", 120, 0.21, 8)]
        [InlineData("C", 60, 0.092, 8)]
        public void GeometryBounceBudgetScenes(string tier, int balls, double ratioLimit, int substeps)
        {
            Report("M02 tier " + tier + " (" + balls + " balls Ø0.34 m, rotating hexagon, " + substeps + " substeps)",
                () => TimeTicks(() => GeometryBounce(balls, substeps: substeps)), ratioLimit * Slack);
        }

        /// <summary>Ratio 0.355–0.459 (0.535 in the loaded burst); limit 0.65, detection threshold 1.83×, margin 1.42× (1.21×).</summary>
        [Fact]
        public void GeometryBounceWithFullSizeSpreadStaysCheap()
        {
            Report("M02 tier A, sizeSpread 100%", () => TimeTicks(() => GeometryBounce(200, diameterSpread: 1f)), 0.65 * Slack);
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
                uniform = Math.Min(uniform, TimeTicks(() => GeometryBounce(120, radius: 0.12f)));
                mixed = Math.Min(mixed, TimeTicks(() => GeometryBounce(120, bigRadius: 1.2f, radius: 0.12f)));
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
        /// Ratio 0.057–0.066 at load 4–8 (0.112–0.136 ms/tick), up to 0.080 at load 8–18; limit 0.105: detection threshold 1.85×,
        /// margin 1.60× (1.31× at load 8–18). QA (round 1): the old 0.13 limit passed the scene at 16 substeps (2.0–2.1× the
        /// cost, ratios 0.107–0.121) in 4 of 4 runs. Eight rounds (the scene is cheap) steady the best batch. Before the
        /// broadphase: 0.14× here at this state, and 1.07× on a GitHub runner with the old time-based warm-up (a later, costlier
        /// state).
        /// </summary>
        [Fact]
        public void RingEscapeHundredRingsTwentyBalls()
        {
            Report("M01 (100 rings + 100 sensors, 20 balls)", () => TimeTicks(() => RingEscape(100), rounds: 8), 0.105 * Slack);
        }

        /// <summary>
        /// Machine-independent guard of the collider broadphase: the same 100-ring scene with the radial index, the sensor
        /// pre-check and the sparse ball grid must run at least 1.3× faster than with the reference exhaustive paths, timed
        /// back to back in this process (measured 1.63–1.86× at this early state, balls still near the centre; 3× over a full
        /// 180 s run, see the headless test).
        /// </summary>
        [Fact]
        public void BroadphaseSpeedsUpTheHundredRingScene()
        {
            double fast = double.MaxValue, slow = double.MaxValue;
            for (int rep = 0; rep < 2 || (rep < 2 * Attempts && slow / fast <= 1.3); rep++)
            {
                fast = Math.Min(fast, TimeTicks(() => RingEscape(100)));
                slow = Math.Min(slow, TimeTicks(() =>
                {
                    var w = RingEscape(100);
                    w.ExhaustiveBroadphase = true;
                    return w;
                }));
            }
            double speedup = slow / fast;
            _out.WriteLine("M01 100 rings: broadphase " + fast.ToString("F3") + " ms/tick, exhaustive " + slow.ToString("F3") + " ms/tick (" + speedup.ToString("F2") + "×)");
            Assert.True(speedup > 1.3, "broadphase speedup only " + speedup + "×");
        }

        /// <summary>
        /// Helix Drop (M05): one ball falling through a tower of 30 spinning platforms with gaps, swiped tower spin. Ratio
        /// 0.0060–0.0080 (0.013–0.015 ms/tick; the scene is cache-resident, load barely moves it); limit 0.0105: detection
        /// threshold 1.75×, margin 1.31×. Eight rounds (the scene is cheap). (The old 0.007 ms/tick was measured after the 0.2 s
        /// warm-up had run the ball out of the tower.)
        /// </summary>
        [Fact]
        public void HelixDropThirtyPlatforms()
        {
            int[] tower = null;
            Report("M05 (30 spin platforms, 1 ball)", () => TimeTicks(() =>
            {
                var w = new PhysicsWorld(SimSettings.Default);
                var rng = Rng.FromSeed(5UL);
                tower = new int[30];
                for (int k = 0; k < 30; k++)
                {
                    tower[k] = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -1.2f * k), Scale = 1f, Spin = rng.NextFloat(0f, SimMath.TwoPi) });
                    float gap = rng.NextFloat(0f, SimMath.TwoPi);
                    w.AddSpinPlatform(tower[k], Vec2.Zero, 1.5f, 0.15f, new[] { AngularGap.Centered(gap, 0.9f) }, SimTestUtil.Wall(0.6f, 0.3f));
                }
                w.AddBall(SimTestUtil.Ball(new Vec2(0.3f, 1f), Vec2.Zero, 0.15f, 0.6f, 0.3f));
                return w;
            }, (world, tick) =>
            {
                float spin = (tick / 30) % 2 == 0 ? 3f : -3f;
                for (int k = 0; k < tower.Length; k++) world.SetBodySpinVelocity(tower[k], spin);
            }, rounds: 8), 0.0105 * Slack);
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
