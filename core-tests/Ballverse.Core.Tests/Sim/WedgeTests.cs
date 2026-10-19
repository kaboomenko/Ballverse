using System;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// A ball wedged in an opening only slightly narrower than itself (two facing segment end caps, or the edge caps of a ring
    /// gap), held there by gravity or pressed into it by a plate of another body. QA (final re-verify of QA round 3): the held
    /// ball built up a large phantom velocity — the sequential responses of two nearly opposite cap normals remove only a sliver
    /// of the push per substep, and a ball resting on a bridge got no response through it at all — and then stuck or shot off
    /// when the push reversed. Its velocity must follow the real motion of the walls, and it must leave at a plausible speed.
    /// </summary>
    public class WedgeTests
    {
        const float T = 0.05f, R = 0.15f;
        /// <summary>Gravity gained in one substep at the default 8 substeps: what a held ball may show at the end of a tick.</summary>
        static readonly float s_substepGravity = SimSettings.StandardGravity / (SimConstants.TickRate * SimSettings.DefaultSubsteps);

        /// <summary>
        /// Kinematic body (at rest) with an opening of free width <paramref name="freeFraction"/> × ball diameter centred at the
        /// origin, its bridge on y = 0: two segments, or a ring (radius 1 m) whose gap edge caps sit at (±halfGap, 0).
        /// </summary>
        static int AddOpening(PhysicsWorld w, bool ring, float freeFraction, in ColliderMaterial m)
        {
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero));
            float halfGap = (2f * R * freeFraction + T) * 0.5f;
            if (ring)
            {
                const float radius = 1f;
                float width = 2f * MathF.Asin(halfGap / radius);
                w.AddRing(body, new Vec2(0f, radius * MathF.Cos(width * 0.5f)), radius, T, new[] { AngularGap.Centered(-SimMath.HalfPi, width) }, m);
            }
            else
            {
                w.AddSegment(body, new Vec2(-2f, 0f), new Vec2(-halfGap, 0f), T, m);
                w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(2f, 0f), T, m);
            }
            return body;
        }

        /// <summary>
        /// Gravity drops the ball into the opening and holds it there: its velocity stays at rest (old code: 0.35 m/s at 99.9 %,
        /// 1.24 m/s at 99.99 % into the opening). The opening then lifts it at 2 m/s: the ball moves with it at 2 m/s (old: 0.7).
        /// When the opening reverses to 2 m/s downwards the ball flies off upwards at the 2 m/s it was carried with (old: 0.72).
        /// </summary>
        [Theory]
        [InlineData(false, 0.999f)]
        [InlineData(false, 0.9999f)]
        [InlineData(true, 0.999f)]
        [InlineData(true, 0.9999f)]
        public void WedgedBallMovesWithTheWallsAndLeavesAtTheirSpeed(bool ring, float freeFraction)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            int body = AddOpening(w, ring, freeFraction, SimTestUtil.Wall(0.5f, 0.3f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.5f), Vec2.Zero, R, 0.5f, 0.3f));
            const float lift = 2f;
            float tol = s_substepGravity + 1e-3f;
            for (int tick = 0; tick < 150; tick++)
            {
                if (tick == 120) w.SetBodyVelocity(body, new Vec2(0f, lift), 0f);
                w.Step();
                Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(b).Position);
                Vec2 v = w.GetBall(b).Velocity;
                Assert.True(local.Y > 0f, "ball passed the opening at tick " + tick + ": " + local);
                if (tick >= 60 && tick < 120) Assert.True(v.Length <= tol, "held ball shows phantom velocity " + v + " at tick " + tick);
                if (tick >= 130) Assert.True((v - new Vec2(0f, lift)).Length <= tol, "lifted ball velocity " + v + " at tick " + tick + " (opening at " + lift + " m/s)");
            }
            Vec2 held = SimTestUtil.ToLocal(w, body, w.GetBall(b).Position);
            Assert.True(held.Y < 0.01f, "ball is not wedged in the opening: " + held);

            w.SetBodyVelocity(body, new Vec2(0f, -lift), 0f);
            for (int k = 1; k <= 10; k++)
            {
                w.Step();
                // Ballistic from the carried 2 m/s: v = 2 − g·t.
                float expected = lift - SimSettings.StandardGravity * k / SimConstants.TickRate;
                Vec2 v = w.GetBall(b).Velocity;
                Assert.True(MathF.Abs(v.Y - expected) <= 0.01f && MathF.Abs(v.X) <= 0.01f, "released ball velocity " + v + " after " + k + " ticks, expected (0, " + expected + ")");
            }
            Vec2 local10 = SimTestUtil.ToLocal(w, body, w.GetBall(b).Position);
            Assert.True(local10.Y > 0.5f, "released ball did not leave the opening: " + local10);
        }

        /// <summary>
        /// The push reverses (the ball's gravity flips): a ball wedged at rest leaves at once, ballistically (old code: the
        /// accumulated 1.24 m/s into the opening kept it stuck for about 8 ticks).
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WedgedBallLeavesAtOnceWhenThePushReverses(bool ring)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            int body = AddOpening(w, ring, 0.9999f, SimTestUtil.Wall(0.5f, 0.3f));
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.5f), Vec2.Zero, R, 0.5f, 0.3f));
            for (int tick = 0; tick < 120; tick++) w.Step();
            float y0 = w.GetBall(b).Position.Y;
            Assert.True(y0 > 0f && y0 < 0.01f, "ball is not wedged in the opening: " + y0);

            w.SetBallGravityScale(b, -1f);
            for (int k = 1; k <= 10; k++)
            {
                w.Step();
                float expected = SimSettings.StandardGravity * k / SimConstants.TickRate;
                Vec2 v = w.GetBall(b).Velocity;
                Assert.True(MathF.Abs(v.Y - expected) <= 0.01f, "velocity " + v + " after " + k + " ticks of reversed push, expected (0, " + expected + ")");
            }
            float rise = w.GetBall(b).Position.Y - y0;
            float free = 0.5f * SimSettings.StandardGravity * (10f / SimConstants.TickRate) * (10f / SimConstants.TickRate);
            Assert.True(rise > 0.9f * free, "ball rose " + rise + " m in 10 ticks, free flight " + free + " m");
        }

        /// <summary>
        /// A plate of another body descends at 1 m/s onto a ball wedged in the opening and stops where the ball no longer fits
        /// above the opening (it would have to pass the bridge to fit); <see cref="StepPlate"/> drives it.
        /// </summary>
        static (PhysicsWorld w, int body, int ball, int plate) PlateScene(bool ring, float freeFraction)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var m = SimTestUtil.Wall(0.3f, 0.3f);
            int body = AddOpening(w, ring, freeFraction, m);
            int plate = w.AddBody(BodyDef.Kinematic(new Vec2(0f, 0.8f)));
            w.AddSegment(plate, new Vec2(-0.6f, 0f), new Vec2(0.6f, 0f), T, m);
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(0f, 0.3f), Vec2.Zero, R, 0.3f, 0.3f));
            return (w, body, b, plate);
        }

        const float PlateSpeed = 1f;

        /// <summary>One tick; from tick 30 the plate descends at <see cref="PlateSpeed"/> until its centre line reaches <paramref name="plateY"/>.</summary>
        static void StepPlate(PhysicsWorld w, int plate, int tick, float plateY)
        {
            if (tick >= 30) w.MoveBodyTo(plate, new Vec2(0f, MathF.Max(plateY, w.GetBody(plate).Position.Y - PlateSpeed / SimConstants.TickRate)), 0f);
            w.Step();
        }

        /// <summary>
        /// Where the ball ended up if it left the legal side of the opening body, else null: through the opening (below the bridge
        /// within reach of the gap), or through a wall elsewhere (out of the ring, below a segment).
        /// </summary>
        static string Escape(bool ring, float freeFraction, Vec2 local)
        {
            float halfGap = (2f * R * freeFraction + T) * 0.5f;
            if (local.Y < 0f && MathF.Abs(local.X) < halfGap + R) return "pushed through the opening";
            if (ring)
            {
                var centre = new Vec2(0f, MathF.Sqrt(1f - halfGap * halfGap));
                if ((local - centre).Length > 1f) return "escaped through the ring wall";
            }
            else if (local.Y < 0f) return "pushed through a segment";
            return null;
        }

        /// <summary>
        /// Covers the overlap pass's bridge guard (no projection candidate may jump across a bridge): pressed by the plate, the
        /// ball does not fit anywhere above the opening, and the nearest feasible candidate of the joint projection is the mirror
        /// image of its cap–cap contact point below the bridge — without the guard the ball is put through the opening (all four
        /// cases fail with the guard disabled). These are full-crush setups: the stopped plate leaves no legal position for the
        /// ball (it would need its centre at most plateY − 0.175 m, while sitting above the opening needs at least 0.071 m at
        /// 90 % and 0.132 m at 60 % free width), so they pin the chosen outcome — the ball stays on the bridge, overlapping the
        /// plate — not a physically valid resting position. An escape through a wall elsewhere is reported as such.
        /// </summary>
        [Theory]
        [InlineData(false, 0.9f, 0.2f)]
        [InlineData(true, 0.9f, 0.2f)]
        [InlineData(false, 0.6f, 0.15f)]
        [InlineData(true, 0.6f, 0.15f)]
        public void PlatePressingBallIntoNarrowOpeningNeverPushesItThrough(bool ring, float freeFraction, float plateY)
        {
            var (w, body, b, plate) = PlateScene(ring, freeFraction);
            for (int tick = 0; tick < 4 * SimConstants.TickRate; tick++)
            {
                StepPlate(w, plate, tick, plateY);
                Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(b).Position);
                string escape = Escape(ring, freeFraction, local);
                Assert.True(escape == null, "ball " + escape + " at tick " + tick + ": " + local);
            }
            Assert.Equal(plateY, w.GetBody(plate).Position.Y, 4);
            Assert.True(SimTestUtil.ToLocal(w, body, w.GetBall(b).Position).Y > 0f, "ball is not above the bridge");
        }

        /// <summary>
        /// The plate crushes the ball onto the bridge and stops (no position fits the ball; the sweep holds it on the bridge).
        /// Once the squeeze has played out (from 2 s; the plate stops after about 1.1 s) the held ball does not move into the
        /// opening and its velocity stays within the plate's speed — old code: gravity accumulated through the bridge, −8.8 m/s
        /// at 2.2 s and −25 m/s at 3.8 s on the ring gap.
        /// </summary>
        [Theory]
        [InlineData(false, 0.9f, 0.2f)]
        [InlineData(true, 0.9f, 0.2f)]
        [InlineData(false, 0.6f, 0.15f)]
        [InlineData(true, 0.6f, 0.15f)]
        public void BallCrushedIntoNarrowOpeningKeepsBoundedVelocity(bool ring, float freeFraction, float plateY)
        {
            var (w, body, b, plate) = PlateScene(ring, freeFraction);
            float tol = s_substepGravity + 1e-3f;
            for (int tick = 0; tick < 4 * SimConstants.TickRate; tick++)
            {
                StepPlate(w, plate, tick, plateY);
                if (tick < 2 * SimConstants.TickRate) continue;
                Assert.Equal(plateY, w.GetBody(plate).Position.Y, 4);
                Vec2 v = w.GetBall(b).Velocity;
                Assert.True(v.Y >= -tol && v.Length <= PlateSpeed, "ball held under the stopped plate: velocity " + v + " at tick " + tick);
            }
            Vec2 local = SimTestUtil.ToLocal(w, body, w.GetBall(b).Position);
            Assert.True(local.Y > 0f && local.Y < 0.1f, "ball is not pressed into the opening: " + local);
        }
        /// <summary>
        /// Wedge responses (held balls, plates crushing balls, bouncing hits whose wedge impulse joins the Hit events) allocate
        /// nothing in steady state.
        /// </summary>
        [Fact]
        public void WedgeSceneDoesNotAllocateInSteadyState()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var m = SimTestUtil.Wall(0.9f, 0.3f);
            const int openings = 8;
            var plates = new int[openings];
            // A closed box keeps every ball in a bounded region (a ball squeezed out sideways falls onto its floor).
            int box = w.AddBody(BodyDef.Static(new Vec2(0f, 0.5f)));
            w.AddPolyline(box, ShapeBuilder.Rectangle(openings * 2.5f + 2f, 4f), true, T, m);
            for (int i = 0; i < openings; i++)
            {
                bool ring = (i & 1) == 1;
                float x = (i - (openings - 1) * 0.5f) * 2.5f;
                int body = w.AddBody(BodyDef.Kinematic(new Vec2(x, 0f)));
                float halfGap = (2f * R * 0.99f + T) * 0.5f;
                if (ring)
                {
                    w.AddRing(body, new Vec2(0f, MathF.Sqrt(1f - halfGap * halfGap)), 1f, T, new[] { AngularGap.Centered(-SimMath.HalfPi, 2f * MathF.Asin(halfGap)) }, m);
                }
                else
                {
                    w.AddSegment(body, new Vec2(-1f, 0f), new Vec2(-halfGap, 0f), T, m);
                    w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(1f, 0f), T, m);
                }
                w.AddBall(SimTestUtil.Ball(new Vec2(x, 0.4f), new Vec2(0f, -6f), R, 0.9f, 0.3f));
                plates[i] = w.AddBody(BodyDef.Kinematic(new Vec2(x, 0.8f)));
                w.AddSegment(plates[i], new Vec2(-0.6f, 0f), new Vec2(0.6f, 0f), T, m);
            }
            const int warmUp = 3 * SimConstants.TickRate;
            long start = 0L;
            for (int tick = 0; tick < warmUp + 5 * SimConstants.TickRate; tick++)
            {
                if (tick == warmUp) start = GC.GetAllocatedBytesForCurrentThread();
                // Half the plates crush their ball, the others swing above it (the ball bounces and is pressed again).
                for (int i = 0; i < openings; i++)
                {
                    float y = i < openings / 2 ? MathF.Max(0.2f, 0.8f - tick * 0.01f) : 0.45f + 0.3f * MathF.Sin(tick * 0.05f);
                    w.MoveBodyTo(plates[i], new Vec2(w.GetBody(plates[i]).Position.X, y), 0f);
                }
                w.Step();
            }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - start);
        }
    }
}
