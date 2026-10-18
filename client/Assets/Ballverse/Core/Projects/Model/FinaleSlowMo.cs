namespace Ballverse.Core.Projects
{
    /// <summary>
    /// <c>clip.finaleSlowMo</c>: the last <see cref="Seconds"/> of the clip (source clip time) play at <see cref="Factor"/>
    /// (spec §3.4: last 1-3 s at 0.25-0.5x). Seconds = 0 disables the effect; the validator raises values in (0, 1) to 1.
    /// </summary>
    public sealed class FinaleSlowMo
    {
        /// <summary>Length of the slowed finale in clip seconds: 0 (off) or 1-3.</summary>
        public double Seconds { get; set; }

        /// <summary>Playback factor of the finale, 0.25-0.5.</summary>
        public double Factor { get; set; } = 0.5;
    }
}
