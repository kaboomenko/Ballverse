using System;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>
    /// Headless M01 run with the card's invariants checked after every tick (spec §2.9 M01 test (б), §8.5): no ball crosses a
    /// solid ring (every crossing of a ring's centre line happens inside one of its gaps), no ball is larger than d_grow, ring
    /// surfaces stay within 25 m/s (and the «Сжатие» shift within 3 m/s), rings count once, and the run ends by one of its end
    /// conditions within 180 s.
    /// </summary>
    internal static class RingEscapeProbe
    {
        internal sealed class Result
        {
            public RingEscapeScene Scene;
            public SimRunner Runner;
            public int Crossings;
            public bool Escaped => Scene.EscapeTick >= 0;
        }

        public static Result RunChecked(ParamSet values, ulong seed, int maxTicks = SimConstants.MaxDurationTicks)
        {
            IModeDefinition mode = RingEscapeMode.Instance;
            var world = new PhysicsWorld();
            var events = new ModeEventBuffer { WantsRender = false, WantsSound = false, WantsReplay = false };
            var scene = (RingEscapeScene)mode.BuildScene(world, new ModeSceneContext(mode, values, seed, new ModeVisualTable(), events));
            var runner = new SimRunner(world, scene);
            runner.Start();
            RingEscapeConfig c = scene.Config;
            int rings = c.RingCount;
            float halfT = c.RingThickness * 0.5f;
            var prevAngle = new float[rings];
            var prevRadius = new float[rings];
            var prevSolid = new bool[rings];
            var prevPos = new Vec2[256];
            var prevAlive = new bool[256];
            var res = new Result { Scene = scene, Runner = runner };
            int passedBefore = 0;
            int roundBefore = 0;
            while (!runner.IsFinished && runner.Tick < maxTicks)
            {
                int ballsBefore = world.BallCount;
                if (ballsBefore > prevPos.Length)
                {
                    Array.Resize(ref prevPos, ballsBefore * 2);
                    Array.Resize(ref prevAlive, ballsBefore * 2);
                }
                Assert.True(ballsBefore <= 64, "balls keep accumulating: " + ballsBefore);
                for (int j = 0; j < rings; j++)
                {
                    ref readonly PhysicsBodyState b = ref world.GetBody(scene.RingBody(j));
                    prevAngle[j] = b.Angle;
                    prevRadius[j] = b.Scale * RingEscapeScene.ReferenceRadius;
                    prevSolid[j] = scene.IsRingInSolver(j) && !scene.IsRingDestroyed(j);
                }
                for (int i = 0; i < ballsBefore; i++)
                {
                    prevPos[i] = world.GetBall(i).Position;
                    prevAlive[i] = world.GetBall(i).Alive;
                }
                int current = scene.CurrentRing;
                runner.Step();

                bool newRound = scene.Round != roundBefore;
                roundBefore = scene.Round;
                Assert.True(scene.RingsPassed >= passedBefore, "rings un-passed");
                passedBefore = scene.RingsPassed;
                if (newRound) continue; // balls were teleported back to ring 0

                for (int j = 0; j < rings; j++)
                {
                    if (!scene.IsRingInSolver(j) || scene.IsRingDestroyed(j)) continue;
                    ref readonly PhysicsBodyState b = ref world.GetBody(scene.RingBody(j));
                    float r = b.Scale * RingEscapeScene.ReferenceRadius;
                    Assert.True(MathF.Abs(b.AngularVelocity) * (r + halfT) <= RingEscapeGeometry.MaxWallSpeed + 1e-3f, "ring " + j + " wall speed");
                    Assert.True(MathF.Abs(b.ScaleVelocity) * RingEscapeScene.ReferenceRadius <= c.RingSpacing / RingEscapeGeometry.ShiftSeconds + 1e-3f, "shift speed");
                }
                float growLimit = 0.5f * scene.GrowLimit(current) + 1e-5f;
                for (int i = scene.RoundFirstBall; i < ballsBefore; i++)
                {
                    ref readonly BallState s = ref world.GetBall(i);
                    if (!s.Alive || !prevAlive[i]) continue;
                    Assert.True(s.Radius <= MathF.Max(growLimit, c.BallSize * 0.5f + 1e-5f), "ball " + i + " larger than d_grow");
                    for (int j = 0; j < rings; j++)
                    {
                        if (!prevSolid[j]) continue;
                        float before = prevPos[i].Length - prevRadius[j];
                        ref readonly PhysicsBodyState b = ref world.GetBody(scene.RingBody(j));
                        float rNow = b.Scale * RingEscapeScene.ReferenceRadius;
                        float after = s.Position.Length - rNow;
                        if (before == 0f || after == 0f || (before > 0f) == (after > 0f)) continue;
                        res.Crossings++;
                        float f = before / (before - after);
                        Vec2 p = Vec2.Lerp(prevPos[i], s.Position, f);
                        float dAngle = SimMath.WrapPi(b.Angle - prevAngle[j]);
                        float ringAngle = prevAngle[j] + dAngle * f;
                        float local = SimMath.Wrap2Pi(SimMath.Atan2(p.Y, p.X) - ringAngle);
                        float moved = Vec2.Distance(prevPos[i], s.Position) / MathF.Max(0.1f, rNow);
                        float tol = 0.03f + MathF.Abs(dAngle) + moved;
                        Assert.True(InGap(local, c.GapCount, RingEscapeGeometry.GapDeg(c, j) * SimMath.DegToRad, tol),
                            "ball " + i + " crossed solid ring " + j + " at tick " + runner.Tick + " (local " + local / SimMath.DegToRad + "°)");
                    }
                }
            }
            Assert.True(runner.IsFinished || runner.Tick >= maxTicks);
            if (runner.IsFinished)
            {
                Assert.NotEqual(SimOutcome.None, scene.Result.Outcome);
                Assert.True(runner.Tick <= SimConstants.MaxDurationTicks);
            }
            if (c.AfterEscape == AfterEscapeKind.Finale)
            {
                Assert.True(scene.RingsPassed <= rings, "a ring counted twice");
                if (scene.EscapeTick >= 0) Assert.Equal(rings, scene.RingsPassed);
            }
            return res;
        }

        static bool InGap(float local, int gapCount, float width, float tol)
        {
            float pitch = SimMath.TwoPi / gapCount;
            for (int k = 0; k < gapCount; k++)
            {
                float rel = SimMath.Wrap2Pi(local - k * pitch + tol);
                if (rel <= width + 2f * tol) return true;
            }
            return false;
        }
    }
}
