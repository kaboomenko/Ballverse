using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Ballverse.Core.RenderStream.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Grid layer of palette indices (spec §2.9 M08 Color War): frame API, header, encoding, random access, corruption.</summary>
    public class GridLayerTests
    {
        readonly ITestOutputHelper _output;

        public GridLayerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        static void AssertFrames(List<RenderFrame> expected, RenderStreamReader reader, IEnumerable<int> ticks)
        {
            var dest = new RenderFrame();
            foreach (int t in ticks)
            {
                reader.ReadFrame(t, dest);
                Assert.True(expected[t].ContentEquals(dest), "tick " + t + ": " + StreamScenes.Describe(expected[t], dest));
            }
        }

        // ------------------------------------------------------------------ RenderFrame API

        [Fact]
        public void FrameGridApi()
        {
            var f = new RenderFrame();
            Assert.False(f.HasGrid);
            Assert.True(f.Grid.IsEmpty);
            f.SetGridSize(3, 2);
            Assert.True(f.HasGrid);
            Assert.Equal(3, f.GridWidth);
            Assert.Equal(2, f.GridHeight);
            Assert.Equal(6, f.Grid.Length);
            f.SetCell(2, 1, 9);
            Assert.Equal(9, f.Grid[5]);   // row-major: y * width + x
            Assert.Equal(9, f.GetCell(2, 1));
            f.Grid[0] = 4;
            Assert.Equal(4, f.GetCell(0, 0));

            f.SetGridSize(3, 2);          // same size keeps cells
            Assert.Equal(9, f.GetCell(2, 1));
            f.AddBody(new BodyState(1, 0, 0, 0, 0, 0, 0, BodyFlags.None));
            f.Clear();                    // Clear keeps the grid
            Assert.Equal(9, f.GetCell(2, 1));
            f.SetGridSize(2, 3);          // new size clears
            Assert.Equal(0, f.GetCell(1, 2));
            Assert.All(f.Grid.ToArray(), c => Assert.Equal(0, c));

            Assert.Throws<ArgumentOutOfRangeException>(() => f.GetCell(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.GetCell(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetCell(-1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetGridSize(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetGridSize(3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetGridSize(RenderStreamLimits.MaxGridWidth + 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.SetGridSize(1, RenderStreamLimits.MaxGridHeight + 1));
            f.SetGridSize(RenderStreamLimits.MaxGridWidth, RenderStreamLimits.MaxGridHeight);
            Assert.Equal(32768, f.Grid.Length);
            f.SetGridSize(0, 0);
            Assert.False(f.HasGrid);
            Assert.Throws<ArgumentOutOfRangeException>(() => f.GetCell(0, 0));
        }

        [Fact]
        public void CopyFromAndContentEqualsIncludeTheGrid()
        {
            var a = new RenderFrame();
            a.SetGridSize(4, 4);
            a.SetCell(3, 3, 7);
            var b = new RenderFrame();
            b.CopyFrom(a);
            Assert.True(a.ContentEquals(b));
            Assert.Equal(7, b.GetCell(3, 3));
            b.SetCell(0, 0, 1);
            Assert.False(a.ContentEquals(b));
            b.SetCell(0, 0, 0);
            Assert.True(a.ContentEquals(b));

            var c = new RenderFrame();
            c.SetGridSize(2, 8);         // same cell count, different shape
            var d = new RenderFrame();
            d.SetGridSize(8, 2);
            Assert.False(c.ContentEquals(d));
            Assert.False(c.ContentEquals(new RenderFrame()));

            var none = new RenderFrame();
            b.CopyFrom(none);
            Assert.False(b.HasGrid);
        }

        // ------------------------------------------------------------------ header

        [Fact]
        public void GridSpecValidation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderGridSpec(RenderQuantization.Default, 0, 5, GridTopology.Square, 0, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderGridSpec(RenderQuantization.Default, 129, 5, GridTopology.Square, 0, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderGridSpec(RenderQuantization.Default, 5, 0, GridTopology.Square, 0, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderGridSpec(RenderQuantization.Default, 5, 257, GridTopology.Square, 0, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderGridSpec(RenderQuantization.Default, 5, 5, (GridTopology)2, 0, 0, 1, 1));
            Assert.Throws<ArgumentException>(() => new RenderGridSpec(RenderQuantization.Default, 5, 5, GridTopology.Square, 1, 0, 1, 1));
            Assert.Throws<ArgumentException>(() => new RenderGridSpec(RenderQuantization.Default, 5, 5, GridTopology.Square, 0, 2, 1, 1));
            Assert.Throws<ArgumentNullException>(() => RenderGridSpec.FromWorld(null, 5, 5, GridTopology.Square, 0, 0, 1, 1));
            var g = new RenderGridSpec(RenderQuantization.Default, 128, 256, GridTopology.HexOddRowsShifted, short.MinValue, short.MinValue, short.MaxValue, short.MaxValue);
            Assert.Equal(32768, g.CellCount);
            var w = RenderGridSpec.FromWorld(RenderQuantization.Default, 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f);
            Assert.Equal(new RenderGridSpec(RenderQuantization.Default, 30, 53, GridTopology.Square, -9216, -16384, 9216, 16384), w);
            Assert.Equal(w.GetHashCode(), new RenderGridSpec(RenderQuantization.Default, 30, 53, GridTopology.Square, -9216, -16384, 9216, 16384).GetHashCode());
            Assert.NotEqual(w, new RenderGridSpec(RenderQuantization.Default, 30, 53, GridTopology.HexOddRowsShifted, -9216, -16384, 9216, 16384));
            Assert.False(w.Equals((object)null));
            Assert.Null(new RenderStreamHeader("m").Grid);
        }

        [Fact]
        public void GridHeaderRoundTrips()
        {
            var grid = new RenderGridSpec(RenderQuantization.Default, 128, 256, GridTopology.HexOddRowsShifted, -32768, -5, 32767, 6);
            var header = new RenderStreamHeader("color_war", 60, 60, RenderQuantization.Default, grid);
            var f = new RenderFrame();
            f.SetGridSize(128, 256);
            using (var r = StreamScenes.Open(StreamScenes.Write(new List<RenderFrame> { f }, header)))
            {
                Assert.Equal(grid, r.Header.Grid);
                Assert.True(f.ContentEquals(r.ReadFrame(0)));
            }
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(5, 0)]
        [InlineData(129, 5)]
        [InlineData(5, 257)]
        public void InvalidGridSizeInHeaderIsInvalidData(int width, int height)
        {
            byte[] file = StreamScenes.Write(new List<RenderFrame>(), StreamScenes.GridHeader("m", 4, 4));
            var parts = BvsSurgery.Split(file);
            BitConverter.GetBytes((ushort)width).CopyTo(parts.Header, 18);
            BitConverter.GetBytes((ushort)height).CopyTo(parts.Header, 20);
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(BvsSurgery.Join(parts)));
        }

        [Fact]
        public void InvalidGridTopologyOrBoundsInHeaderIsInvalidData()
        {
            byte[] file = StreamScenes.Write(new List<RenderFrame>(), StreamScenes.GridHeader("m", 4, 4));
            var parts = BvsSurgery.Split(file);
            parts.Header[22] = 2;
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(BvsSurgery.Join(parts)));

            parts = BvsSurgery.Split(file);
            BitConverter.GetBytes((short)100).CopyTo(parts.Header, 23);  // minX
            BitConverter.GetBytes((short)100).CopyTo(parts.Header, 27);  // maxX
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(BvsSurgery.Join(parts)));

            parts = BvsSurgery.Split(file);
            BitConverter.GetBytes((short)200).CopyTo(parts.Header, 25);  // minY > maxY
            BitConverter.GetBytes((short)100).CopyTo(parts.Header, 29);
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(BvsSurgery.Join(parts)));

            Assert.False(Throws(BvsSurgery.Join(BvsSurgery.Split(file))));
        }

        static bool Throws(byte[] data)
        {
            try
            {
                using (var r = StreamScenes.Open(data))
                {
                    for (int t = 0; t < r.TickCount; t++) r.ReadFrame(t);
                }
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        // ------------------------------------------------------------------ writer validation

        [Fact]
        public void FramesMustMatchTheHeaderGrid()
        {
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, StreamScenes.GridHeader("m", 5, 4), leaveOpen: true))
            {
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, new RenderFrame()));
                var wrong = new RenderFrame();
                wrong.SetGridSize(4, 5);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, wrong));
                Assert.False(w.IsFaulted);
                var ok = new RenderFrame();
                ok.SetGridSize(5, 4);
                ok.SetCell(4, 3, 2);
                w.AppendFrame(0, ok);
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(2, r.ReadFrame(0).GetCell(4, 3));
            }
        }

        [Fact]
        public void ReaderRemovesGridFromDestinationForStreamsWithoutGrid()
        {
            var dest = new RenderFrame();
            dest.SetGridSize(3, 3);
            using (var r = StreamScenes.Open(StreamScenes.Write(StreamScenes.Churn(5, 1))))
            {
                r.ReadFrame(2, dest);
                Assert.False(dest.HasGrid);
            }
        }

        // ------------------------------------------------------------------ round trip and random access

        [Fact]
        public void ColorWarSceneRoundTripsWithRandomAccess()
        {
            // M08 default: 30 x 53 grid, balls painting cells.
            const int ticks = 250;
            var frames = StreamScenes.AddGrid(StreamScenes.Smooth(120, ticks, 8), 30, 53, 11);
            byte[] data = StreamScenes.Write(frames, StreamScenes.GridHeader("color_war", 30, 53));
            using (var reader = StreamScenes.Open(data))
            {
                AssertFrames(frames, reader, new[] { 0, 59, 60, 61, 119, 120, 249, 30, 1, 0, 249, 180, 179, 97, 96, 41, 40 });
                var rng = Rng.FromSeed(5);
                var probes = new List<int>();
                for (int i = 0; i < 100; i++) probes.Add(rng.NextInt(0, ticks));
                AssertFrames(frames, reader, probes);
                var all = new List<int>();
                for (int t = ticks - 1; t >= 0; t--) all.Add(t);
                AssertFrames(frames, reader, all);
                all.Reverse();
                AssertFrames(frames, reader, all);
            }
        }

        [Fact]
        public void MaximumGridWithHeavyChangesRoundTrips()
        {
            const int w = RenderStreamLimits.MaxGridWidth, h = RenderStreamLimits.MaxGridHeight;
            var rng = Rng.FromSeed(77);
            var frames = new List<RenderFrame>();
            var cells = new byte[w * h];
            for (int t = 0; t < 70; t++)
            {
                int mode = t % 4;
                if (mode == 0) for (int i = 0; i < cells.Length; i++) cells[i] = (byte)rng.NextUInt();      // noise: everything changes
                else if (mode == 1) Array.Fill(cells, (byte)t);                                            // uniform fill
                else if (mode == 2) for (int i = 0; i < 50; i++) cells[rng.NextInt(0, cells.Length)] ^= 1; // sparse
                // mode 3: unchanged
                var f = new RenderFrame();
                f.SetGridSize(w, h);
                cells.CopyTo(f.Grid);
                f.AddBody(new BodyState(1, 0, (short)t, 0, 0, 100, 1, BodyFlags.Visible));
                frames.Add(f);
            }
            byte[] data = StreamScenes.Write(frames, StreamScenes.GridHeader("color_war", w, h));
            using (var reader = StreamScenes.Open(data))
            {
                var ticks = new List<int>();
                for (int t = 69; t >= 0; t -= 3) ticks.Add(t);
                for (int t = 0; t < 70; t++) ticks.Add(t);
                AssertFrames(frames, reader, ticks);
            }
        }

        [Fact]
        public void GridOnlyStreamWithoutBodiesOrShapes()
        {
            var frames = StreamScenes.AddGrid(new List<RenderFrame> { new RenderFrame(), new RenderFrame(), new RenderFrame() }, 7, 9, 3);
            byte[] data = StreamScenes.Write(frames, StreamScenes.GridHeader("m", 7, 9), w => { });
            using (var reader = StreamScenes.Open(data))
            {
                Assert.Empty(reader.Shapes);
                AssertFrames(frames, reader, new[] { 2, 0, 1 });
            }
        }

        [Fact]
        public void DeltaPicksTheShorterGridEncoding()
        {
            var codec = new FrameCodec(128, 256);
            var w = new ByteWriter(1024);
            var f = new RenderFrame();
            f.SetGridSize(128, 256);
            codec.Encode(f, w);                    // keyframe: one run of 32768 zeros = uvarint(32767) (3 bytes) + value
            Assert.Equal(1 + 1 + 3 + 1, w.Length); // bodyCount, hudCount, run, value

            int before = w.Length;
            codec.Encode(f, w);                    // unchanged: mode 0, zero changes
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0 }, w.Span.Slice(before).ToArray());

            before = w.Length;
            f.SetCell(5, 0, 3);
            codec.Encode(f, w);                    // one change: list
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 1, 5, 3 }, w.Span.Slice(before).ToArray());

            before = w.Length;
            f.Grid.Fill(9);
            codec.Encode(f, w);                    // everything changes to one value: RLE (mode 1, one run)
            Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 0xFF, 0xFF, 0x01, 9 }, w.Span.Slice(before).ToArray());

            byte[] raw = w.Span.ToArray();
            var decoder = new FrameCodec(128, 256);
            var r = new ByteReader(raw, 0, raw.Length);
            var dest = new RenderFrame();
            for (int i = 0; i < 4; i++) decoder.Decode(ref r, dest);
            Assert.True(f.ContentEquals(dest));
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void DeltaGridTieUsesChangeList()
        {
            // 65 x 2 = 130 cells, all 0, then cells 128 = 1 and 129 = 2:
            //   change list = count (1) + index 128 (2-byte uvarint) + value (1) + gap 0 (1) + value (1) = 6 bytes
            //   RLE         = run of 128 zeros (1 + 1) + run of one 1 (1 + 1) + run of one 2 (1 + 1)   = 6 bytes
            // The documented rule "the writer picks the shorter, ties -> 0" makes this mode 0.
            var codec = new FrameCodec(65, 2);
            var w = new ByteWriter(256);
            var f = new RenderFrame();
            f.SetGridSize(65, 2);
            codec.Encode(f, w);
            int before = w.Length;
            f.Grid[128] = 1;
            f.Grid[129] = 2;
            codec.Encode(f, w);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 2, 0x80, 0x01, 1, 0, 2 }, w.Span.Slice(before).ToArray());

            // Both cells 3: the runs merge, RLE = 4 bytes < list = 6 bytes, so RLE (mode 1) wins.
            before = w.Length;
            f.Grid[128] = 3;
            f.Grid[129] = 3;
            codec.Encode(f, w);
            Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 127, 0, 1, 3 }, w.Span.Slice(before).ToArray());

            byte[] raw = w.Span.ToArray();
            var decoder = new FrameCodec(65, 2);
            var r = new ByteReader(raw, 0, raw.Length);
            var dest = new RenderFrame();
            for (int i = 0; i < 3; i++) decoder.Decode(ref r, dest);
            Assert.True(f.ContentEquals(dest));
        }

        [Fact]
        public void GridSteadyStateAppendDoesNotAllocate()
        {
            // M08 default grid, both a sorted input frame and an unsorted one (the writer's canonicalizing copy includes the grid).
            const int ticks = 360;
            var frames = StreamScenes.AddGrid(StreamScenes.Smooth(60, ticks, 9), 30, 53, 10);
            var unsorted = new List<RenderFrame>(ticks);
            foreach (var f in frames)
            {
                var u = new RenderFrame();
                for (int i = f.BodyCount - 1; i >= 0; i--) u.AddBody(f.GetBody(i));
                u.AddHud(1, f.Hud[1].Value);
                u.AddHud(0, f.Hud[0].Value);
                u.SetGridSize(30, 53);
                f.Grid.CopyTo(u.Grid);
                unsorted.Add(u);
            }
            Assert.False(unsorted[0].IsBodiesSorted());

            foreach (var input in new[] { frames, unsorted })
            {
                var ms = new MemoryStream(4 * 1024 * 1024);
                using (var w = new RenderStreamWriter(ms, StreamScenes.GridHeader("color_war", 30, 53), leaveOpen: true))
                {
                    w.DefineShape(ShapeDefinition.Circle);
                    for (int t = 0; t < 120; t++) w.AppendFrame(t, input[t]);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int t = 120; t < ticks; t++) w.AppendFrame(t, input[t]);
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    _output.WriteLine((input == frames ? "sorted" : "unsorted") + " grid writer allocated over " + (ticks - 120) + " frames: " + allocated + " bytes");
                    Assert.True(allocated < 16384, "allocated " + allocated + " bytes");
                    w.Finish();
                }
                using (var r = StreamScenes.Open(ms.ToArray())) Assert.True(frames[ticks - 1].ContentEquals(r.ReadFrame(ticks - 1)));
            }
        }

        [Fact]
        public void SequentialGridPlaybackDoesNotAllocate()
        {
            var frames = StreamScenes.AddGrid(StreamScenes.Smooth(60, 300, 4), 30, 53, 2);
            byte[] data = StreamScenes.Write(frames, StreamScenes.GridHeader("color_war", 30, 53));
            var dest = new RenderFrame();
            using (var reader = StreamScenes.Open(data))
            {
                for (int t = 0; t < 300; t++) reader.ReadFrame(t, dest);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < 300; t++) reader.ReadFrame(t, dest);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.True(allocated < 1024, "allocated " + allocated + " bytes over 300 frames");
            }
        }

        [Fact]
        public void ColorWarMinuteSize()
        {
            // M08 at its tier-A budget: 120 balls and a 30 x 53 grid for 10 s, extrapolated to a minute.
            const int ticks = 600;
            var frames = StreamScenes.AddGrid(StreamScenes.Smooth(120, ticks, 21), 30, 53, 22);
            byte[] data = StreamScenes.Write(frames, StreamScenes.GridHeader("color_war", 30, 53));
            double perMinute = data.Length * 6.0;
            _output.WriteLine("M08 120 balls + 30x53 grid, 10 s: " + data.Length + " bytes; ≈ " + (perMinute / 1048576.0).ToString("F2") + " MB/min");
            Assert.True(perMinute < 4.0 * 1024 * 1024, "M08 stream ≈ " + perMinute + " bytes/min");
        }

        // ------------------------------------------------------------------ corrupt grid data

        static byte[] Keyframe(Action<ByteWriter> grid)
        {
            var w = new ByteWriter(64);
            w.WriteUVarint(0);
            w.WriteUVarint(0);
            grid(w);
            return w.Span.ToArray();
        }

        static void AssertDecodeThrows(byte[] data, int frames = 1)
        {
            var codec = new FrameCodec(4, 3);
            var r = new ByteReader(data, 0, data.Length);
            var dest = new RenderFrame();
            Assert.Throws<InvalidDataException>(() =>
            {
                for (int i = 0; i < frames; i++) codec.Decode(ref r, dest);
            });
        }

        [Fact]
        public void CorruptGridDataIsInvalidData()
        {
            // 12 cells. Run longer than the grid.
            AssertDecodeThrows(Keyframe(w => { w.WriteUVarint(12); w.WriteByte(1); }));
            // Runs overshooting the remainder.
            AssertDecodeThrows(Keyframe(w => { w.WriteUVarint(5); w.WriteByte(1); w.WriteUVarint(6); w.WriteByte(2); }));
            // Truncated: runs cover only part of the grid.
            AssertDecodeThrows(Keyframe(w => { w.WriteUVarint(5); w.WriteByte(1); }));
            // Huge varint run.
            AssertDecodeThrows(Keyframe(w => { w.WriteUVarint(ulong.MaxValue); w.WriteByte(1); }));

            Action<ByteWriter> full = w => { w.WriteUVarint(11); w.WriteByte(0); };
            Func<Action<ByteWriter>, byte[]> withDelta = delta => Keyframe(w =>
            {
                full(w);
                w.WriteUVarint(0); w.WriteUVarint(0); w.WriteUVarint(0); w.WriteUVarint(0); // despawn, spawn, HUD removed, HUD set
                delta(w);
            });
            AssertDecodeThrows(withDelta(w => w.WriteByte(2)), 2);                                                       // unknown mode
            AssertDecodeThrows(withDelta(w => { w.WriteByte(0); w.WriteUVarint(1); w.WriteUVarint(12); w.WriteByte(1); }), 2); // index beyond grid
            AssertDecodeThrows(withDelta(w => { w.WriteByte(0); w.WriteUVarint(2); w.WriteUVarint(10); w.WriteByte(1); w.WriteUVarint(1); w.WriteByte(1); }), 2); // second index 12
            AssertDecodeThrows(withDelta(w => { w.WriteByte(0); w.WriteUVarint(13); }), 2);                               // more changes than cells
            AssertDecodeThrows(withDelta(w => { w.WriteByte(0); w.WriteUVarint(2); w.WriteUVarint(0); w.WriteByte(1); }), 2); // truncated list
            AssertDecodeThrows(withDelta(w => w.WriteByte(1)), 2);                                                       // empty RLE
            AssertDecodeThrows(withDelta(w => { }), 2);                                                                  // missing mode

            // Valid control: the same keyframe + a one-change delta decodes.
            byte[] ok = withDelta(w => { w.WriteByte(0); w.WriteUVarint(1); w.WriteUVarint(11); w.WriteByte(7); });
            var codec = new FrameCodec(4, 3);
            var r = new ByteReader(ok, 0, ok.Length);
            var dest = new RenderFrame();
            codec.Decode(ref r, dest);
            codec.Decode(ref r, dest);
            Assert.Equal(7, dest.GetCell(3, 2));
            Assert.Equal(0, r.Remaining);
        }
    }
}
