using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    public class PreviewPolicyTests
    {
        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        public void PreviewNeverLowersFps(int fps)
        {
            Assert.Equal(fps, PreviewPolicy.TargetFps(fps));
        }

        [Theory]
        [InlineData(DeviceTier.A, ExportResolution.P1080, 60, false)]
        [InlineData(DeviceTier.B, ExportResolution.P1080, 60, false)]
        [InlineData(DeviceTier.C, ExportResolution.P1080, 60, true)]
        [InlineData(DeviceTier.C, ExportResolution.P720, 60, false)]
        [InlineData(DeviceTier.A, ExportResolution.P1080, 30, false)]
        [InlineData(DeviceTier.C, ExportResolution.P1080, 30, false)]
        [InlineData(DeviceTier.C, ExportResolution.P720, 30, false)]
        public void ReducedInternalResolutionOnlyForTierCAt1080p60(DeviceTier tier, ExportResolution resolution, int fps, bool expected)
        {
            Assert.Equal(expected, PreviewPolicy.ShouldReduceInternalResolution(tier, resolution, fps));
        }

        [Fact]
        public void AvDriftIsOneFrame()
        {
            Assert.Equal(16667L, PreviewPolicy.MaxAvDriftUs(60));
            Assert.Equal(33334L, PreviewPolicy.MaxAvDriftUs(30));
        }

        [Fact]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.TargetFps(24));
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.MaxAvDriftUs(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.ShouldReduceInternalResolution(DeviceTier.A, ExportResolution.P1080, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.ShouldReduceInternalResolution((DeviceTier)8, ExportResolution.P1080, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.ShouldReduceInternalResolution(DeviceTier.C, (ExportResolution)480, 60));
        }

        [Fact]
        public void ScrubbingSamplesTheSameFramesAsExport()
        {
            var t = new StudioTimeline(
                new ClipTiming(0, 600, 600, 125, 90, 40, SimAfterEnd.Loop),
                new UserVideoTiming(250000, 4250000, -1000000, VideoAfterEnd.Loop),
                DurationMode.Manual, 60, 15000000);
            // At every frame time, scrubbing returns exactly what export renders for that frame.
            for (int i = 0; i < t.FrameCount; i++)
            {
                long us = t.FrameTimeUs(i);
                Assert.Equal(i, t.FrameAtTimeUs(us));
                Assert.Equal(t.SampleSim(i), t.SampleSimAtTimeUs(us));
                Assert.Equal(t.SampleVideo(i), t.SampleVideoAtTimeUs(us));
            }
            // Between frame times the sample stays between the frame's sample and the next frame's sample.
            for (long us = 0; us < t.DurationUs; us += 7919)
            {
                int frame = t.FrameAtTimeUs(us);
                Assert.True(t.FrameTimeUs(frame) <= us);
                var atFrame = t.SampleSim(frame);
                var atTime = t.SampleSimAtTimeUs(us);
                Assert.True(atTime.Iteration >= atFrame.Iteration);
                if (atFrame.Iteration == atTime.Iteration) Assert.True(atTime.Tick >= atFrame.Tick);
                if (frame + 1 < t.FrameCount)
                {
                    var next = t.SampleSim(frame + 1);
                    Assert.True(atTime.Iteration <= next.Iteration);
                    if (next.Iteration == atTime.Iteration) Assert.True(atTime.Tick <= next.Tick);
                }
            }
        }
    }
}
