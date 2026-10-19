using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;

namespace Ballverse.Studio
{
    /// <summary>
    /// Makes sure a clip folder (project, run or saved clip) has a readable render stream before it is drawn (spec §7.5
    /// preparation step 2): a missing or unreadable <c>clip.bvs</c> is rebuilt from <c>clip.bvr</c> through the App's
    /// <see cref="IRenderStreamRebuilder"/> into temporary files that are renamed into place only when complete and readable.
    /// One rebuild per folder at a time (later callers await the running one). Start calls on the main thread.
    /// </summary>
    public sealed class RenderStreamRepair
    {
        readonly IRenderStreamRebuilder _rebuilder;
        readonly Dictionary<string, Task<bool>> _running = new Dictionary<string, Task<bool>>(StringComparer.Ordinal);
        readonly object _gate = new object();

        /// <param name="rebuilder">The App's rebuilder, or null (missing streams then stay missing).</param>
        public RenderStreamRepair(IRenderStreamRebuilder rebuilder)
        {
            _rebuilder = rebuilder;
        }

        public bool CanRebuild => _rebuilder != null;

        /// <summary>True when <paramref name="clipDir"/> holds a render stream whose header and index can be read.</summary>
        public static bool HasReadableStream(string clipDir)
        {
            string path = Path.Combine(clipDir, ProjectPaths.ClipRenderFile);
            if (!File.Exists(path)) return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new RenderStreamReader(fs, true))
                {
                    return reader.TickCount > 0;
                }
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>Whether "В видео" can be offered for a run or saved clip.</summary>
        public ClipReadiness Readiness(ClipEntry entry)
        {
            if (entry == null) return ClipReadiness.Missing;
            if (!entry.HasReplay) return ClipReadiness.Unavailable;
            if (entry.HasRenderStream) return ClipReadiness.Ready;
            return CanRebuild ? ClipReadiness.NeedsRebuild : ClipReadiness.Unavailable;
        }

        /// <summary>
        /// Completes with true when <paramref name="clipDir"/> has a readable render stream (already, or rebuilt now); false when it
        /// has none and cannot get one (no rebuilder, no replay). Rebuild failures (corrupt replay, hash mismatch) throw.
        /// </summary>
        public Task<bool> EnsureAsync(string clipDir, IProgress<double> progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(clipDir)) throw new ArgumentException("folder required", nameof(clipDir));
            if (HasReadableStream(clipDir)) return Task.FromResult(true);
            if (_rebuilder == null || !File.Exists(Path.Combine(clipDir, ProjectPaths.ClipReplayFile))) return Task.FromResult(false);
            string key = Path.GetFullPath(clipDir);
            Task<bool> running;
            lock (_gate)
            {
                if (!_running.TryGetValue(key, out running))
                {
                    running = RebuildAsync(key, progress, ct);
                    if (!running.IsCompleted) _running[key] = running;
                }
            }
            return running;
        }

        async Task<bool> RebuildAsync(string clipDir, IProgress<double> progress, CancellationToken ct)
        {
            string stream = Path.Combine(clipDir, ProjectPaths.ClipRenderFile);
            string events = Path.Combine(clipDir, StudioFiles.ClipEventsFile);
            string streamTmp = stream + StudioFiles.TempSuffix;
            string eventsTmp = events + StudioFiles.TempSuffix;
            try
            {
                Delete(streamTmp);
                Delete(eventsTmp);
                await _rebuilder.RebuildAsync(Path.Combine(clipDir, ProjectPaths.ClipReplayFile), streamTmp, eventsTmp, progress, ct);
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(streamTmp)) throw new InvalidDataException("the rebuilder wrote no render stream");
                Replace(streamTmp, stream);
                if (File.Exists(eventsTmp)) Replace(eventsTmp, events);
                if (!HasReadableStream(clipDir))
                {
                    Delete(stream);
                    throw new InvalidDataException("the rebuilt render stream cannot be read");
                }
                return true;
            }
            finally
            {
                Delete(streamTmp);
                Delete(eventsTmp);
                lock (_gate) _running.Remove(clipDir);
            }
        }

        static void Replace(string from, string to)
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // a leftover .tmp is removed by the startup cleanup
            }
        }
    }
}
