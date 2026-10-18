using System;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectHelpersTests
    {
        // ---- §3.10 watermark ----

        [Theory]
        [InlineData(false, false, true, true)]  // no entitlement: always on, toggle ignored
        [InlineData(true, false, true, true)]
        [InlineData(false, true, true, false)] // entitled: toggle decides, default off
        [InlineData(true, true, true, true)]
        [InlineData(true, false, false, false)] // kill switch removes it for everyone
        [InlineData(true, true, false, false)]
        public void WatermarkResolve(bool toggle, bool entitled, bool remoteEnabled, bool expected)
        {
            Assert.Equal(expected, WatermarkPolicy.Resolve(toggle, entitled, remoteEnabled));
        }

        [Fact]
        public void OldProjectReexportedAfterPurchaseHasNoWatermark()
        {
            var d = ProjectDefaults.NewProject("p", "n", Now, "ring_escape");
            Assert.True(WatermarkPolicy.Resolve(d.Output.Watermark, false));
            Assert.False(WatermarkPolicy.Resolve(d.Output.Watermark, true));
        }

        // ---- §3.8 export history ----

        [Fact]
        public void ExportHistoryAppendsAndRemembersSettings()
        {
            var d = Golden();
            ExportHistory.Add(d, new ExportRecord { At = Later, Res = 720, Fps = 30, Ms = 9000 });
            Assert.Equal(2, d.Exports.Count);
            Assert.Equal(720, ExportHistory.Latest(d).Res);
            Assert.Equal(720, d.Output.Resolution);
            Assert.Equal(30, d.Output.Fps);
            Assert.True(ProjectValidator.Sanitize(d).IsClean);
        }

        [Fact]
        public void ExportHistoryCapped()
        {
            var d = Golden();
            d.Exports = null;
            for (int i = 0; i < ProjectLimits.MaxExportRecords + 3; i++) ExportHistory.Add(d, new ExportRecord { At = Now, Ms = i });
            Assert.Equal(ProjectLimits.MaxExportRecords, d.Exports.Count);
            Assert.Equal(3, d.Exports[0].Ms);
            Assert.Null(ExportHistory.Latest(new ProjectDocument()));
            Assert.Null(ExportHistory.Latest(null));
            Assert.Throws<ArgumentNullException>(() => ExportHistory.Add(d, null));
        }

        // ---- timing ----

        [Fact]
        public void ClipOutputDuration()
        {
            Assert.Equal(30000000, ProjectTiming.ClipOutputUs(0, 1800, 1.0, 0, 0.5));
            Assert.Equal(15000000, ProjectTiming.ClipOutputUs(0, 1800, 2.0, 0, 0.5));
            // Last 2 s at 0.5x add 2 s: 28 + 4 = 32 s.
            Assert.Equal(32000000, ProjectTiming.ClipOutputUs(0, 1800, 1.0, 2, 0.5));
            // Slow-mo longer than the clip covers only the clip: 1 s at 0.25x = 4 s.
            Assert.Equal(4000000, ProjectTiming.ClipOutputUs(0, 60, 1.0, 3, 0.25));
            Assert.Equal(0, ProjectTiming.ClipOutputUs(100, 100, 1.0, 0, 0.5));
            Assert.Equal(0, ProjectTiming.ClipOutputUs(200, 100, 1.0, 0, 0.5));
            Assert.Equal(16667, ProjectTiming.TicksToUs(1));
            Assert.Equal(1000000, ProjectTiming.TicksToUs(60));
        }

        [Fact]
        public void ReelDurationByMode()
        {
            var d = Golden();
            Assert.Equal(32000000, ProjectTiming.ReelDurationUs(d));
            d.Output.DurationMode = DurationMode.MatchVideo;
            Assert.Equal(30000000, ProjectTiming.ReelDurationUs(d));
            d.Output.DurationMode = DurationMode.Manual;
            d.Output.ManualDurationUs = 12000000;
            Assert.Equal(12000000, ProjectTiming.ReelDurationUs(d));
            d.UserVideo = null;
            Assert.Equal(0, ProjectTiming.VideoLengthUs(null));
        }
    }
}
