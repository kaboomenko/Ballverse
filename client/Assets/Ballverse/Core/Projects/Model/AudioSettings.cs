using Newtonsoft.Json;

namespace Ballverse.Core.Projects
{
    /// <summary><c>audio</c> section (spec §7.4, §3.6).</summary>
    public sealed class AudioSettings
    {
        /// <summary>Simulation sounds volume, 0-2.0.</summary>
        public double Sim { get; set; } = 1.0;

        /// <summary>
        /// User video sound volume, 0-2.0 (spec §3.6: 0-200%, 0 = off). This is the single source of truth for the mixer;
        /// <see cref="UserVideoSettings.Volume"/> (also in the §7.4 example) is a mirror that the validator keeps equal to it.
        /// </summary>
        public double User { get; set; } = 1.0;

        /// <summary>Background music or null (spec §3.6, optional track).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public MusicSettings Music { get; set; }

        /// <summary>Normalize the final mix to -14 LUFS / -1 dBTP.</summary>
        public bool Normalize { get; set; } = true;

        /// <summary>
        /// Ducking: the user's voice lowers the music (spec §3.6 "Голос важнее", default on). Simulation sounds are never ducked.
        /// Added at schema 1.
        /// </summary>
        public bool DuckMusicUnderVoice { get; set; } = true;
    }
}
