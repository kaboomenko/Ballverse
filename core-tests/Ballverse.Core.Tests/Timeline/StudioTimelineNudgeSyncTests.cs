using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    /// <summary>
    /// The ±1 frame offset buttons (spec §3.4 "точная подстройка ±1 кадр") composed with the timeline: a k-frame nudge
    /// shifts S against V by exactly k output frames, for every k and both origins.
    /// </summary>
    public class StudioTimelineNudgeSyncTests
    {
        const long VideoTrimIn = 2000000;

        static StudioTimeline Build(int k, int fps, DurationMode mode)
        {
            long offset = UserVideoTiming.NudgeOffsetUs(0, k, fps);
            var clip = new ClipTiming(100, 700, 700);
            var video = new UserVideoTiming(VideoTrimIn, VideoTrimIn + 20000000, offset);
            return new StudioTimeline(clip, video, mode, fps, 15000000);
        }

        [Theory]
        [InlineData(60, DurationMode.Auto)]
        [InlineData(60, DurationMode.MatchVideo)]
        [InlineData(60, DurationMode.Manual)]
        [InlineData(30, DurationMode.Auto)]
        [InlineData(30, DurationMode.MatchVideo)]
        [InlineData(30, DurationMode.Manual)]
        public void NudgeByKFrames_ClipFrameMinusVideoFrameIsExactlyK(int fps, DurationMode mode)
        {
            int ticksPerFrame = 60 / fps;
            for (int k = -12; k <= 12; k++)
            {
                var t = Build(k, fps, mode);
                int compared = 0;
                for (int i = 0; i < t.FrameCount; i++)
                {
                    var sim = t.SampleSim(i);
                    var video = t.SampleVideo(i);
                    if (sim.Kind != SimSampleKind.Playing || sim.Iteration != 0 || video.Kind != VideoSampleKind.Frame) continue;
                    // Both tracks sit on their own frame grid: no half-frame phase error.
                    Assert.Equal(0, (sim.Tick - 100) % ticksPerFrame);
                    int clipFrame = (sim.Tick - 100) / ticksPerFrame;
                    long local = video.SourcePtsUs - VideoTrimIn;
                    long videoFrame = local * fps / 1000000;
                    Assert.Equal((videoFrame * 1000000 + fps - 1) / fps, local);
                    Assert.True(clipFrame - videoFrame == k, "fps=" + fps + " k=" + k + " frame " + i + ": clip frame " + clipFrame + ", video frame " + videoFrame);
                    compared++;
                }
                Assert.True(compared > 100);
            }
        }

        [Theory]
        [InlineData(60)]
        [InlineData(30)]
        public void NegativeNudge_ClipStartsExactlyKFramesAfterTheVideo(int fps)
        {
            for (int k = -12; k < 0; k++)
            {
                var t = Build(k, fps, DurationMode.MatchVideo);
                Assert.Equal(-k, t.ClipStartFrame);
                Assert.Equal(VideoSample.Frame(VideoTrimIn), t.SampleVideo(0));
                Assert.Equal(SimSample.Frozen(100), t.SampleSim(-k - 1));
                for (int i = 0; i < 20; i++) Assert.Equal(SimSample.Playing(100 + i * 60 / fps), t.SampleSim(-k + i));
            }
        }

        [Theory]
        [InlineData(60)]
        [InlineData(30)]
        public void PositiveNudge_VideoStartsExactlyKFramesAfterTheClip(int fps)
        {
            for (int k = 1; k <= 12; k++)
            {
                var t = Build(k, fps, DurationMode.Auto);
                Assert.Equal(SimSample.Playing(100), t.SampleSim(0));
                Assert.Equal(VideoSample.Frozen(VideoTrimIn), t.SampleVideo(k - 1));
                for (int i = 0; i < 20; i++) Assert.Equal(VideoSample.Frame(VideoTrimIn + t.FrameTimeUs(i)), t.SampleVideo(k + i));
            }
        }

        [Fact]
        public void MinusOneFrameAt60_SimIsOneFrameBehind()
        {
            var t = Build(-1, 60, DurationMode.MatchVideo);
            Assert.Equal(-16667, t.Spec.Video.OffsetUs);
            Assert.Equal(SimSample.Frozen(100), t.SampleSim(0));
            Assert.Equal(SimSample.Playing(100), t.SampleSim(1));
            Assert.Equal(SimSample.Playing(101), t.SampleSim(2));
            Assert.Equal(VideoSample.Frame(VideoTrimIn), t.SampleVideo(0));
            Assert.Equal(VideoSample.Frame(VideoTrimIn + 16667), t.SampleVideo(1));
        }
    }
}
