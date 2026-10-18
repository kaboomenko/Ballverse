namespace Ballverse.Core.Projects
{
    /// <summary>Outcome of <see cref="ProjectRepository.Load"/>.</summary>
    public enum LoadStatus
    {
        Ok = 0,
        /// <summary>Neither project.json nor its backup exists.</summary>
        NotFound = 1,
        /// <summary>The project was written by a newer app (schemaVersion above current); it is not opened and must not be overwritten.</summary>
        TooNew = 2,
        /// <summary>
        /// Both files are missing or unusable (malformed, unmigratable or with unrepairable errors). I/O read errors are
        /// <see cref="Unavailable"/> instead.
        /// </summary>
        Corrupt = 3,
        /// <summary>
        /// A file could not be read because of an I/O error (<see cref="System.IO.IOException"/>,
        /// <see cref="System.UnauthorizedAccessException"/>), which may be temporary. No fallback to the older backup is made,
        /// because saving over a backup-based document would later erase the newer main file (spec §8 ST6); retry later.
        /// </summary>
        Unavailable = 4,
    }
}
