using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    public class BallBallTests
    {
        readonly ITestOutputHelper _out;

        public BallBallTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void HeadOnElasticEqualMassesExchangeVelocities()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int a = w.AddBall(SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(3f, 0f), 0.2f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(1f, 0f), new Vec2(-1f, 0f), 0.2f));
            bool hit = false;
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind != SimEventKind.BallHit) continue;
                    hit = true;
                    Assert.Equal(a, e.A);
                    Assert.Equal(b, e.B);
                    Assert.Equal(1f, e.Normal.X, 3);
                    Assert.Equal(4f, e.Speed, 2);
                    // Equal masses (π·0.04 kg each), e = 1: impulse = m·Δv = m·4.
                    Assert.Equal(SimMath.Pi * 0.04f * 4f, e.Impulse, 3);
                }
            }
            Assert.True(hit);
            Assert.Equal(-1f, w.GetBall(a).Velocity.X, 3);
            Assert.Equal(3f, w.GetBall(b).Velocity.X, 3);
        }

        [Fact]
        public void HeavyBallBarelySlowsWhenHittingLightBall()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            var heavy = SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(2f, 0f), 0.2f);
            heavy.Mass = 100f;
            var light = SimTestUtil.Ball(new Vec2(1f, 0f), Vec2.Zero, 0.2f);
            light.Mass = 1f;
            int a = w.AddBall(heavy);
            int b = w.AddBall(light);
            for (int t = 0; t < 60; t++) w.Step();
            // 1D elastic: v_heavy = 2·(100−1)/101 ≈ 1.96, v_light = 2·2·100/101 ≈ 3.96.
            Assert.Equal(2f * 99f / 101f, w.GetBall(a).Velocity.X, 3);
            Assert.Equal(400f / 101f, w.GetBall(b).Velocity.X, 3);
        }

        [Fact]
        public void BallPileInBoxSettlesSilentlyWithoutOverlapOrEscape()
        {
            var s = SimSettings.Default;
            var w = new PhysicsWorld(s);
            int box = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(box, ShapeBuilder.Rectangle(3f, 6f), true, 0.1f, SimTestUtil.Wall(0f, 0.5f));
            var rng = Rng.FromSeed(11UL);
            const int n = 120;
            for (int i = 0; i < n; i++)
            {
                float r = rng.NextFloat(0.1f, 0.16f);
                // Jitter so the balls do not start in perfectly aligned (unstable) columns.
                float jitter = rng.NextFloat(-0.04f, 0.04f);
                var d = SimTestUtil.Ball(new Vec2(-1.2f + (i % 8) * 0.34f + jitter, -2.5f + (i / 8) * 0.34f), Vec2.Zero, r, 0.3f, 0.5f);
                w.AddBall(d);
            }
            double ke0 = KineticEnergy(w, n);
            float worstDuring = 0f;
            double keMax = 0;
            for (int t = 0; t < 18 * SimConstants.TickRate; t++)
            {
                w.Step();
                if (t % 30 == 0) worstDuring = MathF.Max(worstDuring, WorstOverlap(w, n));
                if (t == 30) keMax = KineticEnergy(w, n);
            }
            // Last 2 s: the settled pile must be silent (no contact sounds) and still.
            var start = new Vec2[n];
            for (int i = 0; i < n; i++) start[i] = w.GetBall(i).Position;
            int lateEvents = 0;
            for (int t = 0; t < 2 * SimConstants.TickRate; t++)
            {
                w.Step();
                lateEvents += w.EventCount;
            }
            float maxDisplacement = 0f;
            for (int i = 0; i < n; i++)
            {
                ref readonly BallState a = ref w.GetBall(i);
                maxDisplacement = MathF.Max(maxDisplacement, (a.Position - start[i]).Length);
                Assert.InRange(a.Position.X, -1.45f + a.Radius - 0.002f, 1.45f - a.Radius + 0.002f);
                Assert.InRange(a.Position.Y, -2.95f + a.Radius - 0.002f, 2.95f - a.Radius + 0.002f);
            }
            float worstOverlap = WorstOverlap(w, n);
            double keEnd = KineticEnergy(w, n);
            _out.WriteLine("after 20 s: KE " + keEnd + " J (peak " + keMax + "), events in last 2 s " + lateEvents
                + ", max displacement " + maxDisplacement + " m, worst overlap " + worstOverlap * 100f + "% of radius (while settling "
                + worstDuring * 100f + "%)");
            Assert.Equal(0, lateEvents);
            Assert.True(maxDisplacement < 0.01f, "pile creeping: " + maxDisplacement);
            Assert.True(keEnd < keMax * 1e-3, "kinetic energy " + keEnd + " vs peak " + keMax);
            Assert.True(worstOverlap < 0.05f, "overlap " + worstOverlap);
            Assert.True(worstDuring < 0.08f, "overlap while settling " + worstDuring);
            Assert.Equal(0.0, ke0);
        }

        static double KineticEnergy(PhysicsWorld w, int n)
        {
            double ke = 0;
            for (int i = 0; i < n; i++)
            {
                ref readonly BallState a = ref w.GetBall(i);
                ke += 0.5 * a.Mass * a.Velocity.LengthSquared;
            }
            return ke;
        }

        static float WorstOverlap(PhysicsWorld w, int n)
        {
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                ref readonly BallState a = ref w.GetBall(i);
                for (int j = i + 1; j < n; j++)
                {
                    ref readonly BallState b = ref w.GetBall(j);
                    float overlap = a.Radius + b.Radius - (a.Position - b.Position).Length;
                    worst = MathF.Max(worst, overlap / MathF.Min(a.Radius, b.Radius));
                }
            }
            return worst;
        }

        [Fact]
        public void VerticalColumnOfBallsStaysStacked()
        {
            var w = new PhysicsWorld();
            int tube = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(tube, new[] { new Vec2(-0.21f, 5f), new Vec2(-0.21f, 0f), new Vec2(0.21f, 0f), new Vec2(0.21f, 5f) }, false, 0f, SimTestUtil.Wall(0f, 0.3f));
            for (int i = 0; i < 10; i++) w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.2f + 0.4f * i), Vec2.Zero, 0.2f, 0f, 0.3f));
            for (int t = 0; t < 5 * SimConstants.TickRate; t++) w.Step();
            for (int i = 0; i < 10; i++)
            {
                ref readonly BallState b = ref w.GetBall(i);
                Assert.Equal(0.2f + 0.4f * i, b.Position.Y, 1);
                Assert.True(b.Velocity.Length < 0.05f);
            }
        }

        [Fact]
        public void BallCollisionsCanBeSwitchedOffGloballyAndPerBall()
        {
            var s = SimTestUtil.NoGravity();
            s.BallCollisions = false;
            var w = new PhysicsWorld(s);
            int a = w.AddBall(SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(2f, 0f), 0.2f));
            w.AddBall(SimTestUtil.Ball(new Vec2(1f, 0f), new Vec2(-2f, 0f), 0.2f));
            for (int t = 0; t < 60; t++) w.Step();
            Assert.Equal(2f, w.GetBall(a).Velocity.X, 4);

            var w2 = new PhysicsWorld(SimTestUtil.NoGravity());
            var d = SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(2f, 0f), 0.2f);
            d.Mask = 0u; // pure particle (ring fragments)
            int p = w2.AddBall(d);
            w2.AddBall(SimTestUtil.Ball(new Vec2(1f, 0f), new Vec2(-2f, 0f), 0.2f));
            for (int t = 0; t < 60; t++) w2.Step();
            Assert.Equal(2f, w2.GetBall(p).Velocity.X, 4);
        }

        [Fact]
        public void CoincidentBallsSeparateDeterministically()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int a = w.AddBall(SimTestUtil.Ball(Vec2.Zero, Vec2.Zero, 0.2f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, Vec2.Zero, 0.2f, 0f));
            for (int t = 0; t < 30; t++) w.Step();
            float dist = (w.GetBall(a).Position - w.GetBall(b).Position).Length;
            Assert.True(dist > 0.39f, "dist " + dist);
            Assert.True(w.GetBall(a).Position.X < w.GetBall(b).Position.X);
        }

        [Fact]
        public void BallsSpreadFarApartStillCollideViaCoarserGrid()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            // Extent 20 km with 2 cm balls would need 10^12 cells; the grid coarsens instead.
            w.AddBall(SimTestUtil.Ball(new Vec2(-10000f, 0f), Vec2.Zero, 0.01f));
            w.AddBall(SimTestUtil.Ball(new Vec2(10000f, 0f), Vec2.Zero, 0.01f));
            int a = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0f), new Vec2(1f, 0f), 0.01f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.1f, 0f), Vec2.Zero, 0.01f));
            for (int t = 0; t < 30; t++) w.Step();
            Assert.Equal(0f, w.GetBall(a).Velocity.X, 3);
            Assert.Equal(1f, w.GetBall(b).Velocity.X, 3);
        }
    }
}
