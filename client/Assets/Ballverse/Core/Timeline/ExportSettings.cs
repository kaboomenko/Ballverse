using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Export file parameters and the estimates shown on the export sheet (spec §3.8, §7.5, §8.1): resolution, CFR
    /// frame rate, bitrates, 2 s GOP and segments, file-size, free-space and export-time estimates. Immutable.
    /// All methods taking a frame count work on the output frame count of the timeline (<see cref="StudioTimeline.FrameCount"/>).
    /// </summary>
    public sealed class ExportSettings
    {
        /// <summary>Keyframe (IDR) interval, seconds (spec §3.8).</summary>
        public const int KeyframeIntervalSeconds = 2;
        /// <summary>Segment length, seconds; each segment starts with an IDR frame (spec §7.5).</summary>
        public const int SegmentSeconds = 2;
        /// <summary>Fixed part of the container overhead estimate (MP4 boxes), bytes.</summary>
        public const long ContainerOverheadFixedBytes = 64L * 1024L;
        /// <summary>Proportional part of the container overhead estimate (sample tables, NAL headers), percent of payload.</summary>
        public const int ContainerOverheadPercent = 1;
        /// <summary>Extra free space required on top of twice the estimate: 200 MB, taken as MiB (the stricter reading).</summary>
        public const long FreeSpaceReserveBytes = 200L * 1024L * 1024L;
        /// <summary>Video length the §3.8 export-speed table is given for.</summary>
        public const int ExportTimeReferenceSeconds = 30;

        public ExportResolution Resolution { get; }
        public int Fps { get; }
        public ExportBitrateTable Bitrates { get; }

        /// <summary>Frame width in pixels (9:16 portrait).</summary>
        public int Width { get; }
        /// <summary>Frame height in pixels (9:16 portrait).</summary>
        public int Height { get; }

        /// <summary>Target video bitrate, bit/s (from <see cref="Bitrates"/>).</summary>
        public long VideoBitrateBps => Bitrates.GetVideoBps(Resolution, Fps);
        /// <summary>AAC-LC bitrate, bit/s.</summary>
        public long AudioBitrateBps => Bitrates.AudioBps;
        /// <summary>Frames per GOP: one IDR every 2 s.</summary>
        public int GopFrames => KeyframeIntervalSeconds * Fps;
        /// <summary>Frames per full segment (2 s).</summary>
        public int SegmentFrames => SegmentSeconds * Fps;

        /// <param name="bitrates">Optional override table (Remote Config); null = §3.8 defaults.</param>
        public ExportSettings(ExportResolution resolution, int fps, ExportBitrateTable bitrates = null)
        {
            GetDimensions(resolution, out int w, out int h);
            OutputFps.Validate(fps, nameof(fps));
            Resolution = resolution;
            Fps = fps;
            Width = w;
            Height = h;
            Bitrates = bitrates ?? ExportBitrateTable.Default;
        }

        /// <summary>Export sheet defaults (spec §3.8): 1080p, 60 fps on tiers A/B, 30 fps on tier C.</summary>
        public static ExportSettings DefaultFor(DeviceTier tier, ExportBitrateTable bitrates = null)
        {
            return new ExportSettings(ExportResolution.P1080, DefaultFps(tier), bitrates);
        }

        /// <summary>Default export fps by tier (spec §3.8, §8.1): A/B 60, C 30.</summary>
        public static int DefaultFps(DeviceTier tier)
        {
            switch (tier)
            {
                case DeviceTier.A:
                case DeviceTier.B:
                    return OutputFps.Fps60;
                case DeviceTier.C:
                    return OutputFps.Fps30;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown DeviceTier.");
            }
        }

        /// <summary>720×1280 or 1080×1920 (spec §3.8).</summary>
        public static void GetDimensions(ExportResolution resolution, out int width, out int height)
        {
            switch (resolution)
            {
                case ExportResolution.P720:
                    width = 720;
                    height = 1280;
                    return;
                case ExportResolution.P1080:
                    width = 1080;
                    height = 1920;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown ExportResolution.");
            }
        }

        /// <summary>True for frame indices that start a GOP (IDR frame every 2 s).</summary>
        public bool IsKeyframe(int frameIndex)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            return frameIndex % GopFrames == 0;
        }

        /// <summary>Index of the 2 s segment containing frame i: ⌊i / (2 · fps)⌋.</summary>
        public int SegmentIndexOfFrame(int frameIndex)
        {
            if (frameIndex < 0) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            return frameIndex / SegmentFrames;
        }

        /// <summary>Number of segments for <paramref name="frameCount"/> frames; the last one may be shorter than 2 s.</summary>
        public int SegmentCount(int frameCount)
        {
            CheckFrameCount(frameCount);
            return (frameCount + SegmentFrames - 1) / SegmentFrames;
        }

        /// <summary>Frame range of segment <paramref name="index"/> of a <paramref name="frameCount"/>-frame export.</summary>
        public ExportSegment GetSegment(int index, int frameCount)
        {
            int count = SegmentCount(frameCount);
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Segment index must be in [0, SegmentCount).");
            int start = index * SegmentFrames;
            return new ExportSegment(index, start, Math.Min(SegmentFrames, frameCount - start));
        }

        /// <summary>All segments in order.</summary>
        public ExportSegment[] GetSegments(int frameCount)
        {
            int count = SegmentCount(frameCount);
            var result = new ExportSegment[count];
            for (int i = 0; i < count; i++) result[i] = GetSegment(i, frameCount);
            return result;
        }

        /// <summary>
        /// Estimated file size, bytes (spec §3.8 export sheet): payload = (video + audio bitrate) · duration / 8 with
        /// duration = frameCount / fps, plus container overhead of 1% of the payload + 64 KiB. Rounded up.
        /// </summary>
        public long EstimateFileSizeBytes(int frameCount)
        {
            CheckFrameCount(frameCount);
            long payload = StudioTimeline.CeilDiv((VideoBitrateBps + AudioBitrateBps) * frameCount, 8L * Fps);
            long overhead = StudioTimeline.CeilDiv(payload * ContainerOverheadPercent, 100) + ContainerOverheadFixedBytes;
            return payload + overhead;
        }

        /// <summary>Free space needed before export starts: 2 × estimate + 200 MB (spec §3.8 "Мало места").</summary>
        public long RequiredFreeSpaceBytes(int frameCount)
        {
            return 2L * EstimateFileSizeBytes(frameCount) + FreeSpaceReserveBytes;
        }

        /// <summary>True when <paramref name="freeBytes"/> ≥ <see cref="RequiredFreeSpaceBytes"/> (boundary inclusive).</summary>
        public bool HasEnoughFreeSpace(int frameCount, long freeBytes)
        {
            return freeBytes >= RequiredFreeSpaceBytes(frameCount);
        }

        /// <summary>Heavy modes with many bodies get ×1.5 export time (spec §3.8): M09, M15.</summary>
        public static bool IsHeavyMode(ModeCode mode)
        {
            return mode == ModeCode.M09 || mode == ModeCode.M15;
        }

        /// <summary>
        /// Target export time for a 30 s video, ms (spec §3.8 table). The table has no 720p60 column; it is taken as
        /// 2 × 720p30, the same ratio the table uses between 1080p60 and 1080p30 (decision).
        /// </summary>
        public static long ReferenceExportTimeMs(DeviceTier tier, ExportResolution resolution, int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            GetDimensions(resolution, out _, out _);
            long p720x30, p1080x30, p1080x60;
            switch (tier)
            {
                case DeviceTier.A:
                    p720x30 = 10000; p1080x30 = 15000; p1080x60 = 30000;
                    break;
                case DeviceTier.B:
                    p720x30 = 20000; p1080x30 = 30000; p1080x60 = 60000;
                    break;
                case DeviceTier.C:
                    p720x30 = 40000; p1080x30 = 75000; p1080x60 = 150000;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown DeviceTier.");
            }
            bool is60 = fps == OutputFps.Fps60;
            if (resolution == ExportResolution.P1080) return is60 ? p1080x60 : p1080x30;
            return is60 ? 2 * p720x30 : p720x30;
        }

        /// <summary>
        /// Estimated export time, ms (spec §3.8): the 30 s reference scaled linearly by duration (frameCount / fps),
        /// × 1.5 for heavy modes (M09, M15). Rounded up.
        /// </summary>
        public long EstimateExportTimeMs(DeviceTier tier, ModeCode mode, int frameCount)
        {
            CheckFrameCount(frameCount);
            long reference = ReferenceExportTimeMs(tier, Resolution, Fps);
            long heavyNum = IsHeavyMode(mode) ? 3 : 2;
            return StudioTimeline.CeilDiv(reference * frameCount * heavyNum, (long)ExportTimeReferenceSeconds * Fps * 2);
        }

        static void CheckFrameCount(int frameCount)
        {
            if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count must be >= 0.");
        }
    }
}
