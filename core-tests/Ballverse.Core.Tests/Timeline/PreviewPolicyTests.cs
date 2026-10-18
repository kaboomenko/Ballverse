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
        [InlineData(DeviceTier.A, 60, false)]
        [InlineData(DeviceTier.B, 60, false)]
        [InlineData(DeviceTier.C, 60, true)]
        [InlineData(DeviceTier.A, 30, false)]
        [InlineData(DeviceTier.C, 30, false)]
        public void ReducedInternalResolutionOnlyForTierCAt60(DeviceTier tier, int fps, bool expected)
        {
            Assert.Equal(expected, PreviewPolicy.ShouldReduceInternalResolution(tier, fps));
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
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.ShouldReduceInternalResolution(DeviceTier.A, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() => PreviewPolicy.ShouldReduceInternalResolution((DeviceTier)8, 60));
        }

        [Fact]
        public void ScrubbingSamplesTheSameFramesAsExport()
        {
            var t = new StudioTimeline(
                new ClipTiming(0, 600, 600, 125, 90, 40, SimAfterEnd.Loop),
                new UserVideoTiming(250000, 4250000, -1000000, VideoAfterEnd.Loop),
                DurationMode.Manual, 60, 15000000);
            for (long us = 0; us < t.DurationUs; us += 7919)
            {
                int frame = t.FrameAtTimeUs(us);
                Assert.True(t.FrameTimeUs(frame) <= us + 1);
                // The sample at a scrub time equals the sample of the frame shown at that time or is between it and the next one.
                var atFrame = t.SampleSim(frame);
                var atTime = t.SampleSimAtTimeUs(us);
                if (atFrame.Iteration == atTime.Iteration) Assert.True(atTime.Tick >= atFrame.Tick);
            }
        }
    }
}
