using System.Collections.Generic;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    /// <summary>Residual QA test gaps of the merged Timeline module.</summary>
    public class TimelineResidualQaTests
    {
        static long[] Times(StudioTimeline t, int tick)
        {
            var list = new List<long>();
            t.GetTickOutputTimesUs(tick, list);
            return list.ToArray();
        }

        [Fact]
        public void SlowMoReplayRepeatsBeforeTheContentStartAreSkipped()
        {
            // QA: the FirstVisible skip in the SlowMoReplay branch was unpinned. 2 s clip replayed at 0.5× (period 4 s), MatchVideo
            // with the video 5 s late: the first pass (-5..-3 s) and the first repeat start (-3 s) are cut; 1, 5 and 9 s remain.
            var t = new StudioTimeline(new TimelineSpec(new ClipTiming(0, 120, 120, afterEnd: SimAfterEnd.SlowMoReplay),
                new UserVideoTiming(0, 10000000, 5000000), DurationMode.MatchVideo, 60));
            Assert.Equal(new long[] { 1000000, 5000000, 9000000 }, Times(t, 0));
            foreach (int tick in new[] { 0, 30, 119 })
            {
                foreach (long us in Times(t, tick)) Assert.True(us >= t.ContentStartUs, "tick " + tick + " at " + us);
            }
        }

        [Fact]
        public void PreviewAtFrameTimeEqualsExportForOffGridLeadsAtAnySpeed()
        {
            // QA: ContentX (frame start time → exact frame time) was unpinned for off-grid MatchVideo leads at speeds ≠ 1×.
            foreach (int fps in new[] { 30, 60 })
            {
                foreach (int speed in new[] { 51, 75, 133, 199 })
                {
                    foreach (long lead in new long[] { 25817, 37255, 83987, 1, 16667, 33333 })
                    {
                        var clip = new ClipTiming(0, 600, 600, speedPercent: speed);
                        var video = new UserVideoTiming(0, 4000000, -lead);
                        var t = new StudioTimeline(new TimelineSpec(clip, video, DurationMode.MatchVideo, fps));
                        for (int i = 0; i < t.FrameCount; i++)
                        {
                            long us = t.FrameTimeUs(i);
                            Assert.True(t.SampleSim(i) == t.SampleSimAtTimeUs(us),
                                "fps=" + fps + " p=" + speed + " lead=" + lead + " frame=" + i + ": export " + t.SampleSim(i) + " preview " + t.SampleSimAtTimeUs(us));
                            Assert.True(t.SampleVideo(i) == t.SampleVideoAtTimeUs(us));
                        }
                    }
                }
            }
        }
    }
}
