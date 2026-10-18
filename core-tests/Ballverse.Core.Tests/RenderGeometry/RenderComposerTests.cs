using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderGeometry
{
    /// <summary>
    /// The composer end to end on real render streams: Ring Escape / Geometry Bounce in 2D, Helix Drop in pseudo-3D,
    /// preview == export (sequential playback vs seek), and zero allocations per frame.
    /// </summary>
    public class RenderComposerTests
    {
        const int Ticks = 240;

        static RenderQuantization Quant => new RenderQuantization(16f, 16f);

        /// <summary>A Ring Escape-like stream: 12 rotating fixed-thickness rings with a gap, 3 trail balls with labels.</summary>
        static byte[] RingEscapeStream(out RenderEventLog events)
        {
            RenderQuantization q = Quant;
            var ms = new MemoryStream();
            events = new RenderEventLog();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("ring_escape", 60, 60, q), leaveOpen: true))
            {
                int circle = w.DefineShape(ShapeDefinition.Circle);
                var ringShapes = new int[12];
                for (int i = 0; i < 12; i++)
                {
                    ringShapes[i] = w.DefineShape(ShapeDefinition.Ring(q.QuantizeScale(1.2f + 0.6f * i), q.QuantizeScale(0.12f),
                        new[] { RingGap.FromRadians(-0.3f, 0.3f) }, fixedThickness: true));
                }
                int[] labels = { w.DefineLabel("1"), w.DefineLabel("2"), w.DefineLabel("3") };
                var f = new RenderFrame();
                for (int t = 0; t < Ticks; t++)
                {
                    f.Clear();
                    for (int i = 0; i < 12; i++)
                    {
                        float shrink = 1f - Math.Min(0.3f, t * 0.001f);
                        BodyFlags flags = BodyFlags.Visible | (i == 0 && t > 100 ? BodyFlags.Dead : BodyFlags.None);
                        f.AddBody(q.QuantizeBody(100 + i, ringShapes[i], 0f, 0f, t * 0.02f * (i % 2 == 0 ? 1 : -1), shrink, (byte)(PaletteSlots.ThemeDecorFirst + i), flags));
                    }
                    for (int b = 0; b < 3; b++)
                    {
                        float a = t * 0.05f + b * 2f;
                        f.AddBody(q.QuantizeBody(b, circle, MathF.Cos(a) * (0.5f + b * 0.2f), MathF.Sin(a) * 0.6f, a, 0.3f, (byte)b,
                            BodyFlags.Visible | BodyFlags.Trail | (b == 0 ? BodyFlags.Highlight : BodyFlags.None), labels[b]));
                    }
                    w.AppendFrame(t, f);
                    if (t % 7 == 3)
                    {
                        events.Add(RenderEvent.Hit(t, 0, 1f, 0.2f, -1f, 0f, (byte)(t % 3), 0.7f, t % 3, 0.3f, 101, 0, 0.1f));
                    }
                    if (t == 120) events.Add(RenderEvent.Break(t, 1, 100, ringShapes[0], 0f, 0f, 2.4f, 0.88f, PaletteSlots.ThemeDecorFirst, 1f));
                }
                w.Finish();
            }
            return ms.ToArray();
        }

        /// <summary>A Helix Drop-like stream: 20 platforms (ring bodies at heights), one falling ball, splats and a break.</summary>
        static byte[] HelixStream(out RenderEventLog events)
        {
            var q = new RenderQuantization(64f, 8f);
            var ms = new MemoryStream();
            events = new RenderEventLog();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("helix_drop", 60, 60, q), leaveOpen: true))
            {
                int circle = w.DefineShape(ShapeDefinition.Circle);
                int platform = w.DefineShape(ShapeDefinition.Ring(q.QuantizeScale(1.5f), q.QuantizeScale(1.8f), new[] { RingGap.FromRadians(-0.5f, 0.5f) }));
                var f = new RenderFrame();
                for (int t = 0; t < Ticks; t++)
                {
                    f.Clear();
                    float ballY = 0.6f - t * 0.06f;
                    for (int k = 0; k < 20; k++)
                    {
                        float h = -k * 1.8f;
                        if (h > ballY + 1.5f) continue;   // passed platforms are gone
                        f.AddBody(q.QuantizeBody(10 + k, platform, 0f, h, t * 0.03f + k, 1f, PaletteSlots.Arena, BodyFlags.Visible));
                    }
                    f.AddBody(q.QuantizeBody(1, circle, 0f, ballY, 0f, 0.25f, 0, BodyFlags.Visible | BodyFlags.Trail));
                    w.AppendFrame(t, f);
                    if (t % 30 == 0)
                    {
                        events.Add(RenderEvent.Hit(t, 0, 0f, ballY - 0.25f, 0f, 1f, 0, 0.8f, 1, 0.25f, 10 + t / 30, 0, 0f));
                        RenderEvent splat = RenderEvent.Create(t, RenderEventType.Splat, 1, 0f, ballY, 0, 1f);
                        splat.TargetBodyId = 10 + t / 30;
                        splat.LocalAngle = 0.2f;
                        splat.Size = 0.3f;
                        events.Add(splat);
                    }
                    if (t == 60) events.Add(RenderEvent.Break(t, 2, 12, platform, 0f, -3.6f, 1.2f, 1f, PaletteSlots.Arena, 1f));
                }
                w.Finish();
            }
            return ms.ToArray();
        }

        static ComposeSettings Settings(ModeView view, QualityProfile quality = null)
        {
            var s = new ComposeSettings { View = view, Quality = quality ?? QualityProfile.Export, CanvasWidth = 1080, CanvasHeight = 1920 };
            s.Look.Trail = TrailStyle.Long;
            s.Look.Labels = LabelMode.Numbers;
            s.Look.Impact = ImpactFx.Sparks;
            return s;
        }

        static RenderComposer Bind(RenderStreamReader reader)
        {
            var c = new RenderComposer();
            c.SetStream(reader.Header.Quantization, reader.Shapes, reader.Header.Grid);
            return c;
        }

        static void ComposeAt(RenderComposer c, RenderStreamReader reader, RenderStreamFrameSource source, RenderFrame frame, RenderEventLog log, int tick, ComposeSettings s)
        {
            reader.ReadFrame(tick, frame);
            c.Sync(tick, frame, source, s);
            c.Compose(tick, frame, log.GetWindow(tick, VfxSystem.MaxEffectTicks), s);
        }

        [Fact]
        public void RingEscapeFrameHasShapesBallsTrailsParticlesAndLabels()
        {
            byte[] data = RingEscapeStream(out RenderEventLog log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                RenderComposer c = Bind(reader);
                var source = new RenderStreamFrameSource(reader);
                var frame = new RenderFrame();
                ComposeSettings s = Settings(ModeView.Standard2D);
                ComposeAt(c, reader, source, frame, log, 125, s);
                Assert.Equal(3, c.Balls.Length);
                Assert.Equal(3, c.Labels.Length);
                Assert.True(c.Under.IndexCount > 12 * 6 * 20, "rings tessellated: " + c.Under.IndexCount);
                Assert.Equal(0, c.Over.IndexCount);
                Assert.True(c.Trails.IndexCount > 0);
                Assert.True(c.Particles.Length > 0);   // break shards at tick 120 + sparks
                Assert.Equal(new ViewRect(0f, 0f, 9f, 16f), c.View);
                Assert.Equal(120f, c.PixelsPerMetre, 3);
                Assert.True(c.BloomIntensity > 0f);
                // Highlighted ball glows more; labels contrast with the ball colour.
                Assert.True(c.Balls[0].Emission > c.Balls[1].Emission);
                Assert.Equal(ColorMath.ContrastText(c.Balls[1].Color) | 0xFF, c.Labels[1].Color | 0xFF);
                // Fixed camera: ball positions are stream positions.
                frame.TryGetBody(1, out BodyState b1);
                Assert.Equal(reader.Header.Quantization.DequantizePosition(b1.X), c.Balls[1].X, 4);
            }
        }

        [Fact]
        public void LabelsOffAndQualityCHaveFewerItems()
        {
            byte[] data = RingEscapeStream(out RenderEventLog log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                RenderComposer c = Bind(reader);
                var frame = new RenderFrame();
                ComposeSettings full = Settings(ModeView.Standard2D);
                ComposeAt(c, reader, new RenderStreamFrameSource(reader), frame, log, 125, full);
                int fullParticles = c.Particles.Length;
                int fullTrail = c.Trails.VertexCount;

                ComposeSettings low = Settings(ModeView.Standard2D, QualityProfile.C);
                low.Look.Labels = LabelMode.Off;
                RenderComposer c2 = Bind(reader);
                ComposeAt(c2, reader, new RenderStreamFrameSource(reader), frame, log, 125, low);
                Assert.Equal(0, c2.Labels.Length);
                Assert.True(c2.Particles.Length < fullParticles);
                Assert.True(c2.Trails.VertexCount < fullTrail);
                Assert.Equal(0f, c2.BloomIntensity);
            }
        }

        [Theory]
        [InlineData(ModeView.Standard2D)]
        [InlineData(ModeView.Helix)]
        public void PreviewEqualsExportSequentialPlaybackMatchesSeek(ModeView view)
        {
            byte[] data = view == ModeView.Helix ? HelixStream(out RenderEventLog log) : RingEscapeStream(out log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            using (var reader2 = new RenderStreamReader(new MemoryStream(data)))
            {
                ComposeSettings s = Settings(view);
                RenderComposer sequential = Bind(reader);
                var src = new RenderStreamFrameSource(reader);
                var frame = new RenderFrame();
                foreach (int target in new[] { 75, 150, 239 })
                {
                    for (int t = target - 70; t <= target; t++) ComposeAt(sequential, reader, src, frame, log, t, s);
                    // Fresh composer, jump straight to the target (scrub / export resume).
                    RenderComposer seek = Bind(reader2);
                    var frame2 = new RenderFrame();
                    ComposeAt(seek, reader2, new RenderStreamFrameSource(reader2), frame2, log, target, s);
                    AssertSameOutput(sequential, seek);
                }
            }
        }

        static void AssertSameOutput(RenderComposer a, RenderComposer b)
        {
            Assert.Equal(a.View, b.View);
            AssertBatch(a.Under, b.Under);
            AssertBatch(a.Over, b.Over);
            AssertBatch(a.Trails, b.Trails);
            Assert.Equal(a.Balls.ToArray(), b.Balls.ToArray());
            Assert.Equal(a.Particles.ToArray(), b.Particles.ToArray());
            Assert.Equal(a.Labels.ToArray(), b.Labels.ToArray());
        }

        static void AssertBatch(GeometryBatch a, GeometryBatch b)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.IndexCount, b.IndexCount);
            ReadOnlySpan<BatchVertex> va = a.Vertices, vb = b.Vertices;
            for (int i = 0; i < va.Length; i++)
            {
                Assert.True(va[i].X == vb[i].X && va[i].Y == vb[i].Y && va[i].Rgba == vb[i].Rgba && va[i].U == vb[i].U, "vertex " + i + " differs");
            }
            Assert.True(a.Indices.SequenceEqual(b.Indices));
        }

        [Fact]
        public void HelixFrameSplitsPlatformsAroundTheBallAndFollowsIt()
        {
            byte[] data = HelixStream(out RenderEventLog log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                RenderComposer c = Bind(reader);
                var frame = new RenderFrame();
                ComposeSettings s = Settings(ModeView.Helix);
                ComposeAt(c, reader, new RenderStreamFrameSource(reader), frame, log, 200, s);
                Assert.Single(c.Balls.ToArray());
                Assert.True(c.Under.IndexCount > 0);
                Assert.True(c.Over.IndexCount > 0);   // the platform just above the ball
                // Camera follows the ball down: the ball sits near 38 % from the top.
                BallInstance ball = c.Balls[0];
                float fromTop = (c.View.MaxY - ball.Y) / c.View.Height;
                Assert.InRange(fromTop, 0.3f, 0.5f);
                Assert.True(c.Trails.IndexCount > 0);
            }
        }

        [Fact]
        public void HitFlashOverlayAndDeadDimming()
        {
            byte[] data = RingEscapeStream(out RenderEventLog log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                var frame = new RenderFrame();
                ComposeSettings s = Settings(ModeView.Standard2D);
                RenderComposer withEvents = Bind(reader);
                ComposeAt(withEvents, reader, new RenderStreamFrameSource(reader), frame, log, 101, s);   // hit at tick 101 on body 101
                RenderComposer without = Bind(reader);
                reader.ReadFrame(101, frame);
                without.Sync(101, frame, new RenderStreamFrameSource(reader), s);
                without.Compose(101, frame, ReadOnlySpan<RenderEvent>.Empty, s);
                Assert.True(withEvents.Under.VertexCount > without.Under.VertexCount);
            }
        }

        [Theory]
        [InlineData(ModeView.Standard2D)]
        [InlineData(ModeView.Helix)]
        public void SequentialPlaybackDoesNotAllocatePerFrame(ModeView view)
        {
            byte[] data = view == ModeView.Helix ? HelixStream(out RenderEventLog log) : RingEscapeStream(out log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                RenderComposer c = Bind(reader);
                var src = new RenderStreamFrameSource(reader);
                var frame = new RenderFrame();
                ComposeSettings s = Settings(view, QualityProfile.Export);
                s.Look.Impact = ImpactFx.Confetti;
                for (int t = 0; t < Ticks; t++) ComposeAt(c, reader, src, frame, log, t, s);   // warm-up: peak sizes
                ComposeAt(c, reader, src, frame, log, 0, s);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < Ticks; t++) ComposeAt(c, reader, src, frame, log, t, s);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated < 1024, "allocated " + allocated + " bytes over " + Ticks + " frames");
            }
        }

        [Fact]
        public void LiveRingSourceGivesTheSameTrailsAsTheStream()
        {
            byte[] data = RingEscapeStream(out RenderEventLog log);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                ComposeSettings s = Settings(ModeView.Standard2D);
                var ring = new RenderFrameRing(RenderComposer.TrailCapacityTicks + 2);
                RenderComposer live = Bind(reader);
                var frame = new RenderFrame();
                // Live: the sim pushes every tick, the display renders every second tick (2× time scale).
                for (int t = 0; t <= 150; t++)
                {
                    reader.ReadFrame(t, frame);
                    ring.Push(t, frame);
                    if (t % 2 == 0)
                    {
                        live.Sync(t, frame, ring, s);
                        live.Compose(t, frame, log.GetWindow(t, VfxSystem.MaxEffectTicks), s);
                    }
                }
                RenderComposer export = Bind(reader);
                ComposeAt(export, reader, new RenderStreamFrameSource(reader), frame, log, 150, s);
                AssertSameOutput(live, export);
            }
        }

        [Fact]
        public void SquareGridLayerIsDrawnFromPaletteIndices()
        {
            RenderQuantization q = RenderQuantization.Default;
            RenderGridSpec grid = RenderGridSpec.FromWorld(q, 4, 3, GridTopology.Square, -2f, -1.5f, 2f, 1.5f);
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("color_war", 60, 60, q, grid), leaveOpen: true))
            {
                w.DefineShape(ShapeDefinition.Circle);
                var f = new RenderFrame();
                f.SetGridSize(4, 3);
                f.SetCell(1, 1, 2);
                w.AppendFrame(0, f);
                w.Finish();
            }
            using (var reader = new RenderStreamReader(new MemoryStream(ms.ToArray())))
            {
                RenderComposer c = Bind(reader);
                var frame = reader.ReadFrame(0);
                ComposeSettings s = Settings(ModeView.Standard2D);
                c.Sync(0, frame, null, s);
                c.Compose(0, frame, ReadOnlySpan<RenderEvent>.Empty, s);
                Assert.Equal(12 * 4, c.Under.VertexCount);
                Assert.Equal(c.Palette[2], c.Under.Vertices[(1 * 4 + 1) * 4].Rgba);
            }
        }

        [Fact]
        public void HistoryWindowDependsOnTrailAndCamera()
        {
            var s = new ComposeSettings();
            s.Look.Trail = TrailStyle.Off;
            Assert.Equal(0, RenderComposer.HistoryTicks(s));
            s.Look.Trail = TrailStyle.Long;
            Assert.Equal(60, RenderComposer.HistoryTicks(s));
            s.View = ModeView.Helix;
            Assert.Equal(CameraTrack.MinWindowTicks + 54, RenderComposer.HistoryTicks(s));
            Assert.Equal(ModeView.Helix, ModeViews.ForMode("helix_drop"));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode("ring_escape"));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode("geometry_bounce"));
        }

        [Fact]
        public void GeometryBounceFaceFlashSpawnAndFinish()
        {
            RenderQuantization q = Quant;
            var hexagon = new ShapePoint[6];
            for (int i = 0; i < 6; i++) hexagon[i] = q.QuantizePoint(MathF.Cos(i * MathF.PI / 3f) * 4f, MathF.Sin(i * MathF.PI / 3f) * 4f);
            var shapes = new List<ShapeDefinition> { ShapeDefinition.Circle, ShapeDefinition.Polyline(hexagon, true, q.QuantizeScale(0.15f)) };
            var frame = new RenderFrame();
            frame.AddBody(q.QuantizeBody(1, 0, 0.5f, -1f, 0f, 0.5f, 0, BodyFlags.Visible | BodyFlags.Trail));
            frame.AddBody(q.QuantizeBody(50, 1, 0f, 0f, 0.4f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
            frame.Sort();
            var c = new RenderComposer();
            c.SetStream(q, shapes);
            ComposeSettings s = Settings(ModeView.Standard2D);
            c.Sync(30, frame, null, s);
            c.Compose(30, frame, ReadOnlySpan<RenderEvent>.Empty, s);
            int plain = c.Under.VertexCount;
            Assert.Equal(0, c.Particles.Length);

            var log = new RenderEventLog();
            log.Add(RenderEvent.Hit(28, 0, 2f, -3f, 0f, 1f, 2, 0.9f, 1, 0.5f, 50, 4, 0f));
            RenderEvent spawn = RenderEvent.Create(29, RenderEventType.Spawn, 0, 1f, 1f, 3, 1f);
            spawn.Size = 0.4f;
            log.Add(spawn);
            log.Add(RenderEvent.Create(30, RenderEventType.Finish, 0, 0f, 0f, 4, 1f));
            c.Compose(30, frame, log.GetWindow(30, VfxSystem.MaxEffectTicks), s);
            Assert.Equal(plain + 4, c.Under.VertexCount);   // the hit face (segment 4) flashes as one quad
            Assert.True(c.Particles.Length > 70);           // sparks + ripple + bubble + confetti + shockwave
            Assert.Equal(1, c.Balls.Length);
            Assert.True(c.Balls[0].RadiusX < 0.5f);         // squashed by the hit two ticks ago
        }
    }
}
