using System;
using System.IO;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>
    /// Pins the .bvs byte format: TestData/RenderStream/churn75.bvs was produced by this writer from
    /// <see cref="StreamScenes.Churn"/>(75, 2026) (integer-only, so platform independent). Any change to the encoding
    /// must bump <see cref="RenderStreamHeader.CurrentFormatVersion"/> and regenerate the file.
    /// </summary>
    public class RenderStreamGoldenTests
    {
        static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "RenderStream", "churn75.bvs");

        static RenderStreamHeader GoldenHeader()
        {
            return new RenderStreamHeader("golden", 60, 30, new RenderQuantization(12f, 4f));
        }

        [Fact]
        public void WriterReproducesGoldenFile()
        {
            byte[] golden = File.ReadAllBytes(GoldenPath);
            byte[] written = StreamScenes.Write(StreamScenes.Churn(75, 2026), GoldenHeader());
            Assert.Equal(golden, written);
        }

        [Fact]
        public void ReaderDecodesGoldenFile()
        {
            var expected = StreamScenes.Canonical(StreamScenes.Churn(75, 2026));
            using (var reader = new RenderStreamReader(File.OpenRead(GoldenPath)))
            {
                Assert.Equal("golden", reader.Header.ModeId);
                Assert.Equal(60, reader.Header.TickRate);
                Assert.Equal(30, reader.Header.BlockTicks);
                Assert.Equal(12f, reader.Header.Quantization.PositionRange);
                Assert.Equal(4f, reader.Header.Quantization.ScaleMax);
                Assert.Equal(75, reader.TickCount);
                Assert.Equal(3, reader.BlockCount);
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
            Assert.Equal(6, g[18]);                           // mode id length
            Assert.Equal(new byte[] { (byte)'B', (byte)'V', (byte)'S', (byte)'X' }, new ReadOnlySpan<byte>(g, g.Length - 4, 4).ToArray());
            Assert.Equal(75u, BitConverter.ToUInt32(g, g.Length - 28 + 12)); // footer tick count
        }
    }
}
