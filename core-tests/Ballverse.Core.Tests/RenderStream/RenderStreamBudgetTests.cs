using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Spec §7.4 size budget ("~2–4 МБ/мин на 300 тел") and §3.7 scrubbing (≤ 100 ms).</summary>
    public class RenderStreamBudgetTests : IClassFixture<RenderStreamBudgetTests.SmoothStream>
    {
        public const int Bodies = 300;
        public const int Ticks = 60 * 60;
        public const long BudgetBytes = 4L * 1024 * 1024;

        /// <summary>300 bodies × 60 s × 60 Hz, generated and written once for all tests of this class.</summary>
        public sealed class SmoothStream
        {
            public readonly List<RenderFrame> Frames;
            public readonly byte[] Data;

            public SmoothStream()
            {
                Frames = StreamScenes.Smooth(Bodies, Ticks, 2024);
                Data = StreamScenes.Write(Frames, new RenderStreamHeader("ring_escape"));
            }
        }

        readonly SmoothStream _stream;
        readonly ITestOutputHelper _output;

        public RenderStreamBudgetTests(SmoothStream stream, ITestOutputHelper output)
        {
            _stream = stream;
            _output = output;
        }

        [Fact]
        public void ThreeHundredBodiesForSixtySecondsFitInFourMegabytes()
        {
            long size = _stream.Data.Length;
            long rawEstimate = 0;
            using (var reader = StreamScenes.Open(_stream.Data))
            {
                foreach (var b in reader.Blocks) rawEstimate += b.RawLength;
            }
            _output.WriteLine("300 bodies x 60 s x 60 Hz: .bvs = " + size + " bytes (" + (size / 1048576.0).ToString("F2")
                + " MB), raw block data = " + rawEstimate + " bytes, " + (size * 8.0 / Ticks / Bodies).ToString("F2") + " bits/body/frame");
            Assert.True(size <= BudgetBytes, "render stream is " + size + " bytes, budget " + BudgetBytes);
        }

        [Fact]
        public void RandomFrameDecodesQuickly()
        {
            var rng = Rng.FromSeed(99);
            var dest = new RenderFrame();
            double worstMs = 0;
            for (int i = 0; i < 20; i++)
            {
                int tick = rng.NextInt(0, Ticks);
                using (var reader = StreamScenes.Open(_stream.Data))
                {
                    var sw = Stopwatch.StartNew();
                    reader.ReadFrame(tick, dest);
                    sw.Stop();
                    worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds);
                }
                Assert.True(_stream.Frames[tick].ContentEquals(dest), "tick " + tick);
            }
            _output.WriteLine("worst random-frame decode (cold reader, incl. block decompression): " + worstMs.ToString("F3") + " ms");
            Assert.True(worstMs < 100, "random frame decode took " + worstMs + " ms");
        }

        [Fact]
        public void ScrubbingAcrossTheClipIsFast()
        {
            var rng = Rng.FromSeed(98);
            var dest = new RenderFrame();
            using (var reader = StreamScenes.Open(_stream.Data))
            {
                reader.ReadFrame(0, dest); // warm-up (JIT)
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) reader.ReadFrame(rng.NextInt(0, Ticks), dest);
                sw.Stop();
                double avg = sw.Elapsed.TotalMilliseconds / 100;
                _output.WriteLine("average random seek: " + avg.ToString("F3") + " ms");
                Assert.True(avg < 100, "average seek " + avg + " ms");
            }
        }

        [Fact]
        public void HudHeavyLongBlockSeekIsFast()
        {
            // Worst case the writer accepts for HUD merging: the maximum HUD size, unchanged, in one 3600-tick block.
            var frame = new RenderFrame(1, HudEntry.MaxEntriesPerFrame);
            for (int k = 0; k < HudEntry.MaxEntriesPerFrame; k++) frame.AddHud(k * 256, k);
            var ms = new System.IO.MemoryStream();
            var header = new RenderStreamHeader("m", 60, RenderStreamHeader.MaxBlockTicks, RenderQuantization.Default);
            using (var w = new RenderStreamWriter(ms, header, leaveOpen: true))
            {
                for (int t = 0; t < RenderStreamHeader.MaxBlockTicks; t++) w.AppendFrame(t, frame);
                w.Finish();
            }
            using (var reader = StreamScenes.Open(ms.ToArray()))
            {
                var dest = new RenderFrame();
                reader.ReadFrame(1, dest); // warm-up (JIT)
                var sw = Stopwatch.StartNew();
                reader.ReadFrame(RenderStreamHeader.MaxBlockTicks - 1, dest);
                sw.Stop();
                _output.WriteLine("file bytes = " + ms.Length + ", last-frame seek = " + sw.Elapsed.TotalMilliseconds.ToString("F3") + " ms");
                Assert.True(frame.ContentEquals(dest));
                Assert.True(sw.Elapsed.TotalMilliseconds < 100, "seek took " + sw.Elapsed.TotalMilliseconds + " ms");
            }
        }

        [Fact]
        public void SequentialPlaybackDoesNotAllocatePerFrame()
        {
            var dest = new RenderFrame(Bodies, 4);
            using (var reader = StreamScenes.Open(_stream.Data))
            {
                for (int t = 0; t < Ticks; t++) reader.ReadFrame(t, dest); // buffers reach peak size
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 0; t < 600; t++) reader.ReadFrame(t, dest);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                _output.WriteLine("allocated over 600 sequential frames (10 blocks): " + allocated + " bytes");
                Assert.True(allocated < 1024, "allocated " + allocated + " bytes over 600 frames");
            }
        }

        [Fact]
        public void SteadyStateAppendDoesNotAllocatePerFrame()
        {
            var ms = new System.IO.MemoryStream(_stream.Data.Length + 1024);
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("ring_escape"), leaveOpen: true))
            {
                for (int t = 0; t < 240; t++) w.AppendFrame(t, _stream.Frames[t]);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int t = 240; t < 1200; t++) w.AppendFrame(t, _stream.Frames[t]);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                _output.WriteLine("writer allocated over 960 frames: " + allocated + " bytes");
                Assert.True(allocated < 16384, "allocated " + allocated + " bytes over 960 frames");
            }
        }
    }
}
