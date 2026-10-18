using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class StudioTimelineVideoSamplingTests
    {
        static readonly ClipTiming Clip = new ClipTiming(0, 600, 600);

        static StudioTimeline WithVideo(UserVideoTiming video, int fps, long durationUs)
        {
            return new StudioTimeline(Clip, video, DurationMode.Manual, fps, durationUs);
        }

        [Fact]
        public void NoVideo_EveryFrameIsNoVideo()
        {
            var t = new StudioTimeline(Clip, null, DurationMode.Auto, 60);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(VideoSample.NoVideo, t.SampleVideo(i));
            Assert.Equal(VideoSample.NoVideo, t.SampleVideoAtTimeUs(123));
        }

        [Fact]
        public void ZeroOffset_PlaysFromTrimInAtFrameTimes()
        {
            var t = WithVideo(new UserVideoTiming(1000000, 3000000), 60, 2000000);
            Assert.Equal(VideoSample.Frame(1000000), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(1016667), t.SampleVideo(1));
            Assert.Equal(VideoSample.Frame(1050000), t.SampleVideo(3));
            Assert.Equal(VideoSample.Frame(2983334), t.SampleVideo(119));
        }

        [Fact]
        public void PositiveOffset_ShowsFirstFrameFrozenBeforeTheVideoStarts()
        {
            var t = WithVideo(new UserVideoTiming(1000000, 3000000, 500000), 30, 4000000);
            Assert.Equal(VideoSample.Frozen(1000000), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frozen(1000000), t.SampleVideo(14));
            Assert.Equal(VideoSample.Frame(1000000), t.SampleVideo(15));
            Assert.Equal(VideoSample.Frame(1033334), t.SampleVideo(16));
            Assert.Equal(VideoSample.Frame(2966667), t.SampleVideo(74));
            Assert.Equal(VideoSample.Frozen(2999999), t.SampleVideo(75));
            Assert.Equal(VideoSample.Frozen(2999999), t.SampleVideo(119));
        }

        [Fact]
        public void NegativeOffset_SkipsTheVideoHead()
        {
            var t = WithVideo(new UserVideoTiming(1000000, 3000000, -1500000, VideoAfterEnd.Black), 30, 2000000);
            Assert.Equal(VideoSample.Frame(2500000), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(2966667), t.SampleVideo(14));
            Assert.Equal(VideoSample.Black, t.SampleVideo(15));
            Assert.Equal(VideoSample.Black, t.SampleVideo(59));
        }

        [Fact]
        public void NegativeOffset_VideoEntirelyBeforeClipFollowsAfterEnd()
        {
            var t = WithVideo(new UserVideoTiming(0, 1000000, -2000000, VideoAfterEnd.Freeze), 60, 1000000);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(VideoSample.Frozen(999999), t.SampleVideo(i));
        }

        [Fact]
        public void Loop_WrapsToTrimInWithIteration()
        {
            var t = WithVideo(new UserVideoTiming(1000000, 3000000, 500000, VideoAfterEnd.Loop), 30, 8000000);
            Assert.Equal(VideoSample.Frame(2966667, 0), t.SampleVideo(74));
            Assert.Equal(VideoSample.Frame(1000000, 1), t.SampleVideo(75));
            Assert.Equal(VideoSample.Frame(2000000, 1), t.SampleVideo(105));
            Assert.Equal(VideoSample.Frame(1000000, 2), t.SampleVideo(135));
            Assert.Equal(VideoSample.Frame(1000000, 3), t.SampleVideo(195));
        }

        [Fact]
        public void Loop_StillShowsFirstFrameFrozenBeforeStart()
        {
            var t = WithVideo(new UserVideoTiming(0, 1000000, 1000000, VideoAfterEnd.Loop), 60, 3000000);
            Assert.Equal(VideoSample.Frozen(0), t.SampleVideo(59));
            Assert.Equal(VideoSample.Frame(0, 0), t.SampleVideo(60));
            Assert.Equal(VideoSample.Frame(0, 1), t.SampleVideo(120));
        }

        [Fact]
        public void BlackAndExpandSim_AfterTheEnd()
        {
            var black = WithVideo(new UserVideoTiming(0, 1000000, 0, VideoAfterEnd.Black), 60, 2000000);
            Assert.Equal(VideoSample.Frame(983334), black.SampleVideo(59));
            Assert.Equal(VideoSample.Black, black.SampleVideo(60));

            var expand = WithVideo(new UserVideoTiming(0, 1000000, 0, VideoAfterEnd.ExpandSim), 60, 2000000);
            Assert.Equal(VideoSample.Frame(983334), expand.SampleVideo(59));
            Assert.Equal(VideoSample.ExpandSim, expand.SampleVideo(60));
        }

        [Fact]
        public void VideoLongerThanTimeline_PlaysToTheLastFrame()
        {
            var t = WithVideo(new UserVideoTiming(0, 600000000), 60, 180000000);
            Assert.Equal(10800, t.FrameCount);
            Assert.Equal(VideoSample.Frame(179983334), t.SampleVideo(10799));
        }

        [Fact]
        public void OneMicrosecondVideo()
        {
            var t = WithVideo(new UserVideoTiming(5, 6, 0, VideoAfterEnd.Freeze), 60, 1000000);
            Assert.Equal(VideoSample.Frame(5), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frozen(5), t.SampleVideo(1));
        }

        [Fact]
        public void SampleVideoAtTimeUs_MatchesFramesAndIsMicrosecondExact()
        {
            var t = WithVideo(new UserVideoTiming(1000000, 3000000, 500000, VideoAfterEnd.Loop), 60, 6000000);
            for (int i = 0; i < t.FrameCount; i++) Assert.Equal(t.SampleVideo(i), t.SampleVideoAtTimeUs(t.FrameTimeUs(i)));
            Assert.Equal(VideoSample.Frozen(1000000), t.SampleVideoAtTimeUs(499999));
            Assert.Equal(VideoSample.Frame(1000000), t.SampleVideoAtTimeUs(500000));
            Assert.Equal(VideoSample.Frame(2999999), t.SampleVideoAtTimeUs(2499999));
            Assert.Equal(VideoSample.Frame(1000000, 1), t.SampleVideoAtTimeUs(2500000));
        }

        [Fact]
        public void VideoSampling_IsIndependentOfSimAfterEndAndSpeed()
        {
            var video = new UserVideoTiming(0, 3000000, 250000, VideoAfterEnd.Loop);
            var a = new StudioTimeline(new ClipTiming(0, 60, 60, 50, 60, 25, SimAfterEnd.Loop), video, DurationMode.Manual, 60, 9000000);
            var b = new StudioTimeline(new ClipTiming(0, 600, 600, 200), video, DurationMode.Manual, 60, 9000000);
            for (int i = 0; i < a.FrameCount; i++) Assert.Equal(a.SampleVideo(i), b.SampleVideo(i));
        }
    }
}
