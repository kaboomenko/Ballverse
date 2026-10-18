using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    public class RenderStreamRoundTripTests
    {
        static void AssertFrame(RenderFrame expected, RenderFrame actual, int tick)
        {
            Assert.True(expected.ContentEquals(actual), "tick " + tick + ": " + StreamScenes.Describe(expected, actual));
        }

        [Theory]
        [InlineData(1UL, 157)]
        [InlineData(2UL, 60)]
        [InlineData(3UL, 61)]
        [InlineData(4UL, 1)]
        [InlineData(5UL, 400)]
        public void ChurnRoundTripsBitExactSequentially(ulong seed, int ticks)
        {
            var frames = StreamScenes.Churn(ticks, seed);
            var expected = StreamScenes.Canonical(frames);
            byte[] data = StreamScenes.Write(frames);

            using (var reader = StreamScenes.Open(data))
            {
                Assert.Equal(ticks, reader.TickCount);
                Assert.Equal((ticks + 59) / 60, reader.BlockCount);
                int t = 0;
                foreach (var frame in reader.EnumerateFrames())
                {
                    AssertFrame(expected[t], frame, t);
                    t++;
                }
                Assert.Equal(ticks, t);
            }
        }

        [Fact]
        public void ChurnScenarioActuallyChurns()
        {
            // Guard against a generator regression that would make the round-trip test trivial.
            var frames = StreamScenes.Canonical(StreamScenes.Churn(400, 5));
            int spawns = 0, despawns = 0, kindChanges = 0, reuses = 0;
            var everSeen = new HashSet<int>();
            for (int t = 1; t < frames.Count; t++)
            {
                var prev = frames[t - 1];
                var cur = frames[t];
                foreach (var b in cur.Bodies)
                {
                    if (!prev.TryGetBody(b.Id, out var pb))
                    {
                        spawns++;
                        if (everSeen.Contains(b.Id)) reuses++;
                    }
                    else if (pb.Kind != b.Kind)
                    {
                        kindChanges++;
                    }
                }
                foreach (var b in prev.Bodies)
                {
                    everSeen.Add(b.Id);
                    if (!cur.TryGetBody(b.Id, out _)) despawns++;
                }
            }
            Assert.True(spawns > 100 && despawns > 100 && kindChanges > 5 && reuses > 10,
                "spawns " + spawns + ", despawns " + despawns + ", kind changes " + kindChanges + ", reuses " + reuses);
        }

        [Fact]
        public void RandomAccessMatchesAtBlockBoundariesAndMidBlock()
        {
            const int ticks = 250;
            var frames = StreamScenes.Churn(ticks, 77);
            var expected = StreamScenes.Canonical(frames);
            byte[] data = StreamScenes.Write(frames);

            using (var reader = StreamScenes.Open(data))
            {
                var dest = new RenderFrame();
                var probes = new[] { 0, 59, 60, 61, 119, 120, 121, 179, 180, 239, 240, 249, 30, 1, 0, 249, 248, 90, 89, 91, 90, 90 };
                foreach (int t in probes)
                {
                    reader.ReadFrame(t, dest);
                    AssertFrame(expected[t], dest, t);
                }

                var rng = Rng.FromSeed(123);
                for (int i = 0; i < 200; i++)
                {
                    int t = rng.NextInt(0, ticks);
                    reader.ReadFrame(t, dest);
                    AssertFrame(expected[t], dest, t);
                }

                // Backwards, then forwards through everything.
                for (int t = ticks - 1; t >= 0; t--)
                {
                    reader.ReadFrame(t, dest);
                    AssertFrame(expected[t], dest, t);
                }
                for (int t = 0; t < ticks; t++)
                {
                    AssertFrame(expected[t], reader.ReadFrame(t), t);
                }
            }
        }

        [Fact]
        public void EnumerateFromStartTick()
        {
            var frames = StreamScenes.Churn(130, 8);
            var expected = StreamScenes.Canonical(frames);
            using (var reader = StreamScenes.Open(StreamScenes.Write(frames)))
            {
                int t = 75;
                foreach (var f in reader.EnumerateFrames(75)) AssertFrame(expected[t], f, t++);
                Assert.Equal(130, t);
                Assert.Empty(reader.EnumerateFrames(130));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.EnumerateFrames(131));
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.EnumerateFrames(-1));
            }
        }

        [Fact]
        public void SmoothSceneRoundTrips()
        {
            var frames = StreamScenes.Smooth(50, 200, 3);
            byte[] data = StreamScenes.Write(frames);
            using (var reader = StreamScenes.Open(data))
            {
                for (int t = 0; t < frames.Count; t++) AssertFrame(frames[t], reader.ReadFrame(t), t);
            }
        }

        [Fact]
        public void HeaderRoundTrips()
        {
            var header = new RenderStreamHeader("рулетка_m07", 30, 17, new RenderQuantization(9.5f, 2.25f));
            var frames = StreamScenes.Churn(100, 9);
            var expected = StreamScenes.Canonical(frames);
            byte[] data = StreamScenes.Write(frames, header);
            using (var reader = StreamScenes.Open(data))
            {
                Assert.Equal("рулетка_m07", reader.Header.ModeId);
                Assert.Equal(30, reader.Header.TickRate);
                Assert.Equal(17, reader.Header.BlockTicks);
                Assert.Equal(new RenderQuantization(9.5f, 2.25f), reader.Header.Quantization);
                Assert.Equal(6, reader.BlockCount);
                Assert.Equal(85, reader.Blocks[5].FirstTick);
                Assert.Equal(15, reader.Blocks[5].FrameCount);
                for (int t = 99; t >= 0; t -= 3) AssertFrame(expected[t], reader.ReadFrame(t), t);
            }
        }

        [Fact]
        public void BlockIndexIsContiguous()
        {
            byte[] data = StreamScenes.Write(StreamScenes.Churn(200, 10));
            using (var reader = StreamScenes.Open(data))
            {
                long offset = reader.Blocks[0].Offset;
                Assert.Equal(4 + 2 + 2 + 2 + 4 + 4 + 1 + "test_mode".Length, offset);
                for (int i = 0; i < reader.BlockCount; i++)
                {
                    var b = reader.Blocks[i];
                    Assert.Equal(i * 60, b.FirstTick);
                    Assert.Equal(Math.Min(60, 200 - i * 60), b.FrameCount);
                    Assert.Equal(offset, b.Offset);
                    offset += b.CompressedLength;
                }
            }
        }

        [Fact]
        public void EmptyStreamIsValid()
        {
            byte[] data = StreamScenes.Write(new List<RenderFrame>());
            using (var reader = StreamScenes.Open(data))
            {
                Assert.Equal(0, reader.TickCount);
                Assert.Equal(0, reader.BlockCount);
                Assert.Empty(reader.EnumerateFrames());
                Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFrame(0));
            }
        }

        [Fact]
        public void EmptyFramesAndSingleBodyRoundTrip()
        {
            var frames = new List<RenderFrame>();
            for (int t = 0; t < 130; t++)
            {
                var f = new RenderFrame();
                if (t % 7 < 3) f.AddBody(new BodyState(t % 2 == 0 ? 5 : int.MinValue, BodyKind.Segment, (short)t, (short)-t, (ushort)(t * 1000), 7, 3, BodyFlags.Visible));
                if (t % 5 == 0) f.AddHud(0, t);
                frames.Add(f);
            }
            byte[] data = StreamScenes.Write(frames);
            using (var reader = StreamScenes.Open(data))
            {
                for (int t = 0; t < frames.Count; t++) AssertFrame(frames[t], reader.ReadFrame(t), t);
            }
        }

        [Fact]
        public void ExtremeTrajectoriesWrapBitExactly()
        {
            // Values that oscillate between the int16 / uint16 extremes stress the wrapped residuals.
            var frames = new List<RenderFrame>();
            var rng = Rng.FromSeed(31);
            for (int t = 0; t < 180; t++)
            {
                var f = new RenderFrame();
                short ext = (t % 2 == 0) ? short.MinValue : short.MaxValue;
                f.AddBody(new BodyState(1, BodyKind.Circle, ext, (short)(-1 - ext), (ushort)(t % 3 == 0 ? 0 : 65535), (ushort)(t % 2 == 0 ? 65535 : 0), 0, BodyFlags.None));
                f.AddBody(new BodyState(2, BodyKind.Polygon, (short)rng.NextUInt(), (short)rng.NextUInt(), (ushort)rng.NextUInt(), (ushort)rng.NextUInt(), (byte)rng.NextUInt(), (BodyFlags)(byte)rng.NextUInt()));
                f.AddHud(3, t % 2 == 0 ? int.MinValue : int.MaxValue);
                f.AddHud(HudEntry.MaxKey, (int)rng.NextUInt());
                frames.Add(f);
            }
            byte[] data = StreamScenes.Write(frames);
            using (var reader = StreamScenes.Open(data))
            {
                for (int t = 0; t < frames.Count; t++) AssertFrame(frames[t], reader.ReadFrame(t), t);
            }
        }

        [Fact]
        public void WriterIsDeterministic()
        {
            var frames = StreamScenes.Churn(200, 12);
            Assert.Equal(StreamScenes.Write(frames), StreamScenes.Write(frames));
            // Canonical (sorted) input produces the same bytes as shuffled input.
            Assert.Equal(StreamScenes.Write(frames), StreamScenes.Write(StreamScenes.Canonical(frames)));
        }

        [Fact]
        public void WriterDoesNotModifyCallerFrame()
        {
            var f = new RenderFrame();
            f.AddBody(new BodyState(9, BodyKind.Circle, 1, 2, 3, 4, 5, BodyFlags.Visible));
            f.AddBody(new BodyState(3, BodyKind.Circle, 1, 2, 3, 4, 5, BodyFlags.Visible));
            f.AddHud(4, 1);
            f.AddHud(2, 1);
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("m"), leaveOpen: true))
            {
                w.AppendFrame(0, f);
                w.Finish();
            }
            Assert.Equal(9, f.GetBody(0).Id);
            Assert.Equal(4, f.GetHud(0).Key);
        }

        [Fact]
        public void ReaderHonoursStreamBasePosition()
        {
            var frames = StreamScenes.Churn(70, 13);
            var ms = new MemoryStream();
            ms.Write(new byte[] { 1, 2, 3, 4, 5 }, 0, 5);
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("m"), leaveOpen: true))
            {
                for (int t = 0; t < frames.Count; t++) w.AppendFrame(t, frames[t]);
                w.Finish();
                Assert.Equal(ms.Length - 5, w.BytesWritten);
            }
            ms.Position = 5;
            using (var reader = new RenderStreamReader(ms))
            {
                var expected = StreamScenes.Canonical(frames);
                for (int t = 0; t < frames.Count; t++) AssertFrame(expected[t], reader.ReadFrame(t), t);
            }
        }

        [Fact]
        public void DequantizedFramesMatchWorldValuesWithinOneStep()
        {
            var q = RenderQuantization.Default;
            var f = new RenderFrame();
            f.AddBody(q.QuantizeBody(1, BodyKind.Circle, 1.2345f, -7.5f, 1.0f, 0.25f, 2, BodyFlags.Visible));
            using (var reader = StreamScenes.Open(StreamScenes.Write(new List<RenderFrame> { f })))
            {
                var b = reader.ReadFrame(0).GetBody(0);
                var rq = reader.Header.Quantization;
                Assert.InRange(rq.DequantizePosition(b.X), 1.2345f - 0.00025f, 1.2345f + 0.00025f);
                Assert.InRange(rq.DequantizePosition(b.Y), -7.5f - 0.00025f, -7.5f + 0.00025f);
                Assert.InRange(RenderQuantization.DequantizeAngle(b.Angle), 1.0f - 0.0001f, 1.0f + 0.0001f);
                Assert.InRange(rq.DequantizeScale(b.Scale), 0.25f - 0.0001f, 0.25f + 0.0001f);
            }
        }
    }
}
