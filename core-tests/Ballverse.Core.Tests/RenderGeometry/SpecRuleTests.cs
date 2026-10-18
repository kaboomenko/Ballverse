using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    /// <summary>
    /// Spec rules of the release-1 modes that the renderer must honour (M01 rings and camera, M05 transit and camera anchor),
    /// the forward-only stream reading of the export loop, the event log reader and the render ↔ replay event mapping.
    /// </summary>
    public class SpecRuleTests
    {
        // ------------------------------------------------------------------ M05 transit (ball drawn over platform k)

        static (RenderComposer composer, ComposeSettings settings) HelixComposer(RenderQuantization q, List<ShapeDefinition> shapes)
        {
            var c = new RenderComposer();
            c.SetStream(q, shapes);
            return (c, new ComposeSettings { View = ModeView.Helix, Quality = QualityProfile.Export });
        }

        static List<ShapeDefinition> HelixShapes(RenderQuantization q) => Shapes(ShapeDefinition.Circle,
            ShapeDefinition.Ring(q.QuantizeScale(1.5f), q.QuantizeScale(1.8f), new[] { RingGap.FromRadians(-0.4f, 0.4f) }));

        [Theory]
        [InlineData(-0.10f)]   // centre below the top face, ball top inside the slab
        [InlineData(0.05f)]    // centre above the top face, bottom already inside the slab
        [InlineData(-0.39f)]   // ball top −0.14 m, still above the slab bottom (−0.15 m)
        public void BallInTransitIsDrawnOverItsPlatform(float ballY)
        {
            var shapes = HelixShapes(Q);
            var f = Frame(Q.QuantizeBody(10, 1, 0f, 0f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible),
                Q.QuantizeBody(1, 0, 0f, ballY, 0f, 0.25f, 0, BodyFlags.Visible));
            var (c, s) = HelixComposer(Q, shapes);
            c.Sync(0, f, null, s);
            c.Compose(0, f, ReadOnlySpan<RenderEvent>.Empty, s);
            Assert.Equal(0, c.Over.IndexCount);
            Assert.True(c.Under.IndexCount > 0);
        }

        [Fact]
        public void PlatformGoesOverTheBallOnceTheTransitEnded()
        {
            var shapes = HelixShapes(Q);
            // Ball top at −0.45 + 0.25 = −0.20 < slab bottom −0.15: transit over, the platform is above the ball.
            var f = Frame(Q.QuantizeBody(10, 1, 0f, 0f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible),
                Q.QuantizeBody(1, 0, 0f, -0.45f, 0f, 0.25f, 0, BodyFlags.Visible));
            var (c, s) = HelixComposer(Q, shapes);
            c.Sync(0, f, null, s);
            c.Compose(0, f, ReadOnlySpan<RenderEvent>.Empty, s);
            Assert.True(c.Over.IndexCount > 0);
        }

        // ------------------------------------------------------------------ M05 camera: drawn ball at 38 %, 12 platforms

        [Theory]
        [InlineData(28f)]
        [InlineData(40f)]
        [InlineData(15f)]
        public void HelixDrawnBallSitsAt38PercentFromTheTop(float tilt)
        {
            var q = new RenderQuantization(64f, 8f);
            var shapes = HelixShapes(q);
            var f = new RenderFrame();
            for (int k = 0; k < 6; k++) f.AddBody(q.QuantizeBody(10 + k, 1, 0f, -k * 1.8f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
            f.AddBody(q.QuantizeBody(1, 0, 0f, -3.0f, 0f, 0.25f, 0, BodyFlags.Visible));
            f.Sort();
            var (c, s) = HelixComposer(q, shapes);
            s.Helix.TiltDegrees = tilt;
            for (int t = 0; t < 300; t++) c.Sync(t, f, null, s);
            c.Compose(299, f, ReadOnlySpan<RenderEvent>.Empty, s);
            BallInstance b = c.Balls[0];
            float fromTop = (c.View.MaxY - b.Y) / c.View.Height;
            Assert.InRange(fromTop, 0.375f, 0.385f);
        }

        [Theory]
        [InlineData(1.2f)]
        [InlineData(1.8f)]
        [InlineData(3.0f)]
        public void HelixViewShowsTwelvePlatformSpacings(float spacing)
        {
            var q = new RenderQuantization(64f, 8f);
            var shapes = HelixShapes(q);
            var f = Frame(q.QuantizeBody(10, 1, 0f, 0f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible),
                q.QuantizeBody(1, 0, 0f, 0.5f, 0f, 0.25f, 0, BodyFlags.Visible));
            var (c, s) = HelixComposer(q, shapes);
            s.Helix.PlatformSpacing = spacing;
            c.Sync(0, f, null, s);
            c.Compose(0, f, ReadOnlySpan<RenderEvent>.Empty, s);
            float expected = HelixViewSettings.VisiblePlatforms * spacing * MathF.Cos(s.Helix.TiltRadians);
            Assert.Equal(expected, c.View.Height, 3);
            // Unknown spacing keeps the arena height.
            s.Helix.PlatformSpacing = 0f;
            c.Compose(0, f, ReadOnlySpan<RenderEvent>.Empty, s);
            Assert.Equal(16f, c.View.Height, 3);
        }

        // ------------------------------------------------------------------ §2.1 AutoZoom fits all bodies; M01 next-4-rings targets

        static RenderFrame RingsFrame(RenderQuantization q, BodyFlags ballFlags, Func<int, BodyFlags> ringFlags)
        {
            var f = new RenderFrame();
            f.AddBody(q.QuantizeBody(1, 0, 0f, 0f, 0f, 0.3f, 0, BodyFlags.Visible | ballFlags));
            for (int i = 0; i < 4; i++)
            {
                f.AddBody(q.QuantizeBody(10 + i, 1, 0f, 0f, 0f, 2f + 2f * i, PaletteSlots.Arena, BodyFlags.Visible | ringFlags(i)));   // r = 2, 4, 6, 8
            }
            f.Sort();
            return f;
        }

        static ViewRect AutoZoomView(RenderFrame f, RenderQuantization q, List<ShapeDefinition> shapes)
        {
            var c = new RenderComposer();
            c.SetStream(q, shapes);
            var s = new ComposeSettings
            {
                View = ModeView.Standard2D,
                Quality = QualityProfile.Export,
                Camera = new CameraSettings { Mode = CameraMode.AutoZoom, AutoZoomCentered = true, AutoZoomMargin = 1f },
            };
            for (int t = 0; t < 120; t++) c.Sync(t, f, null, s);
            c.Compose(119, f, ReadOnlySpan<RenderEvent>.Empty, s);
            return c.View;
        }

        [Fact]
        public void AutoZoomFitsRingsNotOnlyBalls()
        {
            var q = new RenderQuantization(16f, 16f);
            var shapes = Shapes(ShapeDefinition.Circle,
                ShapeDefinition.Ring(q.QuantizeScale(1f), q.QuantizeScale(0.1f), new[] { RingGap.FromRadians(-0.3f, 0.3f) }));
            ViewRect v = AutoZoomView(RingsFrame(q, BodyFlags.None, _ => BodyFlags.None), q, shapes);
            // Outer ring r = 8 m, stroke 0.8 m (scaled): extent 8.4 m + 1 m margin each side.
            Assert.True(v.Width >= 2f * (8.4f + 1f) - 0.05f, "outer ring cut off: view " + v);
        }

        [Fact]
        public void AutoZoomFitsOnlyCameraTargetsWhenTheModeMarksThem()
        {
            var q = new RenderQuantization(16f, 16f);
            var shapes = Shapes(ShapeDefinition.Circle,
                ShapeDefinition.Ring(q.QuantizeScale(1f), q.QuantizeScale(0.05f), new[] { RingGap.FromRadians(-0.3f, 0.3f) }));
            // Ball and the inner 3 rings (r ≤ 6) are targets; the r = 8 ring is not.
            ViewRect v = AutoZoomView(RingsFrame(q, RenderBodyFlags.CameraTarget, i => i < 3 ? RenderBodyFlags.CameraTarget : BodyFlags.None), q, shapes);
            float ext = 6f + 0.15f;   // r 6 + stroke 0.3 / 2
            Assert.True(v.Width >= 2f * (ext + 1f) - 0.05f, "target ring cut off: " + v);
            Assert.True(v.Width < 2f * (8f + 1f), "non-target ring was fitted: " + v);
        }

        [Fact]
        public void BoundingRadiusCoversEveryKind()
        {
            RenderQuantization q = Q;
            Assert.Equal(0.5f, ShapeTessellator.BoundingRadius(ShapeDefinition.Circle, q, 0.5f), 4);
            var ring = ShapeDefinition.Ring(q.QuantizeScale(2f), q.QuantizeScale(0.2f), ReadOnlySpan<RingGap>.Empty);
            Assert.Equal(2f * 1.5f + 0.15f, ShapeTessellator.BoundingRadius(ring, q, 1.5f), 2);
            var fixedRing = ShapeDefinition.Ring(q.QuantizeScale(2f), q.QuantizeScale(0.2f), ReadOnlySpan<RingGap>.Empty, fixedThickness: true);
            Assert.Equal(3f + 0.1f, ShapeTessellator.BoundingRadius(fixedRing, q, 1.5f), 2);
            var capsule = ShapeDefinition.Capsule(q.QuantizeScale(2f), q.QuantizeScale(0.4f));
            Assert.Equal(1.2f, ShapeTessellator.BoundingRadius(capsule, q, 1f), 2);
            var poly = ShapeDefinition.Polygon(Pts(-3, -4, 3, -4, 0, 2));
            Assert.Equal(5f, ShapeTessellator.BoundingRadius(poly, q, 1f), 2);
            var line = ShapeDefinition.Polyline(Pts(0, 0, 1, 0), false, q.QuantizeScale(0.2f));
            Assert.True(ShapeTessellator.BoundingRadius(line, q, 1f) >= 1.1f);
        }

        // ------------------------------------------------------------------ M01: rings beyond the safe zone fade

        static byte MaxAlpha(GeometryBatch batch)
        {
            byte a = 0;
            foreach (BatchVertex v in batch.Vertices) a = Math.Max(a, v.A);
            return a;
        }

        static byte RingAlpha(float radius, BodyFlags flags)
        {
            var shapes = Shapes(ShapeDefinition.Circle, ShapeDefinition.Ring(Q.QuantizeScale(1f), Q.QuantizeScale(0.05f), ReadOnlySpan<RingGap>.Empty));
            var f = Frame(Q.QuantizeBody(10, 1, 0f, 0f, 0f, radius, PaletteSlots.Arena, BodyFlags.Visible | flags));
            var c = new RenderComposer();
            c.SetStream(Q, shapes);
            var s = new ComposeSettings { View = ModeView.Standard2D, Quality = QualityProfile.Export };
            c.Sync(0, f, null, s);
            c.Compose(0, f, ReadOnlySpan<RenderEvent>.Empty, s);
            Assert.True(c.Under.IndexCount > 0);
            return MaxAlpha(c.Under);
        }

        [Fact]
        public void RingsBeyondTheSafeZoneFadeWhenFlagged()
        {
            BodyFlags fade = RenderBodyFlags.FadeOutsideSafeZone;
            Assert.Equal(255, RingAlpha(2f, fade));                    // inside the 4 m circle
            byte mid = RingAlpha(6f, fade);
            byte far = RingAlpha(10f, fade);
            Assert.InRange(mid, 100, 200);                             // ≈ 2 m overhang of 4: alpha ≈ 0.6
            Assert.InRange(far, (byte)(SafeZoneFade.MinAlpha * 255f - 2f), (byte)(SafeZoneFade.MinAlpha * 255f + 2f));
            Assert.Equal(255, RingAlpha(10f, BodyFlags.None));         // not flagged: unchanged
            Assert.Equal(1f, SafeZoneFade.Alpha(0f, 0f, 3.9f));
            Assert.True(SafeZoneFade.Alpha(1f, 0f, 3.5f) < 1f);        // off-centre bodies reach further
        }

        // ------------------------------------------------------------------ export loop: forward reads only

        sealed class RecordingSource : IRenderFrameSource
        {
            readonly IRenderFrameSource _inner;
            public readonly List<int> Reads = new List<int>();
            public RecordingSource(IRenderFrameSource inner) { _inner = inner; }
            public bool TryReadFrame(int tick, RenderFrame destination)
            {
                Reads.Add(tick);
                return _inner.TryReadFrame(tick, destination);
            }
        }

        static byte[] Stream(RenderQuantization q, int ticks, int bodies)
        {
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("geometry_bounce", 60, 60, q), leaveOpen: true))
            {
                int circle = w.DefineShape(ShapeDefinition.Circle);
                var f = new RenderFrame();
                for (int t = 0; t < ticks; t++)
                {
                    f.Clear();
                    for (int b = 0; b < bodies; b++)
                    {
                        float a = t * 0.03f + b;
                        f.AddBody(q.QuantizeBody(b, circle, MathF.Cos(a) * 3f, MathF.Sin(a * 1.3f) * 5f, a, 0.2f, (byte)(b % 8), BodyFlags.Visible | BodyFlags.Trail));
                    }
                    w.AppendFrame(t, f);
                }
                w.Finish();
            }
            return ms.ToArray();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void SyncAndReadReadsTheStreamForwardsAndMatchesSync(int step)
        {
            var q = new RenderQuantization(16f, 16f);
            byte[] data = Stream(q, 400, 12);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            using (var reader2 = new RenderStreamReader(new MemoryStream(data)))
            using (var reader3 = new RenderStreamReader(new MemoryStream(data)))
            {
                var s = new ComposeSettings { View = ModeView.Standard2D, Quality = QualityProfile.Export };
                s.Look.Trail = TrailStyle.Long;
                var a = new RenderComposer();
                a.SetStream(q, reader.Shapes);
                var src = new RecordingSource(new RenderStreamFrameSource(reader));
                var b = new RenderComposer();
                b.SetStream(q, reader2.Shapes);
                var srcB = new RenderStreamFrameSource(reader2);
                var frameA = new RenderFrame();
                var frameB = new RenderFrame();
                int outputs = 0;
                for (int t = 0; t < 400; t += step)
                {
                    Assert.True(a.SyncAndRead(t, src, frameA, s));
                    reader3.ReadFrame(t, frameB);           // reference: separate readers
                    b.Sync(t, frameB, srcB, s);
                    a.Compose(t, frameA, ReadOnlySpan<RenderEvent>.Empty, s);
                    b.Compose(t, frameB, ReadOnlySpan<RenderEvent>.Empty, s);
                    Assert.Equal(b.Trails.IndexCount, a.Trails.IndexCount);
                    if (outputs % 16 == 0) Assert.Equal(b.Trails.Vertices.ToArray(), a.Trails.Vertices.ToArray());
                    outputs++;
                }
                // Every read is at or after the previous one, and the work is linear: one read per tick advanced.
                for (int i = 1; i < src.Reads.Count; i++) Assert.True(src.Reads[i] >= src.Reads[i - 1], "backward read " + src.Reads[i - 1] + " → " + src.Reads[i]);
                Assert.True(src.Reads.Count <= 400 + RenderComposer.HistoryTicks(s), "reads " + src.Reads.Count);
            }
        }

        [Fact]
        public void SyncAndReadReportsAMissingTick()
        {
            var q = new RenderQuantization(16f, 16f);
            byte[] data = Stream(q, 10, 2);
            using (var reader = new RenderStreamReader(new MemoryStream(data)))
            {
                var c = new RenderComposer();
                c.SetStream(q, reader.Shapes);
                var frame = new RenderFrame();
                var s = new ComposeSettings();
                Assert.False(c.SyncAndRead(50, new RenderStreamFrameSource(reader), frame, s));
                Assert.Equal(0, frame.Bodies.Length);
                Assert.True(c.SyncAndRead(9, new RenderStreamFrameSource(reader), frame, s));
                Assert.Equal(2, frame.Bodies.Length);
            }
        }

        [Theory]
        [InlineData(ModeView.Standard2D)]
        [InlineData(ModeView.Helix)]
        public void NewRulesStayAllocationFree(ModeView view)
        {
            var q = new RenderQuantization(64f, 16f);
            var shapes = Shapes(ShapeDefinition.Circle,
                ShapeDefinition.Ring(q.QuantizeScale(1.5f), q.QuantizeScale(0.2f), new[] { RingGap.FromRadians(-0.4f, 0.4f) }));
            var frames = new RenderFrame[240];
            var log = new RenderEventLog();
            for (int t = 0; t < frames.Length; t++)
            {
                var f = new RenderFrame();
                for (int k = 0; k < 6; k++)
                {
                    BodyFlags flags = BodyFlags.Visible | RenderBodyFlags.FadeOutsideSafeZone | (k < 4 ? RenderBodyFlags.CameraTarget : BodyFlags.None);
                    f.AddBody(q.QuantizeBody(10 + k, 1, 0f, view == ModeView.Helix ? -k * 1.8f : 0f, t * 0.02f, 1f + k, PaletteSlots.Arena, flags));
                }
                for (int b = 0; b < 4; b++)
                {
                    f.AddBody(q.QuantizeBody(b, 0, MathF.Cos(t * 0.05f + b), -t * 0.02f, 0f, 0.25f, (byte)b,
                        BodyFlags.Visible | BodyFlags.Trail | RenderBodyFlags.CameraTarget));
                }
                f.Sort();
                frames[t] = f;
                if (t % 7 == 0)
                {
                    RenderEvent sp = RenderEvent.Create(t, RenderEventType.Spawn, 0, 0f, 0f, 1, 1f);
                    sp.BodyId = t % 4;
                    log.Add(sp);
                }
                if (t % 11 == 0)
                {
                    RenderEvent el = RenderEvent.Create(t, RenderEventType.Eliminate, 1, 1f, 0f, 2, 1f);
                    el.NormalX = 1f;
                    log.Add(el);
                }
            }
            var ring = new RenderFrameRing(64);
            var c = new RenderComposer();
            c.SetStream(q, shapes);
            var s = new ComposeSettings { View = view, Quality = QualityProfile.Export };
            if (view == ModeView.Standard2D) s.Camera = new CameraSettings { Mode = CameraMode.AutoZoom, AutoZoomCentered = true };
            else s.Helix.PlatformSpacing = 1.8f;
            var frame = new RenderFrame();
            void Run()
            {
                for (int t = 0; t < frames.Length; t += 2)
                {
                    ring.Push(t, frames[t]);
                    if (t + 1 < frames.Length) ring.Push(t + 1, frames[t + 1]);
                    Assert.True(c.SyncAndRead(t, ring, frame, s));
                    c.Compose(t, frame, log.GetWindow(t, VfxSystem.MaxEffectTicks), s);
                }
            }
            Run();
            ring.Clear();
            c.ResetHistory();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Run();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(alloc < 1024, "allocated " + alloc + " bytes");
        }

        // ------------------------------------------------------------------ event log reader

        static byte[] LogBytes(RenderEventLog log)
        {
            var ms = new MemoryStream();
            log.WriteTo(ms);
            return ms.ToArray();
        }

        /// <summary>A valid BVE1 file of <paramref name="count"/> Hit events, all in tick 5, ids 0…count−1 (built without Add).</summary>
        static byte[] SameTickLog(int count)
        {
            var one = new RenderEventLog();
            one.Add(RenderEvent.Create(5, RenderEventType.Hit, 0, 0f, 0f, 0, 1f));
            byte[] template = LogBytes(one);
            var data = new byte[10 + count * RenderEventLog.RecordSize + 8];
            Array.Copy(template, data, 10);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), (uint)count);
            for (int i = 0; i < count; i++)
            {
                int at = 10 + i * RenderEventLog.RecordSize;
                Array.Copy(template, 10, data, at, RenderEventLog.RecordSize);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(at + 8), count - 1 - i);   // unsorted ids
            }
            return Rehash(data);
        }

        [Fact]
        public void EventLogReadIsLinearithmicForManySameTickEvents()
        {
            byte[] data = SameTickLog(200_000);
            var sw = Stopwatch.StartNew();
            RenderEventLog back = RenderEventLog.ReadFrom(new MemoryStream(data));
            sw.Stop();
            Assert.Equal(200_000, back.Count);
            // The old per-event backward scan needed ~2e10 comparisons here; sorting the ids takes milliseconds.
            Assert.True(sw.ElapsedMilliseconds < 10000, "ReadFrom took " + sw.ElapsedMilliseconds + " ms");   // ≈ 0.2 s; the old reader: > 1 min
            Assert.Equal(199_999, back.Events[0].Id);
        }

        [Fact]
        public void EventLogReaderRejectsDuplicatesDisorderBadChecksumAndShortStreams()
        {
            var log = new RenderEventLog();
            log.Add(RenderEvent.Create(1, RenderEventType.Hit, 0, 0f, 0f, 0, 1f));
            log.Add(RenderEvent.Create(1, RenderEventType.Hit, 1, 0f, 0f, 0, 1f));
            log.Add(RenderEvent.Create(2, RenderEventType.Splat, 0, 0f, 0f, 0, 1f));
            byte[] good = LogBytes(log);
            Assert.Equal(3, RenderEventLog.ReadFrom(new MemoryStream(good)).Count);

            // Duplicate id within a tick, with a valid checksum (re-hashed): rejected by validation.
            byte[] dup = Rehash(Patch(good, 10 + RenderEventLog.RecordSize + 8, 0));                      // event 2 id 1 → 0
            var e1 = Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(dup)));
            Assert.Contains("Duplicate", e1.Message);
            // Out of order, valid checksum.
            byte[] disorder = Rehash(Patch(good, 10 + 2 * RenderEventLog.RecordSize, 0));                  // event 3 tick 2 → 0
            var e2 = Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(disorder)));
            Assert.Contains("tick order", e2.Message);
            // Corrupt data without re-hashing: the checksum is checked before validation.
            byte[] corrupt = Patch(good, 10 + RenderEventLog.RecordSize + 8, 0);
            var e3 = Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(corrupt)));
            Assert.Contains("checksum", e3.Message);
            // A header claiming more events than the stream holds is rejected before reading them.
            byte[] lying = (byte[])good.Clone();
            lying[6] = 0x20; lying[7] = 0xA1; lying[8] = 0x07; lying[9] = 0x00;   // 500 000 events
            var e4 = Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(lying)));
            Assert.Contains("truncated", e4.Message);
            // Unknown kind (valid checksum).
            byte[] unknown = (byte[])good.Clone();
            unknown[10 + 4] = 9;
            var e5 = Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(Rehash(unknown))));
            Assert.Contains("Unknown", e5.Message);
        }

        static byte[] Patch(byte[] data, int offset, int value)
        {
            byte[] copy = (byte[])data.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value);
            return copy;
        }

        static byte[] Rehash(byte[] data)
        {
            var h = new XxHash64();
            h.Append(data.AsSpan(0, data.Length - 8));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(data.Length - 8), h.Digest());
            return data;
        }

        // ------------------------------------------------------------------ render ↔ replay event kinds

        [Fact]
        public void SharedEventKindsUseTheReplayNumbersAndMapExplicitly()
        {
            Assert.Equal((byte)ReplayEventType.Hit, (byte)RenderEventType.Hit);
            Assert.Equal((byte)ReplayEventType.Break, (byte)RenderEventType.Break);
            Assert.Equal((byte)ReplayEventType.Eliminate, (byte)RenderEventType.Eliminate);
            Assert.Equal((byte)ReplayEventType.Finish, (byte)RenderEventType.Finish);
            Assert.Equal((byte)ReplayEventType.Spawn, (byte)RenderEventType.Spawn);
            foreach (RenderEventType t in (RenderEventType[])Enum.GetValues(typeof(RenderEventType)))
            {
                if (t == RenderEventType.Splat)
                {
                    Assert.False(RenderEventTypes.TryGetReplayType(t, out _));
                    Assert.Throws<ArgumentException>(() => RenderEventTypes.ToReplayType(t));
                    continue;
                }
                ReplayEventType r = RenderEventTypes.ToReplayType(t);
                if ((byte)t < 16) Assert.Equal((byte)t, (byte)r);   // shared kinds: identity
            }
            Assert.Equal(ReplayEventType.Finish, RenderEventTypes.ToReplayType(RenderEventType.Escape));
            Assert.Equal(ReplayEventType.Break, RenderEventTypes.ToReplayType(RenderEventType.Pierce));
            for (int v = 0; v < 256; v++)
            {
                Assert.Equal(Enum.IsDefined(typeof(RenderEventType), (RenderEventType)v), RenderEventTypes.IsDefined((RenderEventType)v));
            }
        }

        // ------------------------------------------------------------------ mode → view through the catalog

        [Fact]
        public void HelixViewIsSelectedForM05()
        {
            Assert.Equal(ModeView.Helix, ModeViews.ForMode("helix_drop"));
            Assert.Equal(ModeView.Helix, ModeViews.ForMode(ModeCatalog.Get(ModeCode.M05).Id));
            Assert.Equal(ModeView.Helix, ModeViews.ForCode(ModeCode.M05));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode("ring_escape"));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode(ModeCatalog.Get(ModeCode.M02).Id));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode(null));
            Assert.Equal(ModeView.Standard2D, ModeViews.ForMode("unknown_mode"));
        }

        // ------------------------------------------------------------------ Look parameters: no allocation per Apply

        [Fact]
        public void LookApplyDoesNotAllocate()
        {
            var values = Ballverse.Core.Params.CommonParams.Schema.CreateDefaults();
            var themes = new RenderThemeCatalog();
            var look = new LookSettings();
            look.Apply(values, themes);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) look.Apply(values, themes);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(alloc == 0, "LookSettings.Apply allocated " + alloc + " bytes over 100 calls");
        }

        // ------------------------------------------------------------------ M02 cosmetics: exit streak, growth pulse

        static uint[] Palette()
        {
            var p = new uint[256];
            new LookSettings().ResolvePalette(p);
            return p;
        }

        [Fact]
        public void EliminateWithAnExitDirectionDrawsAStreakAlongIt()
        {
            var vfx = new VfxSystem();
            var settings = new VfxSettings { Palette = Palette() };
            var output = new Particle[128];
            RenderEvent e = RenderEvent.Create(10, RenderEventType.Eliminate, 0, 4.5f, 0f, 1, 1f);
            e.NormalX = 1f;
            e.Size = 0.25f;
            int n = vfx.Evaluate(new[] { e }, 13, settings, null, Q, output, output.Length);
            int streaks = 0, bands = 0;
            for (int i = 0; i < n; i++)
            {
                if (output[i].Shape == ParticleShape.Streak)
                {
                    streaks++;
                    Assert.True(output[i].VX > 0f, "spark flies against the exit direction");
                }
                if (output[i].Shape == ParticleShape.Rect)
                {
                    bands++;
                    Assert.Equal(0f, output[i].Rotation, 3);
                    Assert.True(output[i].X < 4.5f, "band must trail behind the exit point");
                }
            }
            Assert.Equal(1, bands);
            Assert.Equal(8, streaks);
            // Without a direction: only the puff.
            RenderEvent plain = RenderEvent.Create(10, RenderEventType.Eliminate, 0, 4.5f, 0f, 1, 1f);
            n = vfx.Evaluate(new[] { plain }, 13, settings, null, Q, output, output.Length);
            for (int i = 0; i < n; i++) Assert.Equal(ParticleShape.Dot, output[i].Shape);
        }

        [Fact]
        public void SpawnPulsesTheNamedBall()
        {
            RenderEvent e = RenderEvent.Create(10, RenderEventType.Spawn, 0, 0f, 0f, 0, 1f);
            e.BodyId = 7;
            e.Size = 0.4f;
            var events = new[] { e };
            Assert.Equal(1f, VfxSystem.GetPulse(events, 9, 7));
            Assert.True(VfxSystem.GetPulse(events, 15, 7) > 1.05f);
            Assert.Equal(1f, VfxSystem.GetPulse(events, 15, 8));
            Assert.Equal(1f, VfxSystem.GetPulse(events, 10 + 30, 7));

            var shapes = Shapes(ShapeDefinition.Circle);
            var f = Frame(Q.QuantizeBody(7, 0, 0f, 0f, 0f, 0.4f, 0, BodyFlags.Visible));
            var c = new RenderComposer();
            c.SetStream(Q, shapes);
            var s = new ComposeSettings();
            c.Sync(15, f, null, s);
            c.Compose(15, f, events, s);
            Assert.True(c.Balls[0].RadiusX > 0.42f);
        }

        // ------------------------------------------------------------------ §7.7 colour grade per theme

        [Fact]
        public void ThemesCarryAColourGradeThatRoundTripsThroughJson()
        {
            var catalog = new RenderThemeCatalog();
            RenderTheme mono = catalog.GetOrDefault("Mono");
            Assert.Equal(0f, mono.Saturation);
            Assert.True(mono.HasGrade);
            RenderTheme sunset = catalog.GetOrDefault("Sunset");
            RenderTheme back = RenderThemeJson.Parse(RenderThemeJson.ToJson(sunset));
            Assert.Equal(sunset.Saturation, back.Saturation);
            Assert.Equal(sunset.Contrast, back.Contrast);
            Assert.Equal(sunset.Tint, back.Tint);
            RenderTheme plain = RenderThemeJson.Parse("{\"id\":\"x\",\"palette\":[\"#FF0000\"]}");
            Assert.False(plain.HasGrade);
            Assert.Throws<FormatException>(() => RenderThemeJson.Parse("{\"id\":\"x\",\"palette\":[\"#FF0000\"],\"saturation\":3}"));
            Assert.Throws<FormatException>(() => RenderThemeJson.Parse("{\"id\":\"x\",\"palette\":[\"#FF0000\"],\"tint\":\"blue\"}"));
        }
    }
}
