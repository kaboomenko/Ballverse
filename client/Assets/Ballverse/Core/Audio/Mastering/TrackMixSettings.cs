namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Studio sound settings for the export mix (spec §3.6 table; project.json <c>"audio"</c>, §7.4). Out-of-range values
    /// are clamped when mixing, NaN falls back to the default.
    /// </summary>
    public sealed class TrackMixSettings
    {
        /// <summary>Maximum track gain: 200 % (spec §3.6).</summary>
        public const float MaxTrackGain = 2f;

        /// <summary>Maximum music fade in / out, seconds (spec §3.6).</summary>
        public const float MaxFadeSeconds = 2f;

        /// <summary>Simulation sounds gain 0…2 (0–200 %). Default 1.</summary>
        public float SimGain { get; set; } = 1f;

        /// <summary>User video audio gain 0…2 (0–200 %). Default 1.</summary>
        public float UserGain { get; set; } = 1f;

        /// <summary>User video audio on/off (spec §3.6 "выключение"). Off = silent and no ducking.</summary>
        public bool UserEnabled { get; set; } = true;

        /// <summary>Music gain 0…2. Default 1.</summary>
        public float MusicGain { get; set; } = 1f;

        /// <summary>
        /// Output frame where music frame 0 lands (spec §3.6 "начало"). Negative = the track started earlier, i.e. playback
        /// begins inside the track. The fade-in starts at the first audible frame max(start, 0).
        /// </summary>
        public long MusicStartFrame { get; set; }

        /// <summary>Linear music fade-in 0…2 s from the first audible frame.</summary>
        public float MusicFadeInSeconds { get; set; }

        /// <summary>Linear music fade-out 0…2 s ending at the end of the music or of the output, whichever is first.</summary>
        public float MusicFadeOutSeconds { get; set; }

        /// <summary>"Voice matters more" (spec §3.6, default on): the user track ducks the music while it is active.</summary>
        public bool DuckMusicUnderUser { get; set; } = true;

        /// <summary>Music attenuation while ducked, dB (default −10, range −40…0).</summary>
        public float DuckDepthDb { get; set; } = -10f;

        /// <summary>User level (after its gain) above which the user track counts as active, dBFS (default −40).</summary>
        public float DuckThresholdDb { get; set; } = -40f;

        /// <summary>Normalize the final mix to −14 LUFS with the −1 dBTP limiter (project.json <c>"normalize"</c>). Default on.</summary>
        public bool Normalize { get; set; } = true;
    }
}
