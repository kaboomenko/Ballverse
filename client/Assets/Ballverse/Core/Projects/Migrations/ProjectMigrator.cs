using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Runs schema migrations <c>N -> N+1</c> on raw project JSON (spec §7.4, §3.9). Steps must form a contiguous chain
    /// ending at <see cref="TargetVersion"/>; files below the first step are <see cref="MigrationError.TooOld"/>, files above
    /// the target are <see cref="MigrationError.TooNew"/> (never downgraded).
    /// </summary>
    public sealed class ProjectMigrator
    {
        /// <summary>Schema version written by this build. Version 1 is the first schema, so <see cref="Default"/> has no steps.</summary>
        public const int CurrentSchemaVersion = 1;

        public const string VersionKey = "schemaVersion";

        /// <summary>Migrator of this build: target <see cref="CurrentSchemaVersion"/>, all shipped steps.</summary>
        public static readonly ProjectMigrator Default = new ProjectMigrator(CurrentSchemaVersion, new ProjectMigrationStep[0]);

        readonly ProjectMigrationStep[] _steps; // index = FromVersion - MinVersion

        /// <summary>Version every successful result has.</summary>
        public int TargetVersion { get; }

        /// <summary>Oldest version that can be migrated (first step's from-version, or the target when there are no steps).</summary>
        public int MinVersion { get; }

        /// <param name="targetVersion">Version to migrate to.</param>
        /// <param name="steps">Steps in any order; their from-versions must be exactly MinVersion..targetVersion-1.</param>
        public ProjectMigrator(int targetVersion, IEnumerable<ProjectMigrationStep> steps)
        {
            if (targetVersion < 0) throw new ArgumentOutOfRangeException(nameof(targetVersion));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            var list = new List<ProjectMigrationStep>();
            foreach (var s in steps)
            {
                if (s == null) throw new ArgumentException("Null migration step.", nameof(steps));
                list.Add(s);
            }
            list.Sort((a, b) => a.FromVersion.CompareTo(b.FromVersion));
            int min = list.Count == 0 ? targetVersion : list[0].FromVersion;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].FromVersion != min + i)
                    throw new ArgumentException("Migration steps must be contiguous and unique; unexpected step from " + list[i].FromVersion + ".", nameof(steps));
            }
            if (list.Count > 0 && list[list.Count - 1].ToVersion != targetVersion)
                throw new ArgumentException("Last migration step must end at the target version " + targetVersion + ".", nameof(steps));
            TargetVersion = targetVersion;
            MinVersion = min;
            _steps = list.ToArray();
        }

        /// <summary>
        /// Migrates a copy of <paramref name="document"/> to <see cref="TargetVersion"/>. Never throws for bad input;
        /// returns a result with <see cref="MigrationResult.Error"/> instead.
        /// </summary>
        public MigrationResult Migrate(JObject document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (!document.TryGetValue(VersionKey, StringComparison.Ordinal, out var token))
            {
                return Fail(MigrationError.MissingVersion, -1, "schemaVersion is missing.");
            }
            if (token.Type != JTokenType.Integer)
            {
                return Fail(MigrationError.InvalidVersion, -1, "schemaVersion must be an integer, got " + token.Type + ".");
            }
            long raw = token.Value<long>();
            if (raw < 0 || raw > int.MaxValue)
            {
                return Fail(MigrationError.InvalidVersion, -1, "schemaVersion out of range: " + raw + ".");
            }
            int version = (int)raw;
            if (version > TargetVersion)
            {
                return Fail(MigrationError.TooNew, version, "Project schema " + version + " is newer than supported " + TargetVersion + ".");
            }
            if (version < MinVersion)
            {
                return Fail(MigrationError.TooOld, version, "Project schema " + version + " is older than the oldest migratable " + MinVersion + ".");
            }

            var current = (JObject)document.DeepClone();
            for (int v = version; v < TargetVersion; v++)
            {
                var step = _steps[v - MinVersion];
                JObject next;
                try
                {
                    next = step.Migrate(current);
                }
                catch (Exception e)
                {
                    return Fail(MigrationError.StepFailed, version, "Migration " + v + "->" + (v + 1) + " failed: " + e.Message);
                }
                if (next == null)
                {
                    return Fail(MigrationError.StepFailed, version, "Migration " + v + "->" + (v + 1) + " returned null.");
                }
                next[VersionKey] = v + 1;
                current = next;
            }
            return new MigrationResult(current, MigrationError.None, version, TargetVersion, null);
        }

        /// <summary>Like <see cref="Migrate"/> but throws <see cref="ProjectMigrationException"/> on failure.</summary>
        public JObject MigrateOrThrow(JObject document)
        {
            var r = Migrate(document);
            if (!r.Success) throw new ProjectMigrationException(r.Error, r.FromVersion, r.Message);
            return r.Document;
        }

        MigrationResult Fail(MigrationError error, int fromVersion, string message)
        {
            return new MigrationResult(null, error, fromVersion, fromVersion, message);
        }
    }
}
