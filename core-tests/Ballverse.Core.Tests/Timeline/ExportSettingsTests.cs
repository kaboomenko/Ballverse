using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class ExportSettingsTests
    {
        [Theory]
        [InlineData(ExportResolution.P720, 720, 1280)]
        [InlineData(ExportResolution.P1080, 1080, 1920)]
        public void Dimensions(ExportResolution res, int w, int h)
        {
            var s = new ExportSettings(res, 30);
            Assert.Equal(w, s.Width);
            Assert.Equal(h, s.Height);
            Assert.Equal(res, s.Resolution);
        }

        [Theory]
        [InlineData(ExportResolution.P1080, 60, 16000000L)]
        [InlineData(ExportResolution.P1080, 30, 10000000L)]
        [InlineData(ExportResolution.P720, 60, 10000000L)]
        [InlineData(ExportResolution.P720, 30, 6000000L)]
        public void DefaultBitrates(ExportResolution res, int fps, long expected)
        {
            var s = new ExportSettings(res, fps);
            Assert.Equal(expected, s.VideoBitrateBps);
            Assert.Equal(192000L, s.AudioBitrateBps);
            Assert.Same(ExportBitrateTable.Default, s.Bitrates);
        }

        [Fact]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportSettings(ExportResolution.P1080, 25));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportSettings((ExportResolution)480, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportSettings.DefaultFps((DeviceTier)5));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportSettings.ReferenceExportTimeMs((DeviceTier)5, ExportResolution.P720, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => ExportSettings.ReferenceExportTimeMs(DeviceTier.A, ExportResolution.P720, 24));
            var s = new ExportSettings(ExportResolution.P720, 30);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.SegmentIndexOfFrame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.IsKeyframe(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.SegmentCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.EstimateFileSizeBytes(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M01, -1));
        }

        [Theory]
        [InlineData(DeviceTier.A, 60)]
        [InlineData(DeviceTier.B, 60)]
        [InlineData(DeviceTier.C, 30)]
        public void DefaultFpsByTier(DeviceTier tier, int fps)
        {
            Assert.Equal(fps, ExportSettings.DefaultFps(tier));
            var s = ExportSettings.DefaultFor(tier);
            Assert.Equal(fps, s.Fps);
            Assert.Equal(ExportResolution.P1080, s.Resolution);
        }

        [Fact]
        public void DefaultFor_UsesGivenBitrateTable()
        {
            var table = new ExportBitrateTable(12000000, 8000000, 8000000, 5000000, 128000);
            var s = ExportSettings.DefaultFor(DeviceTier.A, table);
            Assert.Equal(12000000, s.VideoBitrateBps);
            Assert.Equal(128000, s.AudioBitrateBps);
        }

        [Theory]
        [InlineData(30, 60)]
        [InlineData(60, 120)]
        public void GopAndSegmentAreTwoSeconds(int fps, int frames)
        {
            var s = new ExportSettings(ExportResolution.P1080, fps);
            Assert.Equal(frames, s.GopFrames);
            Assert.Equal(frames, s.SegmentFrames);
            Assert.True(s.IsKeyframe(0));
            Assert.False(s.IsKeyframe(1));
            Assert.False(s.IsKeyframe(frames - 1));
            Assert.True(s.IsKeyframe(frames));
            Assert.True(s.IsKeyframe(5 * frames));
        }

        [Fact]
        public void SegmentIndexOfFrame_Boundaries()
        {
            var s30 = new ExportSettings(ExportResolution.P720, 30);
            Assert.Equal(0, s30.SegmentIndexOfFrame(0));
            Assert.Equal(0, s30.SegmentIndexOfFrame(59));
            Assert.Equal(1, s30.SegmentIndexOfFrame(60));
            Assert.Equal(89, s30.SegmentIndexOfFrame(5399));

            var s60 = new ExportSettings(ExportResolution.P720, 60);
            Assert.Equal(0, s60.SegmentIndexOfFrame(119));
            Assert.Equal(1, s60.SegmentIndexOfFrame(120));
            Assert.Equal(89, s60.SegmentIndexOfFrame(10799));
        }

        [Theory]
        [InlineData(30, 0, 0)]
        [InlineData(30, 1, 1)]
        [InlineData(30, 30, 1)]       // 1 s
        [InlineData(30, 60, 1)]
        [InlineData(30, 61, 2)]
        [InlineData(30, 1800, 30)]
        [InlineData(30, 5400, 90)]    // 180 s
        [InlineData(60, 60, 1)]
        [InlineData(60, 10800, 90)]   // 180 s
        [InlineData(60, 10799, 90)]
        public void SegmentCount(int fps, int frames, int expected)
        {
            Assert.Equal(expected, new ExportSettings(ExportResolution.P1080, fps).SegmentCount(frames));
        }

        [Fact]
        public void GetSegment_RangesCoverAllFramesOnce()
        {
            var s = new ExportSettings(ExportResolution.P1080, 30);
            var segs = s.GetSegments(151);
            Assert.Equal(3, segs.Length);
            Assert.Equal(new ExportSegment(0, 0, 60), segs[0]);
            Assert.Equal(new ExportSegment(1, 60, 60), segs[1]);
            Assert.Equal(new ExportSegment(2, 120, 31), segs[2]);
            Assert.Equal(151, segs[2].EndFrame);

            var full = s.GetSegments(5400);
            Assert.Equal(90, full.Length);
            Assert.Equal(60, full[89].FrameCount);
            Assert.Equal(5340, full[89].StartFrame);

            Assert.Throws<ArgumentOutOfRangeException>(() => s.GetSegment(3, 151));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.GetSegment(-1, 151));
            Assert.Empty(s.GetSegments(0));
        }

        [Fact]
        public void Segments_PropertyEveryFrameInItsSegment()
        {
            var rng = Rng.FromSeed(2024, 7);
            for (int n = 0; n < 50; n++)
            {
                int fps = rng.NextBool() ? 60 : 30;
                int frames = rng.NextInt(fps, 180 * fps + 1);
                var s = new ExportSettings(ExportResolution.P720, fps);
                var segs = s.GetSegments(frames);
                int expectedStart = 0;
                foreach (var seg in segs)
                {
                    Assert.Equal(expectedStart, seg.StartFrame);
                    Assert.True(s.IsKeyframe(seg.StartFrame));
                    Assert.InRange(seg.FrameCount, 1, s.SegmentFrames);
                    Assert.Equal(seg.Index, s.SegmentIndexOfFrame(seg.StartFrame));
                    Assert.Equal(seg.Index, s.SegmentIndexOfFrame(seg.EndFrame - 1));
                    expectedStart = seg.EndFrame;
                }
                Assert.Equal(frames, expectedStart);
            }
        }

        [Fact]
        public void EstimateFileSize_1080p60_30s()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            // (16 000 000 + 192 000) · 30 / 8 = 60 720 000; + 1% + 64 KiB.
            Assert.Equal(60720000L + 607200L + 65536L, s.EstimateFileSizeBytes(1800));
        }

        [Fact]
        public void EstimateFileSize_720p30_1s_And180s()
        {
            var s = new ExportSettings(ExportResolution.P720, 30);
            Assert.Equal(774000L + 7740L + 65536L, s.EstimateFileSizeBytes(30));
            Assert.Equal(139320000L + 1393200L + 65536L, s.EstimateFileSizeBytes(5400));
        }

        [Fact]
        public void EstimateFileSize_RoundsUp()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            // 16 192 000 / 480 = 33 733.33 → 33 734; 1% = 337.34 → 338.
            Assert.Equal(33734L + 338L + 65536L, s.EstimateFileSizeBytes(1));
            Assert.Equal(65536L, s.EstimateFileSizeBytes(0));
        }

        [Fact]
        public void EstimateFileSize_UsesOverrideTable()
        {
            var table = ExportBitrateTable.Default.WithOverrides(new ExportBitrateOverrides { Video1080p60Bps = 8000000, AudioBps = 128000 });
            var s = new ExportSettings(ExportResolution.P1080, 60, table);
            // (8 000 000 + 128 000) · 30 / 8 = 30 480 000.
            Assert.Equal(30480000L + 304800L + 65536L, s.EstimateFileSizeBytes(1800));
        }

        [Fact]
        public void FreeSpace_IsTwiceEstimatePlus200MB_BoundaryInclusive()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            long estimate = s.EstimateFileSizeBytes(1800);
            long required = 2 * estimate + 200L * 1024 * 1024;
            Assert.Equal(required, s.RequiredFreeSpaceBytes(1800));
            Assert.Equal(332500672L, required);
            Assert.True(s.HasEnoughFreeSpace(1800, required));
            Assert.False(s.HasEnoughFreeSpace(1800, required - 1));
            Assert.True(s.HasEnoughFreeSpace(1800, long.MaxValue));
            Assert.False(s.HasEnoughFreeSpace(1800, 0));
        }

        [Fact]
        public void FreeSpace_WorstCase180s1080p60_FitsInLong()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            long estimate = s.EstimateFileSizeBytes(10800);
            Assert.Equal(364320000L + 3643200L + 65536L, estimate);
            Assert.Equal(2 * estimate + 209715200L, s.RequiredFreeSpaceBytes(10800));
        }

        [Theory]
        [InlineData(DeviceTier.A, ExportResolution.P720, 30, 10000L)]
        [InlineData(DeviceTier.A, ExportResolution.P1080, 30, 15000L)]
        [InlineData(DeviceTier.A, ExportResolution.P1080, 60, 30000L)]
        [InlineData(DeviceTier.A, ExportResolution.P720, 60, 20000L)]
        [InlineData(DeviceTier.B, ExportResolution.P720, 30, 20000L)]
        [InlineData(DeviceTier.B, ExportResolution.P1080, 30, 30000L)]
        [InlineData(DeviceTier.B, ExportResolution.P1080, 60, 60000L)]
        [InlineData(DeviceTier.B, ExportResolution.P720, 60, 40000L)]
        [InlineData(DeviceTier.C, ExportResolution.P720, 30, 40000L)]
        [InlineData(DeviceTier.C, ExportResolution.P1080, 30, 75000L)]
        [InlineData(DeviceTier.C, ExportResolution.P1080, 60, 150000L)]
        [InlineData(DeviceTier.C, ExportResolution.P720, 60, 80000L)]
        public void ReferenceExportTimes_MatchSpecTable(DeviceTier tier, ExportResolution res, int fps, long ms)
        {
            Assert.Equal(ms, ExportSettings.ReferenceExportTimeMs(tier, res, fps));
            var s = new ExportSettings(res, fps);
            Assert.Equal(ms, s.EstimateExportTimeMs(tier, ModeCode.M01, 30 * fps));
        }

        [Fact]
        public void ExportTime_ScalesLinearlyWithDuration()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            Assert.Equal(60000L, s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M03, 3600));
            Assert.Equal(180000L, s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M03, 10800));
            Assert.Equal(1000L, s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M03, 60));
            Assert.Equal(17L, s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M03, 1)); // 16.67 → 17
            Assert.Equal(0L, s.EstimateExportTimeMs(DeviceTier.A, ModeCode.M03, 0));

            var s30 = new ExportSettings(ExportResolution.P1080, 30);
            Assert.Equal(15000L, s30.EstimateExportTimeMs(DeviceTier.B, ModeCode.M03, 450));
        }

        [Theory]
        [InlineData(ModeCode.M09, true)]
        [InlineData(ModeCode.M15, true)]
        [InlineData(ModeCode.M01, false)]
        [InlineData(ModeCode.M14, false)]
        public void HeavyModes(ModeCode mode, bool heavy)
        {
            Assert.Equal(heavy, ExportSettings.IsHeavyMode(mode));
            var s = new ExportSettings(ExportResolution.P1080, 60);
            Assert.Equal(heavy ? 45000L : 30000L, s.EstimateExportTimeMs(DeviceTier.A, mode, 1800));
            Assert.Equal(heavy ? 225000L : 150000L, new ExportSettings(ExportResolution.P1080, 60).EstimateExportTimeMs(DeviceTier.C, mode, 1800));
        }

        [Fact]
        public void HeavyMode_180sOnTierC()
        {
            var s = new ExportSettings(ExportResolution.P1080, 60);
            Assert.Equal(1350000L, s.EstimateExportTimeMs(DeviceTier.C, ModeCode.M15, 10800));
        }
    }
}
