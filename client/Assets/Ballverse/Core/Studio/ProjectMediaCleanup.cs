using System;
using System.Collections.Generic;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Which imported files in a project's <c>media/</c> folder can be deleted (spec §3.9 "Проект = клип … + видео пользователя
    /// (копия файла)", §3.4 "Отмена / повтор 50 шагов"). Replacing or removing the user video is an undoable edit, so the previous
    /// copy must survive while any undo / redo snapshot may point at it: the studio deletes media files only when no document that
    /// can still become current references them — i.e. when the project is opened or closed (the undo history then holds only the
    /// current document). References compare case-insensitively (iOS and Windows file systems), so a file is kept on any doubt.
    /// </summary>
    public static class ProjectMediaCleanup
    {
        /// <summary>Project-relative media paths that <paramref name="doc"/> references (user video, kept exports).</summary>
        public static IReadOnlyList<string> References(ProjectDocument doc)
        {
            var refs = new List<string>();
            if (doc == null) return refs;
            if (doc.UserVideo != null && !string.IsNullOrEmpty(doc.UserVideo.File)) refs.Add(doc.UserVideo.File);
            if (doc.Clip != null && !string.IsNullOrEmpty(doc.Clip.Replay)) refs.Add(doc.Clip.Replay);
            if (doc.Exports != null)
            {
                foreach (ExportRecord e in doc.Exports)
                {
                    if (e != null && !string.IsNullOrEmpty(e.File)) refs.Add(e.File);
                }
            }
            return refs;
        }

        /// <summary>
        /// The files of <paramref name="mediaFiles"/> (project-relative, '/'-separated, e.g. "media/import_1.mp4") that lie in
        /// <c>media/</c> and that none of <paramref name="documents"/> references, in input order. Files outside <c>media/</c>,
        /// unsafe paths and in-progress work files (<c>.tmp</c>, <c>.part</c>) are never returned.
        /// </summary>
        public static IReadOnlyList<string> Unreferenced(IEnumerable<ProjectDocument> documents, IEnumerable<string> mediaFiles)
        {
            if (documents == null) throw new ArgumentNullException(nameof(documents));
            if (mediaFiles == null) throw new ArgumentNullException(nameof(mediaFiles));
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // membership only
            foreach (ProjectDocument doc in documents)
            {
                foreach (string r in References(doc)) referenced.Add(r);
            }
            var result = new List<string>();
            string prefix = ProjectPaths.MediaDir + "/";
            foreach (string file in mediaFiles)
            {
                if (string.IsNullOrEmpty(file) || !file.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!ProjectPaths.IsSafeMediaPath(file)) continue;
                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                if (!referenced.Contains(file)) result.Add(file);
            }
            return result;
        }
    }
}
