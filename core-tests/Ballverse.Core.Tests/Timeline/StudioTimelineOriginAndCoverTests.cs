using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    /// <summary>Output origin (start of the duration's reference track) and the cover lead-in (spec §3.4).</summary>
    public class StudioTimelineOriginAndCoverTests
    {
        static readonly ClipTiming TenSecondClip = new ClipTiming(0, 600, 600);

        [Fact]
        public void MatchVideo_NegativeOffset_DurationEqualsVideoLengthAndKeepsVideoHead()
        {
            var t = new StudioTimeline(TenSecondClip, new UserVideoTiming(0, 10000000, -3000000), DurationMode.MatchVideo, 60);
            Assert.Equal(600, t.FrameCount);
            Assert.Equal(10000000, t.DurationUs);
            Assert.Equal(VideoSample.Frame(0), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(9983334), t.SampleVideo(599));
            Assert.Equal(180, t.ClipStartFrame);
            Assert.Equal(3000000, t.ClipStartUs);
            for (int i = 0; i < 180; i++) Assert.Equal(SimSample.Frozen(0), t.SampleSim(i));
            Assert.False(t.IsClipPlaying(179));
            Assert.True(t.IsClipPlaying(180));
            for (int i = 180; i < 600; i++) Assert.Equal(SimSample.Playing(i - 180), t.SampleSim(i));
        }

        [Fact]
        public void MatchVideo_PositiveOffset_DurationEqualsVideoLengthAndCutsTheClipHead()
        {
            // "= V" (spec §3.4): the output is exactly the video; the clip, which started 5 s earlier, is already playing.
            var t = new StudioTimeline(TenSecondClip, new UserVideoTiming(0, 10000000, 5000000), DurationMode.MatchVideo, 60);
            Assert.Equal(600, t.FrameCount);
            Assert.Equal(10000000, t.DurationUs);
            Assert.Equal(0, t.VideoStartUs);
            Assert.Equal(-5000000, t.ClipStartUs);
            Assert.Equal(0, t.ClipStartFrame);
            Assert.Equal(300, t.ClipEndFrame);
            Assert.Equal(VideoSample.Frame(0), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(9983334), t.SampleVideo(599));
            Assert.Equal(SimSample.Playing(300), t.SampleSim(0));
            Assert.Equal(SimSample.Playing(599), t.SampleSim(299));
            Assert.Equal(SimSample.Frozen(599), t.SampleSim(300));
        }

        [Fact]
        public void Auto_NegativeOffset_DurationEqualsClipLengthAndCutsTheVideoHead()
        {
            // "Авто (= длина S после обрезки)" (spec §3.4, Foundation DurationMode.Auto): exactly the clip, even when V starts first.
            var t = new StudioTimeline(TenSecondClip, new UserVideoTiming(0, 20000000, -3000000), DurationMode.Auto, 60);
            Assert.Equal(600, t.FrameCount);
            Assert.Equal(10000000, t.DurationUs);
            Assert.Equal(600, t.ClipEndFrame);
            Assert.Equal(0, t.ClipStartUs);
            Assert.Equal(-3000000, t.VideoStartUs);
            Assert.Equal(SimSample.Playing(0), t.SampleSim(0));
            Assert.Equal(SimSample.Playing(599), t.SampleSim(599));
            Assert.Equal(VideoSample.Frame(3000000), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(12983334), t.SampleVideo(599));
        }

        [Fact]
        public void Manual_StartsAtTheClipLikeAuto()
        {
            var t = new StudioTimeline(TenSecondClip, new UserVideoTiming(0, 20000000, -3000000), DurationMode.Manual, 60, 12000000);
            Assert.Equal(720, t.FrameCount);
            Assert.Equal(SimSample.Playing(0), t.SampleSim(0));
            Assert.Equal(VideoSample.Frame(3000000), t.SampleVideo(0));
            Assert.Equal(SimSample.Frozen(599), t.SampleSim(600));
        }

        [Theory]
        [InlineData(10000L, 1)]
        [InlineData(16667L, 1)]   // = FrameTimeUs(1): exactly one frame (the −1 frame nudge)
        [InlineData(16666L, 1)]
        [InlineData(16668L, 2)]
        [InlineData(33333L, 2)]
        [InlineData(33334L, 2)]   // = FrameTimeUs(2)
        [InlineData(50000L, 3)]   // = FrameTimeUs(3), exact
        [InlineData(1000000L, 60)]
        public void FractionalLead_At60Fps_ConsecutiveFramesStillShowConsecutiveTicks(long leadUs, int clipStartFrame)
        {
            var t = new StudioTimeline(TenSecondClip, new UserVideoTiming(0, 3000000, -leadUs), DurationMode.MatchVideo, 60);
            Assert.Equal(clipStartFrame, t.ClipStartFrame);
            for (int i = 0; i < clipStartFrame; i++) Assert.Equal(SimSample.Frozen(0), t.SampleSim(i));
            for (int i = clipStartFrame; i < t.FrameCount; i++) Assert.Equal(SimSample.Playing(i - clipStartFrame), t.SampleSim(i));
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(t.SampleSim(i), t.SampleSimAtTimeUs(t.FrameTimeUs(i)));
        }

        [Theory]
        [InlineData(60, 6)]
        [InlineData(30, 3)]
        public void Cover_PrependsATenthOfASecondOfTheChosenFrame(int fps, int coverFrames)
        {
            var video = new UserVideoTiming(1000000, 21000000);
            var t = new StudioTimeline(TenSecondClip, video, DurationMode.Auto, fps, 0, 2000000);
            Assert.Equal(coverFrames, t.CoverFrameCount);
            Assert.Equal(10 * fps + coverFrames, t.FrameCount);
            Assert.Equal(10100000, t.DurationUs);
            Assert.Equal(100000, t.ContentStartUs);
            Assert.Equal(100000, t.ClipStartUs);
            Assert.Equal(100000, t.FrameTimeUs(coverFrames));
            for (int i = 0; i < coverFrames; i++)
            {
                Assert.True(t.IsCoverFrame(i));
                Assert.Equal(SimSample.Frozen(120), t.SampleSim(i));
                Assert.Equal(VideoSample.Frozen(3000000), t.SampleVideo(i));
            }
            Assert.False(t.IsCoverFrame(coverFrames));
            Assert.Equal(SimSample.Playing(0), t.SampleSim(coverFrames));
            Assert.Equal(VideoSample.Frame(1000000), t.SampleVideo(coverFrames));
            Assert.Equal(SimSample.Playing(599 - (60 / fps - 1)), t.SampleSim(t.FrameCount - 1));
            Assert.Equal(SimSample.Frozen(120), t.SampleSimAtTimeUs(99999));
            Assert.Equal(SimSample.Playing(0), t.SampleSimAtTimeUs(100000));
            Assert.Equal(100000, t.ClipTickToOutputUs(0));

            // Content frames are the cover-less timeline shifted by exactly fps / 10 frames.
            var plain = new StudioTimeline(TenSecondClip, video, DurationMode.Auto, fps);
            for (int i = 0; i < plain.FrameCount; i++)
            {
                Assert.Equal(plain.SampleSim(i), t.SampleSim(i + coverFrames));
                Assert.Equal(plain.SampleVideo(i), t.SampleVideo(i + coverFrames));
                Assert.Equal(plain.FrameTimeUs(i) + 100000, t.FrameTimeUs(i + coverFrames));
            }
        }

        [Theory]
        [InlineData(60)]
        [InlineData(30)]
        public void Cover_TotalStillClampedTo180Seconds(int fps)
        {
            var t = new StudioTimeline(TenSecondClip, null, DurationMode.Manual, fps, 180000000, 0);
            Assert.Equal(180 * fps, t.FrameCount);
            Assert.Equal(180000000, t.DurationUs);
            Assert.Equal(SimSample.Frozen(0), t.SampleSim(0));
            Assert.Equal(VideoSample.NoVideo, t.SampleVideo(0));
        }

        [Fact]
        public void Cover_TimeBeyondContentUsesTheLastContentFrame()
        {
            var t = new StudioTimeline(TenSecondClip, null, DurationMode.Auto, 60, 0, 150000000);
            Assert.Equal(SimSample.Frozen(599), t.SampleSim(0));
        }

        [Theory]
        [InlineData(SimAfterEnd.Loop)]
        [InlineData(SimAfterEnd.SlowMoReplay)]
        [InlineData(SimAfterEnd.ContinueSim)]
        public void Cover_TimeBeyondContent_UsesTheLastContentFrameNotTheAfterEndFrame(SimAfterEnd afterEnd)
        {
            // After the content each behaviour shows a different tick (Loop: trimIn, replay: tick 480, continue: 600).
            var clip = new ClipTiming(0, 600, 1200, afterEnd: afterEnd);
            var video = new UserVideoTiming(0, 5000000, 0, VideoAfterEnd.Loop);
            var t = new StudioTimeline(clip, video, DurationMode.Auto, 60, 0, 150000000);
            Assert.Equal(606, t.FrameCount);
            Assert.Equal(SimSample.Frozen(599), t.SampleSim(0));
            Assert.Equal(VideoSample.Frozen(4983334), t.SampleVideo(0)); // frame 599 = 9.98 s → loop 1 at 4.98 s
            Assert.Equal(SimSample.Playing(599), t.SampleSim(605));
        }

        [Fact]
        public void Cover_OffByDefault()
        {
            var t = new StudioTimeline(TenSecondClip, null, DurationMode.Auto, 60);
            Assert.Equal(0, t.CoverFrameCount);
            Assert.Equal(0, t.ContentStartUs);
            Assert.False(t.IsCoverFrame(0));
            Assert.Null(t.Spec.CoverTimeUs);
        }

        [Fact]
        public void Cover_InvalidTimeRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(TenSecondClip, null, DurationMode.Auto, 60, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimelineSpec(TenSecondClip, null, DurationMode.Auto, 60, 0, 180000001));
            Assert.Equal(180000000, new TimelineSpec(TenSecondClip, null, DurationMode.Auto, 60, 0, 180000000).CoverTimeUs);
        }
    }
}
