using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class ExportBitrateTableTests
    {
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
        public void TableFeedsExportSettings()
        {
            // Remote Config export_bitrates is parsed by the Config module and mapped by Integration.RemoteConfigAdapters.
            var t = ExportBitrateTable.Default.WithOverrides(new ExportBitrateOverrides { Video720p30Bps = 5000000, AudioBps = 160000 });
            var s = new ExportSettings(ExportResolution.P720, 30, t);
            Assert.Equal(5000000L, s.VideoBitrateBps);
            Assert.Equal(160000L, s.AudioBitrateBps);
        }
    }
}
