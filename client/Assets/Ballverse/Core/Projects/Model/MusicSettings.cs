namespace Ballverse.Core.Projects
{
    /// <summary>
    /// <c>audio.music</c>: built-in background track (spec §3.6 "Музыка": volume, start, fade in/out 0-2 s).
    /// Not shown in the §7.4 example (it uses null); field set chosen by this module.
    /// </summary>
    public sealed class MusicSettings
    {
        /// <summary>Built-in track id.</summary>
        public string Track { get; set; }

        /// <summary>Volume, 0-2.0.</summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>Start position inside the track, microseconds (>= 0).</summary>
        public long StartUs { get; set; }

        /// <summary>Fade-in, 0-2 s in microseconds.</summary>
        public long FadeInUs { get; set; }

        /// <summary>Fade-out, 0-2 s in microseconds.</summary>
        public long FadeOutUs { get; set; }
    }
}
