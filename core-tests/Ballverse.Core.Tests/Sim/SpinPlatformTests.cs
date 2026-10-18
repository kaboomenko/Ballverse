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

        // ------------------------------------------------------------------ transit (spec §2.9 M05, §6.5)

        const float PathRadius = 1.5f;
        const float Thickness = 0.15f;

        sealed class PlatformHookCounter : ISimContactHook
        {
            public int PlatformCalls;
            public int Platform = -1;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                if (collider == Platform) PlatformCalls++;
                return true;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact) => true;
        }

        /// <summary>
        /// Helix Drop column: platform with its top at y = 0 (occupying [−T, 0]), half-width R_path, one gap of
        /// <paramref name="gapWidth"/> whose centre faces the ball (azimuth 0) at <paramref name="alignTime"/> s, plus
        /// <paramref name="offset"/>; the ball (LockX at x = 0) falls from rest with its bottom 0.9 m above the top.
        /// </summary>
        static (PhysicsWorld w, int platform, int ball) HelixColumn(float r, float gapWidth, float spinVelocity, float offset, out float alignTime)
        {
            var s = SimSettings.Default;
            var w = new PhysicsWorld(s);
            // The centre reaches the decision level y = r after falling 0.9 m.
            alignTime = MathF.Sqrt(2f * 0.9f / SimSettings.StandardGravity);
            int body = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f, SpinVelocity = spinVelocity });
            // Ball azimuth a = asin(0) − Spin(t) = −ω·t: centre the gap there at alignTime.
            float centre = -spinVelocity * alignTime + offset;
            int platform = w.AddSpinPlatform(body, new Vec2(0f, -Thickness * 0.5f), PathRadius, Thickness, new[] { AngularGap.Centered(centre, gapWidth) }, SimTestUtil.Wall(0.6f, 0f));
            var d = SimTestUtil.Ball(new Vec2(0f, 0.9f + r), Vec2.Zero, r, 0.6f, 0f);
            d.Flags = BallFlags.LockX;
            int ball = w.AddBall(d);
            return (w, platform, ball);
        }

        static float Delta(float r) => SimMath.Asin(r / PathRadius);

        /// <summary>
        /// Spec test: "транзит: платформа, повернувшаяся во время пролёта так, что сплошной или опасный сектор вошёл в след
        /// шара, не даёт ни контакта, ни поражения, ни смещения шара". «Безумная» 240°/s, gap 2δ + 6° aligned when the ball
        /// reaches the top: the platform turns ~33° during the pass, so the gap edge sweeps through the footprint. The ball
        /// must fall exactly like a ball with no platform (no contact, no hook call, no displacement), and the pair is in
        /// transit (listed in the snapshot) only while the ball is inside the platform's height band.
        /// </summary>
        [Theory]
        [InlineData(0.25f, 240f, 0f)]
        [InlineData(0.25f, 240f, -1.2f)]
        [InlineData(0.25f, -240f, 2.5f)]
        [InlineData(0.4f, 70f, -0.3f)]
        [InlineData(0.4f, 240f, 2.9f)]
        public void TransitIgnoresPlatformTurningIntoTheFootprint(float r, float degPerSec, float offsetDeg)
        {
            float gap = 2f * Delta(r) + 6f * SimMath.DegToRad;
            var (w, platform, ball) = HelixColumn(r, gap, degPerSec * SimMath.DegToRad, offsetDeg * SimMath.DegToRad, out _);
            var hook = new PlatformHookCounter { Platform = platform };
            w.ContactHook = hook;
            var reference = new PhysicsWorld(SimSettings.Default);
            var rd = SimTestUtil.Ball(new Vec2(0f, 0.9f + r), Vec2.Zero, r, 0.6f, 0f);
            rd.Flags = BallFlags.LockX;
            reference.AddBall(rd);
            var snapshot = new SimSnapshot();
            int transitTicks = 0;
            float turned = 0f;
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                reference.Step();
                Assert.Equal(reference.GetBall(0).Position, w.GetBall(ball).Position);
                Assert.Equal(reference.GetBall(0).Velocity, w.GetBall(ball).Velocity);
                foreach (var e in w.Events) Assert.False(e.Kind == SimEventKind.Hit, "hit at tick " + t);
                float y = w.GetBall(ball).Position.Y;
                bool inBand = y < r && y > -Thickness - r;
                Assert.Equal(inBand, w.IsInTransit(ball, platform));
                w.WriteSnapshot(snapshot);
                Assert.Equal(inBand ? 1 : 0, snapshot.TransitCount);
                if (inBand)
                {
                    Assert.Equal(new SpinTransit(ball, platform), snapshot.Transits[0]);
                    transitTicks++;
                    turned += MathF.Abs(degPerSec) / SimConstants.TickRate;
                }
            }
            Assert.Equal(0, hook.PlatformCalls);
            Assert.True(w.GetBall(ball).Position.Y < -2f);
            Assert.Equal(0, w.TransitCount);
            Assert.True(transitTicks >= 7, "transit lasted " + transitTicks + " ticks");
            // The edge really entered the footprint: the platform turned more than the 3° alignment margin on either side.
            Assert.True(turned > 6f, "turned " + turned + "°");
        }

        /// <summary>
        /// The pass / bounce footprint is exactly [a − δ, a + δ], δ = asin(r / R_path) (no round end caps widening it): a static
        /// gap of 2δ + 1° centred under the ball passes, 2δ − 1° bounces, and a footprint touching a gap edge bounces
        /// ("Кромка просвета считается сплошной").
        /// </summary>
        [Theory]
        [InlineData(0.25f, 1f, 0f, true)]
        [InlineData(0.25f, -1f, 0f, false)]
        [InlineData(0.25f, 6f, 2.9f, true)]
        [InlineData(0.25f, 6f, 3.1f, false)]
        [InlineData(0.4f, 1f, 0f, true)]
        [InlineData(0.15f, 0.5f, 0f, true)]
        public void FootprintDecidesPassOrBounceAtTheTop(float r, float extraDeg, float offsetDeg, bool passes)
        {
            float gap = 2f * Delta(r) + extraDeg * SimMath.DegToRad;
            var (w, platform, ball) = HelixColumn(r, gap, 0f, offsetDeg * SimMath.DegToRad, out _);
            int hits = 0;
            for (int t = 0; t < 5 * SimConstants.TickRate; t++)
            {
                w.Step();
                foreach (var e in w.Events)
                {
                    if (e.Kind == SimEventKind.Hit && e.B == platform)
                    {
                        hits++;
                        Assert.Equal(0, e.Feature);
                        Assert.Equal(new Vec2(0f, 1f), e.Normal);
                    }
                }
            }
            float y = w.GetBall(ball).Position.Y;
            if (passes)
            {
                Assert.Equal(0, hits);
                Assert.True(y < -2f, "ball stuck at y " + y);
            }
            else
            {
                Assert.True(hits > 0);
                // Rests on the flat top (no caps): centre exactly r above it.
                Assert.Equal(r, y, 3);
            }
        }

        /// <summary>
        /// A rotating platform whose solid sector is under the ball when it arrives makes it bounce; a later arrival with the
        /// footprint inside the gap starts a transit. Two identical runs hash identically every tick.
        /// </summary>
        [Fact]
        public void BounceThenPassIsDeterministic()
        {
            float r = 0.25f;
            float gap = 2f * Delta(r) + 20f * SimMath.DegToRad;
            // Gap aligned half a turn late: the first arrival lands on solid.
            var (w, platform, ball) = HelixColumn(r, gap, SimMath.Pi, SimMath.Pi, out _);
            var (w2, _, _) = HelixColumn(r, gap, SimMath.Pi, SimMath.Pi, out _);
            int bounces = 0, firstTransitTick = -1;
            for (int t = 0; t < 600 && firstTransitTick < 0; t++)
            {
                w.Step();
                w2.Step();
                Assert.Equal(w2.ComputeStateHash(), w.ComputeStateHash());
                foreach (var e in w.Events) if (e.Kind == SimEventKind.Hit && e.B == platform) bounces++;
                if (w.IsInTransit(ball, platform)) firstTransitTick = t;
            }
            Assert.True(bounces >= 1, "no bounce before the pass");
            Assert.True(firstTransitTick > 0, "never passed");
        }

        /// <summary>
        /// Transit state is part of the world hash (spec: "Состояние — часть снимка мира (хэш, реплей…)"): two worlds whose
        /// balls and bodies are identical — one platform under the ball, one out of its reach — differ only by the transit,
        /// and hash differently while it lasts. Killing the ball ends its transits.
        /// </summary>
        [Fact]
        public void TransitIsHashedAndEndsWithTheBall()
        {
            float r = 0.25f;
            float gap = 2f * Delta(r) + 6f * SimMath.DegToRad;
            var (w, platform, ball) = HelixColumn(r, gap, 0f, 0f, out _);
            var other = new PhysicsWorld(SimSettings.Default);
            int body = other.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f });
            other.AddSpinPlatform(body, new Vec2(10f, -Thickness * 0.5f), PathRadius, Thickness, new[] { AngularGap.Centered(0f, gap) }, SimTestUtil.Wall(0.6f, 0f));
            var d = SimTestUtil.Ball(new Vec2(0f, 0.9f + r), Vec2.Zero, r, 0.6f, 0f);
            d.Flags = BallFlags.LockX;
            other.AddBall(d);
            int differing = 0;
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                other.Step();
                Assert.Equal(other.GetBall(0).Position, w.GetBall(ball).Position);
                bool transit = w.IsInTransit(ball, platform);
                Assert.Equal(transit, w.ComputeStateHash() != other.ComputeStateHash());
                if (transit) differing++;
                if (differing == 3) break;
            }
            Assert.Equal(3, differing);
            Assert.Equal(1, w.TransitCount);
            Assert.Equal(new SpinTransit(ball, platform), w.GetTransit(0));
            w.KillBall(ball);
            Assert.False(w.IsInTransit(ball, platform));
            Assert.Equal(0, w.TransitCount);
        }
    }
}
