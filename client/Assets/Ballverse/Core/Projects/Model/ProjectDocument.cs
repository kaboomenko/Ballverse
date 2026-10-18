using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Root of <c>project.json</c> (spec §7.4 "Проект", §3.9). Plain mutable DTO serialized with <see cref="CoreJson"/>;
    /// property declaration order is the on-disk key order. Use <see cref="ProjectValidator.Sanitize"/> before trusting values
    /// read from disk, and <see cref="ProjectJson"/> to read/write it.
    /// </summary>
    public sealed class ProjectDocument
    {
        /// <summary>Schema version of this file (spec §3.9 "Совместимость"); migrated by <see cref="ProjectMigrator"/>.</summary>
        public int SchemaVersion { get; set; } = ProjectMigrator.CurrentSchemaVersion;

        /// <summary>Project id, equal to the folder name <c>projects/{id}/</c> (spec §7.4). Must satisfy <see cref="ProjectPaths.IsSafeId"/>.</summary>
        public string Id { get; set; }

        /// <summary>User-visible project name (spec §3.9).</summary>
        public string Name { get; set; }

        /// <summary>ISO-8601 creation time, e.g. "2026-10-18T12:00:00Z". Passed in by the caller (Core is pure).</summary>
        public string CreatedAt { get; set; }

        /// <summary>ISO-8601 time of the last change.</summary>
        public string UpdatedAt { get; set; }

        /// <summary>Simulation clip: replay, trim, speed, finale (spec §3.4).</summary>
        public ClipSettings Clip { get; set; } = new ClipSettings();

        /// <summary>Imported user video (spec §3.2-§3.4) or null when the project has none.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public UserVideoSettings UserVideo { get; set; }

        /// <summary>Split-screen layout (spec §3.3).</summary>
        public LayoutSettings Layout { get; set; } = new LayoutSettings();

        /// <summary>Text layers, at most <see cref="ProjectLimits.MaxTextLayers"/> (spec §3.5).</summary>
        public List<TextLayer> Texts { get; set; } = new List<TextLayer>();

        /// <summary>Mix volumes (spec §3.6).</summary>
        public AudioSettings Audio { get; set; } = new AudioSettings();

        /// <summary>Output format, duration and watermark preference (spec §3.4, §3.8, §3.10).</summary>
        public OutputSettings Output { get; set; } = new OutputSettings();

        /// <summary>Export history, oldest first (spec §3.8, §3.9 "Экспортировать снова").</summary>
        public List<ExportRecord> Exports { get; set; } = new List<ExportRecord>();
    }
}
