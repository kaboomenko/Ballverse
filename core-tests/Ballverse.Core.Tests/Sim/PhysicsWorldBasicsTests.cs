using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    public class PhysicsWorldBasicsTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(7f, 7f - SimMath.TwoPi)]
        [InlineData(-7f, -7f + SimMath.TwoPi)]
        [InlineData(SimMath.Pi, SimMath.Pi)]
        [InlineData(-SimMath.Pi, SimMath.Pi)]
        public void WrapPiMapsIntoHalfOpenRange(float input, float expected)
        {
            Assert.Equal(expected, SimMath.WrapPi(input), 4);
        }

        [Fact]
        public void Wrap2PiStaysInRangeForLargeAndTinyInputs()
        {
            foreach (float a in new[] { -1e-9f, 1131f, -1131f, SimMath.TwoPi, 0f, 3f })
            {
                float r = SimMath.Wrap2Pi(a);
                Assert.InRange(r, 0f, SimMath.TwoPi);
                Assert.True(r < SimMath.TwoPi);
            }
        }

        [Fact]
        public void GravityFromSpecParameters()
        {
            Vec2 g = SimSettings.GravityFrom(1f, 270f);
            Assert.Equal(0f, g.X, 4);
            Assert.Equal(-9.81f, g.Y, 4);
        }

        [Fact]
        public void FreeFallMatchesSemiImplicitEulerOverOneSecond()
        {
            var w = new PhysicsWorld();
            int b = w.AddBall(BallDef.Create(new Vec2(0f, 10f), 0.2f));
            for (int i = 0; i < 60; i++) w.Step();
            ref readonly BallState s = ref w.GetBall(b);
            Assert.Equal(-9.81f, s.Velocity.Y, 3);
            // Semi-implicit Euler with 480 substeps over 1 s: y = 10 − g·(1 + 1/480)/2.
            Assert.Equal(10f - 9.81f * (1f + 1f / 480f) * 0.5f, s.Position.Y, 3);
            Assert.Equal(60, w.Tick);
        }

        [Fact]
        public void BallComesToRestOnStaticFloorAtRadiusHeight()
        {
            var w = new PhysicsWorld();
            int floor = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(floor, new Vec2(-5f, 0f), new Vec2(5f, 0f), 0.1f, SimTestUtil.Wall());
            var def = BallDef.Create(new Vec2(0f, 3f), 0.25f);
            def.Restitution = 0.5f;
            int b = w.AddBall(def);
            for (int i = 0; i < 300; i++) w.Step();
            ref readonly BallState s = ref w.GetBall(b);
            Assert.InRange(s.Position.Y, 0.25f + 0.05f - 0.002f, 0.25f + 0.05f + 0.002f);
            Assert.True(MathF.Abs(s.Velocity.Y) < 0.05f, "vy=" + s.Velocity.Y);
        }

        [Fact]
        public void ElasticBounceReturnsCloseToDropHeight()
        {
            var w = new PhysicsWorld();
            int floor = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(floor, new Vec2(-5f, 0f), new Vec2(5f, 0f), 0f, SimTestUtil.Wall(1f, 0f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 4f), Vec2.Zero, 0.2f, 1f, 0f));
            float apex = 0f;
            bool bounced = false;
            for (int i = 0; i < 120; i++)
            {
                w.Step();
                ref readonly BallState s = ref w.GetBall(b);
                if (s.Velocity.Y > 0f) bounced = true;
                if (bounced) apex = MathF.Max(apex, s.Position.Y);
            }
            Assert.True(bounced);
            Assert.InRange(apex, 3.9f, 4.1f);
        }

        [Fact]
        public void KilledBallIsIgnoredAndCounted()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int a = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(1f, 0f), 0.2f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0.3f, 0f), Vec2.Zero, 0.2f));
            w.KillBall(b);
            Assert.Equal(1, w.AliveBallCount);
            for (int i = 0; i < 30; i++) w.Step();
            Assert.Equal(0, w.EventCount);
            Assert.Equal(0.5f, w.GetBall(a).Position.X, 3);
            Assert.False(w.GetBall(b).Alive);
            w.KillBall(b);
            Assert.Equal(1, w.AliveBallCount);
        }

        [Fact]
        public void MaxSpeedClampHoldsAfterSuperElasticBounces()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(maxSpeed: 12f));
            SimTestUtil.AddPolygonArena(w, 4, 3f, 0.1f, 0f, 1.2f, 0f);
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(3f, 2f), 0.2f, 1.2f, 0f));
            float max = 0f;
            for (int i = 0; i < 600; i++)
            {
                w.Step();
                max = MathF.Max(max, w.GetBall(b).Velocity.Length);
            }
            Assert.True(max <= 12f + 1e-3f, "max " + max);
            Assert.True(max > 11.9f, "restitution > 1 should reach the clamp, max " + max);
        }

        [Fact]
        public void GapsAreNormalisedMergedAndWrapped()
        {
            var w = new PhysicsWorld();
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            var gaps = new[]
            {
                new AngularGap(1.0f, 1.5f),
                new AngularGap(1.2f, 2.0f),             // overlaps the first
                new AngularGap(-0.2f, 0.3f),            // wraps through 0
                new AngularGap(SimMath.TwoPi - 0.5f, -0.1f), // overlaps the wrapping one
            };
            int ring = w.AddRing(body, Vec2.Zero, 2f, 0.1f, gaps, SimTestUtil.Wall());
            ColliderInfo info = w.GetColliderInfo(ring);
            Assert.Equal(2, info.GapCount);
            AngularGap g0 = w.GetGap(ring, 0);
            AngularGap g1 = w.GetGap(ring, 1);
            Assert.Equal(1.0f, g0.Start, 4);
            Assert.Equal(2.0f, g0.End, 4);
            Assert.Equal(SimMath.TwoPi - 0.5f, g1.Start, 4);
            Assert.Equal(SimMath.TwoPi + 0.3f, g1.End, 4);
            Assert.Equal(0.1f, info.Thickness, 5);
        }

        [Fact]
        public void InvalidGapsAreRejected()
        {
            var w = new PhysicsWorld();
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            Assert.Throws<ArgumentException>(() => w.AddRing(body, Vec2.Zero, 1f, 0.1f, new[] { new AngularGap(1f, 1f) }, SimTestUtil.Wall()));
            Assert.Throws<ArgumentException>(() => w.AddRing(body, Vec2.Zero, 1f, 0.1f,
                new[] { new AngularGap(0f, 3.2f), new AngularGap(3.1f, 6.3f) }, SimTestUtil.Wall()));
        }

        [Fact]
        public void InvalidDefinitionsThrow()
        {
            var w = new PhysicsWorld();
            Assert.Throws<ArgumentException>(() => w.AddBall(BallDef.Create(Vec2.Zero, 0f)));
            Assert.Throws<ArgumentException>(() => w.AddBall(BallDef.Create(new Vec2(float.NaN, 0f), 1f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.GetBall(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => w.AddSegment(3, Vec2.Zero, Vec2.Zero, 0f, ColliderMaterial.Default));
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            Assert.Throws<ArgumentException>(() => w.AddPolyline(body, new[] { Vec2.Zero, new Vec2(1f, 0f) }, true, 0f, ColliderMaterial.Default));
            var bad = SimSettings.Default;
            bad.Substeps = 0;
            Assert.Throws<ArgumentException>(() => new PhysicsWorld(bad));
            Assert.Throws<ArgumentException>(() => w.Settings = bad);
        }

        [Fact]
        public void MoveBodyToLandsExactlyAndStops()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero));
            w.AddSegment(body, new Vec2(-1f, 0f), new Vec2(1f, 0f), 0.1f, SimTestUtil.Wall());
            w.MoveBodyTo(body, new Vec2(0.5f, 0.25f), 0.3f);
            w.Step();
            ref readonly BodyState s = ref w.GetBody(body);
            Assert.Equal(new Vec2(0.5f, 0.25f), s.Position);
            Assert.Equal(0.3f, s.Angle);
            Assert.Equal(Vec2.Zero, s.LinearVelocity);
            Assert.Equal(0f, s.AngularVelocity);
            w.Step();
            Assert.Equal(new Vec2(0.5f, 0.25f), w.GetBody(body).Position);
        }

        [Fact]
        public void BodyAnglesStayWrappedOverLongRuns()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, SimMath.TwoPi));
            w.AddRing(body, Vec2.Zero, 2f, 0.1f, ReadOnlySpan<AngularGap>.Empty, SimTestUtil.Wall());
            for (int i = 0; i < 600; i++) w.Step();
            float a = w.GetBody(body).Angle;
            Assert.InRange(a, -SimMath.Pi, SimMath.Pi);
            // 10 full turns: back to (almost) zero.
            Assert.True(MathF.Abs(a) < 1e-3f, "angle " + a);
        }

        [Fact]
        public void SnapshotMirrorsWorldState()
        {
            var w = new PhysicsWorld();
            int body = w.AddBody(BodyDef.Kinematic(new Vec2(1f, 2f), 0.5f, 1f));
            int ring = w.AddRing(body, Vec2.Zero, 2f, 0.1f, new[] { AngularGap.Centered(0f, 0.5f) }, SimTestUtil.Wall());
            var d = BallDef.Create(new Vec2(1f, 2f), 0.3f);
            d.Tag = 9;
            int b = w.AddBall(d);
            w.Step();
            w.SetColliderEnabled(ring, false);
            var snap = new SimSnapshot();
            w.WriteSnapshot(snap);
            Assert.Equal(1, snap.Tick);
            Assert.Equal(1, snap.BallCount);
            Assert.Equal(w.GetBall(b).Position, snap.Balls[0].Position);
            Assert.Equal(9, snap.Balls[0].Tag);
            Assert.Equal(0.3f, snap.Balls[0].Radius);
            Assert.True(snap.Balls[0].Alive);
            Assert.Equal(1, snap.BodyCount);
            Assert.Equal(w.GetBody(body).Angle, snap.Bodies[0].Angle);
            Assert.Equal(1, snap.ColliderCount);
            Assert.False(snap.ColliderEnabled[0]);
        }

        [Fact]
        public void ColliderInfoExposesGeometryForRenderAdapters()
        {
            var w = new PhysicsWorld();
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            Vec2[] hex = ShapeBuilder.RegularPolygon(6, 3f);
            int poly = w.AddPolyline(body, hex, true, 0.2f, SimTestUtil.Wall());
            ColliderInfo info = w.GetColliderInfo(poly);
            Assert.Equal(ColliderKind.Polyline, info.Kind);
            Assert.Equal(6, info.VertexCount);
            Assert.True(info.Closed);
            for (int i = 0; i < 6; i++) Assert.Equal(hex[i], w.GetPolylineVertex(poly, i));
        }

        [Fact]
        public void ShapeBuilderProducesExpectedPolygons()
        {
            Vec2[] sq = ShapeBuilder.Rectangle(2f, 4f);
            Assert.Equal(new Vec2(-1f, -2f), sq[0]);
            Assert.Equal(new Vec2(1f, 2f), sq[2]);
            Vec2[] star = ShapeBuilder.Star(5, 3f, 1.5f);
            Assert.Equal(10, star.Length);
            Assert.Equal(3f, star[0].Length, 4);
            Assert.Equal(1.5f, star[1].Length, 4);
            Assert.Equal(3f * MathF.Cos(SimMath.Pi / 6f), ShapeBuilder.Inradius(6, 3f), 5);
        }
    }
}
