using System;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Builds the real Timeline inputs (<see cref="ClipTiming"/>, <see cref="UserVideoTiming"/>, <see cref="TimelineSpec"/>)
    /// from a sanitized project document, the way the app does. The constructors throw
    /// <see cref="ArgumentOutOfRangeException"/> for any value the timeline cannot play, so a sanitized document that
    /// builds here is guaranteed to be playable. (Formerly a copy of the Timeline checks, from before both modules shared
    /// a tree; now it calls the real types so the limits can never drift.)
    /// </summary>
    static class TimelineInputMirror
    {
        public const long MaxSourceDurationUs = UserVideoTiming.MaxSourceDurationUs;
        public const long MaxAbsOffsetUs = UserVideoTiming.MaxAbsOffsetUs;
        public const long MaxManualDurationUs = TimelineSpec.MaxManualDurationUs;
        public const long MaxSampleTimeUs = StudioTimeline.MaxSampleTimeUs;

        public static TimelineSpec Validate(ProjectDocument d, int fps, int availableTicks = SimConstants.MaxDurationTicks)
        {
            var clip = d.Clip ?? throw new ArgumentNullException("clip");
            var slow = clip.FinaleSlowMo ?? throw new ArgumentNullException("clip.finaleSlowMo");
            var clipTiming = new ClipTiming(
                clip.TrimInTick, clip.TrimOutTick, availableTicks,
                ClipTiming.PercentFromRatio(clip.Speed),
                ClipTiming.TicksFromSeconds(slow.Seconds),
                ClipTiming.PercentFromRatio(slow.Factor),
                clip.AfterEnd);

            var v = d.UserVideo;
            var video = v != null ? new UserVideoTiming(v.TrimInUs, v.TrimOutUs, v.OffsetUs, v.AfterEnd) : null;

            var o = d.Output ?? throw new ArgumentNullException("output");
            long manual = o.ManualDurationUs ?? 0;
            long? cover = o.Cover != null && o.Cover.PutFirst ? o.Cover.AtUs : (long?)null;
            return new TimelineSpec(clipTiming, video, o.DurationMode, fps, manual, cover);
        }
    }
}
