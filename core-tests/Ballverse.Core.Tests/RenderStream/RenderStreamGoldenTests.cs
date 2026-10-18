using System;
using System.IO;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>
    /// Pins the .bvs byte format: TestData/RenderStream/churn75.bvs was produced by this writer from
    /// <see cref="StreamScenes.Churn"/>(75, 2026) with <see cref="StreamScenes.AddGrid"/>(9 × 13, seed 7), the standard
    /// shape and label tables and <see cref="GoldenHeader"/> (integer-only, so platform independent). It covers every
    /// section: header with grid, blocks (bodies with shape / label / skin ids, HUD, both grid delta encodings), shape table,
    /// label table, index and footer.
    /// Format v1 was revised in place before release (shape table, label table, grid layer, skin ids) and the file was
    /// regenerated then; any further change to the encoding must bump <see cref="RenderStreamHeader.CurrentFormatVersion"/>
    /// and regenerate the file (run <see cref="WriterReproducesGoldenFile"/> after writing the new bytes).
    /// </summary>
    public class RenderStreamGoldenTests
    {
        static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "RenderStream", "churn75.bvs");

        static RenderStreamHeader GoldenHeader()
        {
            var q = new RenderQuantization(12f, 4f);
            var grid = new RenderGridSpec(q, 9, 13, GridTopology.HexOddRowsShifted, -12000, -20000, 12000, 20000);
            return new RenderStreamHeader("golden", 60, 30, q, grid);
        }

        static System.Collections.Generic.List<RenderFrame> GoldenFrames()
        {
            return StreamScenes.AddGrid(StreamScenes.Churn(75, 2026), 9, 13, 7);
        }

        [Fact]
        public void WriterReproducesGoldenFile()
        {
            byte[] written = StreamScenes.Write(GoldenFrames(), GoldenHeader());
            string regenerate = Environment.GetEnvironmentVariable("BALLVERSE_REGENERATE_GOLDEN");
            if (!string.IsNullOrEmpty(regenerate)) File.WriteAllBytes(regenerate, written);
            byte[] golden = File.ReadAllBytes(GoldenPath);
            Assert.Equal(golden, written);
        }

        [Fact]
        public void ReaderDecodesGoldenFile()
        {
            var expected = StreamScenes.Canonical(GoldenFrames());
            using (var reader = new RenderStreamReader(File.OpenRead(GoldenPath)))
            {
                Assert.Equal("golden", reader.Header.ModeId);
                Assert.Equal(60, reader.Header.TickRate);
                Assert.Equal(30, reader.Header.BlockTicks);
                Assert.Equal(12f, reader.Header.Quantization.PositionRange);
                Assert.Equal(4f, reader.Header.Quantization.ScaleMax);
                Assert.Equal(GoldenHeader().Grid, reader.Header.Grid);
                Assert.Equal(75, reader.TickCount);
                Assert.Equal(3, reader.BlockCount);
                Assert.Equal(StreamScenes.StandardShapes, reader.Shapes);
                Assert.Equal(StreamScenes.StandardLabels, StreamScenes.Labels(reader));
                for (int t = 74; t >= 0; t--) Assert.True(expected[t].ContentEquals(reader.ReadFrame(t)), "tick " + t);
            }
        }

        [Fact]
        public void GoldenHeaderBytes()
        {
            byte[] g = File.ReadAllBytes(GoldenPath);
            Assert.Equal(new byte[] { (byte)'B', (byte)'V', (byte)'S', (byte)'1' }, new ReadOnlySpan<byte>(g, 0, 4).ToArray());
            Assert.Equal(1, BitConverter.ToUInt16(g, 4));     // format version
            Assert.Equal(60, BitConverter.ToUInt16(g, 6));    // tick rate
            Assert.Equal(30, BitConverter.ToUInt16(g, 8));    // block ticks
            Assert.Equal(12f, BitConverter.ToSingle(g, 10));  // position range
            Assert.Equal(4f, BitConverter.ToSingle(g, 14));   // scale max
            Assert.Equal(9, BitConverter.ToUInt16(g, 18));    // grid width
            Assert.Equal(13, BitConverter.ToUInt16(g, 20));   // grid height
            Assert.Equal(1, g[22]);                           // grid topology
            Assert.Equal(-12000, BitConverter.ToInt16(g, 23)); // grid min x
            Assert.Equal(20000, BitConverter.ToInt16(g, 29));  // grid max y
            Assert.Equal(6, g[31]);                           // mode id length
            Assert.Equal(new byte[] { (byte)'B', (byte)'V', (byte)'S', (byte)'X' }, new ReadOnlySpan<byte>(g, g.Length - 4, 4).ToArray());
            int footer = g.Length - BvsSurgery.FooterSize;
            Assert.Equal(75u, BitConverter.ToUInt32(g, footer + 28)); // footer tick count
            Assert.Equal(3u, BitConverter.ToUInt32(g, footer + 24));  // footer block count
            ulong tablesOffset = BitConverter.ToUInt64(g, footer);
            Assert.Equal(StreamScenes.StandardShapes.Length, g[tablesOffset]); // shape count (one-byte uvarint)
        }
    }
}
