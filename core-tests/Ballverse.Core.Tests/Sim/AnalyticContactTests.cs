using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Spec §8.5: ball–wall and ball–ball contacts against analytic solutions — reflection angle with restitution and Coulomb
    /// friction on an oblique wall hit, and 2D momentum / energy for oblique collisions of unequal balls. (Head-on 1D cases,
    /// normal bounces and moving-wall friction are covered in <see cref="BallBallTests"/> and <see cref="CollisionTests"/>.)
    /// </summary>
    public class AnalyticContactTests
    {
        readonly ITestOutputHelper _out;

        public AnalyticContactTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>
        /// A non-spinning solid disc (I = m r²/2) hits a static wall at an angle of incidence θ (from the normal) at 5 m/s:
        /// v_n' = e·v_n; the tangential impulse is the sticking one (v_t' = 2/3 v_t: the contact point stops slipping) capped by
        /// μ·(1 + e)·m·v_n (sliding), μ = sqrt(μ_ball·μ_wall).
        /// </summary>
        [Theory]
        [InlineData(30f, 1f, 0f)]
        [InlineData(60f, 1f, 0f)]
        [InlineData(45f, 0.5f, 0f)]
        [InlineData(45f, 1f, 0.3f)]
        [InlineData(70f, 0.8f, 0.05f)]
        [InlineData(20f, 0.8f, 0.9f)]
        public void ObliqueWallBounceMatchesAnalyticReflection(float incidenceDegrees, float e, float mu)
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(maxSpeed: 60f));
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(body, new Vec2(-10f, 0f), new Vec2(10f, 0f), 0.1f, SimTestUtil.Wall(0f, 1f));
            float a = incidenceDegrees * SimMath.DegToRad;
            const float speed = 5f;
            // Ball friction μ² against wall friction 1 combines to μ.
            int b = w.AddBall(SimTestUtil.Ball(new Vec2(-1f, 1f), new Vec2(MathF.Sin(a), -MathF.Cos(a)) * speed, 0.2f, e, mu * mu));
            int hits = 0;
            for (int t = 0; t < SimConstants.TickRate; t++)
            {
                w.Step();
                foreach (var ev in w.Events) if (ev.Kind == SimEventKind.Hit) hits++;
            }
            float vt0 = speed * MathF.Sin(a), vn0 = speed * MathF.Cos(a);
            float vtExpected = vt0 - MathF.Min(mu * (1f + e) * vn0, vt0 / 3f);
            Vec2 v = w.GetBall(b).Velocity;
            float reflected = MathF.Atan2(v.X, v.Y) / SimMath.DegToRad;
            float reflectedExpected = MathF.Atan2(vtExpected, e * vn0) / SimMath.DegToRad;
            _out.WriteLine("θ " + incidenceDegrees + "°, e " + e + ", μ " + mu + ": v " + v + ", expected (" + vtExpected + ", " + e * vn0 + "), reflection "
                + reflected + "° (expected " + reflectedExpected + "°)");
            Assert.Equal(1, hits);
            Assert.Equal(e * vn0, v.Y, 3);
            Assert.Equal(vtExpected, v.X, 3);
            Assert.Equal(reflectedExpected, reflected, 1);
            if (mu == 0f)
            {
                Assert.Equal(0f, w.GetBall(b).AngularVelocity, 4);
                if (e == 1f) Assert.Equal(incidenceDegrees, reflected, 1);
            }
        }

        /// <summary>
        /// Oblique collision of a 2 kg / 0.2 m ball at 4 m/s with a 0.7 kg / 0.15 m ball at −1 m/s, offset sideways: linear
        /// momentum is conserved in both components with any restitution and friction; kinetic energy (with spin) is conserved
        /// for e = 1 without friction (and no spin appears), and never grows otherwise; for e = 1 without friction the result
        /// matches the analytic exchange along the contact normal.
        /// </summary>
        [Theory]
        [InlineData(0.1f, 1f, 0f)]
        [InlineData(0.25f, 1f, 0f)]
        [InlineData(0.1f, 0.6f, 0f)]
        [InlineData(0.1f, 1f, 0.5f)]
        [InlineData(-0.2f, 0.8f, 0.3f)]
        public void ObliqueBallBallCollisionOfUnequalBallsConservesMomentum(float offset, float e, float mu)
        {
            const float ma = 2f, mb = 0.7f, ra = 0.2f, rb = 0.15f;
            var w = new PhysicsWorld(SimTestUtil.NoGravity(maxSpeed: 60f));
            var da = SimTestUtil.Ball(new Vec2(-1f, 0f), new Vec2(4f, 0f), ra, e, mu);
            da.Mass = ma;
            var db = SimTestUtil.Ball(new Vec2(1f, offset), new Vec2(-1f, 0f), rb, e, mu);
            db.Mass = mb;
            int a = w.AddBall(da), b = w.AddBall(db);
            Vec2 p0 = new Vec2(4f, 0f) * ma + new Vec2(-1f, 0f) * mb;
            float ke0 = 0.5f * ma * 16f + 0.5f * mb * 1f;
            Vec2 eventNormal = Vec2.Zero;
            for (int t = 0; t < SimConstants.TickRate; t++)
            {
                w.Step();
                foreach (var ev in w.Events) if (ev.Kind == SimEventKind.BallHit && eventNormal == Vec2.Zero) eventNormal = ev.Normal;
            }
            ref readonly BallState sa = ref w.GetBall(a);
            ref readonly BallState sb = ref w.GetBall(b);
            Vec2 p1 = sa.Velocity * ma + sb.Velocity * mb;
            float ke1 = 0.5f * ma * sa.Velocity.LengthSquared + 0.5f * mb * sb.Velocity.LengthSquared
                + 0.5f * (0.5f * ma * ra * ra) * sa.AngularVelocity * sa.AngularVelocity
                + 0.5f * (0.5f * mb * rb * rb) * sb.AngularVelocity * sb.AngularVelocity;
            _out.WriteLine("offset " + offset + ", e " + e + ", μ " + mu + ": p " + p0 + " → " + p1 + ", KE " + ke0 + " → " + ke1 + ", normal " + eventNormal);
            Assert.NotEqual(Vec2.Zero, eventNormal);
            Assert.Equal(p0.X, p1.X, 4);
            Assert.Equal(p0.Y, p1.Y, 4);
            Assert.True(ke1 <= ke0 * (1f + 1e-5f), "kinetic energy grew: " + ke0 + " → " + ke1);
            if (e == 1f && mu == 0f)
            {
                Assert.Equal(ke0, ke1, 3);
                Assert.Equal(0f, sa.AngularVelocity, 5);
                Assert.Equal(0f, sb.AngularVelocity, 5);
                // Analytic: contact normal from the centres at first touch (relative motion along x). The discrete solver takes
                // the normal at the first overlapping substep (≤ 1 cm deep here), a few mrad off; given that normal, the
                // velocities must be exactly the elastic exchange along it.
                var n = new Vec2(MathF.Sqrt((ra + rb) * (ra + rb) - offset * offset), offset) / (ra + rb);
                Assert.True(Vec2.Dot(eventNormal, n) > 0.999f, "normal " + eventNormal + " vs analytic " + n);
                float j = 2f * Vec2.Dot(new Vec2(5f, 0f), eventNormal) / (1f / ma + 1f / mb);
                Vec2 vaExpected = new Vec2(4f, 0f) - eventNormal * (j / ma);
                Vec2 vbExpected = new Vec2(-1f, 0f) + eventNormal * (j / mb);
                Assert.True((sa.Velocity - vaExpected).Length < 1e-3f, "v_a " + sa.Velocity + " vs " + vaExpected);
                Assert.True((sb.Velocity - vbExpected).Length < 1e-3f, "v_b " + sb.Velocity + " vs " + vbExpected);
            }
        }
    }
}
