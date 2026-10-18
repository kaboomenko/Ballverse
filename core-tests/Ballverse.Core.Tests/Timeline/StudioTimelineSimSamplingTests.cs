using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class StudioTimelineSimSamplingTests
    {
        static StudioTimeline Manual(ClipTiming clip, int fps, long durationUs, UserVideoTiming video = null)
        {
            return new StudioTimeline(clip, video, DurationMode.Manual, fps, durationUs);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1234)]
        public void NormalSpeed60Fps_ConsecutiveFramesShowConsecutiveTicks(int trimIn)
        {
            var t = new StudioTimeline(new ClipTiming(trimIn, trimIn + 3600, 10800), null, DurationMode.Auto, 60);
            Assert.Equal(3600, t.FrameCount);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(SimSample.Playing(trimIn + i), t.SampleSim(i));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        public void NormalSpeed30Fps_FramesShowEverySecondTick(int trimIn)
        {
            var t = new StudioTimeline(new ClipTiming(trimIn, trimIn + 3600, 10800), null, DurationMode.Auto, 30);
            Assert.Equal(1800, t.FrameCount);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(SimSample.Playing(trimIn + 2 * i), t.SampleSim(i));
        }

        [Fact]
        public void DoubleSpeed_SkipsEveryOtherTick()
        {
            var t = new StudioTimeline(new ClipTiming(0, 1200, 1200, 200), null, DurationMode.Auto, 60);
            Assert.Equal(600, t.FrameCount);
            for (int i = 0; i < 600; i++) Assert.Equal(2 * i, t.SampleSim(i).Tick);
        }

        [Fact]
        public void HalfSpeed_ShowsEachTickTwice()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600, 50), null, DurationMode.Auto, 60);
            Assert.Equal(1200, t.FrameCount);
            for (int i = 0; i < 1200; i++) Assert.Equal(i / 2, t.SampleSim(i).Tick);
        }

        [Fact]
        public void OneAndAHalfSpeed_FloorsClipTime()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600, 150), null, DurationMode.Auto, 60);
            int[] expected = { 0, 1, 3, 4, 6, 7, 9 };
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], t.SampleSim(i).Tick);
            Assert.Equal(400, t.FrameCount);
            Assert.Equal(598, t.SampleSim(399).Tick);
        }

        [Fact]
        public void SlowMoFinale_SwitchesRateAtTheExactTick()
        {
            // 10 s clip, last 2 s at 0.5x: ticks 0..479 one per frame, then 480..599 two frames each.
            var t = new StudioTimeline(new ClipTiming(100, 700, 700, 100, 120, 50), null, DurationMode.Auto, 60);
            Assert.Equal(720, t.FrameCount);
            Assert.Equal(579, t.SampleSim(479).Tick);
            Assert.Equal(580, t.SampleSim(480).Tick);
            Assert.Equal(580, t.SampleSim(481).Tick);
            Assert.Equal(581, t.SampleSim(482).Tick);
            Assert.Equal(SimSample.Playing(699), t.SampleSim(719));
            Assert.Equal(SimSample.Playing(698), t.SampleSim(717));
        }

        [Fact]
        public void SlowMoFinale_QuarterFactorAtDoubleSpeed()
        {
            // Speed 2x, factor 0.25 → finale at 0.5x.
            var t = new StudioTimeline(new ClipTiming(0, 600, 600, 200, 120, 25), null, DurationMode.Auto, 60);
            Assert.Equal(480, t.FrameCount);
            Assert.Equal(478, t.SampleSim(239).Tick);
            Assert.Equal(480, t.SampleSim(240).Tick);
            Assert.Equal(480, t.SampleSim(241).Tick);
            Assert.Equal(599, t.SampleSim(479).Tick);
        }

        [Fact]
        public void SlowMoLongerThanClip_WholeClipSlowed()
        {
            var t = new StudioTimeline(new ClipTiming(0, 30, 30, 100, 180, 50), null, DurationMode.Auto, 60);
            Assert.Equal(60, t.FrameCount); // 1 s output
            for (int i = 0; i < 60; i++) Assert.Equal(SimSample.Playing(i / 2), t.SampleSim(i));
        }

        [Fact]
        public void Freeze_ShowsLastClipTickAfterTheEnd()
        {
            var t = Manual(new ClipTiming(100, 700, 900), 60, 15000000);
            Assert.Equal(SimSample.Playing(699), t.SampleSim(599));
            Assert.Equal(SimSample.Frozen(699), t.SampleSim(600));
            Assert.Equal(SimSample.Frozen(699), t.SampleSim(899));
        }

        [Fact]
        public void Freeze_OneTickClipClampedToOneSecond()
        {
            var t = new StudioTimeline(new ClipTiming(10799, 10800, 10800), null, DurationMode.Auto, 30);
            Assert.Equal(30, t.FrameCount);
            Assert.Equal(SimSample.Playing(10799), t.SampleSim(0));
            for (int i = 1; i < 30; i++) Assert.Equal(SimSample.Frozen(10799), t.SampleSim(i));
        }

        [Fact]
        public void Loop_WrapsToTrimInWithIterationCount()
        {
            var t = Manual(new ClipTiming(50, 110, 200, afterEnd: SimAfterEnd.Loop), 60, 3000000);
            Assert.Equal(SimSample.Playing(109, 0), t.SampleSim(59));
            Assert.Equal(SimSample.Playing(50, 1), t.SampleSim(60));
            Assert.Equal(SimSample.Playing(109, 1), t.SampleSim(119));
            Assert.Equal(SimSample.Playing(50, 2), t.SampleSim(120));
            Assert.Equal(SimSample.Playing(109, 2), t.SampleSim(179));
        }

        [Fact]
        public void Loop_HonoursSpeed()
        {
            var t = Manual(new ClipTiming(0, 120, 120, 200, afterEnd: SimAfterEnd.Loop), 60, 2000000);
            Assert.Equal(SimSample.Playing(118, 0), t.SampleSim(59));
            Assert.Equal(SimSample.Playing(0, 1), t.SampleSim(60));
            Assert.Equal(SimSample.Playing(2, 1), t.SampleSim(61));
        }

        [Fact]
        public void Loop_RepeatsTheFinaleSlowMo()
        {
            // 2 s clip, last 1 s at 0.5x → 3 s per pass.
            var t = Manual(new ClipTiming(0, 120, 120, 100, 60, 50, SimAfterEnd.Loop), 60, 7000000);
            Assert.Equal(180, t.ClipFrameCount);
            Assert.Equal(SimSample.Playing(0, 1), t.SampleSim(180));
            Assert.Equal(SimSample.Playing(59, 1), t.SampleSim(239));
            Assert.Equal(SimSample.Playing(60, 1), t.SampleSim(240));
            Assert.Equal(SimSample.Playing(60, 1), t.SampleSim(241));
            Assert.Equal(SimSample.Playing(61, 1), t.SampleSim(242));
            Assert.Equal(SimSample.Playing(119, 1), t.SampleSim(359));
            Assert.Equal(SimSample.Playing(0, 2), t.SampleSim(360));
        }

        [Fact]
        public void Loop_ClipEndNotFrameAligned()
        {
            // 61 ticks at 30 fps: the pass ends between frames 30 and 31.
            var t = Manual(new ClipTiming(0, 61, 100, afterEnd: SimAfterEnd.Loop), 30, 3000000);
            Assert.Equal(SimSample.Playing(60, 0), t.SampleSim(30));
            Assert.Equal(SimSample.Playing(1, 1), t.SampleSim(31));
            Assert.Equal(SimSample.Playing(3, 1), t.SampleSim(32));
        }

        [Fact]
        public void SlowMoReplay_ReplaysLastTwoSecondsAtHalfSpeed()
        {
            var t = Manual(new ClipTiming(100, 700, 900, afterEnd: SimAfterEnd.SlowMoReplay), 60, 20000000);
            Assert.Equal(SimSample.Playing(699, 0), t.SampleSim(599));
            Assert.Equal(SimSample.Playing(580, 1), t.SampleSim(600));
            Assert.Equal(SimSample.Playing(580, 1), t.SampleSim(601));
            Assert.Equal(SimSample.Playing(581, 1), t.SampleSim(602));
            Assert.Equal(SimSample.Playing(699, 1), t.SampleSim(839));
            Assert.Equal(SimSample.Playing(580, 2), t.SampleSim(840));
            Assert.Equal(SimSample.Playing(699, 2), t.SampleSim(1079));
            Assert.Equal(SimSample.Playing(580, 3), t.SampleSim(1080));
        }

        [Fact]
        public void SlowMoReplay_IsAbsoluteHalfSpeedRegardlessOfClipSpeedAndFps()
        {
            var fast = Manual(new ClipTiming(0, 600, 600, 200, 60, 25, SimAfterEnd.SlowMoReplay), 30, 20000000);
            // Clip: 540 ticks at 2x (270 output ticks) + 60 ticks at 0.5x (120 output ticks) = 6.5 s = 195 frames at 30 fps.
            Assert.Equal(195, fast.ClipFrameCount);
            Assert.Equal(SimSample.Playing(480, 1), fast.SampleSim(195));
            Assert.Equal(SimSample.Playing(481, 1), fast.SampleSim(196));
            Assert.Equal(SimSample.Playing(599, 1), fast.SampleSim(195 + 119));
            Assert.Equal(SimSample.Playing(480, 2), fast.SampleSim(195 + 120));
        }

        [Fact]
        public void SlowMoReplay_ClipShorterThanTwoSecondsReplaysWholeClip()
        {
            var t = Manual(new ClipTiming(10, 40, 40, afterEnd: SimAfterEnd.SlowMoReplay), 60, 3000000);
            Assert.Equal(SimSample.Playing(39, 0), t.SampleSim(29));
            Assert.Equal(SimSample.Playing(10, 1), t.SampleSim(30));
            Assert.Equal(SimSample.Playing(39, 1), t.SampleSim(89));
            Assert.Equal(SimSample.Playing(10, 2), t.SampleSim(90));
        }

        [Fact]
        public void ContinueSim_PlaysPastTrimOutThenFreezesAtStreamEnd()
        {
            var t = Manual(new ClipTiming(0, 600, 900, afterEnd: SimAfterEnd.ContinueSim), 60, 20000000);
            Assert.Equal(SimSample.Playing(599), t.SampleSim(599));
            Assert.Equal(SimSample.Playing(600), t.SampleSim(600));
            Assert.Equal(SimSample.Playing(899), t.SampleSim(899));
            Assert.Equal(SimSample.Frozen(899), t.SampleSim(900));
            Assert.Equal(SimSample.Frozen(899), t.SampleSim(1199));
        }

        [Fact]
        public void ContinueSim_ResumesBaseSpeedAfterSlowMo()
        {
            var t = Manual(new ClipTiming(0, 600, 900, 100, 60, 50, SimAfterEnd.ContinueSim), 60, 20000000);
            Assert.Equal(660, t.ClipFrameCount);
            Assert.Equal(SimSample.Playing(599), t.SampleSim(659));
            Assert.Equal(SimSample.Playing(600), t.SampleSim(660));
            Assert.Equal(SimSample.Playing(601), t.SampleSim(661));
        }

        [Fact]
        public void ContinueSim_HonoursSpeed()
        {
            var t = Manual(new ClipTiming(0, 600, 1200, 200, afterEnd: SimAfterEnd.ContinueSim), 60, 11000000);
            Assert.Equal(SimSample.Playing(600), t.SampleSim(300));
            Assert.Equal(SimSample.Playing(602), t.SampleSim(301));
            Assert.Equal(SimSample.Playing(1198), t.SampleSim(599));
            Assert.Equal(SimSample.Frozen(1199), t.SampleSim(600));
        }

        [Fact]
        public void ContinueSim_WithNoExtraTicksFreezesAtLastClipTick()
        {
            var t = Manual(new ClipTiming(0, 600, 600, afterEnd: SimAfterEnd.ContinueSim), 60, 12000000);
            Assert.Equal(SimSample.Frozen(599), t.SampleSim(600));
        }

        [Theory]
        [InlineData(SimAfterEnd.Freeze)]
        [InlineData(SimAfterEnd.Loop)]
        [InlineData(SimAfterEnd.SlowMoReplay)]
        [InlineData(SimAfterEnd.ContinueSim)]
        public void VideoExpandSim_DoesNotAffectSimSampling(SimAfterEnd simAfterEnd)
        {
            var clip = new ClipTiming(30, 330, 600, 120, 90, 30, simAfterEnd);
            var a = Manual(clip, 60, 20000000, new UserVideoTiming(0, 2000000, 0, VideoAfterEnd.ExpandSim));
            var b = Manual(clip, 60, 20000000, new UserVideoTiming(0, 2000000, 0, VideoAfterEnd.Freeze));
            var c = Manual(clip, 60, 20000000);
            for (int i = 0; i < a.FrameCount; i++)
            {
                Assert.Equal(b.SampleSim(i), a.SampleSim(i));
                Assert.Equal(c.SampleSim(i), a.SampleSim(i));
            }
            Assert.Equal(VideoSampleKind.ExpandSim, a.SampleVideo(a.FrameCount - 1).Kind);
        }

        [Fact]
        public void SampleSimAtTimeUs_MatchesFramesAndFloorsBetweenThem()
        {
            var t = Manual(new ClipTiming(0, 600, 600, 100, 120, 50), 60, 12000000);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(t.SampleSim(i), t.SampleSimAtTimeUs(t.FrameTimeUs(i)));
            Assert.Equal(0, t.SampleSimAtTimeUs(16666).Tick);
            Assert.Equal(1, t.SampleSimAtTimeUs(16667).Tick);
            Assert.Equal(480, t.SampleSimAtTimeUs(8000000).Tick);
            Assert.Equal(480, t.SampleSimAtTimeUs(8033333).Tick);
            Assert.Equal(481, t.SampleSimAtTimeUs(8033334).Tick);
            Assert.Equal(SimSample.Frozen(599), t.SampleSimAtTimeUs(12000000));
            Assert.Equal(SimSample.Frozen(599), t.SampleSimAtTimeUs(StudioTimeline.MaxSampleTimeUs));
        }

        [Fact]
        public void ClipTickToOutputUs_InvertsSampling()
        {
            var t = new StudioTimeline(new ClipTiming(100, 700, 700, 100, 120, 50), null, DurationMode.Auto, 60);
            Assert.Equal(0, t.ClipTickToOutputUs(100));
            Assert.Equal(1000000, t.ClipTickToOutputUs(160));
            Assert.Equal(8000000, t.ClipTickToOutputUs(580));
            Assert.Equal(10000000, t.ClipTickToOutputUs(640));
            Assert.Equal(12000000, t.ClipTickToOutputUs(700));
            Assert.Equal(t.ClipOutputDurationUs, t.ClipTickToOutputUs(700));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.ClipTickToOutputUs(99));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.ClipTickToOutputUs(701));
        }

        [Fact]
        public void HiddenIsNeverProduced()
        {
            foreach (SimAfterEnd mode in Enum.GetValues(typeof(SimAfterEnd)))
            {
                var t = Manual(new ClipTiming(0, 30, 60, afterEnd: mode), 30, 5000000);
                for (int i = 0; i < t.FrameCount; i++) Assert.NotEqual(SimSampleKind.Hidden, t.SampleSim(i).Kind);
            }
        }
    }
}
