using System;
using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>Export history helpers (spec §3.8, §3.9 "Экспортировать снова").</summary>
    public static class ExportHistory
    {
        /// <summary>
        /// Appends a finished export to <c>exports[]</c> (oldest first), dropping the oldest records beyond
        /// <see cref="ProjectLimits.MaxExportRecords"/>, and remembers its resolution and fps in <c>output</c> for the next export.
        /// </summary>
        public static void Add(ProjectDocument document, ExportRecord record)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (document.Exports == null) document.Exports = new List<ExportRecord>();
            document.Exports.Add(record);
            int extra = document.Exports.Count - ProjectLimits.MaxExportRecords;
            if (extra > 0) document.Exports.RemoveRange(0, extra);
            if (document.Output == null) document.Output = new OutputSettings();
            document.Output.Resolution = record.Res;
            document.Output.Fps = record.Fps;
        }

        /// <summary>The newest export, or null.</summary>
        public static ExportRecord Latest(ProjectDocument document)
        {
            if (document?.Exports == null || document.Exports.Count == 0) return null;
            return document.Exports[document.Exports.Count - 1];
        }
    }
}
