using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// A timeline bound to export settings: the inputs of the frame loop i = 0 … N−1, t = i / fps (spec §7.5) and the
    /// export sheet estimates (spec §3.8). The timeline fps must equal the export fps.
    /// </summary>
    public sealed class ExportPlan
    {
        public StudioTimeline Timeline { get; }
        public ExportSettings Settings { get; }

        /// <summary>N: exact number of frames to encode (checked again at finalization, spec §7.5).</summary>
        public int FrameCount => Timeline.FrameCount;
        public int SegmentCount => Settings.SegmentCount(Timeline.FrameCount);

        public ExportPlan(StudioTimeline timeline, ExportSettings settings)
        {
            Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (timeline.Fps != settings.Fps)
                throw new ArgumentException("Timeline fps " + timeline.Fps + " differs from export fps " + settings.Fps + ".", nameof(settings));
        }

        /// <summary>Encoder presentation time of frame i, µs (see <see cref="StudioTimeline.FrameTimeUs"/>).</summary>
        public long FramePtsUs(int frameIndex)
        {
            return Timeline.FrameTimeUs(frameIndex);
        }

        public ExportSegment GetSegment(int index)
        {
            return Settings.GetSegment(index, FrameCount);
        }

        public ExportSegment[] GetSegments()
        {
            return Settings.GetSegments(FrameCount);
        }

        public int SegmentIndexOfFrame(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= FrameCount) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            return Settings.SegmentIndexOfFrame(frameIndex);
        }

        /// <summary>First frame to render when resuming after <paramref name="completedSegments"/> finished segments (spec §3.8).</summary>
        public int ResumeFrame(int completedSegments)
        {
            if (completedSegments < 0 || completedSegments > SegmentCount) throw new ArgumentOutOfRangeException(nameof(completedSegments));
            return completedSegments == SegmentCount ? FrameCount : GetSegment(completedSegments).StartFrame;
        }

        public long EstimateFileSizeBytes()
        {
            return Settings.EstimateFileSizeBytes(FrameCount);
        }

        public long RequiredFreeSpaceBytes()
        {
            return Settings.RequiredFreeSpaceBytes(FrameCount);
        }

        public bool HasEnoughFreeSpace(long freeBytes)
        {
            return Settings.HasEnoughFreeSpace(FrameCount, freeBytes);
        }

        public long EstimateExportTimeMs(DeviceTier tier, ModeCode mode)
        {
            return Settings.EstimateExportTimeMs(tier, mode, FrameCount);
        }

        /// <summary>Acceptance check (spec §3.8): the file has exactly N frames.</summary>
        public bool IsFrameCountExact(int actualFrames)
        {
            return actualFrames == FrameCount;
        }

        /// <summary>Acceptance check (spec §3.8 ffprobe): file duration = project duration ± 1 frame (inclusive, µs rounding tolerated).</summary>
        public bool IsDurationAcceptable(long actualDurationUs)
        {
            long frameUs = StudioTimeline.CeilDiv(StudioTimeline.MicrosPerSecond, Settings.Fps);
            return Math.Abs(actualDurationUs - Timeline.DurationUs) <= frameUs;
        }
    }
}
