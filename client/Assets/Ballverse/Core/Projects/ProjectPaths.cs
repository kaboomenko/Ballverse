using System;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Project folder layout (spec §7.4) and path safety rules. All store paths use '/' and are relative to the store root.
    /// Paths taken from project.json are untrusted: they must be relative, use only [A-Za-z0-9._-] segments separated by '/',
    /// and never contain '.'/'..' segments, segments ending in '.', backslashes, drive letters, schemes or absolute roots
    /// (security relevant).
    /// </summary>
    public static class ProjectPaths
    {
        public const string ProjectsRoot = "projects";
        public const string ProjectFile = "project.json";
        public const string BackupFile = "project.json.bak";
        public const string TempFile = "project.json.tmp";
        public const string BackupTempFile = "project.json.bak.tmp";
        public const string ClipReplayFile = "clip.bvr";
        public const string ClipRenderFile = "clip.bvs";
        public const string MediaDir = "media";
        public const string ThumbsDir = "thumbs";
        /// <summary>Conventional folder of exported videos kept with the project (not in the §7.4 layout; see <see cref="ExportRecord.File"/>).</summary>
        public const string ExportsDir = "exports";
        public const string DefaultUserVideoFile = "media/user.mp4";

        /// <summary>
        /// True for an id usable as one folder/file name segment: 1-64 chars of [A-Za-z0-9_-]
        /// (covers UUIDs and "t1"-style text ids; rejects '.', '..', separators and any other character).
        /// </summary>
        public static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > ProjectLimits.MaxIdLength) return false;
            foreach (char c in id)
            {
                if (!IsIdChar(c)) return false;
            }
            return true;
        }

        /// <summary>
        /// True for a path that stays inside the project folder: relative, '/'-separated, every segment non-empty,
        /// not "." or "..", not ending in '.', made of [A-Za-z0-9._-]; total length at most 255.
        /// A trailing dot is rejected because Windows strips it ("project.json." opens project.json in the Windows editor),
        /// so such a segment would alias another name; no legitimate media name needs one.
        /// </summary>
        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > ProjectLimits.MaxPathLength) return false;
            int segStart = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i == path.Length || path[i] == '/')
                {
                    int len = i - segStart;
                    if (len == 0) return false; // leading '/', trailing '/', or '//'
                    if (len == 1 && path[segStart] == '.') return false;
                    if (len == 2 && path[segStart] == '.' && path[segStart + 1] == '.') return false;
                    if (path[i - 1] == '.') return false; // Win32 strips trailing dots: "a." aliases "a"
                    segStart = i + 1;
                    continue;
                }
                char c = path[i];
                if (!IsIdChar(c) && c != '.') return false;
            }
            return true;
        }

        /// <summary>Store path of a project folder: "projects/{id}". Throws for unsafe ids.</summary>
        public static string ProjectDir(string id, string root = ProjectsRoot)
        {
            RequireSafeId(id, nameof(id));
            return root + "/" + id;
        }

        /// <summary>Store path of a file inside a project folder. Throws for unsafe ids or paths.</summary>
        public static string InProject(string id, string relativePath, string root = ProjectsRoot)
        {
            if (!IsSafeRelativePath(relativePath)) throw new ArgumentException("Unsafe project-relative path: " + relativePath, nameof(relativePath));
            return ProjectDir(id, root) + "/" + relativePath;
        }

        /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="id"/> fails <see cref="IsSafeId"/>.</summary>
        public static void RequireSafeId(string id, string paramName)
        {
            if (!IsSafeId(id)) throw new ArgumentException("Unsafe project id: '" + id + "'", paramName);
        }

        /// <summary>
        /// True when <paramref name="relativePath"/> names one of the project's own files at the folder root: project.json, its
        /// backup or their temp files (spec §7.4). Compared ignoring case, as on case-insensitive file systems these names alias.
        /// </summary>
        public static bool IsProjectFileName(string relativePath)
        {
            return EqualsIgnoreCase(relativePath, ProjectFile)
                || EqualsIgnoreCase(relativePath, BackupFile)
                || EqualsIgnoreCase(relativePath, TempFile)
                || EqualsIgnoreCase(relativePath, BackupTempFile);
        }

        /// <summary>
        /// True when <paramref name="relativePath"/> names, or lies inside a folder named like, one of the root-level names a
        /// media reference must not use (ignoring case): the project files (<see cref="IsProjectFileName"/>) and the
        /// rebuildable render stream <see cref="ClipRenderFile"/>. A reference to them would let media import, export cleanup
        /// or cache rebuilding overwrite or delete the project itself; a file inside such a folder would block writing that
        /// file, and Duplicate does not copy those folders (spec §7.4, §3.9).
        /// </summary>
        public static bool IsReservedFileName(string relativePath)
        {
            if (relativePath == null) return false;
            int slash = relativePath.IndexOf('/');
            string first = slash < 0 ? relativePath : relativePath.Substring(0, slash);
            return IsProjectFileName(first) || EqualsIgnoreCase(first, ClipRenderFile);
        }

        /// <summary>
        /// True for a path project.json may reference as media (<c>clip.replay</c>, <c>userVideo.file</c>, <c>exports[].file</c>):
        /// <see cref="IsSafeRelativePath"/> and not <see cref="IsReservedFileName"/> (spec §7.4).
        /// </summary>
        public static bool IsSafeMediaPath(string path)
        {
            return IsSafeRelativePath(path) && !IsReservedFileName(path);
        }

        static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsIdChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
        }
    }
}
