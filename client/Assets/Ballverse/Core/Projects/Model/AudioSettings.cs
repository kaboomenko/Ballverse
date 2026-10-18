using Newtonsoft.Json;

namespace Ballverse.Core.Projects
{
    /// <summary><c>audio</c> section (spec §7.4, §3.6).</summary>
    public sealed class AudioSettings
    {
        /// <summary>Simulation sounds volume, 0-2.0.</summary>
        public double Sim { get; set; } = 1.0;

        /// <summary>User video sound volume, 0-2.0 (0 = off).</summary>
        public double User { get; set; } = 1.0;

        /// <summary>Background music or null (spec §3.6, optional track).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public MusicSettings Music { get; set; }

        /// <summary>Normalize the final mix to -14 LUFS / -1 dBTP.</summary>
        public bool Normalize { get; set; } = true;
    }
}
