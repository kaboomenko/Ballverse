using System;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// One pure schema migration <c>N -> N+1</c> (spec §7.4 "Миграции схем"). <see cref="Migrate"/> receives a private copy
    /// of the document and returns the migrated object (it may mutate and return its argument). It must not touch
    /// <c>schemaVersion</c>: <see cref="ProjectMigrator"/> sets it to <see cref="ToVersion"/> afterwards.
    /// Each step needs a golden-file test with a saved project of version <see cref="FromVersion"/>.
    /// </summary>
    public sealed class ProjectMigrationStep
    {
        public int FromVersion { get; }
        public int ToVersion => FromVersion + 1;
        public Func<JObject, JObject> Migrate { get; }

        public ProjectMigrationStep(int fromVersion, Func<JObject, JObject> migrate)
        {
            if (fromVersion < 0) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            FromVersion = fromVersion;
            Migrate = migrate ?? throw new ArgumentNullException(nameof(migrate));
        }
    }
}
