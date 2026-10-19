using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Export jobs in the export store (spec §3.8, §3.9 "Временные файлы экспорта удаляются после успеха или отмены и при старте
    /// приложения", §7.5 resume): each job is a folder <c>{jobId}/</c> holding <see cref="StudioFiles.ExportStateFile"/>, the PCM mix
    /// and the segments. The state file is written atomically (temp → flush → replace), so after a crash it is either the old
    /// or the new state. Pure over <see cref="IFileStore"/>.
    /// </summary>
    public static class ExportResumeStore
    {
        /// <summary>Store path of the state file of <paramref name="jobId"/>.</summary>
        public static string StatePath(string jobId)
        {
            return StudioFiles.JobFile(jobId, StudioFiles.ExportStateFile);
        }

        /// <summary>Writes <paramref name="state"/> atomically. Throws <see cref="ArgumentException"/> for a structurally invalid state.</summary>
        public static void Save(IFileStore store, ExportResumeState state)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            string error = Validate(state);
            if (error != null) throw new ArgumentException("Invalid export state: " + error, nameof(state));
            string path = StatePath(state.JobId);
            string temp = path + StudioFiles.TempSuffix;
            store.WriteAllText(temp, CoreJson.Serialize(state));
            store.Flush(temp);
            store.Replace(temp, path);
        }

        /// <summary>Loads the state of a job; null when missing, unreadable or invalid (the job is then not resumable).</summary>
        public static ExportResumeState Load(IFileStore store, string jobId, out string error)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            error = null;
            if (!ProjectPaths.IsSafeId(jobId))
            {
                error = "unsafe job id";
                return null;
            }
            string path = StatePath(jobId);
            try
            {
                if (!store.FileExists(path))
                {
                    error = "no state file";
                    return null;
                }
                var state = CoreJson.Deserialize<ExportResumeState>(store.ReadAllText(path));
                if (state != null && state.Project != null)
                {
                    SanitizeResult sanitized = ProjectValidator.Sanitize(state.Project);
                    if (sanitized.HasErrors)
                    {
                        error = "project snapshot cannot be repaired";
                        return null;
                    }
                    state.Project = sanitized.Document;
                }
                error = Validate(state);
                if (error == null && state.JobId != jobId) error = "job id mismatch";
                return error == null ? state : null;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        /// <summary>Resumable jobs (valid state, not finished), newest first by <see cref="ExportResumeState.CreatedAt"/>.</summary>
        public static IReadOnlyList<ExportResumeState> List(IFileStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var list = new List<ExportResumeState>();
            foreach (string dir in store.ListDirectories(""))
            {
                ExportResumeState s = Load(store, dir, out _);
                if (s != null) list.Add(s);
            }
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(b.CreatedAt, a.CreatedAt);
                return c != 0 ? c : string.CompareOrdinal(b.JobId, a.JobId);
            });
            return list;
        }

        /// <summary>
        /// Structural check: ids, sizes, fps, segment list contiguous from 0 with the frame counts the settings imply, mix frames.
        /// Returns null when valid, else a short reason.
        /// </summary>
        public static string Validate(ExportResumeState s)
        {
            if (s == null) return "null state";
            if (s.SchemaVersion != ExportResumeState.CurrentSchemaVersion) return "unsupported schema " + s.SchemaVersion;
            if (!ProjectPaths.IsSafeId(s.JobId)) return "unsafe job id";
            if (!ProjectPaths.IsSafeId(s.ProjectId)) return "unsafe project id";
            if (s.Project == null) return "no project snapshot";
            if (s.Resolution != (int)ExportResolution.P720 && s.Resolution != (int)ExportResolution.P1080) return "bad resolution";
            if (!OutputFps.IsValid(s.Fps)) return "bad fps";
            if (s.VideoBitrateBps < 100000 || s.VideoBitrateBps > ExportBitrateTable.MaxBps) return "bad video bitrate";
            if (s.AudioBitrateBps < 32000 || s.AudioBitrateBps > 320000) return "bad audio bitrate";
            if (s.FrameCount < 1 || s.FrameCount > SimConstants.MaxDurationSeconds * s.Fps) return "bad frame count";
            if (s.SegmentFrames != ExportSettings.SegmentSeconds * s.Fps) return "bad segment length";
            if (s.StreamTicks < 1 || s.StreamTicks > SimConstants.MaxDurationTicks) return "bad stream length";
            if (s.VideoWidth < 0 || s.VideoHeight < 0 || s.VideoBoxWidth < 0 || s.VideoBoxHeight < 0) return "bad video size";
            if (!(s.LogoAspect > 0f) || float.IsInfinity(s.LogoAspect)) return "bad logo aspect";
            if (s.MixFrames < 0) return "bad mix length";
            if (s.Segments == null) return "no segment list";
            int segmentCount = (s.FrameCount + s.SegmentFrames - 1) / s.SegmentFrames;
            if (s.Segments.Count > segmentCount) return "too many segments";
            for (int i = 0; i < s.Segments.Count; i++)
            {
                ExportSegmentRecord r = s.Segments[i];
                if (r == null || r.Index != i) return "segments not contiguous";
                int expected = Math.Min(s.SegmentFrames, s.FrameCount - i * s.SegmentFrames);
                if (r.FrameCount != expected) return "segment " + i + " has " + r.FrameCount + " frames, expected " + expected;
                if (!ProjectPaths.IsSafeRelativePath(r.File)) return "unsafe segment path";
            }
            if (s.OutputName != null && !ProjectPaths.IsSafeRelativePath(s.OutputName)) return "unsafe output name";
            return null;
        }

        /// <summary>
        /// Number of leading segments whose result still exists in the job folder (a file, or a folder for the fake media backend).
        /// Segments after the first missing one must be redone.
        /// </summary>
        public static int CountUsableSegments(IFileStore store, ExportResumeState s)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (s?.Segments == null) return 0;
            int n = 0;
            foreach (ExportSegmentRecord r in s.Segments)
            {
                string path = StudioFiles.JobFile(s.JobId, r.File);
                if (!store.FileExists(path) && !store.DirectoryExists(path)) break;
                n++;
            }
            return n;
        }

        /// <summary>Deletes a job folder (no-op when missing).</summary>
        public static void DeleteJob(IFileStore store, string jobId)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            ProjectPaths.RequireSafeId(jobId, nameof(jobId));
            store.DeleteDirectory(jobId);
        }

        /// <summary>
        /// Startup cleanup (spec §3.9): deletes every job folder except those with a valid state that should stay resumable
        /// (<paramref name="keepResumable"/>) and the ones listed in <paramref name="keep"/>; loose files at the store root go too.
        /// Returns the deleted folder names.
        /// </summary>
        public static IReadOnlyList<string> CleanupAtStartup(IFileStore store, bool keepResumable, ICollection<string> keep = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var deleted = new List<string>();
            foreach (string dir in store.ListDirectories(""))
            {
                if (keep != null && keep.Contains(dir)) continue;
                if (keepResumable && ProjectPaths.IsSafeId(dir) && Load(store, dir, out _) != null) continue;
                try
                {
                    store.DeleteDirectory(dir);
                    deleted.Add(dir);
                }
                catch (IOException)
                {
                    // best effort; retried at the next start
                }
                catch (UnauthorizedAccessException)
                {
                    // best effort
                }
            }
            foreach (string file in store.ListFiles(""))
            {
                try
                {
                    store.DeleteFile(file);
                }
                catch (IOException)
                {
                    // best effort
                }
                catch (UnauthorizedAccessException)
                {
                    // best effort
                }
            }
            return deleted;
        }
    }
}
