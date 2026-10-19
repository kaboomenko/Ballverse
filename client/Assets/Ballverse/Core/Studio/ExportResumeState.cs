using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;
using Newtonsoft.Json;

namespace Ballverse.Core.Studio
{
    /// <summary>One finished 2-second segment of an export job (spec §7.5 step f).</summary>
    public sealed class ExportSegmentRecord
    {
        /// <summary>Segment index (0-based, <see cref="ExportSettings.GetSegment"/>).</summary>
        public int Index { get; set; }
        /// <summary>The writer's result (<c>VideoWriterResult.Path</c>) relative to the job folder, '/'-separated.</summary>
        public string File { get; set; }
        /// <summary>Frames the writer reported for the segment.</summary>
        public int FrameCount { get; set; }
    }

    /// <summary>
    /// Persisted state of a segmented export (spec §3.8 "Приложение выгружено системой", §7.5 <c>ExportResumeState</c>): an immutable
    /// snapshot of everything that shapes the frames — the project document, resolution, fps, bitrates, the watermark decision,
    /// the platform masks, the text boxes, the user-video frame size and the render-stream length — plus the list of finished
    /// segments and the PCM mix. Resuming rebuilds the identical export plan from it, so the joined file has no seam; nothing
    /// is re-checked against Remote Config or entitlements (decision: a resumed export finishes as it started, and the
    /// rewarded gate is not shown again, spec RW4). Written atomically after every finished segment by
    /// <see cref="ExportResumeStore"/>; plain DTO serialized with <see cref="CoreJson"/>.
    /// </summary>
    public sealed class ExportResumeState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Job folder name in the export store (<see cref="ProjectPaths.IsSafeId"/>).</summary>
        public string JobId { get; set; }
        public string ProjectId { get; set; }
        /// <summary>ISO-8601 time the export started.</summary>
        public string CreatedAt { get; set; }
        /// <summary>Snapshot of the project at the start (spec §7.5 step 1).</summary>
        public ProjectDocument Project { get; set; }
        /// <summary>720 or 1080.</summary>
        public int Resolution { get; set; }
        public int Fps { get; set; }
        public long VideoBitrateBps { get; set; }
        public long AudioBitrateBps { get; set; }
        /// <summary>Watermark decision at the start (spec §3.10, checked at export time).</summary>
        public bool Watermark { get; set; }
        /// <summary>Normalized platform mask rects used for the watermark corner ([x, y, w, h] each).</summary>
        public List<double[]> MaskRects { get; set; } = new List<double[]>();
        /// <summary>Normalized text boxes measured at the 1080 reference ([x, y, w, h] each).</summary>
        public List<double[]> TextBounds { get; set; } = new List<double[]>();
        /// <summary>Decoded user-video frame size (0 × 0 without a video).</summary>
        public int VideoWidth { get; set; }
        public int VideoHeight { get; set; }
        /// <summary>Bounding box the video reader was opened with (<see cref="VideoDecodeSizing"/>); a resume reopens it with the same box.</summary>
        public int VideoBoxWidth { get; set; }
        public int VideoBoxHeight { get; set; }
        /// <summary>Width / height of the watermark artwork used for the placement.</summary>
        public float LogoAspect { get; set; } = WatermarkLayout.DefaultLogoAspect;
        /// <summary>Tick count of the clip's render stream when the export started.</summary>
        public int StreamTicks { get; set; }
        /// <summary>N, frames of the export plan.</summary>
        public int FrameCount { get; set; }
        /// <summary>Frames per full segment (2 s · fps).</summary>
        public int SegmentFrames { get; set; }
        /// <summary>Finished segments in order 0, 1, 2, … (contiguous).</summary>
        public List<ExportSegmentRecord> Segments { get; set; } = new List<ExportSegmentRecord>();
        /// <summary>True once the PCM mix (<see cref="StudioFiles.MixPcmFile"/>) is complete.</summary>
        public bool MixReady { get; set; }
        /// <summary>Frames of the PCM mix (48 kHz).</summary>
        public long MixFrames { get; set; }
        /// <summary>The rewarded gate was passed for this job (never shown again on resume).</summary>
        public bool GatePassed { get; set; }
        /// <summary>Save the finished file to the gallery (spec §3.8, setting).</summary>
        public bool SaveToGallery { get; set; } = true;
        /// <summary>Keep a copy in the project's <c>exports/</c> folder.</summary>
        public bool KeepInProject { get; set; }
        /// <summary>Output file name (no folders).</summary>
        public string OutputName { get; set; }
        /// <summary>Wall time spent rendering so far, ms (export history, spec §7.4 <c>exports[].ms</c>).</summary>
        public long ElapsedMs { get; set; }

        [JsonIgnore]
        public ExportResolution ResolutionValue => ProjectComposition.ResolutionOf(Resolution);

        /// <summary>Export settings with the stored bitrates (the encoder must keep its configuration across segments).</summary>
        public ExportSettings ToSettings()
        {
            long video = Clamp(VideoBitrateBps, ExportBitrateTable.MinBps, ExportBitrateTable.MaxBps);
            long audio = Clamp(AudioBitrateBps, ExportBitrateTable.MinBps, ExportBitrateTable.MaxBps);
            var table = new ExportBitrateTable(video, video, video, video, audio);
            return new ExportSettings(ResolutionValue, Fps, table);
        }

        /// <summary>Stored mask rects as <see cref="FloatRect"/>s.</summary>
        public IReadOnlyList<FloatRect> GetMaskRects()
        {
            return ToRects(MaskRects);
        }

        /// <summary>Stored text boxes as <see cref="FloatRect"/>s.</summary>
        public IReadOnlyList<FloatRect> GetTextBounds()
        {
            return ToRects(TextBounds);
        }

        /// <summary>Converts rects for storage.</summary>
        public static List<double[]> FromRects(IReadOnlyList<FloatRect> rects)
        {
            var list = new List<double[]>();
            if (rects == null) return list;
            for (int i = 0; i < rects.Count; i++) list.Add(new double[] { rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height });
            return list;
        }

        /// <summary>Index of the first frame still to render: the start of the first unfinished segment.</summary>
        [JsonIgnore]
        public int NextFrame => Math.Min(FrameCount, Segments.Count * SegmentFrames);

        /// <summary>True when every segment is finished (only the join is left).</summary>
        [JsonIgnore]
        public bool AllSegmentsDone => SegmentFrames > 0 && Segments.Count == (FrameCount + SegmentFrames - 1) / SegmentFrames;

        static IReadOnlyList<FloatRect> ToRects(List<double[]> values)
        {
            var list = new List<FloatRect>();
            if (values == null) return list;
            foreach (double[] v in values)
            {
                if (v == null || v.Length != 4) continue;
                bool finite = true;
                for (int i = 0; i < 4; i++) finite &= !double.IsNaN(v[i]) && !double.IsInfinity(v[i]);
                if (!finite) continue;
                list.Add(new FloatRect((float)v[0], (float)v[1], (float)v[2], (float)v[3]));
            }
            return list;
        }

        static long Clamp(long v, long min, long max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
