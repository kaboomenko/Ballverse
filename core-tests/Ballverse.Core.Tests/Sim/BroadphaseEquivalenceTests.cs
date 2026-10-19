using System;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// The collider broadphase (radial index, PhysicsWorld.Broadphase.cs), the end-of-tick sensor bound pre-check and the
    /// sparse ball–ball grid are pure culling / layout changes: they may only skip tests that cannot produce a contact or a
    /// sensor event, and must keep every solve order. Each scene is built twice,
    /// one world stepping with the broadphase and one forced onto the old exhaustive scans
    /// (<see cref="PhysicsWorld.ExhaustiveBroadphase"/>, which also keeps the dense ball grid), and the two are compared after every tick: the state hash, the
    /// exact float bits of every ball's position, velocity, angle and spin, and every event field by field. The scenes cover
    /// the release-1 shapes (Ring Escape rings with gaps and passage sensors, Geometry Bounce polygons with many balls,
    /// Helix Drop platforms) and a mixed one with every collider kind as walls and sensors, moving / scaling / scripted
    /// bodies, collision filters, toggled colliders and a contact hook that rejects contacts and kills balls; two more keep
    /// their colliders moving (compressing rings, a translating tower) with no membership change, so the index must re-sort
    /// on motion alone (<see cref="RunMovingLockstep"/>).
    /// </summary>
    public class BroadphaseEquivalenceTests
    {
        static void AssertSameBits(float a, float b, string what, int tick)
        {
            Assert.True(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b), what + " differs at tick " + tick + ": " + a + " vs " + b);
        }

        static void AssertSameTick(PhysicsWorld a, PhysicsWorld b, int tick)
        {
            Assert.True(a.ComputeStateHash() == b.ComputeStateHash(), "state hash differs at tick " + tick);
            for (int i = 0; i < a.BallCount; i++)
            {
                ref readonly BallState x = ref a.GetBall(i);
                ref readonly BallState y = ref b.GetBall(i);
                Assert.True(x.Alive == y.Alive, "ball " + i + " alive differs at tick " + tick);
                AssertSameBits(x.Position.X, y.Position.X, "ball " + i + " x", tick);
                AssertSameBits(x.Position.Y, y.Position.Y, "ball " + i + " y", tick);
                AssertSameBits(x.Velocity.X, y.Velocity.X, "ball " + i + " vx", tick);
                AssertSameBits(x.Velocity.Y, y.Velocity.Y, "ball " + i + " vy", tick);
                AssertSameBits(x.Angle, y.Angle, "ball " + i + " angle", tick);
                AssertSameBits(x.AngularVelocity, y.AngularVelocity, "ball " + i + " spin", tick);
            }
            ReadOnlySpan<SimEvent> ea = a.Events, eb = b.Events;
            Assert.True(ea.Length == eb.Length, "event count differs at tick " + tick + ": " + ea.Length + " vs " + eb.Length);
            for (int k = 0; k < ea.Length; k++)
            {
                SimEvent x = ea[k], y = eb[k];
                string what = "event " + k + " (" + x.Kind + " " + x.A + "/" + x.B + ")";
                Assert.True(x.Kind == y.Kind && x.A == y.A && x.B == y.B && x.Body == y.Body && x.Tick == y.Tick && x.Feature == y.Feature,
                    what + " differs at tick " + tick + ": " + y.Kind + " " + y.A + "/" + y.B);
                AssertSameBits(x.Point.X, y.Point.X, what + " point x", tick);
                AssertSameBits(x.Point.Y, y.Point.Y, what + " point y", tick);
                AssertSameBits(x.Normal.X, y.Normal.X, what + " normal x", tick);
                AssertSameBits(x.Normal.Y, y.Normal.Y, what + " normal y", tick);
                AssertSameBits(x.Impulse, y.Impulse, what + " impulse", tick);
                AssertSameBits(x.Speed, y.Speed, what + " speed", tick);
                AssertSameBits(x.LocalAngle, y.LocalAngle, what + " angle", tick);
            }
        }

        /// <summary>
        /// Steps <paramref name="build"/>'s world with the broadphase and with the exhaustive scans in lockstep, comparing every
        /// tick; <paramref name="beforeStep"/> drives both identically (mode input). Returns the fraction of ticks whose last
        /// substep used the wall / sensor index and the sparse ball grid (so a scene cannot silently test the reference path
        /// only), and the number of events.
        /// </summary>
        static (float wallIndexed, float sensorIndexed, int events, float sparse) RunLockstep(Func<PhysicsWorld> build, int ticks, Action<PhysicsWorld, int> beforeStep = null,
            int threshold = -1)
        {
            PhysicsWorld fast = build();
            PhysicsWorld slow = build();
            if (threshold >= 0) fast.RadialIndexThreshold = threshold;
            slow.ExhaustiveBroadphase = true;
            int wallTicks = 0, sensorTicks = 0, events = 0, sparseTicks = 0;
            for (int t = 0; t < ticks; t++)
            {
                beforeStep?.Invoke(fast, t);
                beforeStep?.Invoke(slow, t);
                fast.Step();
                slow.Step();
                Assert.False(slow.WallIndexUsed || slow.SensorIndexUsed || slow.SparseGridUsed, "the exhaustive world used the index or the sparse grid");
                if (fast.WallIndexUsed) wallTicks++;
                if (fast.SensorIndexUsed) sensorTicks++;
                if (fast.SparseGridUsed) sparseTicks++;
                events += fast.EventCount;
                AssertSameTick(fast, slow, t);
            }
            return (wallTicks / (float)ticks, sensorTicks / (float)ticks, events, sparseTicks / (float)ticks);
        }

        /// <summary>
        /// Ring Escape-like: <paramref name="rings"/> concentric rings with gaps on their own kinematic bodies (alternating spin),
        /// a passage sensor ring outside each, balls launched from the centre under gravity. <paramref name="modeStyle"/>: the
        /// mode's construction instead — one reference ring scaled by its body (scale = radius / reference), no sensors, a
        /// compressing scale rate and rings broken (body disabled) as the run goes.
        /// </summary>
        static PhysicsWorld RingScene(int rings, int balls, float ballRadius, bool modeStyle, ulong seed)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(seed, 21);
            for (int k = 0; k < rings; k++)
            {
                float radius = 0.8f + 0.2f * k;
                float omega = (k % 2 == 0 ? 1f : -1f) * rng.NextFloat(0.5f, 2.5f);
                var gaps = new[] { AngularGap.Centered(0f, rng.NextFloat(0.25f, 0.9f)) };
                if (modeStyle)
                {
                    const float reference = 1f;
                    int body = w.AddBody(new BodyDef
                    {
                        Motion = BodyMotion.Kinematic,
                        Position = Vec2.Zero,
                        Angle = rng.NextFloat(0f, SimMath.TwoPi),
                        AngularVelocity = omega,
                        Scale = radius / reference,
                        ScaleVelocity = -0.01f * (k % 3),
                        Tag = k,
                    });
                    var m = SimTestUtil.Wall(1f, 0.1f);
                    m.Tag = k;
                    w.AddRing(body, Vec2.Zero, reference, 0.05f, gaps, m);
                }
                else
                {
                    int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, rng.NextFloat(0f, SimMath.TwoPi), omega));
                    w.AddRing(body, Vec2.Zero, radius, 0.04f, gaps, SimTestUtil.Wall(0.9f, 0.1f));
                    w.AddRing(body, Vec2.Zero, radius + 0.08f, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor(k));
                }
            }
            for (int i = 0; i < balls; i++)
            {
                w.AddBall(SimTestUtil.Ball(new Vec2(rng.NextFloat(-0.3f, 0.3f), rng.NextFloat(-0.3f, 0.3f)),
                    Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * rng.NextFloat(4f, 12f), ballRadius, 0.95f, 0.1f));
            }
            return w;
        }

        [Theory]
        [InlineData(100, 20, 0.05f, false)]
        [InlineData(100, 8, 0.12f, false)]
        [InlineData(12, 20, 0.08f, false)]
        [InlineData(60, 16, 0.07f, true)]
        public void RingEscapeScenesMatchTheExhaustiveScan(int rings, int balls, float ballRadius, bool modeStyle)
        {
            var (wall, sensor, events, sparse) = RunLockstep(() => RingScene(rings, balls, ballRadius, modeStyle, 3UL), 900, (w, t) =>
            {
                // Mode-style runs break a ring now and then (body disabled) and restore it later.
                if (!modeStyle || t % 90 != 45) return;
                int body = (t / 90) % rings;
                w.SetBodyEnabled(body, !w.GetBody(body).Enabled);
            });
            Assert.True(wall > 0.99f, "wall index used on " + wall * 100f + " % of ticks");
            if (!modeStyle) Assert.True(sensor > 0.99f, "sensor index used on " + sensor * 100f + " % of ticks");
            Assert.True(events > 100, "only " + events + " events: the scene does not exercise contacts");
            // 20 small balls spread over the ring stack: the ball grid is stored sparsely most of the time.
            if (balls == 20 && rings == 100) Assert.True(sparse > 0.5f, "sparse grid on " + sparse * 100f + " % of ticks");
        }

        /// <summary>
        /// Geometry Bounce-like: many balls in a rotating hexagon; with <paramref name="nested"/>, an inner rotating triangle with
        /// an open side (free caps: bridges and the widened candidate reach), a star and a few pegs, so the index has work to do.
        /// </summary>
        static PhysicsWorld PolygonScene(int balls, bool nested, ulong seed)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            SimTestUtil.AddPolygonArena(w, 6, 3.6f, 0.12f, 45f * SimMath.DegToRad, 0.9f, 0.2f);
            if (nested)
            {
                int inner = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0.3f, -70f * SimMath.DegToRad));
                Vec2[] tri = ShapeBuilder.RegularPolygon(3, 1.4f);
                w.AddPolyline(inner, tri, false, 0.08f, SimTestUtil.Wall(1f, 0.1f));
                w.AddPolyline(inner, ShapeBuilder.Star(5, 0.5f, 0.25f), true, 0.05f, SimTestUtil.Wall(0.8f, 0.1f));
                int pegs = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 20f * SimMath.DegToRad));
                for (int k = 0; k < 8; k++) w.AddCircle(pegs, Vec2.FromAngle(k * SimMath.TwoPi / 8f) * 2.4f, 0.12f, SimTestUtil.Wall(0.9f, 0.2f));
                w.AddSegment(pegs, new Vec2(-0.6f, -2.0f), new Vec2(0.6f, -2.0f), 0.06f, SimTestUtil.Wall(0.9f, 0.2f));
                w.AddCircle(pegs, new Vec2(0f, 2.9f), 0.3f, ColliderMaterial.Sensor(1));
            }
            w.ReserveBalls(balls);
            var rng = Rng.FromSeed(seed, 1);
            float inr = ShapeBuilder.Inradius(6, 3.6f) - 0.4f;
            for (int i = 0; i < balls; i++)
            {
                Vec2 p;
                do
                {
                    p = new Vec2(rng.NextFloat(-inr, inr), rng.NextFloat(-inr, inr));
                } while (p.Length > inr || (nested && p.Length < 1.8f));
                w.AddBall(SimTestUtil.Ball(p, new Vec2(rng.NextFloat(-5f, 5f), rng.NextFloat(-5f, 5f)), rng.NextFloat(0.1f, 0.2f), 0.9f, 0.2f));
            }
            return w;
        }

        [Theory]
        [InlineData(200, false)]
        [InlineData(120, true)]
        public void GeometryBounceScenesMatchTheExhaustiveScan(int balls, bool nested)
        {
            // Threshold 0: the index runs even for the single-arena scene (by default it starts at 8 colliders).
            var (wall, _, events, sparse) = RunLockstep(() => PolygonScene(balls, nested, 11UL), 600, threshold: 0);
            // A crowd in an arena: the dense grid.
            Assert.True(sparse == 0f, "sparse grid on " + sparse * 100f + " % of ticks");
            Assert.True(wall > 0.99f, "wall index used on " + wall * 100f + " % of ticks");
            Assert.True(events > 1000, "only " + events + " events");
        }

        /// <summary>Helix Drop-like: a tower of spin platforms with gaps, a passage sensor segment under each, swiped spin.</summary>
        static PhysicsWorld TowerScene(int platforms, int balls, ulong seed, out int[] tower)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(seed, 5);
            tower = new int[platforms];
            for (int k = 0; k < platforms; k++)
            {
                tower[k] = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(0f, -1.2f * k), Scale = 1f, Spin = rng.NextFloat(0f, SimMath.TwoPi) });
                w.AddSpinPlatform(tower[k], Vec2.Zero, 1.5f, 0.15f, new[] { AngularGap.Centered(rng.NextFloat(0f, SimMath.TwoPi), rng.NextFloat(0.7f, 1.4f)) }, SimTestUtil.Wall(0.6f, 0.3f));
                w.AddSegment(tower[k], new Vec2(-1.5f, -0.4f), new Vec2(1.5f, -0.4f), 0f, ColliderMaterial.Sensor(k));
            }
            int walls = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(walls, new Vec2(-1.7f, 2f), new Vec2(-1.7f, -1.2f * platforms), 0.1f, SimTestUtil.Wall(0.5f, 0.2f));
            w.AddSegment(walls, new Vec2(1.7f, 2f), new Vec2(1.7f, -1.2f * platforms), 0.1f, SimTestUtil.Wall(0.5f, 0.2f));
            for (int i = 0; i < balls; i++) w.AddBall(SimTestUtil.Ball(new Vec2(rng.NextFloat(-1f, 1f), 1f + 0.4f * i), Vec2.Zero, 0.15f, 0.6f, 0.3f));
            return w;
        }

        [Theory]
        [InlineData(30, 1)]
        [InlineData(30, 4)]
        public void HelixDropScenesMatchTheExhaustiveScan(int platforms, int balls)
        {
            // Both worlds are built identically, so the tower body ids are the same in each.
            int[] tower = null;
            var (wall, sensor, events, _) = RunLockstep(() => TowerScene(platforms, balls, 5UL, out tower), 900, (w, t) =>
            {
                float spin = (t / 30) % 2 == 0 ? 3f : -3f;
                for (int k = 0; k < tower.Length; k++) w.SetBodySpinVelocity(tower[k], spin);
            });
            Assert.True(wall > 0.99f && sensor > 0.99f, "index used on " + wall * 100f + " / " + sensor * 100f + " % of ticks");
            Assert.True(events >= 5 * balls, "only " + events + " events");
        }

        /// <summary>
        /// Lockstep run (<see cref="RunLockstep"/>) that also checks the index re-sorts on collider motion alone: after the
        /// first tick no collider's membership may change, and at least <paramref name="minRebuildFraction"/> of the substeps
        /// must still re-sort the index (the intervals move). QA (wedge restitution fix, round 1): with the interval-change
        /// branch no longer invalidating the index, every scene above stayed green — their colliders either keep their radial
        /// intervals or change membership often enough to force rebuilds — while 60 compressing rings diverged from the
        /// exhaustive path at tick 72 and a rising tower at tick 66.
        /// </summary>
        static void RunMovingLockstep(Func<PhysicsWorld> build, int ticks, Action<PhysicsWorld, int> beforeStep, float minRebuildFraction)
        {
            PhysicsWorld probe = build();
            beforeStep?.Invoke(probe, 0);
            probe.Step();
            int membershipAfterFirstTick = probe.RadialMembershipChanges, rebuildsAfterFirstTick = probe.RadialIndexRebuilds;
            int substeps = probe.Settings.Substeps;
            var (wall, sensor, events, _) = RunLockstep(build, ticks, beforeStep);
            Assert.True(wall > 0.99f && sensor > 0.99f, "index used on " + wall * 100f + " / " + sensor * 100f + " % of ticks");
            Assert.True(events > 100, "only " + events + " events");
            // The same run again outside the lockstep, for the index counters (deterministic: identical to the run above).
            for (int t = 1; t < ticks; t++)
            {
                beforeStep?.Invoke(probe, t);
                probe.Step();
            }
            Assert.True(probe.RadialMembershipChanges == membershipAfterFirstTick,
                "membership changed in " + (probe.RadialMembershipChanges - membershipAfterFirstTick) + " substeps: the scene must move colliders only");
            int rebuilds = probe.RadialIndexRebuilds - rebuildsAfterFirstTick;
            int total = (ticks - 1) * substeps;
            Assert.True(rebuilds >= minRebuildFraction * total, "index re-sorted in only " + rebuilds + " of " + total + " substeps");
        }

        /// <summary>
        /// Ring Escape «Сжатие» (release-1 M01 compresses its rings through SetBodyScaleVelocity, no membership change): 60
        /// concentric rings, each a reference ring (radius 1 m) with a passage sensor on a body scaled to its radius, all
        /// shrinking at 4 % of their initial scale per second (0.5 m/s radially at the outer ring), the rate reversed every
        /// 4 s; 16 balls launched from the centre. No ring is ever disabled.
        /// </summary>
        [Fact]
        public void CompressingRingsWithoutMembershipChangesMatchTheExhaustiveScan()
        {
            const int rings = 60;
            float[] rate = new float[rings];
            RunMovingLockstep(() =>
            {
                var w = new PhysicsWorld(SimSettings.Default);
                var rng = Rng.FromSeed(23UL, 21);
                for (int k = 0; k < rings; k++)
                {
                    float scale = 0.8f + 0.2f * k;
                    rate[k] = -0.04f * scale;
                    int body = w.AddBody(new BodyDef
                    {
                        Motion = BodyMotion.Kinematic,
                        Position = Vec2.Zero,
                        Angle = rng.NextFloat(0f, SimMath.TwoPi),
                        AngularVelocity = (k % 2 == 0 ? 1f : -1f) * rng.NextFloat(0.5f, 2.5f),
                        Scale = scale,
                        ScaleVelocity = rate[k],
                    });
                    w.AddRing(body, Vec2.Zero, 1f, 0.04f / scale, new[] { AngularGap.Centered(0f, rng.NextFloat(0.25f, 0.9f)) }, SimTestUtil.Wall(0.95f, 0.1f));
                    w.AddRing(body, Vec2.Zero, 1f + 0.08f / scale, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor(k));
                }
                for (int i = 0; i < 16; i++)
                {
                    w.AddBall(SimTestUtil.Ball(new Vec2(rng.NextFloat(-0.3f, 0.3f), rng.NextFloat(-0.3f, 0.3f)),
                        Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * rng.NextFloat(4f, 12f), 0.05f, 0.95f, 0.1f));
                }
                return w;
            }, 600, (w, t) =>
            {
                if (t == 0 || t % 240 != 0) return;
                float sign = (t / 240) % 2 == 0 ? 1f : -1f;
                for (int k = 0; k < rings; k++) w.SetBodyScaleVelocity(k, sign * rate[k]);
            }, 0.95f);
        }

        /// <summary>
        /// Helix Drop tower (spin platforms with passage sensors) moving as a whole at 2 m/s, up and down in turns of 1 s, plus
        /// a row of 12 pegs sliding sideways at 1.5 m/s, against static side walls (the pivot): every platform's interval moves
        /// every substep while no collider is toggled.
        /// </summary>
        [Fact]
        public void TranslatingTowerWithoutMembershipChangesMatchesTheExhaustiveScan()
        {
            int[] tower = null;
            int pegs = -1;
            RunMovingLockstep(() =>
            {
                PhysicsWorld w = TowerScene(30, 6, 13UL, out tower);
                pegs = w.AddBody(BodyDef.Kinematic(new Vec2(0f, -6f), 0f, 0f));
                for (int k = 0; k < 12; k++) w.AddCircle(pegs, new Vec2(-1.4f + 0.25f * k, 0.3f * (k % 3)), 0.06f, SimTestUtil.Wall(0.8f, 0.2f));
                return w;
            }, 600, (w, t) =>
            {
                float up = (t / 60) % 2 == 0 ? 2f : -2f;
                float spin = (t / 30) % 2 == 0 ? 3f : -3f;
                for (int k = 0; k < tower.Length; k++)
                {
                    w.SetBodyVelocity(tower[k], new Vec2(0f, up), 0f);
                    w.SetBodySpinVelocity(tower[k], spin);
                }
                w.SetBodyVelocity(pegs, new Vec2((t / 45) % 2 == 0 ? 1.5f : -1.5f, 0f), 0f);
            }, 0.95f);
        }

        /// <summary>Rejects contacts with collider <see cref="OneWay"/> from below, kills balls touching <see cref="Deadly"/>, toggles <see cref="Toggle"/> on every 7th hit.</summary>
        sealed class MixedHook : ISimContactHook
        {
            public int OneWay = -1, Deadly = -1, Toggle = -1;
            int _hits;

            public bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact)
            {
                if (collider == Deadly)
                {
                    world.KillBall(ball);
                    return false;
                }
                if (++_hits % 7 == 0 && Toggle >= 0) world.SetColliderEnabled(Toggle, !world.IsColliderEnabled(Toggle));
                return collider != OneWay || contact.Normal.Y > 0f;
            }

            public bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact) => true;
        }

        /// <summary>
        /// Every collider kind as walls and as sensors, rings with offset centres, a linearly moving body, a scaling body, a
        /// scripted (MoveBodyTo) body, collision filters, an open polyline (free caps) and the hook above.
        /// </summary>
        static PhysicsWorld MixedScene(ulong seed, out int scripted)
        {
            var w = new PhysicsWorld(SimSettings.Default);
            var rng = Rng.FromSeed(seed, 9);
            int box = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddPolyline(box, ShapeBuilder.Rectangle(12f, 12f), true, 0.2f, SimTestUtil.Wall(0.9f, 0.2f));
            int spinner = w.AddBody(BodyDef.Kinematic(new Vec2(-2.5f, 2f), 0f, 1.7f));
            w.AddRing(spinner, new Vec2(0.4f, 0f), 1.1f, 0.05f, new[] { AngularGap.Centered(0f, 0.5f), AngularGap.Centered(3f, 0.3f) }, SimTestUtil.Wall(1f, 0.1f));
            w.AddRing(spinner, new Vec2(0.4f, 0f), 0.6f, 0f, ReadOnlySpan<AngularGap>.Empty, ColliderMaterial.Sensor(2));
            int slider = w.AddBody(BodyDef.Kinematic(new Vec2(2f, -3f), 0f, 0f));
            w.SetBodyVelocity(slider, new Vec2(0.8f, 0.3f), -0.5f);
            w.AddSegment(slider, new Vec2(-1f, 0f), new Vec2(1f, 0f), 0.1f, SimTestUtil.Wall(0.7f, 0.3f));
            w.AddPolyline(slider, new[] { new Vec2(-1f, 1f), new Vec2(0f, 1.6f), new Vec2(1f, 1f) }, false, 0.06f, SimTestUtil.Wall(0.8f, 0.2f));
            w.AddPolyline(slider, ShapeBuilder.Rectangle(0.6f, 0.6f, new Vec2(0f, -0.8f)), true, 0f, ColliderMaterial.Sensor(3));
            int grower = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(2.5f, 2.5f), Scale = 0.6f, ScaleVelocity = 0.05f, AngularVelocity = -1f });
            w.AddPolyline(grower, ShapeBuilder.RegularPolygon(5, 1f), true, 0.08f, SimTestUtil.Wall(0.9f, 0.1f));
            w.AddCircle(grower, Vec2.Zero, 0.25f, SimTestUtil.Wall(0.9f, 0.1f));
            w.AddCircle(grower, new Vec2(0f, 1.5f), 0.3f, ColliderMaterial.Sensor(4));
            scripted = w.AddBody(BodyDef.Kinematic(new Vec2(0f, 4.5f), 0f, 0f));
            var filtered = SimTestUtil.Wall(0.9f, 0.2f);
            filtered.Category = 2u;
            w.AddSegment(scripted, new Vec2(-1.2f, 0f), new Vec2(1.2f, 0f), 0.08f, filtered);
            w.AddSegment(scripted, new Vec2(-0.8f, -0.5f), new Vec2(0.8f, -0.5f), 0.02f, ColliderMaterial.Sensor(5));
            int platform = w.AddBody(new BodyDef { Motion = BodyMotion.Kinematic, Position = new Vec2(-2.5f, -3.5f), Scale = 1f, SpinVelocity = 2f });
            w.AddSpinPlatform(platform, Vec2.Zero, 1.2f, 0.12f, new[] { AngularGap.Centered(0f, 1f) }, SimTestUtil.Wall(0.6f, 0.3f));
            w.AddSpinPlatform(platform, new Vec2(0f, -0.8f), 1.2f, 0.1f, new[] { AngularGap.Centered(2f, 1f) }, ColliderMaterial.Sensor(6));
            int pegs = w.AddBody(BodyDef.Static(Vec2.Zero));
            for (int k = 0; k < 6; k++) w.AddCircle(pegs, new Vec2(-4.5f + 1.8f * k, -1f), 0.15f, SimTestUtil.Wall(0.9f, 0.2f));
            int oneWay = w.AddSegment(pegs, new Vec2(-5f, 0.5f), new Vec2(-3.5f, 0.5f), 0.05f, SimTestUtil.Wall(0.5f, 0.2f));
            int deadly = w.AddSegment(pegs, new Vec2(3.5f, -5.6f), new Vec2(5.5f, -5.6f), 0.05f, SimTestUtil.Wall(0.5f, 0.2f));
            int toggle = w.AddCircle(pegs, new Vec2(0f, 0.5f), 0.3f, SimTestUtil.Wall(0.9f, 0.2f));
            w.ContactHook = new MixedHook { OneWay = oneWay, Deadly = deadly, Toggle = toggle };
            for (int i = 0; i < 60; i++)
            {
                var d = SimTestUtil.Ball(new Vec2(rng.NextFloat(-5f, 5f), rng.NextFloat(-5f, 5f)), Vec2.FromAngle(rng.NextFloat(0f, SimMath.TwoPi)) * rng.NextFloat(2f, 9f),
                    rng.NextFloat(0.06f, 0.3f), 0.9f, 0.2f);
                if (i % 5 == 0) d.Mask = ~2u;
                w.AddBall(d);
            }
            return w;
        }

        /// <summary>
        /// Ring Escape with 12 rings over 30 s: the balls leave the stack and fall away without a floor, so the dense ball grid
        /// would grow to its 65 536-cell cap; the sparse grid must give the same pairs.
        /// </summary>
        [Fact]
        public void EscapedBallsFallingAwayMatchTheDenseGrid()
        {
            var (_, _, _, sparse) = RunLockstep(() => RingScene(12, 20, 0.08f, false, 7UL), 1800);
            Assert.True(sparse > 0.3f, "sparse grid on " + sparse * 100f + " % of ticks");
        }

        [Fact]
        public void MixedSceneMatchesTheExhaustiveScan()
        {
            int scriptedBody = -1;
            var (wall, sensor, events, _) = RunLockstep(() => MixedScene(17UL, out scriptedBody), 1200, (w, t) =>
            {
                // Scripted motion: a sliding gate.
                float x = 2f * MathF.Sin(t * 0.03f);
                w.MoveBodyTo(scriptedBody, new Vec2(x, 4.5f), 0.4f * MathF.Sin(t * 0.05f));
            }, threshold: 0);
            Assert.True(wall > 0.99f && sensor > 0.99f, "index used on " + wall * 100f + " / " + sensor * 100f + " % of ticks");
            Assert.True(events > 500, "only " + events + " events");
        }

        /// <summary>
        /// Steady state with the radial indexes (walls and sensors), the sensor pre-check, the edge frames and both ball-grid
        /// layouts in use allocates nothing: the 100-ring scene (balls spreading over the stack: sparse and dense grid) and
        /// the mixed scene (every collider kind, the hook toggling a collider).
        /// </summary>
        [Fact]
        public void BroadphaseDoesNotAllocateInSteadyState()
        {
            int scripted = -1;
            foreach (bool mixed in new[] { false, true })
            {
                PhysicsWorld w = mixed ? MixedScene(17UL, out scripted) : RingScene(100, 20, 0.05f, false, 3UL);
                w.RadialIndexThreshold = 0;
                int sparse = 0, dense = 0;
                long before = 0;
                // Warm-up: the event and sensor-pair buffers grow while the balls first spread over the stack.
                const int warm = 1200;
                for (int t = 0; t < warm + 600; t++)
                {
                    if (t == warm) before = GC.GetAllocatedBytesForCurrentThread();
                    if (mixed) w.MoveBodyTo(scripted, new Vec2(2f * MathF.Sin(t * 0.03f), 4.5f), 0f);
                    w.Step();
                    if (t < warm) continue;
                    Assert.True(w.WallIndexUsed && w.SensorIndexUsed);
                    if (w.SparseGridUsed) sparse++;
                    else dense++;
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated == 0L, (mixed ? "mixed" : "rings") + " scene allocated " + allocated + " bytes in 600 ticks (sparse " + sparse + ", dense " + dense + ")");
                if (!mixed) Assert.True(sparse > 0, "the ring scene never used the sparse grid");
            }
        }

        /// <summary>The index culls: for a ball among 100 concentric rings it must leave only the rings near the ball's radius.</summary>
        [Fact]
        public void RingIndexKeepsOnlyRingsNearTheBall()
        {
            var w = new PhysicsWorld(SimSettings.Default);
            for (int k = 0; k < 100; k++)
            {
                int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, 1f));
                w.AddRing(body, Vec2.Zero, 0.8f + 0.2f * k, 0.04f, new[] { AngularGap.Centered(0f, 0.6f) }, SimTestUtil.Wall(0.9f, 0.1f));
            }
            w.AddBall(SimTestUtil.Ball(new Vec2(0f, 10.9f), new Vec2(1f, 0f), 0.05f));
            w.Step();
            Assert.True(w.WallIndexUsed);
            int scanned = w.CountRadialCandidates(new Vec2(0f, 10.9f), new Vec2(0.02f, 10.9f), 0.15f, false);
            Assert.InRange(scanned, 1, 3);
        }
    }
}
