namespace Ballverse.Core.Ads
{
    public enum InterstitialDecisionKind
    {
        /// <summary>Nothing pending: carry on.</summary>
        None = 0,
        /// <summary>Show the interstitial now (after the 0.5 s «Реклама» label, IN8).</summary>
        Show = 1,
        /// <summary>An interstitial was pending but is not shown here; see <see cref="InterstitialDecision.Reason"/>.</summary>
        Suppressed = 2,
    }

    /// <summary>Why a pending interstitial was not shown (spec §5.3.2). Also the analytics <c>reason</c> of <c>ad_interstitial_skip</c>.</summary>
    public enum InterstitialSuppressReason
    {
        None = 0,
        /// <summary>IN7: the user owns <c>NO_INTERSTITIALS</c>. The counter is cleared.</summary>
        Entitlement = 1,
        /// <summary>IN3: «В видео» pressed. Stays pending.</summary>
        ToVideo = 2,
        /// <summary>IN3: in the studio. Stays pending.</summary>
        InStudio = 3,
        /// <summary>IN3: during or right after export. Stays pending.</summary>
        Exporting = 4,
        /// <summary>IN3: purchase flow open. Stays pending.</summary>
        PurchaseFlow = 5,
        /// <summary>IN3: within the quiet period after a rewarded ad. Stays pending.</summary>
        RewardedQuietPeriod = 6,
        /// <summary>IN5: the previous interstitial was shown less than the minimum interval ago. Stays pending.</summary>
        MinInterval = 7,
        /// <summary>IN4: no ad loaded (no fill, error, offline). The counter is reset to 0; the user is never delayed.</summary>
        AdNotReady = 8,
    }

    /// <summary>Result of <see cref="InterstitialPolicy.OnNaturalBreak"/> (spec §5.3.2).</summary>
    public readonly struct InterstitialDecision
    {
        public InterstitialDecisionKind Kind { get; }
        public InterstitialSuppressReason Reason { get; }

        /// <summary>True when this decision reset the counter to 0 (IN4 ad not ready, IN7 entitlement).</summary>
        public bool CounterReset { get; }

        InterstitialDecision(InterstitialDecisionKind kind, InterstitialSuppressReason reason, bool counterReset)
        {
            Kind = kind;
            Reason = reason;
            CounterReset = counterReset;
        }

        public bool IsShow => Kind == InterstitialDecisionKind.Show;

        public static InterstitialDecision None => new InterstitialDecision(InterstitialDecisionKind.None, InterstitialSuppressReason.None, false);

        public static InterstitialDecision Show => new InterstitialDecision(InterstitialDecisionKind.Show, InterstitialSuppressReason.None, false);

        public static InterstitialDecision Suppressed(InterstitialSuppressReason reason, bool counterReset)
        {
            return new InterstitialDecision(InterstitialDecisionKind.Suppressed, reason, counterReset);
        }

        public override string ToString()
        {
            return Kind == InterstitialDecisionKind.Suppressed ? "Suppressed(" + Reason + ")" : Kind.ToString();
        }
    }
}
