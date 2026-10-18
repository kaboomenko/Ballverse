using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Complete timing input of a studio project (spec §3.4): the clip, the optional user video, the duration mode
    /// and the output frame rate. Immutable; compile it with <see cref="StudioTimeline"/> to sample frames.
    /// </summary>
    public sealed class TimelineSpec
    {
        public ClipTiming Clip { get; }
        /// <summary>The user video, or null when the project has none.</summary>
        public UserVideoTiming Video { get; }
        public DurationMode DurationMode { get; }
        /// <summary>Requested duration for <see cref="DurationMode.Manual"/>, µs (&gt; 0; clamped to 1..180 s later). Ignored otherwise.</summary>
        public long ManualDurationUs { get; }
        /// <summary>Output frame rate, 30 or 60.</summary>
        public int Fps { get; }

        public TimelineSpec(ClipTiming clip, UserVideoTiming video, DurationMode durationMode, int fps, long manualDurationUs = 0)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (!Enum.IsDefined(typeof(DurationMode), durationMode))
                throw new ArgumentOutOfRangeException(nameof(durationMode), durationMode, "Unknown DurationMode.");
            OutputFps.Validate(fps, nameof(fps));
            if (durationMode == DurationMode.Manual && manualDurationUs <= 0)
                throw new ArgumentOutOfRangeException(nameof(manualDurationUs), manualDurationUs, "Manual duration must be > 0.");
            if (manualDurationUs < 0 || manualDurationUs > UserVideoTiming.MaxAbsOffsetUs)
                throw new ArgumentOutOfRangeException(nameof(manualDurationUs), manualDurationUs, "Manual duration out of range.");

            Clip = clip;
            Video = video;
            DurationMode = durationMode;
            Fps = fps;
            ManualDurationUs = manualDurationUs;
        }
    }
}
