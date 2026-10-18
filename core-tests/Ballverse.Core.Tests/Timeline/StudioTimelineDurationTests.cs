using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class StudioTimelineDurationTests
    {
        static StudioTimeline Auto(ClipTiming clip, int fps)
        {
            return new StudioTimeline(clip, null, DurationMode.Auto, fps);
        }

        [Theory]
        [InlineData(60, 60, 60)]
        [InlineData(60, 30, 30)]
        [InlineData(1800, 60, 1800)]
        [InlineData(1800, 30, 900)]
        [InlineData(601, 60, 601)]
        [InlineData(601, 30, 301)]   // last partial frame kept: ⌈600.5 / 2⌉
        [InlineData(10800, 60, 10800)]
        [InlineData(10800, 30, 5400)]
        public void Auto_AtNormalSpeed_FrameCountIsExact(int lengthTicks, int fps, int expectedFrames)
        {
            var t = Auto(new ClipTiming(0, lengthTicks, SimConstants.MaxDurationTicks), fps);
            Assert.Equal(expectedFrames, t.FrameCount);
            Assert.Equal(expectedFrames, t.ClipEndFrame);
        }

        [Fact]
        public void Auto_ExactlyOneSecondAndExactly180Seconds()
        {
            var one = Auto(new ClipTiming(500, 560, 1000), 60);
            Assert.Equal(60, one.FrameCount);
            Assert.Equal(1000000, one.DurationUs);

            var max = Auto(new ClipTiming(0, 10800, 10800), 60);
            Assert.Equal(10800, max.FrameCount);
            Assert.Equal(180000000, max.DurationUs);
        }

        [Theory]
        [InlineData(1, 60, 60)]
        [InlineData(1, 30, 30)]
        [InlineData(59, 60, 60)]
        [InlineData(30, 30, 30)]
        public void ShortClip_IsClampedToOneSecond(int lengthTicks, int fps, int expectedFrames)
        {
            var t = Auto(new ClipTiming(0, lengthTicks, 100), fps);
            Assert.Equal(expectedFrames, t.FrameCount);
            Assert.True(t.ClipEndFrame < expectedFrames || lengthTicks >= 60);
        }

        [Theory]
        [InlineData(50, 60, 10800)]   // 180 s at 0.5x = 360 s → 180 s
        [InlineData(50, 30, 5400)]
        [InlineData(100, 60, 10800)]
        [InlineData(200, 60, 5400)]   // 90 s
        public void LongClip_IsClampedTo180Seconds(int speed, int fps, int expectedFrames)
        {
            var t = Auto(new ClipTiming(0, 10800, 10800, speed), fps);
            Assert.Equal(expectedFrames, t.FrameCount);
        }

        [Fact]
        public void ClampedLongClip_LastFrameShowsTheRightTick()
        {
            var t = Auto(new ClipTiming(0, 10800, 10800, 50), 60);
            Assert.Equal(21600, t.ClipEndFrame);
            Assert.Equal(SimSample.Playing(5399), t.SampleSim(10799));
        }

        [Theory]
        [InlineData(200, 60, 900)]     // 30 s clip at 2x = 15 s
        [InlineData(50, 60, 3600)]     // 60 s
        [InlineData(150, 60, 1200)]    // 20 s
        [InlineData(150, 30, 600)]
        [InlineData(125, 60, 1440)]    // 24 s
        [InlineData(75, 30, 1200)]     // 40 s
        public void Speed_ScalesClipOutputLength(int speed, int fps, int expectedFrames)
        {
            var t = Auto(new ClipTiming(0, 1800, 1800, speed), fps);
            Assert.Equal(expectedFrames, t.FrameCount);
        }

        [Fact]
        public void Speed_NonDivisibleLength_RoundsFrameCountUpAndDurationUp()
        {
            // 100 ticks at 1.5x = 100 / 90 s = 1.1111… s → 66.67 frames at 60 fps.
            var t = Auto(new ClipTiming(0, 100, 100, 150), 60);
            Assert.Equal(67, t.FrameCount);
            Assert.Equal(1111112, t.ClipOutputDurationUs);
            Assert.Equal(1116667, t.DurationUs);
        }

        [Theory]
        // 10 s clip, last 2 s slow-mo.
        [InlineData(100, 50, 60, 720)]  // 8 s + 4 s
        [InlineData(100, 25, 60, 960)]  // 8 s + 8 s
        [InlineData(200, 25, 60, 480)]  // 4 s + 4 s
        [InlineData(50, 50, 60, 1440)]  // 16 s + 8 s
        [InlineData(100, 50, 30, 360)]
        [InlineData(150, 40, 60, 520)]  // 480/1.5 = 320 frames + 120/0.6 = 200 frames
        public void SlowMo_LastSecondsOfClipPlayAtSpeedTimesFactor(int speed, int factor, int fps, int expectedFrames)
        {
            var t = Auto(new ClipTiming(0, 600, 600, speed, 120, factor), fps);
            Assert.Equal(expectedFrames, t.FrameCount);
        }

        [Fact]
        public void SlowMo_LongerThanClip_SlowsTheWholeClip()
        {
            var t = Auto(new ClipTiming(0, 60, 60, 100, 180, 50), 60);
            Assert.Equal(120, t.FrameCount);
            Assert.Equal(2000000, t.ClipOutputDurationUs);
        }

        [Fact]
        public void SlowMo_ZeroTicksDisablesIt()
        {
            var off = Auto(new ClipTiming(0, 600, 600, 100, 0, 25), 60);
            Assert.Equal(600, off.FrameCount);
        }

        [Fact]
        public void ClipOutputDuration_IsExactRational()
        {
            // 600 ticks, 3 s slow-mo at 0.37 × 1.3: 420 / 78 s + 180 / 28.86 s.
            var t = Auto(new ClipTiming(0, 600, 600, 130, 180, 37), 60);
            double expectedSeconds = 420.0 / (60 * 1.3) + 180.0 / (60 * 1.3 * 0.37);
            Assert.InRange(t.ClipOutputDurationUs - expectedSeconds * 1e6, 0.0, 1.0);
            Assert.Equal((int)Math.Ceiling(expectedSeconds * 60), t.ClipEndFrame);
        }

        [Theory]
        [InlineData(0L, 2000000L, 500000L, 30, 75)]       // ends at 2.5 s
        [InlineData(0L, 2000000L, 500000L, 60, 150)]
        [InlineData(0L, 2500001L, 0L, 30, 76)]            // partial frame kept
        [InlineData(1000000L, 6000000L, 0L, 60, 300)]     // trimmed length 5 s
        [InlineData(0L, 2000000L, -1500000L, 60, 120)]    // video starts first: output = video length
        [InlineData(0L, 2000000L, -5000000L, 60, 120)]    // video ends before the clip starts: still the video length
        [InlineData(0L, 500000L, -5000000L, 30, 30)]      // 0.5 s video → 1 s minimum
        [InlineData(0L, 200000000L, 0L, 60, 10800)]       // 200 s → 180 s
        [InlineData(0L, 180000000L, 0L, 30, 5400)]        // exactly 180 s
        [InlineData(0L, 179000000L, 1000000L, 30, 5400)]  // ends exactly at 180 s
        [InlineData(0L, 1000000L, 0L, 30, 30)]            // exactly 1 s
        public void MatchVideo_EndsWhereTheVideoEnds(long trimIn, long trimOut, long offset, int fps, int expectedFrames)
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600), new UserVideoTiming(trimIn, trimOut, offset), DurationMode.MatchVideo, fps);
            Assert.Equal(expectedFrames, t.FrameCount);
        }

        [Fact]
        public void MatchVideo_WithoutVideoFallsBackToAuto()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600), null, DurationMode.MatchVideo, 60);
            Assert.Equal(600, t.FrameCount);
        }

        [Theory]
        [InlineData(1L, 60, 60)]
        [InlineData(999999L, 60, 60)]
        [InlineData(1000000L, 60, 60)]
        [InlineData(1000001L, 60, 61)]
        [InlineData(10000000L, 60, 600)]
        [InlineData(10000000L, 30, 300)]
        [InlineData(180000000L, 60, 10800)]
        [InlineData(180000001L, 60, 10800)]
        [InlineData(600000000L, 30, 5400)]
        public void Manual_IsClampedAndRoundedUp(long manualUs, int fps, int expectedFrames)
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600), null, DurationMode.Manual, fps, manualUs);
            Assert.Equal(expectedFrames, t.FrameCount);
        }

        [Fact]
        public void Auto_IgnoresVideoAndManualValue()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600), new UserVideoTiming(0, 20000000), DurationMode.Auto, 60, 30000000);
            Assert.Equal(600, t.FrameCount);
        }

        [Fact]
        public void FrameTimes_AreExactMultiplesOfTheFramePeriod()
        {
            var t60 = Auto(new ClipTiming(0, 600, 600), 60);
            Assert.Equal(0, t60.FrameTimeUs(0));
            Assert.Equal(16667, t60.FrameTimeUs(1));
            Assert.Equal(33334, t60.FrameTimeUs(2));
            Assert.Equal(50000, t60.FrameTimeUs(3));
            Assert.Equal(9983334, t60.FrameTimeUs(599));

            var t30 = Auto(new ClipTiming(0, 600, 600), 30);
            Assert.Equal(33334, t30.FrameTimeUs(1));
            Assert.Equal(1000000, t30.FrameTimeUs(30));
        }

        [Fact]
        public void FrameAtTimeUs_InvertsFrameTimeAndClamps()
        {
            var t = Auto(new ClipTiming(0, 600, 600), 60);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(i, t.FrameAtTimeUs(t.FrameTimeUs(i)));
            Assert.Equal(0, t.FrameAtTimeUs(-100));
            Assert.Equal(0, t.FrameAtTimeUs(16666));
            Assert.Equal(599, t.FrameAtTimeUs(10000000));
            Assert.Equal(599, t.FrameAtTimeUs(long.MaxValue / 100));
        }

        [Fact]
        public void FrameIndexOutOfRangeThrows()
        {
            var t = Auto(new ClipTiming(0, 60, 60), 60);
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SampleSim(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SampleSim(60));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SampleVideo(60));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.FrameTimeUs(60));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.IsClipPlaying(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SampleSimAtTimeUs(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.SampleVideoAtTimeUs(StudioTimeline.MaxSampleTimeUs + 1));
        }

        [Fact]
        public void IsClipPlaying_MarksFirstPass()
        {
            var t = new StudioTimeline(new ClipTiming(0, 61, 100), null, DurationMode.Manual, 30, 3000000);
            Assert.Equal(31, t.ClipEndFrame);
            Assert.True(t.IsClipPlaying(30));
            Assert.False(t.IsClipPlaying(31));
        }
    }
}
