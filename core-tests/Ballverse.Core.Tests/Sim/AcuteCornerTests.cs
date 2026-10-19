using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Acute corners (triangle 60°, star tips): the overlap pass must never push a ball across a wall's centre line, even when
    /// ball–ball pushes jam it into the corner (spec §6.5 "0 пролётов"; M02 test "Эволюция 12 → 3 на предельной плотности —
    /// ни одного шара снаружи контура").
    /// </summary>
    public class AcuteCornerTests
    {
        readonly ITestOutputHelper _out;

        public AcuteCornerTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>Point inside a CCW polygon at the given inward distances from the centre lines of edges k and k+1.</summary>
        static Vec2 PointAtCorner(Vec2[] v, int k, float distK, float distNext)
        {
            int n = v.Length;
            Vec2 a1 = v[k], b1 = v[(k + 1) % n];
            Vec2 a2 = b1, b2 = v[(k + 2) % n];
            Vec2 n1 = (b1 - a1).Perp / (b1 - a1).Length;
            Vec2 n2 = (b2 - a2).Perp / (b2 - a2).Length;
            // n1·p = n1·a1 + distK, n2·p = n2·a2 + distNext.
            float c1 = Vec2.Dot(n1, a1) + distK;
            float c2 = Vec2.Dot(n2, a2) + distNext;
            float det = n1.X * n2.Y - n1.Y * n2.X;
            return new Vec2((c1 * n2.Y - n1.Y * c2) / det, (n1.X * c2 - c1 * n2.X) / det);
        }

        static float SignedDistance(Vec2[] v, int k, Vec2 p)
        {
            Vec2 a = v[k], b = v[(k + 1) % v.Length];
            Vec2 n = (b - a).Perp / (b - a).Length;
            return Vec2.Dot(p - a, n);
        }

        [Fact]
        public void BallJammedIntoTriangleCornerStaysInside()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            Vec2[] verts = ShapeBuilder.RegularPolygon(3, 4.2f);
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(body, verts, true, 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
            // Corner at vertex 2 (edges 1 and 2), 0.080 m from edge 1's centre line and 0.050 m from edge 2's.
            Vec2 p = PointAtCorner(verts, 1, 0.080f, 0.050f);
            int ball = w.AddBall(SimTestUtil.Ball(p, Vec2.Zero, 0.17f, 0.9f, 0.2f));
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                Vec2 q = w.GetBall(ball).Position;
                Assert.True(TunnellingPropertyTests.PointInPolygon(verts, q), "tick " + t + ": ball outside at " + q
                    + " (d1 " + SignedDistance(verts, 1, q) + ", d2 " + SignedDistance(verts, 2, q) + ")");
            }
            // Pushed out of both walls into the corner pocket: at least rt from both centre lines.
            Vec2 end = w.GetBall(ball).Position;
            Assert.True(SignedDistance(verts, 1, end) >= 0.17f + 0.06f - 1e-3f, "d1 " + SignedDistance(verts, 1, end));
            Assert.True(SignedDistance(verts, 2, end) >= 0.17f + 0.06f - 1e-3f, "d2 " + SignedDistance(verts, 2, end));
        }

        [Theory]
        [InlineData(0.080f, 0.050f)]
        [InlineData(0.005f, 0.005f)]
        [InlineData(0.0005f, 0.0005f)]
        [InlineData(0.20f, 0.001f)]
        [InlineData(0.001f, 0.20f)]
        public void BallInStarTipStaysInside(float d1, float d2)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            Vec2[] verts = ShapeBuilder.Star(5, 4.2f, 2.1f);
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(body, verts, true, 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
            // Tip at vertex 2: edges 1 and 2.
            Vec2 p = PointAtCorner(verts, 1, d1, d2);
            int ball = w.AddBall(SimTestUtil.Ball(p, Vec2.Zero, 0.15f, 0.9f, 0.2f));
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                Vec2 q = w.GetBall(ball).Position;
                Assert.True(TunnellingPropertyTests.PointInPolygon(verts, q), "tick " + t + ": ball outside at " + q);
            }
        }

        /// <summary>Dense piles with ball–ball on, under gravity and rotation (QA probes: 29–48 of 111 escaped from the triangle).</summary>
        [Theory]
        [InlineData(3, 4.2f, 0f, 111, 0.17f, 120f, 8, false)]
        [InlineData(3, 4.2f, 0f, 111, 0.17f, 336f, 8, false)]
        [InlineData(3, 4.2f, 0f, 111, 0.17f, 336f, 16, false)]
        [InlineData(3, 4.2f, 0f, 111, 0.17f, 336f, 8, true)]
        [InlineData(5, 4.2f, 2.1f, 80, 0.15f, 120f, 8, false)]
        [InlineData(5, 4.2f, 2.1f, 80, 0.15f, 300f, 8, false)]
        public void DensePileInAcuteArenaKeepsEveryBallInside(int points, float radius, float innerRadius, int balls, float r, float degPerSec, int substeps,
            bool separateSegments)
        {
            var s = SimSettings.Default;
            s.Substeps = substeps;
            s.MaxSpeed = 25f;
            var w = new PhysicsWorld(s);
            Vec2[] verts = innerRadius > 0f ? ShapeBuilder.Star(points, radius, innerRadius) : ShapeBuilder.RegularPolygon(points, radius);
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, degPerSec * SimMath.DegToRad));
            if (separateSegments)
            {
                for (int k = 0; k < verts.Length; k++) w.AddSegment(body, verts[k], verts[(k + 1) % verts.Length], 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
            }
            else
            {
                w.AddPolyline(body, verts, true, 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
            }
            var rng = Rng.FromSeed(2026UL, (ulong)(points * 1000 + balls));
            w.ReserveBalls(balls);
            Vec2[] starts = LatticePoints(rng, verts, r, 0.06f, balls);
            for (int i = 0; i < balls; i++)
            {
                Vec2 p = starts[i];
                var d = SimTestUtil.Ball(p, Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 5f, r, 0.9f, 0.2f);
                w.AddBall(d);
            }
            int outside = 0, firstTick = -1;
            for (int tick = 0; tick < 20 * SimConstants.TickRate; tick++)
            {
                w.Step();
                for (int i = 0; i < balls; i++)
                {
                    if (!w.GetBall(i).Alive) continue;
                    if (!TunnellingPropertyTests.PointInPolygon(verts, SimTestUtil.ToLocal(w, body, w.GetBall(i).Position)))
                    {
                        outside++;
                        if (firstTick < 0) firstTick = tick;
                        w.KillBall(i);
                    }
                }
            }
            _out.WriteLine("outside " + outside + "/" + balls + ", first at tick " + firstTick);
            Assert.Equal(0, outside);
        }

        /// <summary>
        /// M02 «Эволюция» 12 → 3 (spec §2.9 M02 tests): R 4.2 m, wall 0.12 m, 120 balls of Ø0.34 m (φ ≈ 0.48 in the triangle), 1 g,
        /// rotation 360°/s capped at 25 m/s ÷ R̂. Every 2 s the next polygon appears around the previous one (its inner face
        /// beyond the old outer vertices: the triangle at scale ≈ 2.06) and shrinks to scale 1 over 1 s. No ball may end up
        /// outside the current figure, no speed above the effective clamp, and that clamp stays near ω·R̂ + radial + 5 m/s.
        /// QA probe before the joint overlap pass: 38–40 of 120 balls ended up outside with e 0.9.
        /// </summary>
        [Theory]
        [InlineData(0.9f, 8)]
        [InlineData(0.9f, 4)]
        [InlineData(1.1f, 8)]
        public void EvolutionTwelveToThreeAtLimitDensityKeepsEveryBallInside(float restitution, int substeps)
        {
            const float radius = 4.2f, wall = 0.12f, r = 0.17f;
            const int balls = 120, figureTicks = 2 * SimConstants.TickRate, shrinkTicks = SimConstants.TickRate;
            var s = SimSettings.Default;
            s.Substeps = substeps;
            s.MaxSpeed = 25f;
            var w = new PhysicsWorld(s);
            var rng = Rng.FromSeed(1203UL, (ulong)substeps);
            Vec2[] verts = ShapeBuilder.RegularPolygon(12, radius);
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero));
            w.AddPolyline(body, verts, true, wall, SimTestUtil.Wall(restitution, 0.2f));
            w.ReserveBalls(balls);
            Vec2[] starts = LatticePoints(rng, verts, r, wall * 0.5f, balls);
            for (int i = 0; i < balls; i++) w.AddBall(SimTestUtil.Ball(starts[i], Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 5f, r, restitution, 0.2f));

            int outside = 0, firstTick = -1;
            float maxClamp = 0f, maxSpeedSeen = 0f;
            int sides = 12, figureStart = 0;
            for (int tick = 0; tick < 10 * figureTicks; tick++)
            {
                if (tick > 0 && tick % figureTicks == 0 && sides > 3)
                {
                    // Next figure: one side fewer, its inner face just outside the previous figure's outer vertices.
                    sides--;
                    float angle = w.GetBody(body).Angle;
                    w.SetBodyEnabled(body, false);
                    float scale0 = (radius + wall) / ShapeBuilder.Inradius(sides, radius);
                    verts = ShapeBuilder.RegularPolygon(sides, radius);
                    body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Angle = angle, Scale = scale0, ScaleVelocity = -(scale0 - 1f) });
                    w.AddPolyline(body, verts, true, wall, SimTestUtil.Wall(restitution, 0.2f));
                    figureStart = tick;
                }
                if (tick - figureStart == shrinkTicks)
                {
                    w.SetBodyScaleVelocity(body, 0f);
                    w.SetBodyScale(body, 1f);
                }
                float omega = MathF.Min(SimMath.TwoPi, 25f / (radius * w.GetBody(body).Scale));
                w.SetBodyAngularVelocity(body, omega);
                float clamp = w.EffectiveMaxSpeed;
                maxClamp = MathF.Max(maxClamp, clamp);
                w.Step();
                float sc = w.GetBody(body).Scale;
                for (int i = 0; i < balls; i++)
                {
                    ref readonly BallState b = ref w.GetBall(i);
                    if (!b.Alive) continue;
                    maxSpeedSeen = MathF.Max(maxSpeedSeen, b.Velocity.Length);
                    Assert.True(b.Velocity.Length <= clamp * 1.0001f, "speed " + b.Velocity.Length + " above the clamp " + clamp);
                    if (!TunnellingPropertyTests.PointInPolygon(verts, SimTestUtil.ToLocal(w, body, b.Position) / sc))
                    {
                        outside++;
                        if (firstTick < 0) firstTick = tick;
                        w.KillBall(i);
                    }
                }
            }
            _out.WriteLine("e " + restitution + ", " + substeps + " substeps: outside " + outside + "/" + balls + " (first at tick " + firstTick
                + "), clamp up to " + maxClamp + " m/s, fastest ball " + maxSpeedSeen + " m/s");
            Assert.Equal(3, sides);
            Assert.Equal(0, outside);
            // ω·R̂ ≤ 25 m/s, wall half-thickness, radial speed of the shrink ≤ ~1.06·4.26 m/s, + 5 m/s margin.
            Assert.InRange(maxClamp, 30f, 35f);
        }

        /// <summary>Joint corner projection is deterministic: a jammed triangle pile run twice gives identical hashes every tick.</summary>
        [Fact]
        public void DenseTrianglePileIsDeterministic()
        {
            PhysicsWorld Build()
            {
                var w = new PhysicsWorld(SimSettings.Default);
                Vec2[] verts = ShapeBuilder.RegularPolygon(3, 4.2f);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 200f * SimMath.DegToRad));
                w.AddPolyline(body, verts, true, 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
                var rng = Rng.FromSeed(99UL);
                Vec2[] starts = LatticePoints(rng, verts, 0.17f, 0.06f, 111);
                for (int i = 0; i < starts.Length; i++) w.AddBall(SimTestUtil.Ball(starts[i], Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 5f, 0.17f, 0.9f, 0.2f));
                return w;
            }
            var a = Build();
            var b = Build();
            for (int t = 0; t < 600; t++)
            {
                a.Step();
                b.Step();
                Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
            }
        }

        /// <summary>Up to <paramref name="count"/> hexagonal-lattice points (spacing 2r + 1 cm) inside the polygon with wall clearance, shuffled.</summary>
        internal static Vec2[] LatticePoints(Rng rng, Vec2[] verts, float r, float halfThickness, int count)
        {
            float extent = 0f;
            foreach (Vec2 v in verts) extent = MathF.Max(extent, v.Length);
            float step = 2f * r + 0.01f;
            float rowStep = step * 0.8660254f;
            var list = new System.Collections.Generic.List<Vec2>();
            int row = 0;
            for (float y = -extent; y <= extent; y += rowStep, row++)
            {
                for (float x = -extent + ((row & 1) != 0 ? step * 0.5f : 0f); x <= extent; x += step)
                {
                    var p = new Vec2(x, y);
                    if (!TunnellingPropertyTests.PointInPolygon(verts, p)) continue;
                    bool ok = true;
                    for (int k = 0; k < verts.Length && ok; k++)
                    {
                        Vec2 a = verts[k], b = verts[(k + 1) % verts.Length];
                        Vec2 d = b - a;
                        float u = SimMath.Clamp(Vec2.Dot(p - a, d) / d.LengthSquared, 0f, 1f);
                        if ((p - (a + d * u)).Length < r + halfThickness + 0.005f) ok = false;
                    }
                    if (ok) list.Add(p);
                }
            }
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            if (list.Count < count) throw new InvalidOperationException("only " + list.Count + " lattice points fit");
            return list.GetRange(0, count).ToArray();
        }
    }
}
