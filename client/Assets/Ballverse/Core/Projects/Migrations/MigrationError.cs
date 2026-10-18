namespace Ballverse.Core.Projects
{
    /// <summary>Why <see cref="ProjectMigrator.Migrate"/> refused a document (spec §3.9 "Совместимость").</summary>
    public enum MigrationError
    {
        None = 0,
        /// <summary>No <c>schemaVersion</c> key.</summary>
        MissingVersion,
        /// <summary><c>schemaVersion</c> is not a non-negative integer.</summary>
        InvalidVersion,
        /// <summary>The file is from a newer app (version above current); it must not be opened or overwritten.</summary>
        TooNew,
        /// <summary>The version is older than the oldest registered step (no migration path).</summary>
        TooOld,
        /// <summary>A step threw or returned null.</summary>
        StepFailed,
    }
}
