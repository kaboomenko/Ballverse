using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Projects
{
    /// <summary>Outcome of <see cref="ProjectMigrator.Migrate"/>.</summary>
    public sealed class MigrationResult
    {
        /// <summary>Migrated document (a copy; the input is untouched), or null on failure.</summary>
        public JObject Document { get; }
        public MigrationError Error { get; }
        /// <summary>Version found in the input, or -1 when missing or invalid.</summary>
        public int FromVersion { get; }
        /// <summary>Version of <see cref="Document"/> (current on success).</summary>
        public int ToVersion { get; }
        public string Message { get; }

        public bool Success => Error == MigrationError.None;
        /// <summary>True when at least one step ran.</summary>
        public bool Migrated => Success && FromVersion != ToVersion;

        public MigrationResult(JObject document, MigrationError error, int fromVersion, int toVersion, string message)
        {
            Document = document;
            Error = error;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Message = message;
        }
    }
}
