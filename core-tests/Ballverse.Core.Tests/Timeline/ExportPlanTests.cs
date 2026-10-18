using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class ExportPlanTests
    {
        static ExportPlan Plan(int lengthTicks, int fps, ExportResolution res = ExportResolution.P1080)
        {
            var timeline = new StudioTimeline(new ClipTiming(0, lengthTicks, SimConstants.MaxDurationTicks), null, DurationMode.Auto, fps);
            return new ExportPlan(timeline, new ExportSettings(res, fps));
        }

        [Fact]
        public void Constructor_Validates()
        {
            var timeline = new StudioTimeline(new ClipTiming(0, 600, 600), null, DurationMode.Auto, 60);
            Assert.Throws<ArgumentException>(() => new ExportPlan(timeline, new ExportSettings(ExportResolution.P1080, 30)));
            Assert.Throws<ArgumentNullException>(() => new ExportPlan(null, new ExportSettings(ExportResolution.P1080, 60)));
            Assert.Throws<ArgumentNullException>(() => new ExportPlan(timeline, null));
        }

        [Fact]
        public void FrameLoop_ThirtySecondsAt60()
        {
            var p = Plan(1800, 60);
            Assert.Equal(1800, p.FrameCount);
            Assert.Equal(15, p.SegmentCount);
            Assert.Equal(0, p.FramePtsUs(0));
            Assert.Equal(16667, p.FramePtsUs(1));
            Assert.Equal(29983334, p.FramePtsUs(1799));
            Assert.Equal(new ExportSegment(14, 1680, 120), p.GetSegment(14));
            Assert.Equal(14, p.SegmentIndexOfFrame(1799));
            Assert.Equal(15, p.GetSegments().Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SegmentIndexOfFrame(1800));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.FramePtsUs(1800));
        }

        [Fact]
        public void ResumeFrame_StartsAtNextSegment()
        {
            var p = Plan(301, 30); // 151 frames → 3 segments
            Assert.Equal(151, p.FrameCount);
            Assert.Equal(0, p.ResumeFrame(0));
            Assert.Equal(60, p.ResumeFrame(1));
            Assert.Equal(120, p.ResumeFrame(2));
            Assert.Equal(151, p.ResumeFrame(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.ResumeFrame(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.ResumeFrame(-1));
        }

        [Fact]
        public void Estimates_DelegateWithTimelineFrameCount()
        {
            var p = Plan(1800, 60);
            Assert.Equal(61392736L, p.EstimateFileSizeBytes());
            Assert.Equal(332500672L, p.RequiredFreeSpaceBytes());
            Assert.True(p.HasEnoughFreeSpace(332500672L));
            Assert.False(p.HasEnoughFreeSpace(332500671L));
            Assert.Equal(30000L, p.EstimateExportTimeMs(DeviceTier.A, ModeCode.M02));
            Assert.Equal(45000L, p.EstimateExportTimeMs(DeviceTier.A, ModeCode.M09));
        }

        [Fact]
        public void Acceptance_FrameCountAndDurationWithinOneFrame()
        {
            var p = Plan(1800, 60);
            Assert.True(p.IsFrameCountExact(1800));
            Assert.False(p.IsFrameCountExact(1799));
            Assert.False(p.IsFrameCountExact(1801));
            Assert.True(p.IsDurationAcceptable(30000000));
            Assert.True(p.IsDurationAcceptable(30016667));
            Assert.True(p.IsDurationAcceptable(29983333));
            Assert.False(p.IsDurationAcceptable(30016668));
            Assert.False(p.IsDurationAcceptable(29983332));
        }

        [Fact]
        public void MinAndMaxDurationPlans()
        {
            var min = Plan(1, 30, ExportResolution.P720);
            Assert.Equal(30, min.FrameCount);
            Assert.Equal(1, min.SegmentCount);
            Assert.Equal(new ExportSegment(0, 0, 30), min.GetSegment(0));

            var max = Plan(10800, 60);
            Assert.Equal(10800, max.FrameCount);
            Assert.Equal(90, max.SegmentCount);
            Assert.Equal(new ExportSegment(89, 10680, 120), max.GetSegment(89));
            Assert.Equal(179983334, max.FramePtsUs(10799));
        }
    }
}
