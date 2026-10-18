using System;
using System.IO;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    /// <summary>
    /// Freezes the v1 byte layout: <c>TestData/Replay/sample_v1.bvr</c> is <see cref="ReplayTestUtil.Sample"/> written with
    /// 3 records per chunk. If the writer's output changes, old replays in users' libraries would break (spec §3.9).
    /// To regenerate deliberately, set BVR_GOLDEN_OUT to a file path and run the tests.
    /// </summary>
    public class ReplayGoldenFileTests
    {
        static readonly string GoldenPath = Path.Combine(AppContext.BaseDirectory, "TestData", "Replay", "sample_v1.bvr");

        [Fact]
        public void WriterOutputMatchesGoldenFile()
        {
            byte[] actual = ReplayWriter.WriteToArray(ReplayTestUtil.Sample(), 3);
            string regen = Environment.GetEnvironmentVariable("BVR_GOLDEN_OUT");
            if (!string.IsNullOrEmpty(regen)) File.WriteAllBytes(regen, actual);
            Assert.Equal(File.ReadAllBytes(GoldenPath), actual);
        }

        [Fact]
        public void GoldenFileReadsBackToSample()
        {
            var read = ReplayReader.Read(File.ReadAllBytes(GoldenPath));
            ReplayTestUtil.AssertEqual(ReplayTestUtil.Sample(), read);
        }
    }
}
