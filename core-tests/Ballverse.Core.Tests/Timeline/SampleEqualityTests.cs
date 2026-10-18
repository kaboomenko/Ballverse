using System;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    [Collection(SerialMeasurementCollection.Name)] // zero-allocation check; see SerialMeasurementCollection
    public class SampleEqualityTests
    {
        [Fact]
        public void SimSample_ValueEquality()
        {
            Assert.True(SimSample.Playing(3, 1) == SimSample.Playing(3, 1));
            Assert.True(SimSample.Playing(3, 1) != SimSample.Playing(3, 2));
            Assert.True(SimSample.Playing(3) != SimSample.Frozen(3));
            Assert.True(SimSample.Playing(3) != SimSample.Playing(4));
            Assert.Equal(SimSample.Frozen(7).GetHashCode(), SimSample.Frozen(7).GetHashCode());
            Assert.True(SimSample.Frozen(7).Equals((object)SimSample.Frozen(7)));
            Assert.False(SimSample.Frozen(7).Equals((object)7));
        }

        [Fact]
        public void VideoSample_ValueEquality()
        {
            Assert.True(VideoSample.Frame(10, 1) == VideoSample.Frame(10, 1));
            Assert.True(VideoSample.Frame(10) != VideoSample.Frozen(10));
            Assert.True(VideoSample.Frame(10, 1) != VideoSample.Frame(10, 2));
            Assert.True(VideoSample.Black != VideoSample.ExpandSim);
            Assert.Equal(VideoSample.Frame(5000000000L).GetHashCode(), VideoSample.Frame(5000000000L).GetHashCode());
            Assert.False(VideoSample.NoVideo.Equals(null));
        }

        [Fact]
        public void ExportSegment_ValueEquality()
        {
            Assert.True(new ExportSegment(1, 60, 60) == new ExportSegment(1, 60, 60));
            Assert.True(new ExportSegment(1, 60, 60) != new ExportSegment(1, 60, 59));
            Assert.Equal(new ExportSegment(2, 120, 60).GetHashCode(), new ExportSegment(2, 120, 60).GetHashCode());
            Assert.True(new ExportSegment(0, 0, 1).Equals((object)new ExportSegment(0, 0, 1)));
        }

        [Fact]
        public void ComparingSamplesDoesNotAllocate()
        {
            // A 1 s clip frozen for 49 s: a renderer skipping unchanged frames compares every sample with the previous one.
            var t = new StudioTimeline(new ClipTiming(0, 60, 60), new UserVideoTiming(0, 1000000), DurationMode.Manual, 60, 50000000);
            var prevSim = t.SampleSim(0);
            var prevVideo = t.SampleVideo(0);
            int same = prevSim.Equals(t.SampleSim(0)) && prevSim == t.SampleSim(0) && prevVideo.Equals(t.SampleVideo(0)) ? 0 : -1; // JIT warm-up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i < t.FrameCount; i++)
            {
                var sim = t.SampleSim(i);
                var video = t.SampleVideo(i);
                if (sim.Equals(prevSim) && sim == prevSim && video.Equals(prevVideo) && !(video != prevVideo)) same++;
                prevSim = sim;
                prevVideo = video;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(t.FrameCount - 61, same); // frame 60 switches from Playing(59) to Frozen(59)
        }
    }
}
