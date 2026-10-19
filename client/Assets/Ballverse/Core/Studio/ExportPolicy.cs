using System;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>Device thermal level as the studio sees it (Media <c>ThermalState</c> maps 1:1; spec §3.8 "Перегрев").</summary>
    public enum ExportThermalLevel
    {
        Unknown = -1,
        Nominal = 0,
        Fair = 1,
        /// <summary>iOS <c>serious</c> / Android <c>THERMAL_STATUS_SEVERE</c>: slow down (pause between segments).</summary>
        Serious = 2,
        Critical = 3,
    }

    /// <summary>Result of <see cref="ExportPolicy.Preflight"/>.</summary>
    public readonly struct ExportPreflight
    {
        public ExportErrorCode Error { get; }
        /// <summary>Spec §3.8: 2 × size estimate + 200 MB.</summary>
        public long RequiredBytes { get; }
        public long EstimatedFileBytes { get; }
        public long FreeBytes { get; }

        public ExportPreflight(ExportErrorCode error, long requiredBytes, long estimatedFileBytes, long freeBytes)
        {
            Error = error;
            RequiredBytes = requiredBytes;
            EstimatedFileBytes = estimatedFileBytes;
            FreeBytes = freeBytes;
        }

        public bool Ok => Error == ExportErrorCode.None;
    }

    /// <summary>
    /// Rules of the export run that are not geometry (spec §3.8 "Прерывания и надёжность", §7.5): free-space preflight, thermal
    /// slowdown, encoder retry, per-Unity-frame time budget and the disk-saving join option. Pure.
    /// </summary>
    public static class ExportPolicy
    {
        /// <summary>Main-thread budget per Unity frame for the frame loop, ms (spec §7.5 "~25 мс").</summary>
        public const double FrameBudgetMs = 25.0;
        /// <summary>Spec §3.8: one automatic retry of a segment after an encoder error.</summary>
        public const int MaxSegmentAttempts = 2;
        /// <summary>Pause between segments at <see cref="ExportThermalLevel.Serious"/>, ms.</summary>
        public const int SeriousPauseMs = 1500;
        /// <summary>Pause between segments at <see cref="ExportThermalLevel.Critical"/>, ms.</summary>
        public const int CriticalPauseMs = 4000;

        /// <summary>
        /// Free-space check before the start (spec §3.8: ≥ 2 × estimate + 200 MB). <paramref name="freeBytes"/> &lt; 0 means unknown
        /// (the platform could not tell): the export starts and a write failure is reported later.
        /// </summary>
        public static ExportPreflight Preflight(ExportPlan plan, long freeBytes)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            long required = plan.RequiredFreeSpaceBytes();
            long estimate = plan.EstimateFileSizeBytes();
            bool ok = freeBytes < 0 || plan.HasEnoughFreeSpace(freeBytes);
            return new ExportPreflight(ok ? ExportErrorCode.None : ExportErrorCode.InsufficientSpace, required, estimate, freeBytes);
        }

        /// <summary>Pause to insert after each finished segment at this thermal level (quality is never lowered, spec §3.8).</summary>
        public static int PauseBetweenSegmentsMs(ExportThermalLevel level)
        {
            switch (level)
            {
                case ExportThermalLevel.Serious: return SeriousPauseMs;
                case ExportThermalLevel.Critical: return CriticalPauseMs;
                default: return 0;
            }
        }

        /// <summary>True when the UI shows the "phone is hot, export slowed down" hint.</summary>
        public static bool IsThrottled(ExportThermalLevel level)
        {
            return level == ExportThermalLevel.Serious || level == ExportThermalLevel.Critical;
        }

        /// <summary>
        /// Whether the join should delete the segment files right after the remux, before the fast-start rewrite (Media
        /// <c>SegmentConcatOptions.DeleteSegmentsBeforeFastStart</c>): the join needs ≈ 2 × the file size beyond the segments
        /// (output + rewrite copy); when the free space is below that plus the 200 MB reserve, the segments are given up early
        /// (a failed rewrite then restarts the export from the first segment). Unknown free space (&lt; 0) keeps the segments.
        /// </summary>
        public static bool DeleteSegmentsBeforeFastStart(long freeBytes, long estimatedFileBytes)
        {
            if (freeBytes < 0) return false;
            long need = SafeAdd(SafeAdd(estimatedFileBytes, estimatedFileBytes), ExportSettings.FreeSpaceReserveBytes);
            return freeBytes < need;
        }

        /// <summary>True when another attempt of the same segment is allowed after <paramref name="failedAttempts"/> encoder failures.</summary>
        public static bool CanRetrySegment(int failedAttempts)
        {
            return failedAttempts < MaxSegmentAttempts;
        }

        /// <summary>
        /// True when the frame loop may start another frame in this Unity frame: <paramref name="elapsedMs"/> since the loop
        /// started this frame plus the average cost of one frame stays within the budget. At least one frame per Unity frame
        /// always runs, so a slow device still progresses.
        /// </summary>
        public static bool FitsInBudget(double elapsedMs, double averageFrameMs, int framesThisTick, double budgetMs = FrameBudgetMs)
        {
            if (framesThisTick <= 0) return true;
            double avg = averageFrameMs > 0 && !double.IsNaN(averageFrameMs) ? averageFrameMs : 0;
            return elapsedMs + avg <= budgetMs;
        }

        static long SafeAdd(long a, long b)
        {
            if (b > 0 && a > long.MaxValue - b) return long.MaxValue;
            return a + b;
        }
    }

    /// <summary>Gallery outcome of an export (spec §3.8 "Сохранение в галерею").</summary>
    public enum ExportGalleryStatus
    {
        /// <summary>Saving was off (setting) or the export failed before it.</summary>
        NotRequested = 0,
        Saved = 1,
        /// <summary>The user refused the permission: offer "Share" instead.</summary>
        PermissionDenied = 2,
        Failed = 3,
    }

    /// <summary>Outcome of one export run, for the "Готово" screen, analytics and the history (spec §3.8, §7.12).</summary>
    public sealed class StudioExportResult
    {
        public ExportErrorCode Error { get; set; }
        /// <summary>Readable detail for logs (no personal paths).</summary>
        public string Message { get; set; }
        public string ProjectId { get; set; }
        public string JobId { get; set; }
        /// <summary>The finished MP4 (absolute path) for "Share", or null on failure.</summary>
        public string OutputPath { get; set; }
        /// <summary>Project-relative copy kept in <c>exports/</c>, or null.</summary>
        public string ProjectFile { get; set; }
        public int FrameCount { get; set; }
        public long DurationUs { get; set; }
        public int Resolution { get; set; }
        public int Fps { get; set; }
        /// <summary>Wall time spent rendering and encoding, ms (history <c>exports[].ms</c>).</summary>
        public long ElapsedMs { get; set; }
        public ExportGalleryStatus Gallery { get; set; }
        /// <summary>Gallery location (content URI / asset id) when saved.</summary>
        public string GalleryLocation { get; set; }
        /// <summary>True when the file came from the fake media backend (editor): not a playable MP4.</summary>
        public bool IsSynthetic { get; set; }
        /// <summary>
        /// The job folder (finished segments, audio mix, resume state) was kept although this run failed — only the final join is
        /// missing (e.g. the app was suspended while muxing): <c>ResumeAsync(JobId)</c> finishes it without a new ad.
        /// </summary>
        public bool Resumable { get; set; }
        /// <summary>Spec §3.8: after an encoder failure the UI offers "Экспорт в 720p/30".</summary>
        public bool SuggestLowerSettings => Error == ExportErrorCode.EncoderFailed;

        public bool Succeeded => Error == ExportErrorCode.None;

        public static StudioExportResult Failure(ExportErrorCode error, string message, string projectId, string jobId)
        {
            return new StudioExportResult { Error = error, Message = message, ProjectId = projectId, JobId = jobId };
        }
    }
}
