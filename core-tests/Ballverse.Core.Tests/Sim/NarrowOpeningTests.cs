using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Openings narrower than the ball (spec §6.5 "0 пролётов"; §2.9 M02 «Открытая грань»: a ball wider than the opening can
    /// never leave): two facing end caps, a ring gap, a wall end next to another wall's face. Before bridges (QA round 3) a ball
    /// pressed between two caps was linearised against two nearly opposite tangent half-planes and could be pushed out on the
    /// far side; ring outer faces also missed chords that dip into the band and leave it within one substep.
    /// </summary>
    public class NarrowOpeningTests
    {
        readonly ITestOutputHelper _out;

        public NarrowOpeningTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>
        /// QA's deterministic repro: a dividing wall of two segments with an opening of free width 0.242 m, a 0.271 m ball at
        /// up to 27 m/s (restitution 1.18) inside a closed static box, no gravity, 6 substeps. It crossed at tick 52.
        /// </summary>
        [Fact]
        public void BallNeverSqueezesThroughOpeningBetweenTwoCaps()
        {
            var s = SimTestUtil.NoGravity(6, 60f);
            var w = new PhysicsWorld(s);
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            var m = SimTestUtil.Wall(1.1784506f, 0.10684678f);
            const float t = 0.043573588f, halfGap = 0.14263935f, L = 1.5f, r = 0.13569415f;
            w.AddSegment(body, new Vec2(-L, 0f), new Vec2(-halfGap, 0f), t, m);
            w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(L, 0f), t, m);
            w.AddPolyline(body, ShapeBuilder.Rectangle(2f * L, 2f * L), true, t, m);
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(-0.25511128f, -0.86925656f), new Vec2(6.0268054f, 15.776816f), r, m.Restitution, m.Friction));
            Assert.True(2f * halfGap - t < 2f * r);
            int capHits = 0;
            for (int tick = 0; tick < 600; tick++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind == SimEventKind.Hit && e.B <= 1 && MathF.Abs(e.Point.X) < halfGap + t) capHits++;
                }
                Assert.True(w.GetBall(b).Position.Y < 0f, "ball crossed the wall at tick " + tick + ": " + w.GetBall(b).Position);
            }
            Assert.True(capHits > 0, "the ball never reached the opening");
        }

        sealed class GhostHook : ISimContactHook
        {
            public int Ghost;
            public int Asked;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                Asked++;
                return collider != Ghost;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact) => true;
        }

        /// <summary>
        /// A bridge hit is a contact like any other for the mode hook: when the hook lets the ball pass one side of a closed
        /// opening (ghost wall), the ball goes through; when it accepts both, the ball is held and bounces back.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ContactHookDecidesWhetherAClosedOpeningHolds(bool ghost)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(8, 60f));
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            const float t = 0.05f, r = 0.15f, halfGap = 0.12f;
            w.AddSegment(body, new Vec2(-2f, 0f), new Vec2(-halfGap, 0f), t, SimTestUtil.Wall(1f, 0f));
            int right = w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(2f, 0f), t, SimTestUtil.Wall(1f, 0f));
            var hook = new GhostHook { Ghost = ghost ? right : -1 };
            w.ContactHook = hook;
            // Off-centre: the path misses the left cap (0.18 m > r + t/2 from its centre) but crosses the bridge and the right cap.
            // The ball starts touching the right cap, so the sweep resolves that cap discretely and the bridge decides.
            float rt = r + t * 0.5f;
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.06f, -MathF.Sqrt(rt * rt - 0.06f * 0.06f)), new Vec2(0f, 3f), r, 1f, 0f));
            for (int tick = 0; tick < SimConstants.TickRate; tick++) w.Step();
            Assert.True(hook.Asked > 0);
            if (ghost) Assert.True(w.GetBall(b).Position.Y > 2f && MathF.Abs(w.GetBall(b).Position.X - 0.06f) < 1e-3f, "ghost wall held the ball: " + w.GetBall(b).Position);
            else Assert.True(w.GetBall(b).Position.Y < -0.2f && w.GetBall(b).Velocity.Y < 0f, "opening did not hold: " + w.GetBall(b).Position);
        }

        /// <summary>
        /// Property: two collinear capsules leaving an opening narrower than the ball (free width 30–95 % of the diameter),
        /// balls on both sides aimed at the opening, static or rotating walls, ball speeds up to 20 or 60 m/s, 4–8 substeps,
        /// a third of the scenes with gravity. No ball centre may change side.
        /// </summary>
        [Theory]
        [InlineData(20f, false)]
        [InlineData(60f, false)]
        [InlineData(60f, true)]
        public void NarrowOpeningBetweenTwoCapsNeverLetsBallsThrough(float vmax, bool rotate)
        {
            var rng = Rng.FromSeed(777UL, rotate ? 2UL : 1UL);
            const int scenes = 250, balls = 20;
            long hits = 0;
            for (int scene = 0; scene < scenes; scene++)
            {
                int substeps = new[] { 4, 6, 8 }[rng.NextInt(0, 3)];
                float t = rng.NextFloat(0.04f, 0.12f);
                float r = rng.NextFloat(0.05f, 0.2f);
                float free = 2f * r * rng.NextFloat(0.3f, 0.95f);
                float halfGap = (free + t) * 0.5f;
                var s = SimTestUtil.NoGravity(substeps, 60f);
                if (scene % 3 == 0) s.Gravity = new Vec2(0f, -SimSettings.StandardGravity);
                var w = new PhysicsWorld(s);
                float omega = rotate ? rng.NextFloat(-3f, 3f) : 0f;
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, omega));
                var m = SimTestUtil.Wall(rng.NextFloat(0.8f, 1.2f), rng.NextFloat(0f, 0.5f));
                const float L = 1.5f;
                w.AddSegment(body, new Vec2(-L, 0f), new Vec2(-halfGap, 0f), t, m);
                w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(L, 0f), t, m);
                w.AddPolyline(body, ShapeBuilder.Rectangle(2f * L, 2f * L), true, t, m);
                var above = new bool[balls];
                for (int i = 0; i < balls; i++)
                {
                    above[i] = i % 2 == 0;
                    float y = rng.NextFloat(t + r + 0.05f, L - t - r - 0.05f) * (above[i] ? 1f : -1f);
                    var p = new Vec2(rng.NextFloat(-0.5f, 0.5f), y);
                    Vec2 to = new Vec2(rng.NextFloat(-halfGap, halfGap), 0f) - p;
                    Vec2 dir = i % 4 == 0 ? Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) : to.Normalized;
                    var d = SimTestUtil.Ball(p, dir * rng.NextFloat(2f, vmax), r, m.Restitution, m.Friction);
                    d.Category = 2u;
                    d.Mask = 1u;
                    w.AddBall(d);
                }
                for (int tick = 0; tick < 2 * SimConstants.TickRate; tick++)
                {
                    w.Step();
                    hits += w.EventCount;
                    for (int i = 0; i < balls; i++)
                    {
                        Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position);
                        Assert.True(above[i] ? local.Y > 0f : local.Y < 0f, "scene " + scene + " (substeps " + substeps + ", t " + t + ", r " + r
                            + ", free " + free + ", ω " + omega + "): ball " + i + " crossed at tick " + tick + ", local " + local);
                    }
                }
            }
            _out.WriteLine(scenes * balls + " balls, " + hits + " events, 0 crossed");
        }

        /// <summary>
        /// Property: rings whose gaps (1–4) are narrower than the ball, static, rotating up to the 25 m/s envelope or pulsing
        /// radially at 5 m/s, balls inside and outside at up to 60 m/s inside a static gapless container. A ball may never change
        /// side; at a gap angle the side is decided by the chord between the gap's edge caps (the bridge), elsewhere by the
        /// ring's centre line.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void RingGapNarrowerThanBallNeverLetsBallsThrough(int motion)
        {
            var rng = Rng.FromSeed(4242UL, 3UL + (ulong)motion);
            const int scenes = 150, balls = 30;
            long hits = 0;
            int built = 0;
            for (int scene = 0; scene < scenes; scene++)
            {
                int substeps = new[] { 4, 6, 8 }[rng.NextInt(0, 3)];
                float R = rng.NextFloat(0.6f, 4f);
                float t = rng.NextFloat(0.04f, 0.12f);
                float r = rng.NextFloat(0.05f, 0.2f);
                float omega = motion == 1 ? rng.NextFloat(-1f, 1f) * MathF.Min(SimMath.TwoPi, 25f / (R + t * 0.5f)) : 0f;
                int gapCount = rng.NextInt(1, 5);
                var gaps = new AngularGap[gapCount];
                for (int g = 0; g < gapCount; g++)
                {
                    float free = 2f * r * rng.NextFloat(0.3f, 0.95f);
                    float width = 2f * MathF.Asin(MathF.Min(0.99f, (free + t) / (2f * R)));
                    gaps[g] = AngularGap.Centered(g * SimMath.TwoPi / gapCount + rng.NextFloat(-0.2f, 0.2f), width);
                }
                var s = SimTestUtil.NoGravity(substeps, 60f);
                if (scene % 3 == 0) s.Gravity = new Vec2(0f, -SimSettings.StandardGravity);
                var w = new PhysicsWorld(s);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, rng.NextFloat(0f, SimMath.TwoPi), omega));
                var m = SimTestUtil.Wall(rng.NextFloat(0.8f, 1.2f), rng.NextFloat(0f, 0.5f));
                int ring = w.AddRing(body, Vec2.Zero, R, t, gaps, m);
                built++;
                int box = w.AddBody(BodyDef.Static(Vec2.Zero));
                float outer = R + 4f * r + t + 0.3f;
                w.AddRing(box, Vec2.Zero, outer, 0.05f, ReadOnlySpan<AngularGap>.Empty, m);
                var info = w.GetColliderInfo(ring);
                var normalized = new AngularGap[info.GapCount];
                for (int g = 0; g < normalized.Length; g++) normalized[g] = w.GetGap(ring, g);
                var inside = new bool[balls];
                for (int i = 0; i < balls; i++)
                {
                    inside[i] = i % 2 == 0;
                    float rad = inside[i]
                        ? rng.NextFloat(0f, MathF.Max(0.01f, R * 0.9f - t - r - 0.05f))
                        : rng.NextFloat(R + t + r + 0.02f, outer - 0.05f - r - 0.02f);
                    float speed = i % 3 == 0 ? 60f : rng.NextFloat(5f, 60f);
                    var d = SimTestUtil.Ball(Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * rad,
                        Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * speed, r, m.Restitution, m.Friction);
                    d.Category = 2u;
                    d.Mask = 1u;
                    w.AddBall(d);
                }
                for (int tick = 0; tick < 2 * SimConstants.TickRate; tick++)
                {
                    if (motion == 2)
                    {
                        float sc = w.GetBody(body).Scale;
                        if (sc <= 0.85f) w.SetBodyScaleVelocity(body, 5f / R);
                        else if (sc >= 1f) w.SetBodyScaleVelocity(body, -5f / R);
                    }
                    w.Step();
                    hits += w.EventCount;
                    float scale = w.GetBody(body).Scale;
                    for (int i = 0; i < balls; i++)
                    {
                        Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position) / scale;
                        Assert.True(InsideGappedRing(local, R, normalized) == inside[i], "scene " + scene + " (substeps " + substeps + ", R " + R
                            + ", t " + t + ", r " + r + ", ω " + omega + ", gaps " + gapCount + "): ball " + i + " changed side at tick " + tick
                            + ", |p| " + local.Length);
                    }
                }
            }
            _out.WriteLine(built + " rings, " + built * balls + " balls, " + hits + " events, 0 crossed");
        }

        /// <summary>
        /// Inside the region bounded by the solid arcs (radius R) and, across each gap, the chord between its edge-cap centres.
        /// </summary>
        internal static bool InsideGappedRing(Vec2 local, float R, AngularGap[] gaps)
        {
            float theta = MathF.Atan2(local.Y, local.X);
            foreach (AngularGap g in gaps)
            {
                float span = g.Span;
                if (!SimMath.AngleInArc(theta, g.Start, span)) continue;
                Vec2 mid = Vec2.FromAngle(g.Start + span * 0.5f);
                return Vec2.Dot(local, mid) < R * MathF.Cos(span * 0.5f);
            }
            return local.Length < R;
        }

        /// <summary>
        /// Property: a box split by a wall whose top cap leaves a neck narrower than the ball below the box's top face (a
        /// cap–face bridge between different colliders of one body); a crowd of balls with ball–ball contacts is pushed into the
        /// neck by gravity of 0.5–3 g while the box rotates. No ball may reach the other half.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CrowdPushedIntoNeckBetweenCapAndFaceStaysOnItsSide(bool segmentBox)
        {
            var rng = Rng.FromSeed(99UL, segmentBox ? 12UL : 2UL);
            const int scenes = 40, balls = 30;
            const float L = 1.5f;
            for (int scene = 0; scene < scenes; scene++)
            {
                int substeps = new[] { 4, 6, 8 }[rng.NextInt(0, 3)];
                float t = rng.NextFloat(0.04f, 0.12f);
                float r = rng.NextFloat(0.05f, 0.2f);
                float free = 2f * r * rng.NextFloat(0.3f, 0.95f);
                float top = L - t - free;
                var s = SimSettings.Default;
                s.Substeps = substeps;
                s.MaxSpeed = 60f;
                s.Gravity = new Vec2(SimSettings.StandardGravity * rng.NextFloat(-1f, 1f), SimSettings.StandardGravity * rng.NextFloat(0.5f, 3f));
                var w = new PhysicsWorld(s);
                float omega = rng.NextFloat(-3f, 3f);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, omega));
                var m = SimTestUtil.Wall(rng.NextFloat(0.8f, 1.2f), rng.NextFloat(0f, 0.5f));
                w.AddSegment(body, new Vec2(0f, -L), new Vec2(0f, top), t, m);
                Vec2[] boxVerts = ShapeBuilder.Rectangle(2f * L, 2f * L);
                if (segmentBox)
                {
                    for (int k = 0; k < 4; k++) w.AddSegment(body, boxVerts[k], boxVerts[(k + 1) % 4], t, m);
                }
                else
                {
                    w.AddPolyline(body, boxVerts, true, t, m);
                }
                for (int i = 0; i < balls; i++)
                {
                    var p = new Vec2(-rng.NextFloat(t + r + 0.05f, L - t - r - 0.05f), rng.NextFloat(-0.5f, L - t - r - 0.05f));
                    Vec2 to = new Vec2(0f, rng.NextFloat(top, L)) - p;
                    w.AddBall(SimTestUtil.Ball(p, to.Normalized * rng.NextFloat(2f, 15f), r, m.Restitution, m.Friction));
                }
                for (int tick = 0; tick < 10 * SimConstants.TickRate; tick++)
                {
                    w.Step();
                    for (int i = 0; i < balls; i++)
                    {
                        Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position);
                        Assert.True(local.X < 0f, "scene " + scene + " (substeps " + substeps + ", t " + t + ", r " + r + ", free " + free + ", ω " + omega
                            + "): ball " + i + " passed the neck at tick " + tick + ", local " + local);
                    }
                }
            }
        }

        /// <summary>
        /// QA's spec-legal M02 «Открытая грань» crowd: dodecagon R 2.0 m, wall 0.05 m, openWidth 30 % (free width 0.261 m), 80
        /// balls of 0.3 m (φ 0.48), 1 g, 200–360 °/s, e 1.0–1.1, maxSpeed 30. Before the fix 103 of 800 balls escaped in 10 × 60 s.
        /// </summary>
        [Theory]
        [InlineData(200f, 1.0f)]
        [InlineData(360f, 1.1f)]
        public void OpenFaceNarrowerThanBallsHoldsTheCrowd(float degreesPerSecond, float restitution)
        {
            const int sides = 12, balls = 80;
            const float R = 2f, t = 0.05f, open = 0.3f, r = 0.15f;
            Vec2[] v = ShapeBuilder.RegularPolygon(sides, R);
            Vec2 a = v[sides - 1], b = v[0];
            Assert.True(open * (b - a).Length - t < 2f * r);
            var pts = new List<Vec2> { Vec2.Lerp(a, b, 0.5f + open * 0.5f) };
            for (int k = 0; k < sides; k++) pts.Add(v[k]);
            pts.Add(Vec2.Lerp(a, b, 0.5f - open * 0.5f));
            long hits = 0;
            for (int seed = 0; seed < 3; seed++)
            {
                var s = SimSettings.Default;
                s.MaxSpeed = 30f;
                var w = new PhysicsWorld(s);
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, seed * 0.5f, degreesPerSecond * SimMath.DegToRad * (seed % 2 == 0 ? 1 : -1)));
                var m = SimTestUtil.Wall(restitution, 0.2f);
                w.AddPolyline(body, pts.ToArray(), false, t, m);
                var rng = Rng.FromSeed((ulong)seed, 6);
                w.ReserveBalls(balls);
                for (int i = 0; i < balls; i++)
                {
                    Vec2 p;
                    do
                    {
                        p = new Vec2(rng.NextFloat(-1.6f, 1.6f), rng.NextFloat(-1.6f, 1.6f));
                    } while (p.Length > 1.6f);
                    w.AddBall(SimTestUtil.Ball(p, Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 10f, r, restitution, 0.2f));
                }
                for (int tick = 0; tick < 20 * SimConstants.TickRate; tick++)
                {
                    w.Step();
                    hits += w.EventCount;
                    for (int i = 0; i < balls; i++)
                    {
                        Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(i).Position);
                        Assert.True(TunnellingPropertyTests.PointInPolygon(v, local), "seed " + seed + ": ball " + i + " left through the opening at tick " + tick
                            + ", local " + local);
                    }
                }
            }
            _out.WriteLine(3 * balls + " balls × 20 s, " + hits + " events, 0 escaped");
        }

        static PhysicsWorld OpenFaceCrowd(int balls, ulong seed)
        {
            const int sides = 12;
            Vec2[] v = ShapeBuilder.RegularPolygon(sides, 2f);
            Vec2 a = v[sides - 1], b = v[0];
            var pts = new List<Vec2> { Vec2.Lerp(a, b, 0.65f) };
            for (int k = 0; k < sides; k++) pts.Add(v[k]);
            pts.Add(Vec2.Lerp(a, b, 0.35f));
            var s = SimSettings.Default;
            s.MaxSpeed = 30f;
            var w = new PhysicsWorld(s);
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 300f * SimMath.DegToRad));
            w.AddPolyline(body, pts.ToArray(), false, 0.05f, SimTestUtil.Wall(1.05f, 0.2f));
            // Ring with narrow gaps and a disc peg on the same body: every bridge kind (cap–cap, cap–face, cap–disc).
            w.AddRing(body, Vec2.Zero, 0.8f, 0.05f, new[] { AngularGap.Centered(0f, 0.25f), AngularGap.Centered(3f, 0.25f) }, SimTestUtil.Wall(1.05f, 0.2f));
            w.AddCircle(body, new Vec2(0.8f, 0.45f), 0.1f, SimTestUtil.Wall(1.05f, 0.2f));
            var rng = Rng.FromSeed(seed, 9);
            w.ReserveBalls(balls);
            for (int i = 0; i < balls; i++)
            {
                Vec2 p;
                do
                {
                    p = new Vec2(rng.NextFloat(-1.6f, 1.6f), rng.NextFloat(-1.6f, 1.6f));
                } while (p.Length > 1.6f || MathF.Abs(p.Length - 0.8f) < 0.25f);
                w.AddBall(SimTestUtil.Ball(p, Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * 10f, 0.15f, 1.05f, 0.2f));
            }
            return w;
        }

        /// <summary>Bridges and repeated overlap gathering keep the solver deterministic: two runs, identical hashes every tick.</summary>
        [Fact]
        public void ScenesWithBridgesAreDeterministic()
        {
            var w1 = OpenFaceCrowd(60, 5UL);
            var w2 = OpenFaceCrowd(60, 5UL);
            for (int tick = 0; tick < 10 * SimConstants.TickRate; tick++)
            {
                w1.Step();
                w2.Step();
                Assert.Equal(w1.ComputeStateHash(), w2.ComputeStateHash());
            }
            _out.WriteLine("hash@600 = 0x" + w1.ComputeStateHash().ToString("X16"));
        }

        /// <summary>The bridge scratch buffers grow once; steady-state steps of a cap-heavy crowd allocate nothing.</summary>
        [Fact]
        public void ScenesWithBridgesDoNotAllocateInSteadyState()
        {
            var w = OpenFaceCrowd(60, 7UL);
            for (int tick = 0; tick < 3 * SimConstants.TickRate; tick++) w.Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 5 * SimConstants.TickRate; tick++) w.Step();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0L, allocated);
        }

        /// <summary>
        /// A ball outside a gapless ring (R 0.6 m, thickness 0.12 m, ball Ø0.35 m — the M01 minimums) at 60 m/s along a chord
        /// that enters the outer band only for part of one substep: the contact must be found (Hit event, deflection) even
        /// though the substep ends outside the band again.
        /// </summary>
        [Theory]
        [InlineData(8, 0f)]
        [InlineData(8, 0.5f)]
        [InlineData(4, 0f)]
        [InlineData(4, 0.75f)]
        public void ChordGrazingRingOuterFaceWithinOneSubstepIsAContact(int substeps, float phase)
        {
            const float R = 0.6f, t = 0.12f, r = 0.175f, speed = 60f;
            float rho = R + t * 0.5f + r;
            var w = new PhysicsWorld(SimTestUtil.NoGravity(substeps, 60f));
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddRing(body, Vec2.Zero, R, t, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall(1f, 0f));
            float step = speed / (SimConstants.TickRate * substeps);
            // The chord inside the band is 0.6 substep long and centred between substep ends.
            float half = 0.3f * step;
            float y = MathF.Sqrt(rho * rho - half * half);
            float x0 = -half - step * (0.2f + 0.1f * phase) - step * 10f;
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(x0, y), new Vec2(speed, 0f), r, 1f, 0f));
            int hits = 0;
            for (int tick = 0; tick < 4; tick++)
            {
                w.Step();
                foreach (var e in w.Events) if (e.Kind == SimEventKind.Hit && e.A == b) hits++;
            }
            Vec2 vel = w.GetBall(b).Velocity;
            Assert.True(hits >= 1, "graze missed: no Hit event, velocity " + vel);
            Assert.True(vel.Y > 0.5f, "graze missed: velocity " + vel);
            Assert.Equal(speed, vel.Length, 2);
        }
    }
}
