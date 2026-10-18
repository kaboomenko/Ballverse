using System;

namespace Ballverse.Core.Projects
{
    /// <summary>Thrown by <see cref="ProjectMigrator.MigrateOrThrow"/>; <see cref="Error"/> tells why (e.g. <see cref="MigrationError.TooNew"/>).</summary>
    public sealed class ProjectMigrationException : Exception
    {
        public MigrationError Error { get; }
        public int FileVersion { get; }

        public ProjectMigrationException(MigrationError error, int fileVersion, string message, Exception inner = null)
            : base(message, inner)
        {
            Error = error;
            FileVersion = fileVersion;
        }
    }
}
