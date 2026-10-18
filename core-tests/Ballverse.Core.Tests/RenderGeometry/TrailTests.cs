using System;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class TrailTests
    {
        static readonly System.Collections.Generic.List<ShapeDefinition> S = Shapes(ShapeDefinition.Circle);
        const BodyFlags TrailFlags = BodyFlags.Visible | BodyFlags.Trail;

        static RenderFrame BallAt(int id, float x, float y, BodyFlags flags = TrailFlags, byte color = 0)
        {
            return Frame(Q.QuantizeBody(id, 0, x, y, 0f, 0.25f, color, flags));
        }

        static uint[] Palette()
        {
            var p = new uint[256];
            var look = new LookSettings();
            look.ResolvePalette(p);
            return p;
        }

        [Fact]
        public void RecordsOnlyVisibleTrailBodiesAndRestartsAfterAGap()
        {
            var t = new TrailTracker(4, 10);
            for (int tick = 0; tick < 5; tick++) t.Record(tick, BallAt(1, tick * 0.1f, 0f), Q, S);
            t.Record(5, BallAt(2, 0f, 0f, BodyFlags.Visible), Q, S);   // no Trail flag
            Assert.True(t.TryGetSlot(1, out int slot));
            Assert.False(t.TryGetSlot(2, out _));
            Assert.Equal(5, t.SampleCount(slot));
            Assert.Equal(4, t.GetSample(slot, 0).Tick);
            Assert.Equal(0.4f, t.GetSample(slot, 0).X, 3);
            Assert.Equal(0.25f, t.GetSample(slot, 0).Radius, 3);

            t.Record(8, BallAt(1, 1f, 0f), Q, S);   // skipped ticks 5–7
            Assert.Equal(1, t.SampleCount(slot));
            // Ring buffer keeps only the capacity.
            for (int tick = 9; tick < 40; tick++) t.Record(tick, BallAt(1, tick * 0.1f, 0f), Q, S);
            Assert.Equal(10, t.SampleCount(slot));
            Assert.Equal(39, t.GetSample(slot, 0).Tick);
            Assert.Equal(30, t.GetSample(slot, 9).Tick);
        }

        [Fact]
        public void StaleTrailsAreReleasedAndBudgetIsRespected()
        {
            var t = new TrailTracker(2, 5);
            var f = Frame(Q.QuantizeBody(1, 0, 0, 0, 0, 0.2f, 0, TrailFlags), Q.QuantizeBody(2, 0, 1, 0, 0, 0.2f, 0, TrailFlags), Q.QuantizeBody(3, 0, 2, 0, 0, 0.2f, 0, TrailFlags));
            t.Record(0, f, Q, S);
            Assert.Equal(2, t.ActiveCount);   // body 3 over budget
            Assert.False(t.TryGetSlot(3, out _));
            for (int tick = 1; tick < 10; tick++) t.Record(tick, BallAt(3, tick, 0), Q, S);
            Assert.True(t.TryGetSlot(3, out _));
            Assert.False(t.TryGetSlot(1, out _));
        }

        [Fact]
        public void RibbonTapersAndFadesWithAge()
        {
            var t = new TrailTracker(4, 60);
            for (int tick = 0; tick <= 30; tick++) t.Record(tick, BallAt(1, tick * 0.05f, 0f), Q, S);
            var batch = new GeometryBatch();
            var proj = new PlanarProjector();
            var settings = TrailBuildSettings.Create(TrailStyle.Long, 1f, 1.5f, ColorMath.White);
            int ribbons = TrailRibbonBuilder.Build(t, 30, settings, Palette(), batch, ref proj);
            Assert.Equal(1, ribbons);
            ReadOnlySpan<BatchVertex> v = batch.Vertices;
            Assert.Equal(31 * 2, v.Length);
            Assert.Equal(30 * 6, batch.IndexCount);
            // Width and alpha decrease from head to tail.
            float prevWidth = float.MaxValue;
            int prevAlpha = 256;
            for (int i = 0; i < v.Length; i += 2)
            {
                float width = Math.Abs(v[i].Y - v[i + 1].Y);
                Assert.True(width <= prevWidth + 1e-5f);
                Assert.True(v[i].A <= prevAlpha);
                prevWidth = width;
                prevAlpha = v[i].A;
            }
            Assert.Equal(0.25f * 2f * 0.8f, Math.Abs(v[0].Y - v[1].Y), 3);   // head width = diameter × 0.8
            Assert.Equal(1.5f, v[0].U);
        }

        [Fact]
        public void TrailLengthFollowsStyleAndQuality()
        {
            Assert.Equal(15, TrailBuildSettings.Create(TrailStyle.Short, 1f, 1f, 0).LengthTicks);
            Assert.Equal(60, TrailBuildSettings.Create(TrailStyle.Long, 1f, 1f, 0).LengthTicks);
            Assert.Equal(42, TrailBuildSettings.Create(TrailStyle.Long, 0.7f, 1f, 0).LengthTicks);
            Assert.Equal(24, TrailBuildSettings.Create(TrailStyle.Rainbow, 0.4f, 1f, 0).LengthTicks);
            Assert.Equal(0, TrailBuildSettings.Create(TrailStyle.Off, 1f, 1f, 0).LengthTicks);
            Assert.True(TrailBuildSettings.Create(TrailStyle.Rainbow, 1f, 1f, 0).Rainbow);
        }

        [Fact]
        public void RestingBodyDrawsNoRibbonAndDespawnedTrailFadesOut()
        {
            var t = new TrailTracker(4, 60);
            for (int tick = 0; tick < 10; tick++) t.Record(tick, BallAt(1, 0.5f, 0.5f), Q, S);
            var batch = new GeometryBatch();
            var proj = new PlanarProjector();
            var settings = TrailBuildSettings.Create(TrailStyle.Long, 1f, 1f, 0);
            Assert.Equal(0, TrailRibbonBuilder.Build(t, 9, settings, Palette(), batch, ref proj));
            Assert.Equal(0, batch.VertexCount);

            var t2 = new TrailTracker(4, 60);
            for (int tick = 0; tick < 10; tick++) t2.Record(tick, BallAt(1, tick * 0.1f, 0f), Q, S);
            // Body gone after tick 9: at tick 30 the tail is still visible, at tick 80 it has faded completely.
            Assert.Equal(1, TrailRibbonBuilder.Build(t2, 30, settings, Palette(), batch, ref proj));
            batch.Clear();
            Assert.Equal(0, TrailRibbonBuilder.Build(t2, 80, settings, Palette(), batch, ref proj));
        }

        [Fact]
        public void BuildingRibbonsDoesNotAllocate()
        {
            var t = new TrailTracker(64, 60);
            var frame = new RenderFrame(64, 1);
            var batch = new GeometryBatch();
            var proj = new PlanarProjector();
            var settings = TrailBuildSettings.Create(TrailStyle.Rainbow, 1f, 1f, 0);
            uint[] palette = Palette();
            for (int tick = 0; tick < 200; tick++)
            {
                frame.Clear();
                for (int b = 0; b < 50; b++) frame.AddBody(Q.QuantizeBody(b, 0, MathF.Cos(tick * 0.05f + b), MathF.Sin(tick * 0.07f + b), 0, 0.2f, (byte)b, TrailFlags));
                t.Record(tick, frame, Q, S);
                batch.Clear();
                TrailRibbonBuilder.Build(t, tick, settings, palette, batch, ref proj);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 200; tick < 400; tick++)
            {
                frame.Clear();
                for (int b = 0; b < 50; b++) frame.AddBody(Q.QuantizeBody(b, 0, MathF.Cos(tick * 0.05f + b), MathF.Sin(tick * 0.07f + b), 0, 0.2f, (byte)b, TrailFlags));
                t.Record(tick, frame, Q, S);
                batch.Clear();
                TrailRibbonBuilder.Build(t, tick, settings, palette, batch, ref proj);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void RibbonsAreDrawnInBodyIdOrderWhateverTheSlotHistory()
        {
            var a = new TrailTracker(8, 30);
            var b = new TrailTracker(8, 30);
            // a meets body 5 first (slot 0), b meets body 2 first (slot 0); both trails are full length at tick 19.
            for (int t = 0; t < 20; t++)
            {
                var both = Frame(Q.QuantizeBody(2, 0, t * 0.1f, -1f, 0f, 0.25f, 1, TrailFlags), Q.QuantizeBody(5, 0, t * 0.1f, 1f, 0f, 0.25f, 0, TrailFlags));
                a.Record(t, t < 5 ? BallAt(5, t * 0.1f, 1f) : both, Q, S);
                b.Record(t, t < 5 ? BallAt(2, t * 0.1f, -1f, TrailFlags, 1) : both, Q, S);
            }
            Assert.True(a.TryGetSlot(5, out int slot5) && slot5 == 0);
            Assert.True(b.TryGetSlot(2, out int slot2) && slot2 == 0);
            var ba = new GeometryBatch();
            var bb = new GeometryBatch();
            var proj = new PlanarProjector();
            var settings = TrailBuildSettings.Create(TrailStyle.Short, 1f, 1f, 0);
            TrailRibbonBuilder.Build(a, 19, settings, Palette(), ba, ref proj);
            TrailRibbonBuilder.Build(b, 19, settings, Palette(), bb, ref proj);
            Assert.Equal(ba.Vertices.ToArray().Length, bb.Vertices.ToArray().Length);
            for (int i = 0; i < ba.VertexCount; i++)
            {
                Assert.Equal(ba.Vertices[i].X, bb.Vertices[i].X);
                Assert.Equal(ba.Vertices[i].Y, bb.Vertices[i].Y);
            }
            Assert.True(ba.Vertices[0].Y < 0f);   // body 2 (y = −1) first
        }
    }
}
