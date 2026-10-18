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
    /// float/sqrt calibration loop measured in the same process (best of several runs for both), and the bound is a ratio:
    /// a several-fold solver regression fails on any machine, a slow or busy CI runner does not. Limits are ~3–4× the ratios
    /// measured on the development container (x64, .NET 8 Release: tier A ≈ 0.55 ms/tick ≈ 0.3× calibration). Measured
    /// times and the ratio to the 0.5 ms target are written to the test output; the absolute target is tracked, not asserted.
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
            s_calibrationMs = best;
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
        /// Best average tick time (ms) over many short batches after the process-wide JIT warm-up and a scene warm-up of at
        /// least 60 ticks / 0.2 s. Short batches and the minimum keep preemption on a busy machine (4–16 ms scheduler slices)
        /// out of the result.
        /// </summary>
        static double TimeTicks(PhysicsWorld w, Action<PhysicsWorld> beforeStep = null, int batches = 30, int ticksPerBatch = 10)
        {
            EnsureJitWarm();
            var warm = Stopwatch.StartNew();
            for (int t = 0; t < 60 || warm.ElapsedMilliseconds < 200; t++)
            {
                beforeStep?.Invoke(w);
                w.Step();
            }
            double best = double.MaxValue;
            for (int b = 0; b < batches; b++)
            {
                var sw = Stopwatch.StartNew();
                for (int t = 0; t < ticksPerBatch; t++)
                {
                    beforeStep?.Invoke(w);
                    w.Step();
                }
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds / ticksPerBatch);
            }
            return best;
        }

        void Report(string scene, double ms, double ratioLimit)
        {
            double calib = CalibrationMs();
            double ratio = ms / calib;
            _out.WriteLine(scene + ": " + ms.ToString("F3") + " ms/tick (" + (ms / 0.5).ToString("F2") + "× the 0.5 ms target), "
                + ratio.ToString("F2") + "× calibration (" + calib.ToString("F3") + " ms), limit " + ratioLimit + "×");
            Assert.True(ratio < ratioLimit, scene + ": " + ms + " ms/tick is " + ratio + "× the calibration loop (limit " + ratioLimit + "×)");
        }

#if DEBUG
        // Unoptimised Core code is 10–13× slower relative to the (also unoptimised) calibration loop.
        const double Slack = 20.0;
#else
        const double Slack = 1.0;
#endif

        /// <summary>Geometry Bounce (M02): balls of 0.34 m in a rotating hexagon (R 3.6 m, wall 0.12 m), 1 g, 8 substeps.</summary>
        static PhysicsWorld GeometryBounce(int balls, float diameterSpread = 0f, float bigRadius = 0f, float radius = 0.17f)
        {
            var w = new PhysicsWorld(SimSettings.Default);
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

        [Theory]
        [InlineData("A", 200, 2.5)]
        [InlineData("B", 120, 1.0)]
        [InlineData("C", 60, 0.4)]
        public void GeometryBounceBudgetScenes(string tier, int balls, double ratioLimit)
        {
            var w = GeometryBounce(balls);
            double ms = TimeTicks(w);
            Assert.Equal(balls, w.AliveBallCount);
            Report("M02 tier " + tier + " (" + balls + " balls Ø0.34 m, rotating hexagon, 8 substeps)", ms, ratioLimit * Slack);
        }

        [Fact]
        public void GeometryBounceWithFullSizeSpreadStaysCheap()
        {
            var w = GeometryBounce(200, diameterSpread: 1f);
            Report("M02 tier A, sizeSpread 100%", TimeTicks(w), 3.0 * Slack);
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
                uniform = Math.Min(uniform, TimeTicks(GeometryBounce(120, radius: 0.12f), batches: 15));
                mixed = Math.Min(mixed, TimeTicks(GeometryBounce(120, bigRadius: 1.2f, radius: 0.12f), batches: 15));
            }
            double ratio = mixed / uniform;
            _out.WriteLine("120 balls r 0.12: " + uniform.ToString("F3") + " ms/tick; + one r 1.2 ball: " + mixed.ToString("F3") + " ms/tick ("
                + ratio.ToString("F2") + "×)");
            Assert.True(ratio < 2.5, "one grown ball made the tick " + ratio + "× slower");
        }

        /// <summary>Ring Escape (M01) at its budget: 100 rotating rings with a gap each (own bodies), a passage sensor per ring, 20 balls.</summary>
        [Fact]
        public void RingEscapeHundredRingsTwentyBalls()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(3UL);
            for (int k = 0; k < 100; k++)
            {
                float radius = 0.8f + 0.2f * k;
                float omega = (k % 2 == 0 ? 1f : -1f) * rng.NextFloat(0.5f, 2f);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, rng.NextFloat(0f, SimMath.TwoPi), omega));
                w.AddRing(body, Vec2.Zero, radius, 0.04f, new[] { AngularGap.Centered(0f, 0.6f) }, SimTestUtil.Wall(0.9f, 0.1f));
                w.AddRing(body, Vec2.Zero, radius + 0.08f, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor());
            }
            for (int i = 0; i < 20; i++)
            {
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-0.3f, 0.3f), rng.NextFloat(-0.3f, 0.3f)),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 8f, 0.05f, 0.9f, 0.1f);
                w.AddBall(d);
            }
            Report("M01 (100 rings + 100 sensors, 20 balls)", TimeTicks(w), 1.0 * Slack);
        }

        /// <summary>Helix Drop (M05): one ball falling through a tower of 30 spinning platforms with gaps, swiped tower spin.</summary>
        [Fact]
        public void HelixDropThirtyPlatforms()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(5UL);
            var tower = new int[30];
            for (int k = 0; k < 30; k++)
            {
                tower[k] = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -1.2f * k), Scale = 1f, Spin = rng.NextFloat(0f, SimMath.TwoPi) });
                float gap = rng.NextFloat(0f, SimMath.TwoPi);
                w.AddSpinPlatform(tower[k], Vec2.Zero, 1.5f, 0.15f, new[] { AngularGap.Centered(gap, 0.9f) }, SimTestUtil.Wall(0.6f, 0.3f));
            }
            w.AddBall(SimTestUtil.Ball(new Vec2(0.3f, 1f), Vec2.Zero, 0.15f, 0.6f, 0.3f));
            int frame = 0;
            Report("M05 (30 spin platforms, 1 ball)", TimeTicks(w, world =>
            {
                float spin = (frame++ / 30) % 2 == 0 ? 3f : -3f;
                for (int k = 0; k < tower.Length; k++) world.SetBodySpinVelocity(tower[k], spin);
            }), 0.1 * Slack);
        }
    }
}
