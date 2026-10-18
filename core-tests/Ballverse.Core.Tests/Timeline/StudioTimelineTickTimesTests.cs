using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    /// <summary>Output times of every tick appearance, for placing simulation sounds (spec §3.7, §7.8).</summary>
    public class StudioTimelineTickTimesTests
    {
        static List<long> Times(StudioTimeline t, int tick)
        {
            var list = new List<long>();
            Assert.Equal(t.GetTickOutputTimesUs(tick, list), list.Count);
            return list;
        }

        static StudioTimeline Manual(ClipTiming clip, long durationUs, UserVideoTiming video = null)
        {
            return new StudioTimeline(clip, video, DurationMode.Manual, 60, durationUs);
        }

        [Fact]
        public void Freeze_OnlyTheFirstPass()
        {
            var t = Manual(new ClipTiming(0, 600, 900), 20000000);
            Assert.Equal(new long[] { 0 }, Times(t, 0));
            Assert.Equal(new long[] { 9983334 }, Times(t, 599));
            Assert.Empty(Times(t, 600));
            Assert.Empty(Times(t, -1));
        }

        [Fact]
        public void Loop_EveryRepeat()
        {
            var t = Manual(new ClipTiming(0, 60, 60, afterEnd: SimAfterEnd.Loop), 3000000);
            Assert.Equal(new long[] { 0, 1000000, 2000000 }, Times(t, 0));
            Assert.Equal(new long[] { 500000, 1500000, 2500000 }, Times(t, 30));
        }

        [Fact]
        public void SlowMoReplay_EveryReplay()
        {
            var t = Manual(new ClipTiming(100, 700, 900, afterEnd: SimAfterEnd.SlowMoReplay), 20000000);
            Assert.Equal(new long[] { 8000000, 10000000, 14000000, 18000000 }, Times(t, 580));
            Assert.Equal(new long[] { 9983334, 13966667, 17966667 }, Times(t, 699));
            Assert.Equal(new long[] { 7983334 }, Times(t, 579));
        }

        [Fact]
        public void ContinueSim_TicksPastTrimOut()
        {
            var t = Manual(new ClipTiming(0, 600, 900, afterEnd: SimAfterEnd.ContinueSim), 20000000);
            Assert.Equal(new long[] { 10000000 }, Times(t, 600));
            Assert.Equal(new long[] { 14983334 }, Times(t, 899));
            Assert.Empty(Times(t, 900));
        }

        [Fact]
        public void PreRollAndCoverShiftTheTimes()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600), new UserVideoTiming(0, 5000000, -1000000), DurationMode.MatchVideo, 60, 0, 0);
            Assert.Equal(new long[] { 1100000 }, Times(t, 0));
            Assert.Equal(new long[] { 1100000 + 1000000 }, Times(t, 60));
        }

        [Fact]
        public void CutClipHeadHasNoTimes()
        {
            // MatchVideo with V starting 1 s after S: the first second of the clip is before the output start.
            var t = new StudioTimeline(new ClipTiming(0, 600, 600, afterEnd: SimAfterEnd.Loop), new UserVideoTiming(0, 15000000, 1000000), DurationMode.MatchVideo, 60, 0, 0);
            Assert.Equal(-900000, t.ClipStartUs);
            Assert.Equal(-900000, t.ClipTickToOutputUs(0));
            Assert.Equal(new long[] { 100000 + 9000000 }, Times(t, 0));          // only the loop repeat
            Assert.Equal(new long[] { 100000 + 9000000 + 983334 }, Times(t, 59));
            Assert.Equal(new long[] { 100000, 100000 + 10000000 }, Times(t, 60)); // plays at the content start
        }

        [Fact]
        public void TimesAtOrAfterTheOutputEndAreDropped()
        {
            var t = new StudioTimeline(new ClipTiming(0, 600, 600, afterEnd: SimAfterEnd.Loop), null, DurationMode.Auto, 60);
            Assert.Equal(10000000, t.DurationUs);
            Assert.Equal(new long[] { 0 }, Times(t, 0));
        }

        [Fact]
        public void NullResultsThrow()
        {
            var t = Manual(new ClipTiming(0, 60, 60), 1000000);
            Assert.Throws<ArgumentNullException>(() => t.GetTickOutputTimesUs(0, null));
        }
    }
}
