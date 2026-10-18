using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Complete timing input of a studio project (spec §3.4): the clip, the optional user video, the duration mode,
    /// the output frame rate and the optional cover lead-in. Immutable; compile it with <see cref="StudioTimeline"/> to
    /// sample frames.
    /// </summary>
    public sealed class TimelineSpec
    {
        /// <summary>
        /// Largest accepted <see cref="ManualDurationUs"/>, µs (10 min). Only guards against garbage: values above 180 s
        /// are accepted and clamped to 180 s by <see cref="StudioTimeline"/> (spec §3.4 "не более 180 с").
        /// </summary>
        public const long MaxManualDurationUs = 600L * 1000000L;

        public ClipTiming Clip { get; }
        /// <summary>The user video, or null when the project has none.</summary>
        public UserVideoTiming Video { get; }
        public DurationMode DurationMode { get; }
        /// <summary>Requested duration for <see cref="DurationMode.Manual"/>, µs (&gt; 0; clamped to 1..180 s later). Ignored otherwise.</summary>
        public long ManualDurationUs { get; }
        /// <summary>Output frame rate, 30 or 60.</summary>
        public int Fps { get; }

        /// <summary>
        /// Cover toggle "Поставить выбранный кадр первым на 0,1 с" (spec §3.4): when not null, the frame shown at this
        /// content time (µs, measured without the lead-in) is prepended as a 0.1 s still. Null = toggle off.
        /// </summary>
        public long? CoverTimeUs { get; }

        public TimelineSpec(ClipTiming clip, UserVideoTiming video, DurationMode durationMode, int fps, long manualDurationUs = 0, long? coverTimeUs = null)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (!Enum.IsDefined(typeof(DurationMode), durationMode))
                throw new ArgumentOutOfRangeException(nameof(durationMode), durationMode, "Unknown DurationMode.");
            OutputFps.Validate(fps, nameof(fps));
            if (durationMode == DurationMode.Manual && manualDurationUs <= 0)
                throw new ArgumentOutOfRangeException(nameof(manualDurationUs), manualDurationUs, "Manual duration must be > 0.");
            if (manualDurationUs < 0 || manualDurationUs > MaxManualDurationUs)
                throw new ArgumentOutOfRangeException(nameof(manualDurationUs), manualDurationUs, "Manual duration out of range.");
            if (coverTimeUs.HasValue && (coverTimeUs.Value < 0 || coverTimeUs.Value > StudioTimeline.MaxSampleTimeUs))
                throw new ArgumentOutOfRangeException(nameof(coverTimeUs), coverTimeUs, "Cover time must be in [0, 180 s].");

            Clip = clip;
            Video = video;
            DurationMode = durationMode;
            Fps = fps;
            ManualDurationUs = manualDurationUs;
            CoverTimeUs = coverTimeUs;
        }
    }
}
