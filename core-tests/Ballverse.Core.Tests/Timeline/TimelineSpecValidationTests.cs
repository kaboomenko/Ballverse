using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class TimelineSpecValidationTests
    {
        [Fact]
        public void ClipTiming_StoresValuesAndDerivedLengths()
        {
            var c = new ClipTiming(100, 700, 900, 150, 120, 30, SimAfterEnd.Loop);
            Assert.Equal(100, c.TrimInTick);
            Assert.Equal(700, c.TrimOutTick);
            Assert.Equal(900, c.AvailableTicks);
            Assert.Equal(150, c.SpeedPercent);
            Assert.Equal(120, c.SlowMoTicks);
            Assert.Equal(30, c.SlowMoFactorPercent);
            Assert.Equal(SimAfterEnd.Loop, c.AfterEnd);
            Assert.Equal(600, c.LengthTicks);
            Assert.Equal(120, c.EffectiveSlowMoTicks);
            Assert.Equal(699, c.LastTick);
        }

        [Fact]
        public void ClipTiming_Defaults()
        {
            var c = new ClipTiming(0, 60, 60);
            Assert.Equal(100, c.SpeedPercent);
            Assert.Equal(0, c.SlowMoTicks);
            Assert.Equal(50, c.SlowMoFactorPercent);
            Assert.Equal(SimAfterEnd.Freeze, c.AfterEnd);
        }

        [Fact]
        public void ClipTiming_SlowMoLongerThanClipIsClampedToClip()
        {
            var c = new ClipTiming(10, 40, 100, slowMoTicks: 180);
            Assert.Equal(30, c.EffectiveSlowMoTicks);
        }

        [Fact]
        public void ClipTiming_AcceptsExtremeTrimsAndRanges()
        {
            var full = new ClipTiming(0, SimConstants.MaxDurationTicks, SimConstants.MaxDurationTicks, 200, 180, 25);
            Assert.Equal(SimConstants.MaxDurationTicks, full.LengthTicks);
            var oneTick = new ClipTiming(SimConstants.MaxDurationTicks - 1, SimConstants.MaxDurationTicks, SimConstants.MaxDurationTicks, 50, 0, 50);
            Assert.Equal(1, oneTick.LengthTicks);
        }

        [Theory]
        [InlineData(-1, 10, 10, 100, 0, 50)]          // negative trimIn
        [InlineData(10, 10, 20, 100, 0, 50)]          // empty clip
        [InlineData(10, 5, 20, 100, 0, 50)]           // reversed trims
        [InlineData(0, 21, 20, 100, 0, 50)]           // trimOut past render stream
        [InlineData(0, 10, 0, 100, 0, 50)]            // empty render stream
        [InlineData(0, 10, 10801, 100, 0, 50)]        // render stream longer than one run
        [InlineData(0, 10, 10, 49, 0, 50)]            // speed < 0.5x
        [InlineData(0, 10, 10, 201, 0, 50)]           // speed > 2x
        [InlineData(0, 10, 10, 100, -1, 50)]          // negative slow-mo
        [InlineData(0, 10, 10, 100, 181, 50)]         // slow-mo > 3 s
        [InlineData(0, 10, 10, 100, 60, 24)]          // factor < 0.25
        [InlineData(0, 10, 10, 100, 60, 51)]          // factor > 0.5
        public void ClipTiming_RejectsInvalidValues(int trimIn, int trimOut, int available, int speed, int slowMo, int factor)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClipTiming(trimIn, trimOut, available, speed, slowMo, factor));
        }

        [Fact]
        public void ClipTiming_RejectsUnknownAfterEnd()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClipTiming(0, 10, 10, afterEnd: (SimAfterEnd)42));
        }

        [Theory]
        [InlineData(1.0, 100)]
        [InlineData(0.5, 50)]
        [InlineData(2.0, 200)]
        [InlineData(1.25, 125)]
        [InlineData(0.333, 33)]
        [InlineData(0.256, 26)]
        [InlineData(1.1, 110)]
        public void PercentFromRatio_RoundsToHundredths(double ratio, int expected)
        {
            Assert.Equal(expected, ClipTiming.PercentFromRatio(ratio));
        }

        [Theory]
        [InlineData(2.0, 120)]
        [InlineData(0.0, 0)]
        [InlineData(3.0, 180)]
        [InlineData(1.5, 90)]
        [InlineData(0.0083, 0)]
        [InlineData(0.0084, 1)]
        public void TicksFromSeconds_RoundsToTicks(double seconds, int expected)
        {
            Assert.Equal(expected, ClipTiming.TicksFromSeconds(seconds));
        }

        [Fact]
        public void Conversions_RejectNonFinite()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ClipTiming.PercentFromRatio(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => ClipTiming.TicksFromSeconds(double.PositiveInfinity));
        }

        [Fact]
        public void UserVideoTiming_StoresValuesAndDerived()
        {
            var v = new UserVideoTiming(1000000, 3000000, -500000, VideoAfterEnd.Black);
            Assert.Equal(2000000, v.LengthUs);
            Assert.Equal(1500000, v.EndOnOutputUs);
            Assert.Equal(2999999, v.LastPtsUs);
            Assert.Equal(VideoAfterEnd.Black, v.AfterEnd);
            Assert.Equal(-500000, v.OffsetUs);
        }

        [Theory]
        [InlineData(-1L, 100L, 0L)]
        [InlineData(100L, 100L, 0L)]
        [InlineData(200L, 100L, 0L)]
        [InlineData(0L, 3600000001L, 0L)]
        [InlineData(0L, 100L, 3600000001L)]
        [InlineData(0L, 100L, -3600000001L)]
        public void UserVideoTiming_RejectsInvalidValues(long trimIn, long trimOut, long offset)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UserVideoTiming(trimIn, trimOut, offset));
        }

        [Fact]
        public void UserVideoTiming_RejectsUnknownAfterEnd()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UserVideoTiming(0, 10, 0, (VideoAfterEnd)9));
        }

        [Fact]
        public void TimelineSpec_Validation()
        {
            var clip = new ClipTiming(0, 60, 60);
            Assert.Throws<ArgumentNullException>(() => new TimelineSpec(null, null, DurationMode.Auto, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, DurationMode.Auto, 25));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, DurationMode.Auto, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, (DurationMode)7, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, DurationMode.Manual, 60, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, DurationMode.Manual, 60, -5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(clip, null, DurationMode.Auto, 60, -5));
            Assert.Throws<ArgumentNullException>(() => new StudioTimeline(null));

            var spec = new TimelineSpec(clip, null, DurationMode.Manual, 30, 5000000);
            Assert.Same(clip, spec.Clip);
            Assert.Null(spec.Video);
            Assert.Equal(30, spec.Fps);
            Assert.Equal(5000000, spec.ManualDurationUs);
        }

        [Fact]
        public void OutputFps_OnlyThirtyAndSixty()
        {
            Assert.True(OutputFps.IsValid(30));
            Assert.True(OutputFps.IsValid(60));
            Assert.False(OutputFps.IsValid(24));
            Assert.False(OutputFps.IsValid(120));
            Assert.False(OutputFps.IsValid(-60));
        }
    }
}
