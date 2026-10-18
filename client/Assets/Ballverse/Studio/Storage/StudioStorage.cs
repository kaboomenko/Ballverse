using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Platform;

namespace Ballverse.Studio
{
    /// <summary>
    /// Streaming disk copy between <see cref="UnityFileStore"/>s (<c>File.Copy</c> + fsync of the copy), so copying a render stream
    /// of tens of MB into a project never loads it into memory. Other stores fall back to <see cref="BufferedFileCopier"/>.
    /// </summary>
    public sealed class DiskFileCopier : IFileCopier
    {
        public static readonly DiskFileCopier Instance = new DiskFileCopier();

        public void Copy(IFileStore from, string fromPath, IFileStore to, string toPath)
        {
            if (from is UnityFileStore a && to is UnityFileStore b)
            {
                string src = a.FullPath(fromPath), dst = b.FullPath(toPath);
                if (!File.Exists(src)) throw new FileNotFoundException("File not found: " + fromPath, fromPath);
                string dir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Copy(src, dst, true);
                UnityFileStore.FsyncFile(dst);
                return;
            }
            BufferedFileCopier.Instance.Copy(from, fromPath, to, toPath);
        }
    }

    /// <summary>Real clocks for Core: UTC ISO-8601 wall time and a monotonic millisecond counter.</summary>
    public sealed class StudioClock : IStudioClock
    {
        public static readonly StudioClock Instance = new StudioClock();
        readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

        public string NowIso()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        public long NowMs()
        {
            return _watch.ElapsedMilliseconds;
        }
    }

    /// <summary>Folder ids: 32 hex digits of a random GUID (satisfies <see cref="ProjectPaths.IsSafeId"/>).</summary>
    public sealed class GuidIdSource : IIdSource
    {
        public static readonly GuidIdSource Instance = new GuidIdSource();

        public string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>Bytes used by the studio, for the "Хранилище" screen (spec §3.9).</summary>
    public sealed class StudioStorageUsage
    {
        public long ProjectsBytes { get; set; }
        public long ClipLibraryBytes { get; set; }
        public long RecentRunsBytes { get; set; }
        public long ExportScratchBytes { get; set; }
        public long TotalBytes => ProjectsBytes + ClipLibraryBytes + RecentRunsBytes + ExportScratchBytes;
        /// <summary>Spec §3.9: warn above 2 GB.</summary>
        public bool ShouldWarn => TotalBytes > StudioStorage.WarnBytes;
    }

    /// <summary>
    /// The studio's three file stores (spec §3.9) and their startup cleanup:
    /// <list type="bullet">
    /// <item><see cref="Library"/>: persistent root — <c>projects/</c> (spec §7.4) and <c>clips/</c> (clip library); in the iOS backup.</item>
    /// <item><see cref="Runs"/>: <c>{cache}/render-streams</c> — the ring of the last 5 runs (<c>runs/{id}/</c>); never backed up.</item>
    /// <item><see cref="Exports"/>: <c>{cache}/export</c> — export jobs (segments, PCM mix, resume state) and finished outputs.</item>
    /// </list>
    /// </summary>
    public sealed class StudioStorage
    {
        /// <summary>Spec §3.9 storage warning threshold (2 GB).</summary>
        public const long WarnBytes = 2L * 1024 * 1024 * 1024;
        /// <summary>Folder of finished export files kept for "Share" until the next start.</summary>
        public const string OutputsDir = "outputs";

        public StudioStorage(string persistentRoot, string runCacheRoot, string exportRoot)
        {
            Library = new UnityFileStore(persistentRoot);
            Runs = new UnityFileStore(runCacheRoot);
            Exports = new UnityFileStore(exportRoot);
        }

        /// <summary>Stores at the app's standard folders (<see cref="IAppPaths"/>).</summary>
        public static StudioStorage FromAppPaths(IAppPaths paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            return new StudioStorage(paths.PersistentRoot, paths.RenderStreamCache, paths.ExportScratch);
        }

        public UnityFileStore Library { get; }
        public UnityFileStore Runs { get; }
        public UnityFileStore Exports { get; }

        /// <summary>
        /// Startup cleanup (spec §3.9 "Временные файлы экспорта удаляются … при старте приложения"): export jobs except resumable
        /// ones (when <paramref name="keepResumableExports"/>), unfinished runs and clip saves, orphan project folders, and
        /// leftover temp files of atomic writes. Call once at boot, before any studio work starts. Never throws; returns a short
        /// report for the log.
        /// </summary>
        public string CleanupAtStartup(ClipStore clips, bool keepResumableExports = true)
        {
            var report = new List<string>();
            Try(report, "exports", () => ExportResumeStore.CleanupAtStartup(Exports, keepResumableExports).Count + " job folders");
            if (clips != null)
            {
                Try(report, "runs", () => clips.CleanupRuns().Count + " unfinished runs");
                Try(report, "clips", () => clips.CleanupClips().Count + " unfinished clips");
                Try(report, "projects", () => clips.Projects.CleanupOrphans().Count + " orphan projects");
            }
            Try(report, "temp", () =>
                (Library.DeleteFilesWithSuffix(ProjectPaths.ProjectsRoot, StudioFiles.TempSuffix)
                 + Library.DeleteFilesWithSuffix(StudioFiles.ClipsRoot, StudioFiles.TempSuffix)
                 + Runs.DeleteFilesWithSuffix(StudioFiles.RunsRoot, StudioFiles.TempSuffix)) + " temp files");
            return string.Join("; ", report);
        }

        /// <summary>Usage per area (walks the folders; call off the critical path).</summary>
        public StudioStorageUsage MeasureUsage()
        {
            return new StudioStorageUsage
            {
                ProjectsBytes = Library.DirectorySize(ProjectPaths.ProjectsRoot),
                ClipLibraryBytes = Library.DirectorySize(StudioFiles.ClipsRoot),
                RecentRunsBytes = Runs.DirectorySize(StudioFiles.RunsRoot),
                ExportScratchBytes = Exports.DirectorySize(string.Empty),
            };
        }

        static void Try(List<string> report, string what, Func<string> action)
        {
            try
            {
                report.Add(what + ": " + action());
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                report.Add(what + ": failed (" + e.GetType().Name + ")");
            }
        }
    }
}
