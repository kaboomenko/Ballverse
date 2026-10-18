namespace Ballverse.Core.Ads
{
    /// <summary>Natural pauses of the play loop where a pending interstitial may appear (spec §5.3.2 IN2, IN3).</summary>
    public enum NaturalBreakKind
    {
        /// <summary>«Ещё раз» on the result screen (same seed).</summary>
        Retry = 0,
        /// <summary>«🎲» on the result screen (new seed).</summary>
        Generate = 1,
        /// <summary>«Назад» on the result screen.</summary>
        BackFromResult = 2,
        /// <summary>Leaving to the gallery.</summary>
        ToGallery = 3,
        /// <summary>«В видео»: going to the studio. Never shows an interstitial (IN3, the creative flow wins).</summary>
        ToVideo = 4,
    }

    /// <summary>
    /// Everything <see cref="InterstitialPolicy.OnNaturalBreak"/> needs about the moment of a natural break (spec §5.3.2).
    /// The caller fills the flags from the current app state; the policy never reads globals.
    /// </summary>
    public sealed class NaturalBreakContext
    {
        public NaturalBreakKind Kind { get; set; }

        /// <summary>Current time, unix milliseconds. Must be ≥ 0.</summary>
        public long NowMs { get; set; }

        /// <summary>A loaded interstitial is available right now (IN4). False when offline (spec §5.4).</summary>
        public bool AdReady { get; set; }

        /// <summary>The user is in the studio (IN3).</summary>
        public bool InStudio { get; set; }

        /// <summary>
        /// An export is in progress or its result screen is still open (IN3 "during and right after export").
        /// The caller keeps it true from the export start until the user leaves the export result screen.
        /// </summary>
        public bool Exporting { get; set; }

        /// <summary>A store purchase sheet or purchase flow is open (IN3).</summary>
        public bool InPurchaseFlow { get; set; }

        /// <summary>The user owns <c>NO_INTERSTITIALS</c> (IN7, spec §5.1).</summary>
        public bool HasNoInterstitialsEntitlement { get; set; }

        public NaturalBreakContext()
        {
        }

        public NaturalBreakContext(NaturalBreakKind kind, long nowMs, bool adReady)
        {
            Kind = kind;
            NowMs = nowMs;
            AdReady = adReady;
        }
    }
}
