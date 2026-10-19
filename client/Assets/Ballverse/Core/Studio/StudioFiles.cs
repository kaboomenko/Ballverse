using System;
using System.Globalization;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// File and folder names of the studio runtime (spec §3.1 recent runs and clip library, §3.8 export scratch, §3.9 storage)
    /// on top of the project layout of <see cref="ProjectPaths"/> (spec §7.4). All store paths use '/' and are relative to the
    /// root of the <see cref="IFileStore"/> they live in:
    /// <list type="bullet">
    /// <item>cache store (Application.temporaryCachePath/render-streams): <c>runs/{runId}/</c> — the ring buffer of the last 5
    /// runs (unsaved, spec §3.1);</item>
    /// <item>cache store (Application.temporaryCachePath/export): <c>{jobId}/</c> — one export job (segments, PCM mix, resume
    /// state, final file);</item>
    /// <item>persistent store (Application.persistentDataPath): <c>projects/{id}/</c> (spec §7.4) and <c>clips/{clipId}/</c> — the
    /// clip library (spec §3.9 "Библиотека клипов").</item>
    /// </list>
    /// A run or clip folder holds the same files as the clip part of a project: <c>clip.bvr</c>, <c>clip.bvs</c>,
    /// <c>clip.bve</c> and its metadata file.
    /// </summary>
    public static class StudioFiles
    {
        /// <summary>Render events of a clip (VFX, spec §7.7), next to <c>clip.bvs</c>; format owned by the Render module (RenderEventLog.ClipFileName).</summary>
        public const string ClipEventsFile = "clip.bve";
        /// <summary>Folder of the recent runs inside the run cache store.</summary>
        public const string RunsRoot = "runs";
        /// <summary>Folder of the saved clips inside the persistent store (spec §3.9).</summary>
        public const string ClipsRoot = "clips";
        /// <summary>Metadata of a recent run (written last: a run folder without it is an unfinished recording).</summary>
        public const string RunMetaFile = "run.json";
        /// <summary>Metadata of a saved clip (written last).</summary>
        public const string ClipMetaFile = "clip.json";
        /// <summary>Suffix of atomic-write temp files of this module.</summary>
        public const string TempSuffix = ".tmp";
        /// <summary>Resume state of an export job (spec §7.5 <c>ExportResumeState</c>).</summary>
        public const string ExportStateFile = "export_state.json";
        /// <summary>The offline audio mix of an export job in the PcmFile format (f32le, stereo, 48 kHz).</summary>
        public const string MixPcmFile = "mix.pcm";
        /// <summary>Folder of the 2-second video segments of an export job.</summary>
        public const string SegmentsDir = "segments";
        /// <summary>Folder of exported videos kept with a project (same as <see cref="ProjectPaths.ExportsDir"/>).</summary>
        public const string ProjectExportsDir = ProjectPaths.ExportsDir;
        /// <summary>Gallery album / Movies sub-folder of saved exports (spec §3.8).</summary>
        public const string GalleryAlbum = "Ballverse";

        /// <summary>The clip files that make a project clip or a run, in copy order (replay first: it is the source of truth).</summary>
        public static readonly string[] ClipDataFiles = { ProjectPaths.ClipReplayFile, ProjectPaths.ClipRenderFile, ClipEventsFile };

        /// <summary>
        /// Segment file name inside <see cref="SegmentsDir"/>: <c>seg_0000.mp4</c>, and <c>seg_0000_r1.mp4</c>, <c>_r2</c>… for later
        /// attempts of the same segment (after a pause or an encoder retry). A new attempt never reuses the name of a discarded
        /// writer, whose partial file may still be being deleted by the platform.
        /// </summary>
        public static string SegmentFileName(int index, int attempt = 0)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt));
            string name = "seg_" + index.ToString("D4", CultureInfo.InvariantCulture);
            if (attempt > 0) name += "_r" + attempt.ToString(CultureInfo.InvariantCulture);
            return name + ".mp4";
        }

        /// <summary>Store path of a segment (attempt) of job <paramref name="jobId"/>.</summary>
        public static string SegmentPath(string jobId, int index, int attempt = 0)
        {
            ProjectPaths.RequireSafeId(jobId, nameof(jobId));
            return jobId + "/" + SegmentsDir + "/" + SegmentFileName(index, attempt);
        }

        /// <summary>Store path of a file of job <paramref name="jobId"/> in the export store.</summary>
        public static string JobFile(string jobId, string fileName)
        {
            ProjectPaths.RequireSafeId(jobId, nameof(jobId));
            if (!ProjectPaths.IsSafeRelativePath(fileName)) throw new ArgumentException("Unsafe file name: " + fileName, nameof(fileName));
            return jobId + "/" + fileName;
        }

        /// <summary>Store path of a run folder: <c>runs/{runId}</c>.</summary>
        public static string RunDir(string runId)
        {
            ProjectPaths.RequireSafeId(runId, nameof(runId));
            return RunsRoot + "/" + runId;
        }

        /// <summary>Store path of a saved clip folder: <c>clips/{clipId}</c>.</summary>
        public static string ClipDir(string clipId)
        {
            ProjectPaths.RequireSafeId(clipId, nameof(clipId));
            return ClipsRoot + "/" + clipId;
        }
    }
}
