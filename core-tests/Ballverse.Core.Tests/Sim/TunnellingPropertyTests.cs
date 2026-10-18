using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Anti-tunnelling across colliders (spec §6.5: "0 пролётов во всём конверте при радиусе шара ≥ 0,05 м и толщине стенки
    /// ≥ 0,04 м", substeps 4–16) — closely spaced rings and polygons made of separate colliders, where a ball bouncing off
    /// one collider must still be swept against the others — plus the randomized 10 000-throw property test.
    /// </summary>
    public class TunnellingPropertyTests
    {
        const float EnvelopeBallSpeed = 60f;
        const float EnvelopeWallSurfaceSpeed = 25f;
        const float EnvelopeAngularSpeed = SimMath.TwoPi;
        const float EnvelopeRadialSpeed = 5f;

        readonly ITestOutputHelper _out;

        public TunnellingPropertyTests(ITestOutputHelper output)
        {
            _out = output;
        }

        static SimSettings Envelope(int substeps, bool gravity = false)
        {
            var s = SimTestUtil.NoGravity(substeps, EnvelopeBallSpeed);
            if (gravity) s.Gravity = new Vec2(0f, -SimSettings.StandardGravity);
            return s;
        }

        /// <summary>
        /// Two gapless counter-rotating rings at the minimum Ring Escape spacing (ballSize + ringThickness + 0.05 m), each on its
        /// own body, balls at 60 m/s between them; optionally the inner ring "breathes" radially at the 5 m/s envelope limit.
        /// Before the all-collider sweep, a ball bouncing off the outer ring could end past the inner ring's centre line.
        /// </summary>
        [Theory]
        [InlineData(4, 0f)]
        [InlineData(6, 0f)]
        [InlineData(8, 0f)]
        [InlineData(8, 5f)]
        [InlineData(4, 5f)]
        public void BallsBetweenTwoGaplessRingsAtMinimumSpacingNeverCrossEither(int substeps, float radialSpeed)
        {
            const float r = 0.05f, t = 0.04f, r0 = 1.2f;
            const float spacing = 2f * r + t + 0.05f;
            var w = new PhysicsWorld(Envelope(substeps));
            int inner = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 60f * SimMath.DegToRad));
            int outer = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, -60f * SimMath.DegToRad));
            w.AddRing(inner, Vec2.Zero, r0, t, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1f, 0f));
            w.AddRing(outer, Vec2.Zero, r0 + spacing, t, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1f, 0f));
            var rng = Rng.FromSeed(31UL + (ulong)substeps, (ulong)radialSpeed);
            const int balls = 40;
            for (int i = 0; i < balls; i++)
            {
                var d = SimTestUtil.Ball(Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * (r0 + spacing * 0.5f),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * EnvelopeBallSpeed, r, 1f, 0f);
                d.Category = 2u;
                d.Mask = 1u;
                w.AddBall(d);
            }
            long hits = 0;
            for (int tick = 0; tick < 30 * SimConstants.TickRate; tick++)
            {
                if (radialSpeed > 0f)
                {
                    float sc = w.GetBody(inner).Scale;
                    if (sc <= 0.9f) w.SetBodyScaleVelocity(inner, radialSpeed / r0);
                    else if (sc >= 1f) w.SetBodyScaleVelocity(inner, -radialSpeed / r0);
                }
                w.Step();
                hits += w.EventCount;
                float innerR = r0 * w.GetBody(inner).Scale;
                for (int i = 0; i < balls; i++)
                {
                    float d = w.GetBall(i).Position.Length;
                    Assert.True(d > innerR && d < r0 + spacing,
                        "ball " + i + " crossed a ring at tick " + tick + ": |p| = " + d + ", rings at " + innerR + " / " + (r0 + spacing));
                }
            }
            Assert.True(hits > 20000, "hits " + hits);
        }

        /// <summary>
        /// Polygons built from separate Segment colliders (every corner shared by two colliders) at 4 substeps, balls at 60 m/s
        /// aimed at the corners. Before the all-collider sweep, a ball bouncing off the higher-id side of a corner was never
        /// swept against the lower-id side again and could end beyond it.
        /// </summary>
        [Theory]
        [InlineData(3, 0f)]
        [InlineData(3, 341f)]
        [InlineData(4, 0f)]
        [InlineData(6, 200f)]
        public void PolygonMadeOfSeparateSegmentsHoldsBallsAtFourSubsteps(int sides, float degreesPerSecond)
        {
            const float r = 0.05f, t = 0.04f, radius = 2f;
            var w = new PhysicsWorld(Envelope(4));
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, degreesPerSecond * SimMath.DegToRad));
            Vec2[] verts = ShapeBuilder.RegularPolygon(sides, radius);
            for (int k = 0; k < sides; k++) w.AddSegment(body, verts[k], verts[(k + 1) % sides], t, SimTestUtil.Wall(1f, 0.3f));
            var rng = Rng.FromSeed(77UL + (ulong)sides, (ulong)degreesPerSecond);
            const int balls = 200;
            w.ReserveBalls(balls);
            for (int i = 0; i < balls; i++)
            {
                Vec2 p = RandomPointInside(rng, verts, r + t + 0.05f);
                Vec2 to = verts[rng.NextInt(0, sides)] - p;
                float a = MathF.Atan2(to.Y, to.X) + rng.NextFloat(-0.02f, 0.02f);
                var d = SimTestUtil.Ball(p, new Vec2(MathF.Cos(a), MathF.Sin(a)) * EnvelopeBallSpeed, r, 1f, 0.3f);
                d.Category = 2u;
                d.Mask = 1u;
                w.AddBall(d);
            }
            for (int tick = 0; tick < 10 * SimConstants.TickRate; tick++)
            {
                w.Step();
                for (int i = 0; i < balls; i++)
                {
                    Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position);
                    Assert.True(PointInPolygon(verts, local), "ball " + i + " left the polygon at tick " + tick + ", local " + local);
                }
            }
        }

        enum Arena
        {
            Polygon,
            Star,
            SegmentPolygon,
            Ring,
            DoubleRing,
        }

        /// <summary>
        /// Spec §6.5 property test: 10 000 random throws over the whole velocity envelope — balls up to 60 m/s (half of them at
        /// exactly 60, half of those aimed at a corner), walls up to 360°/s with surface speed up to 25 m/s, radial motion up
        /// to 5 m/s, ball radius ≥ 0.05 m, wall thickness ≥ 0.04 m, 4–8 substeps, closed polygons, stars, polygons of separate
        /// segments, gapless rings and ring pairs at the minimum spacing; one scene in four at the worst corner of the
        /// envelope, one in four with gravity. No ball centre may ever cross a wall's centre line.
        /// </summary>
        [Fact]
        public void TenThousandRandomThrowsAcrossTheEnvelopeNeverTunnel()
        {
            const int scenes = 200;
            const int ballsPerScene = 50;
            const int ticks = 2 * SimConstants.TickRate;
            var rng = Rng.FromSeed(6_5_2026UL, 1);
            int throws = 0;
            long hits = 0;
            for (int scene = 0; scene < scenes; scene++)
            {
                hits += RunEnvelopeScene(rng, scene, ballsPerScene, ticks);
                throws += ballsPerScene;
            }
            _out.WriteLine(throws + " throws, " + hits + " wall hits, 0 tunnelled");
            Assert.Equal(10000, throws);
            Assert.True(hits > 100000, "hits " + hits);
        }

        long RunEnvelopeScene(Rng rng, int scene, int balls, int ticks)
        {
            var arena = (Arena)(scene % 5);
            bool worst = scene % 4 == 0;
            bool gravity = scene % 4 == 1;
            int[] substepChoices = { 4, 6, 8 };
            int substeps = worst ? 4 : substepChoices[rng.NextInt(0, substepChoices.Length)];
            float r = worst ? 0.05f : rng.NextFloat(0.05f, 0.2f);
            float t = worst ? 0.04f : rng.NextFloat(0.04f, 0.12f);
            float radius = rng.NextFloat(2f, 4f);
            float omegaMax = MathF.Min(EnvelopeAngularSpeed, EnvelopeWallSurfaceSpeed / (radius * 1.1f));
            float omega = (worst ? omegaMax : rng.NextFloat(0f, omegaMax)) * (rng.NextInt(0, 2) == 0 ? 1f : -1f);
            float radial = worst ? EnvelopeRadialSpeed : (rng.NextInt(0, 2) == 0 ? 0f : rng.NextFloat(0f, EnvelopeRadialSpeed));
            float restitution = rng.NextFloat(0.8f, 1.2f);
            float friction = rng.NextFloat(0f, 0.5f);

            var w = new PhysicsWorld(Envelope(substeps, gravity));
            var wall = SimTestUtil.Wall(restitution, friction);
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, omega));
            Vec2[] verts = null;
            float innerR = 0f, outerR = 0f;
            switch (arena)
            {
                case Arena.Polygon:
                    verts = ShapeBuilder.RegularPolygon(rng.NextInt(3, 9), radius);
                    w.AddPolyline(body, verts, true, t, wall);
                    break;
                case Arena.Star:
                    verts = ShapeBuilder.Star(rng.NextInt(4, 9), radius, radius * rng.NextFloat(0.5f, 0.75f));
                    w.AddPolyline(body, verts, true, t, wall);
                    break;
                case Arena.SegmentPolygon:
                    verts = ShapeBuilder.RegularPolygon(rng.NextInt(3, 7), radius);
                    for (int k = 0; k < verts.Length; k++) w.AddSegment(body, verts[k], verts[(k + 1) % verts.Length], t, wall);
                    break;
                case Arena.Ring:
                    outerR = radius;
                    w.AddRing(body, Vec2.Zero, radius, t, ReadOnlySpan<AngularGap>.Empty, wall);
                    break;
                case Arena.DoubleRing:
                    innerR = radius * 0.6f;
                    outerR = innerR + 2f * r + t + 0.05f;
                    w.AddRing(body, Vec2.Zero, innerR, t, ReadOnlySpan<AngularGap>.Empty, wall);
                    int outerBody = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, -omega));
                    w.AddRing(outerBody, Vec2.Zero, outerR, t, ReadOnlySpan<AngularGap>.Empty, wall);
                    break;
            }

            w.ReserveBalls(balls);
            for (int i = 0; i < balls; i++)
            {
                Vec2 p;
                if (arena == Arena.DoubleRing) p = Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * ((innerR + outerR) * 0.5f);
                else if (arena == Arena.Ring) p = Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * rng.NextFloat(0f, radius * 0.7f);
                else p = RandomPointInside(rng, verts, r + t + 0.05f);
                float speed = i % 2 == 0 ? EnvelopeBallSpeed : rng.NextFloat(5f, EnvelopeBallSpeed);
                Vec2 dir = verts != null && i % 4 == 0 ? AimedOrRandomDirection(rng, p, verts) : Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi));
                var d = SimTestUtil.Ball(p, dir * speed, r, restitution, friction);
                d.Category = 2u;
                d.Mask = 1u;
                w.AddBall(d);
            }

            // Radial motion: the outer wall moves at `radial` m/s, the scale bouncing between 0.8 and 1.0 (double ring: the
            // inner ring between 0.85 and 1.0, so the gap never drops below the minimum spacing).
            int pulsing = body;
            float pulseRadius = arena == Arena.DoubleRing ? innerR : radius;
            float minScale = arena == Arena.DoubleRing ? 0.85f : 0.8f;
            long hits = 0;
            for (int tick = 0; tick < ticks; tick++)
            {
                if (radial > 0f)
                {
                    float sc = w.GetBody(pulsing).Scale;
                    if (sc <= minScale) w.SetBodyScaleVelocity(pulsing, radial / pulseRadius);
                    else if (sc >= 1f) w.SetBodyScaleVelocity(pulsing, -radial / pulseRadius);
                }
                w.Step();
                hits += w.EventCount;
                float scale = w.GetBody(body).Scale;
                for (int i = 0; i < balls; i++)
                {
                    Vec2 pos = w.GetBall(i).Position;
                    bool ok;
                    if (arena == Arena.Ring)
                    {
                        ok = pos.Length < radius * scale;
                    }
                    else if (arena == Arena.DoubleRing)
                    {
                        float d = pos.Length;
                        ok = d > innerR * scale && d < outerR;
                    }
                    else
                    {
                        ok = PointInPolygon(verts, SimTestUtil.ToLocal(w, body, pos) / scale);
                    }
                    if (!ok)
                    {
                        Assert.Fail("scene " + scene + " (" + arena + ", substeps " + substeps + ", r " + r + ", t " + t + ", R " + radius + ", ω "
                            + omega + ", radial " + radial + ", gravity " + gravity + "): ball " + i + " crossed the wall at tick " + tick
                            + ", position " + pos);
                    }
                }
            }
            return hits;
        }

        static Vec2 RandomPointInside(Rng rng, Vec2[] verts, float clearance)
        {
            float extent = 0f;
            foreach (Vec2 v in verts) extent = MathF.Max(extent, v.Length);
            while (true)
            {
                var p = new Vec2(rng.NextFloat(-extent, extent), rng.NextFloat(-extent, extent));
                if (PointInPolygon(verts, p) && DistanceToPolygon(verts, p) > clearance) return p;
            }
        }

        /// <summary>Half the time straight at a random vertex (corner hits), otherwise a random direction.</summary>
        static Vec2 AimedOrRandomDirection(Rng rng, Vec2 p, Vec2[] verts)
        {
            if (rng.NextInt(0, 2) == 0)
            {
                Vec2 to = verts[rng.NextInt(0, verts.Length)] - p;
                float a = MathF.Atan2(to.Y, to.X) + rng.NextFloat(-0.01f, 0.01f);
                return new Vec2(MathF.Cos(a), MathF.Sin(a));
            }
            float b = rng.NextFloat(0f, SimMath.TwoPi);
            return new Vec2(MathF.Cos(b), MathF.Sin(b));
        }

        internal static bool PointInPolygon(Vec2[] v, Vec2 q)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                if ((v[i].Y > q.Y) != (v[j].Y > q.Y))
                {
                    float x = v[j].X + (q.Y - v[j].Y) * (v[i].X - v[j].X) / (v[i].Y - v[j].Y);
                    if (q.X < x) inside = !inside;
                }
            }
            return inside;
        }

        static float DistanceToPolygon(Vec2[] v, Vec2 q)
        {
            float best = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                Vec2 a = v[i], b = v[(i + 1) % v.Length];
                Vec2 d = b - a;
                float u = SimMath.Clamp(Vec2.Dot(q - a, d) / d.LengthSquared, 0f, 1f);
                best = MathF.Min(best, (q - (a + d * u)).Length);
            }
            return best;
        }
    }
}
