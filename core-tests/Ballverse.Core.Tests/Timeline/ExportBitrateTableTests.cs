using System;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class ExportBitrateTableTests
    {
        static string ReadTestData(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Timeline", name));
        }

        [Fact]
        public void Default_MatchesSpecTable()
        {
            var t = ExportBitrateTable.Default;
            Assert.Equal(16000000L, t.GetVideoBps(ExportResolution.P1080, 60));
            Assert.Equal(10000000L, t.GetVideoBps(ExportResolution.P1080, 30));
            Assert.Equal(10000000L, t.GetVideoBps(ExportResolution.P720, 60));
            Assert.Equal(6000000L, t.GetVideoBps(ExportResolution.P720, 30));
            Assert.Equal(192000L, t.AudioBps);
        }

        [Fact]
        public void GetVideoBps_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportBitrateTable.Default.GetVideoBps(ExportResolution.P720, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportBitrateTable.Default.GetVideoBps((ExportResolution)1, 30));
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(31999L)]
        [InlineData(62500001L)]
        public void Constructor_RejectsOutOfRangeBitrates(long bps)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportBitrateTable(bps, 1000000, 1000000, 1000000, 128000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportBitrateTable(1000000, 1000000, 1000000, 1000000, bps));
        }

        [Fact]
        public void Constructor_AcceptsBounds()
        {
            var t = new ExportBitrateTable(ExportBitrateTable.MaxBps, ExportBitrateTable.MinBps, 1000000, 1000000, ExportBitrateTable.MinBps);
            Assert.Equal(ExportBitrateTable.MaxBps, t.Video1080p60Bps);
            Assert.Equal(ExportBitrateTable.MinBps, t.Video1080p30Bps);
        }

        [Fact]
        public void WithOverrides_ReplacesOnlyGivenFields()
        {
            var t = ExportBitrateTable.Default.WithOverrides(new ExportBitrateOverrides { Video720p60Bps = 9000000 });
            Assert.Equal(9000000L, t.Video720p60Bps);
            Assert.Equal(16000000L, t.Video1080p60Bps);
            Assert.Equal(10000000L, t.Video1080p30Bps);
            Assert.Equal(6000000L, t.Video720p30Bps);
            Assert.Equal(192000L, t.AudioBps);
            Assert.Same(ExportBitrateTable.Default, ExportBitrateTable.Default.WithOverrides(null));
            // The default table is not mutated.
            Assert.Equal(10000000L, ExportBitrateTable.Default.Video720p60Bps);
        }

        [Fact]
        public void WithOverrides_RejectsOutOfRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportBitrateTable.Default.WithOverrides(new ExportBitrateOverrides { AudioBps = 0 }));
        }

        [Fact]
        public void Overrides_JsonRoundTripIsCamelCaseAndSkipsNulls()
        {
            var o = new ExportBitrateOverrides { Video1080p60Bps = 14000000, AudioBps = 160000 };
            string json = o.ToJson();
            Assert.Equal("{\"video1080p60Bps\":14000000,\"audioBps\":160000}", json);
            var back = ExportBitrateOverrides.FromJson(json);
            Assert.Equal(14000000L, back.Video1080p60Bps);
            Assert.Equal(160000L, back.AudioBps);
            Assert.Null(back.Video720p30Bps);
        }

        [Fact]
        public void TryFromRemoteConfig_AppliesTestDataFile()
        {
            Assert.True(ExportBitrateTable.TryFromRemoteConfig(ReadTestData("bitrate_overrides.json"), out var t));
            Assert.Equal(14000000L, t.Video1080p60Bps);
            Assert.Equal(10000000L, t.Video1080p30Bps);
            Assert.Equal(10000000L, t.Video720p60Bps);
            Assert.Equal(5000000L, t.Video720p30Bps);
            Assert.Equal(160000L, t.AudioBps);

            var s = new ExportSettings(ExportResolution.P720, 30, t);
            Assert.Equal(5000000L, s.VideoBitrateBps);
            Assert.Equal(160000L, s.AudioBitrateBps);
        }

        [Fact]
        public void TryFromRemoteConfig_InvalidValuesFallBackToDefault()
        {
            Assert.False(ExportBitrateTable.TryFromRemoteConfig(ReadTestData("bitrate_overrides_invalid.json"), out var t));
            Assert.Same(ExportBitrateTable.Default, t);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[1,2]")]
        [InlineData("{\"audioBps\":\"loud\"}")]
        [InlineData("{\"audioBps\":1e40}")]
        public void TryFromRemoteConfig_MalformedFallsBackToDefault(string json)
        {
            Assert.False(ExportBitrateTable.TryFromRemoteConfig(json, out var t));
            Assert.Same(ExportBitrateTable.Default, t);
        }

        [Fact]
        public void TryFromRemoteConfig_EmptyObjectKeepsDefaults()
        {
            Assert.True(ExportBitrateTable.TryFromRemoteConfig("{}", out var t));
            Assert.Equal(16000000L, t.Video1080p60Bps);
            Assert.True(ExportBitrateTable.TryFromRemoteConfig("{\"unknownKey\":5}", out t));
            Assert.Equal(192000L, t.AudioBps);
        }
    }
}
