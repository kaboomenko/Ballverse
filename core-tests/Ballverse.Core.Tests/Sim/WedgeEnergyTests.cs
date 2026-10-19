using System;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// A ball bouncing off an opening narrower than itself (two facing segment end caps, a ring gap) touches two walls with
    /// nearly opposite normals at once, and the overlap pass gives it one simultaneous wedge response. QA (residual minors,
    /// round 1): that response took per-contact restitution targets from a velocity that already held the sweep's bounce off
    /// one cap; nearly opposite normals put the intersection of the two targets far out along the opening's axis, so a 9.5 m/s
    /// hit left at 12.1 m/s with restitution 0.9 against static walls (up to +126 % mechanical energy on M01-like gaps).
    /// Static walls do no work: below restitution 1 a ball must never gain energy (spec 02_modes.md: restitution "> 1 — тела
    /// набирают энергию"); a moving wall adds at most what its own speed accounts for. At restitution exactly 1 the fixed-step
    /// integration under gravity leaves a small drift per bounce (order v·g·h; the code before the wedge response showed up to
    /// +7.5 % in these drops), so e = 1 gets a looser bound.
    /// </summary>
    public class WedgeEnergyTests
    {
        const float T = 0.05f, R = 0.15f;
        static readonly float s_g = SimSettings.StandardGravity;

        /// <summary>Allowed relative energy gain below restitution 1 (integration noise).</summary>
        const float InelasticTolerance = 0.01f;
        /// <summary>Allowed relative energy gain at restitution 1 (integration drift; measured at most 1.9 %).</summary>
        const float ElasticTolerance = 0.03f;

        static float Tolerance(float e) => e < 1f ? InelasticTolerance : ElasticTolerance;

        static float HalfGap(float freeFraction) => (2f * R * freeFraction + T) * 0.5f;

        static float Energy(in BallState b) => 0.5f * b.Velocity.LengthSquared + s_g * b.Position.Y;

        static BallDef DropBall(Vec2 p, Vec2 v, float e)
        {
            var def = SimTestUtil.Ball(p, v, R, e, 0f);
            def.Flags |= BallFlags.NoRotation;
            return def;
        }

        /// <summary>
        /// Static opening of free width <paramref name="freeFraction"/> × ball diameter centred at the origin (bridge on y = 0):
        /// two segments, or a ring of radius 1 m (the ball falls inside it) whose gap edge caps sit at (±halfGap, 0). One ball
        /// is aimed at the opening's centre; returns the largest mechanical energy gain (0.5·v² + g·y, y = 0 on the bridge)
        /// over 1 s relative to the launch energy.
        /// </summary>
        static float StaticDropGain(bool ring, float freeFraction, float e, float speed, float angleDeg)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            float halfGap = HalfGap(freeFraction);
            var m = SimTestUtil.Wall(e, 0f);
            if (ring)
            {
                float width = 2f * MathF.Asin(halfGap);
                w.AddRing(body, new Vec2(0f, MathF.Sqrt(1f - halfGap * halfGap)), 1f, T, new[] { AngularGap.Centered(-SimMath.HalfPi, width) }, m);
            }
            else
            {
                w.AddSegment(body, new Vec2(-2f, 0f), new Vec2(-halfGap, 0f), T, m);
                w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(2f, 0f), T, m);
            }
            float a = angleDeg * SimMath.Pi / 180f;
            var dir = new Vec2(MathF.Cos(a), MathF.Sin(a));
            int b = w.AddBall(DropBall(dir * -0.5f + new Vec2(0f, 0.05f), dir * speed, e));
            float e0 = Energy(w.GetBall(b));
            float worst = 0f;
            for (int tick = 0; tick < SimConstants.TickRate; tick++)
            {
                w.Step();
                float gain = (Energy(w.GetBall(b)) - e0) / e0;
                if (gain > worst) worst = gain;
            }
            return worst;
        }

        /// <summary>
        /// Drops onto static narrow openings (free width 50–99.9 % of the ball diameter, 1–12 m/s, straight and slanted):
        /// mechanical energy never grows beyond the tolerance. The code under review gained up to +2400 % here (126 cases
        /// over 1 % on segments at e = 0.9); before the wedge response at most +0.3 % below e = 1.
        /// </summary>
        [Theory]
        [InlineData(false, 0.5f)]
        [InlineData(false, 0.9f)]
        [InlineData(false, 1f)]
        [InlineData(true, 0.5f)]
        [InlineData(true, 0.9f)]
        [InlineData(true, 1f)]
        public void BounceOffNarrowStaticOpeningNeverGainsEnergy(bool ring, float e)
        {
            float[] fractions = { 0.5f, 0.7f, 0.9f, 0.95f, 0.98f, 0.99f, 0.995f, 0.998f, 0.999f };
            float[] speeds = { 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f, 12f };
            float[] angles = { -90f, -87f, -80f, -100f };
            float worst = 0f;
            string worstCase = "";
            foreach (float ff in fractions)
            foreach (float s in speeds)
            foreach (float an in angles)
            {
                float gain = StaticDropGain(ring, ff, e, s, an);
                if (gain <= worst) continue;
                worst = gain;
                worstCase = "free width " + ff + ", " + s + " m/s at " + an + "°";
            }
            Assert.True(worst <= Tolerance(e), "energy gain " + (worst * 100f) + " % (" + worstCase + ")");
        }

        /// <summary>
        /// Ring (radius 1.5 m, M01-like) rotating at <paramref name="omega"/> about its centre, one gap of free width
        /// <paramref name="freeFraction"/> × ball diameter at the bottom (under the ring centre at <paramref name="phase"/> = 0
        /// when the ball arrives); the ball falls inside it from <paramref name="y0"/> above the bridge at
        /// <paramref name="speed"/>. Returns the largest energy gain over 1 s beyond what the wall speed u = ωR can add to a
        /// ball of speed s (2·u·s + 2·u²), relative to the launch energy.
        /// </summary>
        static float RotatingDropGain(float freeFraction, float e, float speed, float y0, float omega, float phase)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            const float radius = 1.5f;
            float halfGap = HalfGap(freeFraction);
            var centre = new Vec2(0f, MathF.Sqrt(radius * radius - halfGap * halfGap));
            float width = 2f * MathF.Asin(halfGap / radius);
            float fall = (MathF.Sqrt(speed * speed + 2f * s_g * (y0 - R)) - speed) / s_g;
            int body = w.AddBody(BodyDef.Kinematic(centre, phase - omega * fall, omega));
            w.AddRing(body, Vec2.Zero, radius, T, new[] { AngularGap.Centered(-SimMath.HalfPi, width) }, SimTestUtil.Wall(e, 0f));
            int b = w.AddBall(DropBall(new Vec2(0f, y0), new Vec2(0f, -speed), e));
            float e0 = Energy(w.GetBall(b));
            float u = MathF.Abs(omega) * radius;
            float allowance = 2f * u * MathF.Sqrt(2f * e0) + 2f * u * u;
            float worst = 0f;
            for (int tick = 0; tick < SimConstants.TickRate; tick++)
            {
                w.Step();
                float gain = (Energy(w.GetBall(b)) - e0 - allowance) / e0;
                if (gain > worst) worst = gain;
            }
            return worst;
        }

        /// <summary>
        /// Drops onto a rotating ring gap, the gap phase scanned across the ball's arrival (QA: at ω = 0.2 rad/s a 10.3 m/s hit
        /// on a 0.98 gap left at 19 m/s). The code under review exceeded the wall-work bound by up to +527 % at e = 0.9.
        /// </summary>
        [Theory]
        [InlineData(0.9f)]
        [InlineData(1f)]
        public void BounceOffNarrowRotatingRingGapGainsAtMostTheWallWork(float e)
        {
            float worst = 0f;
            string worstCase = "";
            foreach (float ff in new[] { 0.98f, 0.99f })
            foreach (float omega in new[] { 0.2f, 1f })
            foreach (float speed in new[] { 3f, 9f })
            foreach (float y0 in new[] { 0.55f, 1.4f })
            {
                for (int k = -40; k <= 40; k++)
                {
                    float gain = RotatingDropGain(ff, e, speed, y0, omega, k * 0.0015f);
                    if (gain <= worst) continue;
                    worst = gain;
                    worstCase = "free width " + ff + ", ω " + omega + ", " + speed + " m/s from " + y0 + " m, phase " + (k * 0.0015f);
                }
            }
            Assert.True(worst <= Tolerance(e), "energy gain beyond the wall work " + (worst * 100f) + " % (" + worstCase + ")");
        }

        /// <summary>
        /// The wedge response's velocity change is part of the Hit events' impulse (sound / haptics intensity): no gravity,
        /// friction 0, a ball hits a static narrow opening; the impulse of its Hit events covers the momentum change, which
        /// all comes from the opening (the code under review recorded about 19 % of it in QA's drop).
        /// </summary>
        [Theory]
        [InlineData(false, 0.99f, 0.9f, -90f)]
        [InlineData(false, 0.999f, 0.5f, -87f)]
        [InlineData(true, 0.99f, 0.9f, -90f)]
        [InlineData(true, 0.999f, 0.5f, -80f)]
        public void HitEventImpulseCoversTheWedgeResponse(bool ring, float freeFraction, float e, float angleDeg)
        {
            var s = SimTestUtil.NoGravity();
            s.HitEventMinSpeed = 0f;
            var w = new PhysicsWorld(s);
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            float halfGap = HalfGap(freeFraction);
            var m = SimTestUtil.Wall(e, 0f);
            if (ring)
            {
                w.AddRing(body, new Vec2(0f, MathF.Sqrt(1f - halfGap * halfGap)), 1f, T, new[] { AngularGap.Centered(-SimMath.HalfPi, 2f * MathF.Asin(halfGap)) }, m);
            }
            else
            {
                w.AddSegment(body, new Vec2(-2f, 0f), new Vec2(-halfGap, 0f), T, m);
                w.AddSegment(body, new Vec2(halfGap, 0f), new Vec2(2f, 0f), T, m);
            }
            float a = angleDeg * SimMath.Pi / 180f;
            var dir = new Vec2(MathF.Cos(a), MathF.Sin(a));
            int b = w.AddBall(DropBall(dir * -0.5f, dir * 9f, e));
            float mass = w.GetBall(b).Mass;
            float recorded = 0f;
            int hits = 0;
            for (int tick = 0; tick < 20; tick++)
            {
                Vec2 before = w.GetBall(b).Velocity;
                w.Step();
                Vec2 after = w.GetBall(b).Velocity;
                float tickImpulse = 0f;
                foreach (var ev in w.Events)
                {
                    if (ev.Kind != SimEventKind.Hit || ev.A != b) continue;
                    tickImpulse += ev.Impulse;
                    hits++;
                }
                recorded += tickImpulse;
                float change = mass * (after - before).Length;
                Assert.True(tickImpulse >= 0.99f * change - 1e-6f, "tick " + tick + ": Hit impulse " + tickImpulse + " < momentum change " + change);
            }
            Assert.True(hits > 0 && recorded > 0f, "the ball never hit the opening");
        }
    }
}
