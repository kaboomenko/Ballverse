using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>Helix Drop building blocks: edge-on spinning platforms with gaps.</summary>
    public class SpinPlatformTests
    {
        static (PhysicsWorld w, int body, int platform, int ball) Scene(float spin, float spinVelocity = 0f)
        {
            var w = new PhysicsWorld();
            int body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f, Spin = spin, SpinVelocity = spinVelocity });
            // 90° gap centred on azimuth 0 (facing the camera when Spin = 0).
            int platform = w.AddSpinPlatform(body, Vec2.Zero, 2f, 0.2f, new[] { AngularGap.Centered(0f, SimMath.HalfPi) }, SimTestUtil.Wall(0f, 0f));
            var d = SimTestUtil.Ball(new Vec2(0f, 1.5f), Vec2.Zero, 0.2f, 0f, 0f);
            d.Flags = BallFlags.LockX;
            int ball = w.AddBall(d);
            return (w, body, platform, ball);
        }

        [Fact]
        public void BallFallsThroughWhenGapFacesCamera()
        {
            var (w, _, _, ball) = Scene(0f);
            for (int t = 0; t < 60; t++) w.Step();
            Assert.True(w.GetBall(ball).Position.Y < -1f);
        }

        [Fact]
        public void BallLandsWhenSolidSectorFacesCameraAndEventReportsAzimuth()
        {
            var (w, _, platform, ball) = Scene(SimMath.Pi);
            float azimuth = -1f;
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind == SimEventKind.Hit && e.B == platform) azimuth = e.LocalAngle;
                }
            }
            ref readonly BallState b = ref w.GetBall(ball);
            Assert.Equal(0.2f + 0.1f, b.Position.Y, 2);
            Assert.Equal(0f, b.Position.X);
            // Ball at x = 0 touches world azimuth 0, i.e. platform azimuth −Spin = π.
            Assert.Equal(SimMath.Pi, azimuth, 3);
        }

        [Fact]
        public void SpinningTheTowerOpensTheGapUnderTheBall()
        {
            var (w, body, _, ball) = Scene(SimMath.Pi);
            for (int t = 0; t < 60; t++) w.Step();
            Assert.True(w.GetBall(ball).Position.Y > 0f);
            // Swipe: turn the tower at half a turn per second; the gap's leading edge reaches the front after 0.75 s and
            // the gap stays under the ball for 0.5 s, long enough to drop through.
            w.SetBodySpinVelocity(body, SimMath.Pi);
            int fallTick = -1;
            for (int t = 0; t < 150 && fallTick < 0; t++)
            {
                w.Step();
                if (w.GetBall(ball).Position.Y < -0.5f) fallTick = t;
            }
            Assert.True(fallTick > 0, "ball never fell");
            Assert.InRange(fallTick, 45, 110);
        }

        [Fact]
        public void GapEdgeIsSolidForBallOffCentre()
        {
            // Ball at x = 1.9 sits at azimuth asin(0.95) ≈ 72°, outside the ±45° gap: lands even with the gap in front.
            var w = new PhysicsWorld();
            int body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f });
            w.AddSpinPlatform(body, Vec2.Zero, 2f, 0.2f, new[] { AngularGap.Centered(0f, SimMath.HalfPi) }, SimTestUtil.Wall(0f, 0f));
            var d = SimTestUtil.Ball(new Vec2(1.9f, 1.5f), Vec2.Zero, 0.1f, 0f, 0f);
            d.Flags = BallFlags.LockX;
            int ball = w.AddBall(d);
            for (int t = 0; t < 60; t++) w.Step();
            Assert.True(w.GetBall(ball).Position.Y > 0f);
        }

        [Fact]
        public void LockedBallKeepsItsColumnOnTiltedContacts()
        {
            var (w, _, _, ball) = Scene(SimMath.Pi, 3f);
            for (int t = 0; t < 120; t++) w.Step();
            Assert.Equal(0f, w.GetBall(ball).Position.X);
            Assert.Equal(0f, w.GetBall(ball).Velocity.X);
        }
    }
}
