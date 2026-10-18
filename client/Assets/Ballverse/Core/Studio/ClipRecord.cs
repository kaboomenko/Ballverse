using System;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Metadata of a recorded run (<see cref="StudioFiles.RunMetaFile"/>) or of a saved clip (<see cref="StudioFiles.ClipMetaFile"/>),
    /// spec §3.1 "Предыдущие запуски" / "Сохранить клип", §3.9 clip library. Plain DTO serialized with <see cref="CoreJson"/>.
    /// </summary>
    public sealed class ClipRecord
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        /// <summary>Folder name (<see cref="ProjectPaths.IsSafeId"/>).</summary>
        public string Id { get; set; }
        /// <summary>Stable mode id ("ring_escape", "geometry_bounce", "helix_drop").</summary>
        public string ModeId { get; set; }
        /// <summary>ISO-8601 time the run finished recording (or the clip was saved).</summary>
        public string CreatedAt { get; set; }
        /// <summary>Recorded length in ticks (render stream tick count, or the replay duration).</summary>
        public int DurationTicks { get; set; }
        /// <summary>Order of runs: strictly increasing per commit (newest = highest). 0 for saved clips.</summary>
        public long Sequence { get; set; }
        /// <summary>User-visible label of a saved clip, or null.</summary>
        public string Name { get; set; }
        /// <summary>Run the clip was saved from, or null.</summary>
        public string SourceRunId { get; set; }
        /// <summary>Winner id from the replay result, or null (card subtitle).</summary>
        public string WinnerId { get; set; }

        /// <summary>Parses a metadata file; null when the text is not a usable record.</summary>
        public static ClipRecord TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var r = CoreJson.Deserialize<ClipRecord>(json);
                if (r == null || !ProjectPaths.IsSafeId(r.Id) || string.IsNullOrEmpty(r.ModeId) || r.DurationTicks < 1) return null;
                if (r.SchemaVersion > CurrentSchemaVersion) return null;
                if (r.DurationTicks > SimConstants.MaxDurationTicks) r.DurationTicks = SimConstants.MaxDurationTicks;
                return r;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                return null;
            }
        }

        public string ToJson()
        {
            return CoreJson.Serialize(this);
        }

        public ClipRecord Clone()
        {
            return (ClipRecord)MemberwiseClone();
        }
    }

    /// <summary>A run or saved clip as listed: its record and which files are present.</summary>
    public sealed class ClipEntry
    {
        public ClipRecord Record { get; }
        /// <summary>Store path of the folder.</summary>
        public string Directory { get; }
        public bool HasReplay { get; }
        public bool HasRenderStream { get; }
        public bool HasEvents { get; }
        public long ReplayBytes { get; }
        /// <summary>Bytes of the derived visual cache: render stream + render events.</summary>
        public long StreamBytes { get; }

        public ClipEntry(ClipRecord record, string directory, bool hasReplay, bool hasRenderStream, bool hasEvents, long replayBytes, long streamBytes)
        {
            Record = record;
            Directory = directory;
            HasReplay = hasReplay;
            HasRenderStream = hasRenderStream;
            HasEvents = hasEvents;
            ReplayBytes = replayBytes;
            StreamBytes = streamBytes;
        }

        public string Id => Record.Id;
    }
}
