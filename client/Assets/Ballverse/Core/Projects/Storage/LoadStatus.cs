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
        /// <summary>Both files are missing or unusable (unreadable, malformed, unmigratable or with unrepairable errors).</summary>
        Corrupt = 3,
    }
}
