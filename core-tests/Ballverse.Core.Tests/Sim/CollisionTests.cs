using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    public class CollisionTests
    {
        readonly ITestOutputHelper _out;

        public CollisionTests(ITestOutputHelper output)
        {
            _out = output;
        }

        // ------------------------------------------------------------------ tunnelling

        [Theory]
        [InlineData(8)]
        [InlineData(1)]
        public void BallsAtMaxSpeedNeverEscapeThinRotatingHexagon(int substeps)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(substeps, maxSpeed: 60f));
            // 360°/s, 2 cm thick walls; balls of 5 cm radius at 60 m/s (1 m per tick, 2.4× the wall+ball width per substep at 8 substeps).
            var (body, _) = SimTestUtil.AddPolygonArena(w, 6, 4f, 0.02f, SimMath.TwoPi, 1.2f, 0.3f);
            var rng = Rng.FromSeed(5UL);
            for (int i = 0; i < 24; i++)
            {
                Vec2 dir = Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi));
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)), dir * 60f, 0.05f, 1.2f, 0.3f);
                d.Mask = 1u;          // walls only
                d.Category = 2u;
                w.AddBall(d);
            }
            int hits = 0;
            for (int t = 0; t < 60 * SimConstants.TickRate; t++)
            {
                w.Step();
                hits += w.EventCount;
                for (int i = 0; i < w.BallCount; i++)
                {
                    Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position);
                    Assert.True(SimTestUtil.InsideConvexPolygon(local, 6, 4f, 0.011f),
                        "ball " + i + " escaped at tick " + t + " local " + local);
                }
            }
            Assert.True(hits > 10000, "hits " + hits);
            _out.WriteLine("substeps=" + substeps + " hits=" + hits);
        }

        [Fact]
        public void BallsAtMaxSpeedNeverEscapeGaplessRotatingRingFromInsideOrOutside()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(4, maxSpeed: 60f));
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, -SimMath.TwoPi));
            w.AddRing(body, Vec2.Zero, 3f, 0.02f, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1.2f, 0.5f));
            // Outer box keeps the outside balls nearby so they keep hitting the ring from outside.
            int box = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(box, ShapeBuilder.Rectangle(9f, 9f), true, 0.02f, SimTestUtil.Wall(1.2f, 0.5f));
            var rng = Rng.FromSeed(9UL);
            for (int i = 0; i < 20; i++)
            {
                bool inside = i % 2 == 0;
                Vec2 p = inside ? new Vec2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)) : new Vec2(rng.NextFloat(3.5f, 4.2f), rng.NextFloat(-4f, 4f));
                var d = SimTestUtil.Ball(p, Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 60f, 0.04f, 1.2f, 0.5f);
                d.Mask = 1u;
                d.Category = 2u;
                d.Tag = inside ? 1 : 0;
                w.AddBall(d);
            }
            for (int t = 0; t < 60 * SimConstants.TickRate; t++)
            {
                w.Step();
                for (int i = 0; i < w.BallCount; i++)
                {
                    ref readonly BallState b = ref w.GetBall(i);
                    float r = b.Position.Length;
                    if (b.Tag == 1) Assert.True(r < 3f, "inside ball " + i + " escaped at tick " + t + " r=" + r);
                    else Assert.True(r > 3f, "outside ball " + i + " got in at tick " + t + " r=" + r);
                    Assert.True(MathF.Abs(b.Position.X) < 4.5f && MathF.Abs(b.Position.Y) < 4.5f, "left the box");
                }
            }
        }

        [Fact]
        public void FastRotatingWallSweepingOverRestingBallPushesItInsteadOfPassing()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(1, maxSpeed: 60f));
            // A 4 m bar spinning at 4 turns/s: its tip moves 100 m/s, i.e. 1.7 m per tick at one substep.
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 8f * SimMath.Pi));
            w.AddSegment(body, Vec2.Zero, new Vec2(4f, 0f), 0.02f, SimTestUtil.Wall(0.5f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 3.5f), Vec2.Zero, 0.05f, 0.5f, 0f));
            bool hit = false;
            for (int t = 0; t < 30 && !hit; t++)
            {
                w.Step();
                foreach (var e in w.Events) hit |= e.Kind == SimEventKind.Hit;
            }
            Assert.True(hit, "the bar must hit the ball");
            Vec2 v = w.GetBall(b).Velocity;
            // Counter-clockwise bar at the ball's radius: wall speed ≈ 8π·3.5 ≈ 88 m/s pushing in −x, clamped at 60.
            Assert.True(v.X < -30f, "ball velocity " + v);
        }

        // ------------------------------------------------------------------ energy

        [Fact]
        public void StaticPolygonElasticBallKeepsSpeedOverSixtySeconds()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            SimTestUtil.AddPolygonArena(w, 6, 4f, 0.1f, 0f, 1f, 0f);
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.3f, -0.2f), new Vec2(5f, 3.7f), 0.2f, 1f, 0f));
            float v0 = w.GetBall(b).Velocity.Length;
            for (int t = 0; t < 3600; t++) w.Step();
            float v1 = w.GetBall(b).Velocity.Length;
            Assert.True(MathF.Abs(v1 - v0) / v0 < 1e-3f, "speed " + v0 + " -> " + v1);
        }

        [Fact]
        public void RotatingPolygonElasticBallsConserveJacobiIntegralAndBoundedSpeed()
        {
            // Walls static in the rotating frame + e = 1 + no friction + no gravity: the Jacobi integral
            // J = |v|²/2 − ω·(r × v) is exactly conserved by the continuous dynamics, so its drift measures the solver's
            // energy error against moving walls (the raw speed legitimately changes at each bounce off a moving wall).
            const float omega = 1.2f;
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            SimTestUtil.AddPolygonArena(w, 6, 4f, 0.1f, omega, 1f, 0f);
            var rng = Rng.FromSeed(3UL);
            for (int i = 0; i < 8; i++)
            {
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-1.5f, 1.5f), rng.NextFloat(-1.5f, 1.5f)),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 6f, 0.15f, 1f, 0f);
                d.Mask = 1u;
                d.Category = 2u;
                w.AddBall(d);
            }
            var j0 = new float[w.BallCount];
            var bound = new float[w.BallCount];
            for (int i = 0; i < w.BallCount; i++)
            {
                j0[i] = Jacobi(w.GetBall(i), omega);
                // |v|² = 2J + 2ω(r × v) ≤ 2J + 2ωR|v| with R the circumradius ⇒ |v| ≤ ωR + sqrt((ωR)² + 2J).
                float wr = omega * 4f;
                bound[i] = wr + MathF.Sqrt(wr * wr + 2f * j0[i]);
            }
            float worstDrift = 0f, maxSpeed = 0f, minSpeed = float.MaxValue;
            for (int t = 0; t < 3600; t++)
            {
                w.Step();
                for (int i = 0; i < w.BallCount; i++)
                {
                    float v = w.GetBall(i).Velocity.Length;
                    maxSpeed = MathF.Max(maxSpeed, v);
                    minSpeed = MathF.Min(minSpeed, v);
                    Assert.True(v <= bound[i] * 1.01f, "ball " + i + " speed " + v + " exceeds the Jacobi bound " + bound[i]);
                }
            }
            for (int i = 0; i < w.BallCount; i++)
            {
                float drift = MathF.Abs(Jacobi(w.GetBall(i), omega) - j0[i]) / MathF.Abs(j0[i]);
                worstDrift = MathF.Max(worstDrift, drift);
            }
            _out.WriteLine("Jacobi drift " + worstDrift + ", speed range " + minSpeed + ".." + maxSpeed);
            Assert.True(worstDrift < 0.01f, "Jacobi drift " + worstDrift);
        }

        /// <summary>
        /// Slow elastic balls without gravity (spec M02 «Невесомость», «Медитация»): the restitution threshold scales with
        /// gravity, so a 0.2 m/s ball neither sticks to the walls nor loses energy.
        /// </summary>
        [Fact]
        public void SlowElasticBallWithoutGravityKeepsSpeedOverSixtySeconds()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            Assert.Equal(0.25f, w.Settings.RestitutionThreshold);
            SimTestUtil.AddPolygonArena(w, 6, 3.6f, 0.12f, 0f, 1f, 0f);
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(0.2f, 0.05f), 0.25f, 1f, 0f));
            float v0 = w.GetBall(b).Velocity.Length;
            int hits = 0;
            for (int t = 0; t < 3600; t++)
            {
                w.Step();
                hits += w.EventCount;
            }
            float v1 = w.GetBall(b).Velocity.Length;
            Assert.True(hits >= 2, "the ball must bounce off walls, hits " + hits);
            Assert.True(MathF.Abs(v1 - v0) / v0 < 1e-3f, "speed " + v0 + " -> " + v1);
        }

        [Fact]
        public void SlowElasticBallsInSlowlyRotatingPolygonWithoutGravityConserveJacobiIntegral()
        {
            const float omega = 0.15f;
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            SimTestUtil.AddPolygonArena(w, 6, 3.6f, 0.12f, omega, 1f, 0f);
            var rng = Rng.FromSeed(12UL);
            for (int i = 0; i < 12; i++)
            {
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-2f, 2f), rng.NextFloat(-2f, 2f)),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 0.2f, 0.15f, 1f, 0f);
                d.Mask = 1u;
                d.Category = 2u;
                w.AddBall(d);
            }
            var j0 = new float[w.BallCount];
            for (int i = 0; i < w.BallCount; i++) j0[i] = Jacobi(w.GetBall(i), omega);
            int hits = 0;
            for (int t = 0; t < 3600; t++)
            {
                w.Step();
                hits += w.EventCount;
            }
            float worst = 0f;
            for (int i = 0; i < w.BallCount; i++)
            {
                worst = MathF.Max(worst, MathF.Abs(Jacobi(w.GetBall(i), omega) - j0[i]) / MathF.Abs(j0[i]));
            }
            _out.WriteLine("slow rotating polygon: hits " + hits + ", Jacobi drift " + worst);
            Assert.True(hits > 20, "hits " + hits);
            Assert.True(worst < 0.02f, "Jacobi drift " + worst);
        }

        [Fact]
        public void SlowElasticBallPairWithoutGravityBouncesInsteadOfMerging()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int a = w.AddBall(SimTestUtil.Ball(new Vec2(-0.5f, 0f), new Vec2(0.05f, 0f), 0.2f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.5f, 0f), new Vec2(-0.05f, 0f), 0.2f));
            for (int t = 0; t < 600; t++) w.Step();
            Assert.Equal(-0.05f, w.GetBall(a).Velocity.X, 4);
            Assert.Equal(0.05f, w.GetBall(b).Velocity.X, 4);
        }

        [Fact]
        public void GravityScaleZeroBallInGravityWorldKeepsSlowElasticBounces()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            SimTestUtil.AddPolygonArena(w, 4, 3f, 0.1f, 0f, 1f, 0f);
            var d = SimTestUtil.Ball(Vec2.Zero, new Vec2(0.15f, 0f), 0.2f, 1f, 0f);
            d.GravityScale = 0f;
            int b = w.AddBall(d);
            for (int t = 0; t < 3600; t++) w.Step();
            Assert.Equal(0.15f, w.GetBall(b).Velocity.Length, 4);
        }

        static float Jacobi(in BallState b, float omega)
        {
            return 0.5f * b.Velocity.LengthSquared - omega * Vec2.Cross(b.Position, b.Velocity);
        }

        // ------------------------------------------------------------------ moving walls

        [Theory]
        [InlineData(1f, 10f)]
        [InlineData(0.5f, 7.5f)]
        [InlineData(0f, 5f)]
        public void MovingPaddleImpartsOnePlusRestitutionTimesItsSpeed(float restitution, float expected)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int paddle = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(-1f, 0f), LinearVelocity = new Vec2(5f, 0f) });
            w.AddSegment(paddle, new Vec2(0f, -1f), new Vec2(0f, 1f), 0.1f, SimTestUtil.Wall(0f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, Vec2.Zero, 0.2f, restitution, 0f));
            for (int t = 0; t < 30; t++) w.Step();
            Vec2 v = w.GetBall(b).Velocity;
            Assert.Equal(expected, v.X, 2);
            Assert.Equal(0f, v.Y, 4);
        }

        [Fact]
        public void RotatingBarImpartsTangentialSpeedAtContactRadius()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            const float omega = 2f;
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, omega));
            w.AddSegment(body, Vec2.Zero, new Vec2(3f, 0f), 0f, SimTestUtil.Wall(0f, 0f));
            // Ball resting at radius 2, just above the bar's start angle.
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(2f, 0.5f), Vec2.Zero, 0.1f, 1f, 0f));
            SimEvent first = default;
            bool hit = false;
            for (int t = 0; t < 60 && !hit; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (!hit && e.Kind == SimEventKind.Hit)
                    {
                        hit = true;
                        first = e;
                    }
                }
            }
            Assert.True(hit);
            // Elastic hit by a wall moving at ω·r along its normal: ball speed ≈ 2·ω·r.
            float rContact = first.Point.Length;
            float speed = w.GetBall(b).Velocity.Length;
            Assert.InRange(speed, 2f * omega * rContact * 0.9f, 2f * omega * rContact * 1.1f);
            Assert.True(first.Impulse > 0f);
        }

        [Fact]
        public void ConveyorFloorDragsBallIntoRollingWithFriction()
        {
            var w = new PhysicsWorld();
            // A long kinematic floor sliding along itself at 3 m/s (conveyor).
            int floor = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, LinearVelocity = new Vec2(3f, 0f), Scale = 1f });
            w.AddSegment(floor, new Vec2(-1000f, 0f), new Vec2(1000f, 0f), 0f, SimTestUtil.Wall(0f, 1f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.2f), Vec2.Zero, 0.2f, 0f, 1f));
            for (int t = 0; t < 120; t++) w.Step();
            ref readonly BallState s = ref w.GetBall(b);
            // Solid disc dragged from rest: rolling without slip when v + ω·r = 3, with Δ(ω·r) = 2·Δv ⇒ v = 1 m/s.
            Assert.Equal(1f, s.Velocity.X, 2);
            Assert.Equal(2f, s.AngularVelocity * s.Radius, 2);
        }

        [Fact]
        public void ShrinkingRingPushesBallInward()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f, ScaleVelocity = -0.5f });
            w.AddRing(body, Vec2.Zero, 3f, 0.1f, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(2.7f, 0f), Vec2.Zero, 0.2f, 1f, 0f));
            for (int t = 0; t < 30; t++) w.Step();
            ref readonly BallState s = ref w.GetBall(b);
            // Radial wall speed at r ≈ 2.9: 0.5·2.9/scale ≈ 1.5 m/s; elastic ⇒ ball moves inward at ≈ 3 m/s.
            Assert.True(s.Velocity.X < -2f, "v=" + s.Velocity);
            Assert.True(s.Position.Length < w.GetBody(body).Scale * 3f);
        }

        // ------------------------------------------------------------------ rings and gaps

        [Fact]
        public void BallPassesThroughRingGapButBouncesOffSolidPart()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            // 60° gap centred on +X.
            int ring = w.AddRing(body, Vec2.Zero, 2f, 0.1f, new[] { AngularGap.Centered(0f, SimMath.Pi / 3f) }, SimTestUtil.Wall(1f, 0f));
            // The two balls start on top of each other; they only collide with walls.
            var e0 = SimTestUtil.Ball(Vec2.Zero, new Vec2(4f, 0f), 0.2f);
            e0.Mask = 1u;
            e0.Category = 2u;
            int escaping = w.AddBall(e0);
            var d = SimTestUtil.Ball(new Vec2(0f, 0.01f), new Vec2(-4f, 0f), 0.2f);
            d.Mask = 1u;
            d.Category = 2u;
            int bouncing = w.AddBall(d);
            bool bounceHit = false;
            // 40 ticks: the bouncing ball hits the solid side at ~0.44 s and is still inside, heading back, at 0.67 s.
            for (int t = 0; t < 40; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    Assert.NotEqual(escaping, e.A);
                    if (e.Kind == SimEventKind.Hit && e.A == bouncing)
                    {
                        bounceHit = true;
                        Assert.Equal(ring, e.B);
                        Assert.InRange(e.LocalAngle, SimMath.Pi - 0.02f, SimMath.Pi + 0.02f);
                        Assert.Equal(0, e.Feature);
                    }
                }
            }
            Assert.True(w.GetBall(escaping).Position.Length > 2.3f, "ball should have left through the gap");
            Assert.True(bounceHit);
            Assert.True(w.GetBall(bouncing).Position.Length < 2f, "ball hitting the solid part stays inside");
        }

        [Fact]
        public void RotatingRingLetsBallOutOnlyWhenGapComesAround()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            // Gap centred on −Y while the ball flies to +Y; ring turns 180° in 1 s.
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, SimMath.Pi));
            w.AddRing(body, Vec2.Zero, 2f, 0.1f, new[] { AngularGap.Centered(-SimMath.HalfPi, 0.9f) }, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(0f, 6f), 0.15f));
            int escapeTick = -1, hits = 0;
            for (int t = 0; t < 300 && escapeTick < 0; t++)
            {
                w.Step();
                hits += w.EventCount;
                if (w.GetBall(b).Position.Length > 2.3f) escapeTick = t;
            }
            Assert.True(hits >= 2, "bounces before escaping: " + hits);
            Assert.True(escapeTick > 0, "never escaped");
        }

        [Fact]
        public void BallWiderThanGapIsStoppedByGapEdgeCaps()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            // Gap chord ≈ 2·2·sin(0.05) = 0.2 m, ball diameter 0.6 m.
            w.AddRing(body, Vec2.Zero, 2f, 0.1f, new[] { AngularGap.Centered(0f, 0.1f) }, SimTestUtil.Wall(0.5f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(5f, 0f), 0.3f, 0.5f));
            for (int t = 0; t < 120; t++) w.Step();
            Assert.True(w.GetBall(b).Position.Length < 2f, "ball squeezed through a narrow gap: " + w.GetBall(b).Position);
        }

        [Fact]
        public void ConcentricRingsWithGapsLetBallEscapeLevelByLevel()
        {
            // Ring Escape shape: 5 rings alternating direction, one gap each; the ball eventually leaves all of them
            // once rules disable each ring on passage (here: never disabled, the ball just bounces through the gaps).
            var w = new PhysicsWorld(SimTestUtil.NoGravity(maxSpeed: 20f));
            int[] rings = new int[5];
            for (int i = 0; i < 5; i++)
            {
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, i * 1.3f, (i % 2 == 0 ? 1f : -1f) * (1f + 0.3f * i)));
                rings[i] = w.AddRing(body, Vec2.Zero, 1f + 0.6f * i, 0.08f, new[] { AngularGap.Centered(0f, 0.8f) }, SimTestUtil.Wall(1f, 0f));
            }
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(3f, 4f), 0.12f));
            int t = 0;
            for (; t < 120 * SimConstants.TickRate && w.GetBall(b).Position.Length < 4f; t++) w.Step();
            _out.WriteLine("escaped after " + t / 60f + " s");
            Assert.True(w.GetBall(b).Position.Length >= 4f, "ball did not escape in 120 s");
        }

        [Fact]
        public void GrowingBallStaysInsideRotatingRingUntilItFillsIt()
        {
            // Geometry Bounce growth variant: +4 % radius per wall hit, elastic, no gravity, in a rotating circle arena.
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 1f));
            const float arena = 3f, thickness = 0.1f;
            float inner = arena - thickness * 0.5f;
            int ring = w.AddRing(body, Vec2.Zero, arena, thickness, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.5f, 0f), new Vec2(4f, 2.5f), 0.2f));
            int hits = 0;
            for (int t = 0; t < 60 * SimConstants.TickRate; t++)
            {
                w.Step();
                // After every step (before this tick's growth is applied) the ball is fully inside the wall.
                ref readonly BallState s = ref w.GetBall(b);
                Assert.True(s.Position.Length + s.Radius <= inner + 0.002f,
                    "ball pushed through the wall at tick " + t + ": |p| + r = " + (s.Position.Length + s.Radius));
                foreach (var e in w.Events)
                {
                    if (e.Kind != SimEventKind.Hit || e.B != ring) continue;
                    hits++;
                    float r = MathF.Min(w.GetBall(b).Radius * 1.04f, inner * 0.95f);
                    w.SetBallRadius(b, r);
                }
            }
            Assert.True(w.GetBall(b).Radius >= inner * 0.95f - 1e-4f, "ball should have grown to fill the arena, r=" + w.GetBall(b).Radius);
            Assert.True(hits > 50);
        }

        // ------------------------------------------------------------------ misc colliders

        [Fact]
        public void CircleColliderDeflectsBall()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int peg = w.AddCircle(body, new Vec2(2f, 0f), 0.3f, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(5f, 0f), 0.2f));
            bool hit = false;
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                foreach (var e in w.Events) hit |= e.Kind == SimEventKind.Hit && e.B == peg;
            }
            Assert.True(hit);
            Assert.Equal(-5f, w.GetBall(b).Velocity.X, 2);
        }

        [Fact]
        public void PolylineEventReportsSegmentIndex()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int box = w.AddPolyline(body, ShapeBuilder.Rectangle(4f, 4f), true, 0.1f, SimTestUtil.Wall(1f, 0f));
            w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(0f, 5f), 0.2f));
            int feature = -1;
            for (int t = 0; t < 30 && feature < 0; t++)
            {
                w.Step();
                foreach (var e in w.Events) if (e.Kind == SimEventKind.Hit && e.B == box) feature = e.Feature;
            }
            // Rectangle points: bottom-left, bottom-right, top-right, top-left ⇒ top edge is segment 2.
            Assert.Equal(2, feature);
        }

        [Fact]
        public void CollisionFilteringSkipsMaskedColliders()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            var mat = SimTestUtil.Wall(1f, 0f);
            mat.Category = 4u;
            w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.1f, mat);
            var d = SimTestUtil.Ball(Vec2.Zero, new Vec2(3f, 0f), 0.2f);
            d.Mask = ~4u;
            int b = w.AddBall(d);
            for (int t = 0; t < 30; t++) w.Step();
            Assert.True(w.GetBall(b).Position.X > 1.4f);
        }

        [Fact]
        public void DisabledColliderAndBodyAreIgnored()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int seg = w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.1f, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(3f, 0f), 0.2f));
            w.SetColliderEnabled(seg, false);
            for (int t = 0; t < 30; t++) w.Step();
            Assert.True(w.GetBall(b).Position.X > 1.4f);
            Assert.False(w.IsColliderEnabled(seg));
        }

        [Fact]
        public void ContactHookCanMakeWallsPassable()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.1f, SimTestUtil.Wall(1f, 0f));
            int ghost = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.5f), new Vec2(3f, 0f), 0.2f));
            int solid = w.AddBall(SimTestUtil.Ball(new Vec2(0f, -0.5f), new Vec2(3f, 0f), 0.2f));
            var hook = new GhostHook { Ghost = ghost };
            w.ContactHook = hook;
            for (int t = 0; t < 40; t++) w.Step();
            Assert.True(w.GetBall(ghost).Position.X > 1.5f);
            Assert.True(w.GetBall(solid).Position.X < 1f);
            Assert.True(hook.Calls > 0);
        }

        /// <summary>A hook that kills the ball (deadly sector) ends its contacts: no response or Hit event from later colliders.</summary>
        [Fact]
        public void BallKilledInContactHookStopsCollidingAndEmitsNoFurtherHits()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int deadly = w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.02f, SimTestUtil.Wall(0.5f, 0f));
            w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.02f, SimTestUtil.Wall(0.5f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(20f, 0f), 0.1f));
            var hook = new KillHook { Collider = deadly };
            w.ContactHook = hook;
            int hitEvents = 0;
            for (int t = 0; t < 10; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind == SimEventKind.Hit) hitEvents++;
                }
            }
            Assert.False(w.GetBall(b).Alive);
            Assert.Equal(1, hook.Kills);
            Assert.Equal(0, hitEvents);
            Assert.Equal(20f, w.GetBall(b).Velocity.X);
            Assert.Equal(0f, w.GetBall(b).Velocity.Y);
        }

        [Fact]
        public void BallKilledInBallHitHookIsNotResolved()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int a = w.AddBall(SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(3f, 0f), 0.2f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(1f, 0f), new Vec2(-3f, 0f), 0.2f));
            w.ContactHook = new KillHook { Collider = -1, KillOnBallHit = true };
            int ballHits = 0;
            for (int t = 0; t < 40; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind == SimEventKind.BallHit) ballHits++;
                }
            }
            Assert.Equal(0, ballHits);
            Assert.False(w.GetBall(a).Alive);
            Assert.True(w.GetBall(b).Alive);
            Assert.Equal(-3f, w.GetBall(b).Velocity.X);
        }

        [Fact]
        public void StructuralChangesInsideContactHookAreRefused()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0.02f, SimTestUtil.Wall(0.5f, 0f));
            w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(10f, 0f), 0.1f));
            var hook = new MutatingHook();
            w.ContactHook = hook;
            Assert.False(w.IsStepping);
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                for (int t = 0; t < 30; t++) w.Step();
            });
            Assert.Contains("Step()", ex.Message);
            Assert.True(hook.WasStepping);
            // The world stays usable after the refused change.
            Assert.False(w.IsStepping);
            w.ContactHook = null;
            w.AddBall(SimTestUtil.Ball(new Vec2(-1f, 0f), Vec2.Zero, 0.1f));
            w.Step();
            Assert.Equal(2, w.BallCount);
        }

        sealed class KillHook : ISimContactHook
        {
            public int Collider;
            public bool KillOnBallHit;
            public int Kills;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                if (collider != Collider) return true;
                world.KillBall(ball);
                Kills++;
                return false;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact)
            {
                // Kill and still accept: the solver must notice the dead ball and skip the response.
                if (KillOnBallHit) world.KillBall(ballA);
                return true;
            }
        }

        sealed class MutatingHook : ISimContactHook
        {
            public bool WasStepping;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                WasStepping = world.IsStepping;
                world.AddBall(SimTestUtil.Ball(new Vec2(-2f, 0f), Vec2.Zero, 0.1f));
                return true;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact)
            {
                return true;
            }
        }

        sealed class GhostHook : ISimContactHook
        {
            public int Ghost;
            public int Calls;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                Calls++;
                return ball != Ghost;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact)
            {
                return true;
            }
        }
    }
}
