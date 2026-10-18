using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    public class EventTests
    {
        [Fact]
        public void EventsAreSortedByKindThenIdsAndUniquePerPair()
        {
            var w = SimTestUtil.HexagonScene(200);
            var sensorBody = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddCircle(sensorBody, Vec2.Zero, 1.5f, ColliderMaterial.Sensor(1));
            int total = 0;
            var kinds = new HashSet<SimEventKind>();
            for (int t = 0; t < 240; t++)
            {
                w.Step();
                var seen = new HashSet<(SimEventKind, int, int)>();
                for (int i = 0; i < w.EventCount; i++)
                {
                    ref readonly SimEvent e = ref w.GetEvent(i);
                    Assert.Equal(t, e.Tick);
                    Assert.True(seen.Add((e.Kind, e.A, e.B)), "duplicate event for a pair in one tick");
                    kinds.Add(e.Kind);
                    if (i > 0)
                    {
                        ref readonly SimEvent p = ref w.GetEvent(i - 1);
                        int c = ((int)p.Kind).CompareTo((int)e.Kind);
                        if (c == 0) c = p.A.CompareTo(e.A);
                        if (c == 0) c = p.B.CompareTo(e.B);
                        Assert.True(c < 0, "events out of order at " + i);
                    }
                    if (e.Kind == SimEventKind.BallHit) Assert.True(e.A < e.B);
                    if (e.Kind == SimEventKind.Hit || e.Kind == SimEventKind.BallHit)
                    {
                        Assert.True(e.Impulse > 0f);
                        Assert.True(e.Speed >= w.Settings.HitEventMinSpeed);
                    }
                }
                total += w.EventCount;
            }
            Assert.True(total > 1000);
            Assert.Contains(SimEventKind.Hit, kinds);
            Assert.Contains(SimEventKind.BallHit, kinds);
            Assert.Contains(SimEventKind.SensorEnter, kinds);
            Assert.Contains(SimEventKind.SensorExit, kinds);
        }

        [Fact]
        public void EventStreamIsIdenticalAcrossRuns()
        {
            List<SimEvent> Run()
            {
                var w = SimTestUtil.HexagonScene(150, seed: 77UL);
                var all = new List<SimEvent>();
                for (int t = 0; t < 180; t++)
                {
                    w.Step();
                    all.AddRange(SimTestUtil.CopyEvents(w));
                }
                return all;
            }
            var a = Run();
            var b = Run();
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].Kind, b[i].Kind);
                Assert.Equal(a[i].A, b[i].A);
                Assert.Equal(a[i].B, b[i].B);
                Assert.Equal(a[i].Impulse, b[i].Impulse);
                Assert.Equal(a[i].Point, b[i].Point);
            }
        }

        [Fact]
        public void RestingContactsDoNotSpamHitEvents()
        {
            var w = new PhysicsWorld();
            int floor = w.AddBody(BodyDef.Static(Vec2.Zero));
            w.AddSegment(floor, new Vec2(-5f, 0f), new Vec2(5f, 0f), 0f, SimTestUtil.Wall());
            w.AddBall(SimTestUtil.Ball(new Vec2(0f, 1f), Vec2.Zero, 0.2f, 0.3f, 0.3f));
            for (int t = 0; t < 180; t++) w.Step();
            int late = 0;
            for (int t = 0; t < 60; t++)
            {
                w.Step();
                late += w.EventCount;
            }
            Assert.Equal(0, late);
        }

        [Fact]
        public void ThinSensorLineCrossedWithinOneTickReportsEnterThenExit()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity(1, 60f));
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int line = w.AddSegment(body, new Vec2(0.5f, -1f), new Vec2(0.5f, 1f), 0f, ColliderMaterial.Sensor(5));
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(60f, 0f), 0.05f));
            w.Step();
            Assert.Equal(2, w.EventCount);
            Assert.Equal(SimEventKind.SensorEnter, w.GetEvent(0).Kind);
            Assert.Equal(SimEventKind.SensorExit, w.GetEvent(1).Kind);
            Assert.Equal(b, w.GetEvent(0).A);
            Assert.Equal(line, w.GetEvent(0).B);
            Assert.Equal(body, w.GetEvent(0).Body);
            Assert.False(w.IsTouchingSensor(b, line));
            // The wall-less sensor never deflects the ball.
            Assert.Equal(60f, w.GetBall(b).Velocity.X, 3);
        }

        [Fact]
        public void AreaSensorReportsEnterOnceStayAndExit()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int zone = w.AddPolyline(body, ShapeBuilder.Rectangle(2f, 2f, new Vec2(3f, 0f)), true, 0f, ColliderMaterial.Sensor());
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, new Vec2(3f, 0f), 0.1f));
            var log = new List<(int tick, SimEventKind kind)>();
            bool insideSeen = false;
            for (int t = 0; t < 150; t++)
            {
                w.Step();
                foreach (var e in w.Events) log.Add((t, e.Kind));
                if (w.GetBall(b).Position.X > 2.5f && w.GetBall(b).Position.X < 3.5f)
                {
                    Assert.True(w.IsTouchingSensor(b, zone));
                    insideSeen = true;
                }
            }
            Assert.True(insideSeen);
            Assert.Equal(2, log.Count);
            Assert.Equal(SimEventKind.SensorEnter, log[0].kind);
            Assert.Equal(SimEventKind.SensorExit, log[1].kind);
            // Enters when the ball's edge reaches x = 2 (t ≈ 0.63 s), leaves after its edge passes x = 4 (t ≈ 1.37 s).
            Assert.InRange(log[0].tick, 36, 39);
            Assert.InRange(log[1].tick, 81, 84);
        }

        [Fact]
        public void KillingBallInsideSensorEmitsExit()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int zone = w.AddCircle(body, Vec2.Zero, 1f, ColliderMaterial.Sensor());
            int b = w.AddBall(SimTestUtil.Ball(Vec2.Zero, Vec2.Zero, 0.1f));
            w.Step();
            Assert.Equal(SimEventKind.SensorEnter, w.GetEvent(0).Kind);
            w.Step();
            Assert.Equal(0, w.EventCount);
            w.KillBall(b);
            w.Step();
            Assert.Equal(1, w.EventCount);
            Assert.Equal(SimEventKind.SensorExit, w.GetEvent(0).Kind);
            Assert.Equal(zone, w.GetEvent(0).B);
        }

        [Fact]
        public void HitImpulsesAreAggregatedPerPairPerTick()
        {
            var w = new PhysicsWorld(SimTestUtil.NoGravity());
            int body = w.AddBody(BodyDef.Static(Vec2.Zero));
            int wall = w.AddSegment(body, new Vec2(1f, -1f), new Vec2(1f, 1f), 0f, SimTestUtil.Wall(0.5f, 0f));
            var d = SimTestUtil.Ball(new Vec2(0.7f, 0f), new Vec2(10f, 0f), 0.2f, 0.5f);
            int b = w.AddBall(d);
            w.Step();
            Assert.Equal(1, w.EventCount);
            SimEvent e = w.GetEvent(0);
            Assert.Equal(SimEventKind.Hit, e.Kind);
            Assert.Equal(wall, e.B);
            float m = w.GetBall(b).Mass;
            // Momentum change m·(10 + 5) equals the summed normal impulse.
            Assert.Equal(m * 15f, e.Impulse, 2);
            Assert.Equal(10f, e.Speed, 2);
            Assert.Equal(-1f, e.Normal.X, 4);
            Assert.Equal(1f, e.Point.X, 3);
        }
    }
}
