using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary><c>clip</c> section of project.json (spec §7.4, §3.4).</summary>
    public sealed class ClipSettings
    {
        /// <summary>Replay file, relative to the project folder (spec §7.4: "clip.bvr").</summary>
        public string Replay { get; set; } = ProjectPaths.ClipReplayFile;

        /// <summary>Stable mode id, e.g. "ring_escape" (see <see cref="ModeCatalog"/>).</summary>
        public string ModeId { get; set; }

        /// <summary>First tick of the trimmed clip (inclusive), 60 ticks per second.</summary>
        public int TrimInTick { get; set; }

        /// <summary>End tick of the trimmed clip (exclusive); must be greater than <see cref="TrimInTick"/>.</summary>
        public int TrimOutTick { get; set; } = SimConstants.MaxDurationTicks;

        /// <summary>Playback speed of the whole clip, 0.5-2.0 (spec §3.4).</summary>
        public double Speed { get; set; } = 1.0;

        /// <summary>Finale slow motion (spec §3.4).</summary>
        public FinaleSlowMo FinaleSlowMo { get; set; } = new FinaleSlowMo();

        /// <summary>What the simulation slot shows when the clip is shorter than the video (spec §3.4).</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public SimAfterEnd AfterEnd { get; set; } = SimAfterEnd.Freeze;

        /// <summary>Sound pack id, e.g. "Melody" (spec §3.6, §2.7).</summary>
        public string SoundPack { get; set; }

        /// <summary>Melody id for the melody sound pack, or null.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string Melody { get; set; }
    }
}
