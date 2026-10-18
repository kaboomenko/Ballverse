using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>Result of <see cref="ProjectRepository.Load"/>.</summary>
    public sealed class LoadResult
    {
        public LoadStatus Status { get; }
        /// <summary>Migrated and sanitized document, or null unless <see cref="Status"/> is <see cref="LoadStatus.Ok"/>.</summary>
        public ProjectDocument Document { get; }
        public LoadSource Source { get; }
        /// <summary>Schema version found in the file that was used (before migration), or -1.</summary>
        public int FileSchemaVersion { get; }
        /// <summary>Problems with the main file (when the backup was used) and sanitizer warnings, in order.</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        public bool Success => Status == LoadStatus.Ok;
        /// <summary>True when the document was migrated from an older schema (the caller may want to save it).</summary>
        public bool Migrated => Success && FileSchemaVersion != ProjectMigrator.CurrentSchemaVersion;

        public LoadResult(LoadStatus status, ProjectDocument document, LoadSource source, int fileSchemaVersion, IReadOnlyList<ValidationIssue> issues)
        {
            Status = status;
            Document = document;
            Source = source;
            FileSchemaVersion = fileSchemaVersion;
            Issues = issues;
        }
    }
}
