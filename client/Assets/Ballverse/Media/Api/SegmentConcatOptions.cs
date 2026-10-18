using System;
using Ballverse.Core.Timeline;

namespace Ballverse.Media
{
    /// <summary>
    /// Settings of <see cref="IMediaBridge.ConcatSegmentsAsync"/> (spec §7.5 segmented export). Immutable.
    /// </summary>
    /// <remarks>
    /// <para><b>Timing.</b> The output places the video samples of segment k (in list order) at
    /// <c>pts − firstPts(k) + <see cref="SegmentStartUs"/>(frames of segments 0 … k−1)</c>, where firstPts(k) is the
    /// presentation time of the segment's first (IDR) frame. With the studio's 2-second segments written with
    /// pts = <c>ExportPlan.FramePtsUs(i) − ExportPlan.FramePtsUs(segment.StartFrame)</c> this reproduces exactly the
    /// export plan's frame times <c>FramePtsUs(i)</c> (segment starts fall on whole seconds, so no µs rounding drift).</para>
    /// <para><b>Disk space.</b> With E = the export size, the call needs the segments (≈ E) + the remuxed output (≈ E)
    /// and — where the platform muxer writes <c>moov</c> at the end (Android) — a temp copy for the fast-start rewrite
    /// (≈ E): peak ≈ 3E, above the §3.8 pre-check of 2E + 200 MB. <see cref="DeleteSegmentsBeforeFastStart"/> lowers the
    /// peak to ≈ 2E. iOS (AVAssetWriter writes moov at the front) peaks at ≈ 2E either way.</para>
    /// </remarks>
    public sealed class SegmentConcatOptions
    {
        /// <summary>Constant frame rate of the segments (30 or 60).</summary>
        public int Fps { get; }

        /// <summary>AAC-LC bitrate of the single audio track encoded from the PCM mix.</summary>
        public int AudioBitrateBps { get; }

        /// <summary>Title/date written into the output, or null.</summary>
        public Mp4Metadata Metadata { get; }

        /// <summary>
        /// When true, the segment files are deleted as soon as the remux succeeded, before the fast-start rewrite, so the
        /// peak disk use is ≈ 2 × the export size instead of ≈ 3× (see remarks). Trade-off: if the rewrite then fails or
        /// is cancelled, the output is deleted and the segments are already gone, so the export must be redone from the
        /// first segment. Default false (segments are never touched).
        /// </summary>
        public bool DeleteSegmentsBeforeFastStart { get; }

        public SegmentConcatOptions(int fps, int audioBitrateBps = VideoWriterOptions.DefaultAudioBitrateBps, Mp4Metadata metadata = null,
            bool deleteSegmentsBeforeFastStart = false)
        {
            OutputFps.Validate(fps, nameof(fps));
            if (audioBitrateBps < 32000 || audioBitrateBps > 320000) throw new ArgumentOutOfRangeException(nameof(audioBitrateBps));
            Fps = fps;
            AudioBitrateBps = audioBitrateBps;
            Metadata = metadata == null || metadata.IsEmpty ? null : metadata;
            DeleteSegmentsBeforeFastStart = deleteSegmentsBeforeFastStart;
        }

        /// <summary>Options for an export plan's settings (fps, audio bitrate).</summary>
        public static SegmentConcatOptions FromExportSettings(ExportSettings settings, Mp4Metadata metadata = null,
            bool deleteSegmentsBeforeFastStart = false)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new SegmentConcatOptions(settings.Fps, checked((int)settings.AudioBitrateBps), metadata, deleteSegmentsBeforeFastStart);
        }

        /// <summary>
        /// Output start time, µs, of a segment preceded by <paramref name="framesBefore"/> frames:
        /// framesBefore · 10⁶ / fps rounded up (the same rounding as <c>StudioTimeline.FrameTimeUs</c>). The native
        /// concatenators (Java <c>SegmentConcat.segmentStartUs</c>, ObjC++ <c>BVSegmentStartUs</c>) use this formula.
        /// </summary>
        public long SegmentStartUs(long framesBefore)
        {
            return StartUs(framesBefore, Fps);
        }

        internal static long StartUs(long frames, int fps)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            return (frames * 1000000L + fps - 1) / fps;
        }
    }
}
