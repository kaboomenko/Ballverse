namespace Ballverse.Core.Ads
{
    /// <summary>What happened after the rewarded offer dialog was shown (spec §5.3.1).</summary>
    public enum RewardedOfferOutcome
    {
        /// <summary>RW2: the SDK fired <c>OnUserEarnedReward</c>.</summary>
        UserEarnedReward = 0,
        /// <summary>RW2: the user closed the ad before the reward.</summary>
        AdClosedEarly = 1,
        /// <summary>RW3: no ad loaded within <c>rewarded_wait_timeout_sec</c>.</summary>
        AdLoadTimedOut = 2,
        /// <summary>RW3: no fill or an SDK error.</summary>
        AdFailed = 3,
        /// <summary>RW3, spec §5.4: no network.</summary>
        Offline = 4,
        /// <summary>The user chose «Экспорт без рекламы навсегда» (Remove Export Ads).</summary>
        PurchaseRemoveExportAdsChosen = 5,
        /// <summary>The user pressed «Отмена» in the dialog.</summary>
        Cancelled = 6,
    }

    public enum RewardedGateStepKind
    {
        /// <summary>Start the export now (after the ad has closed, RW6).</summary>
        StartExport = 0,
        /// <summary>RW2: show the offer dialog again.</summary>
        ShowOfferAgain = 1,
        /// <summary>Run the Remove Export Ads purchase; afterwards call <see cref="RewardedExportGate.Decide"/> again.</summary>
        StartPurchase = 2,
        /// <summary>Back to the studio, no export.</summary>
        Abort = 3,
    }

    /// <summary>How an export started from the offer is funded.</summary>
    public enum ExportFunding
    {
        None = 0,
        /// <summary>RW2/RW5: a reward was earned; the project now holds one export credit.</summary>
        RewardEarned = 1,
        /// <summary>RW3, Р14: the ad network could not deliver; export without an ad and without a credit.</summary>
        AdUnavailable = 2,
    }

    /// <summary>Result of <see cref="RewardedExportGate.OnOfferOutcome"/>.</summary>
    public readonly struct RewardedGateStep
    {
        public RewardedGateStepKind Kind { get; }
        public ExportFunding Funding { get; }

        /// <summary>
        /// A rewarded ad was actually on screen (earned or closed early). The caller then reports
        /// <see cref="InterstitialPolicy.OnRewardedShown"/> for the IN3 quiet period.
        /// </summary>
        public bool RewardedAdWasShown { get; }

        public RewardedGateStep(RewardedGateStepKind kind, ExportFunding funding, bool rewardedAdWasShown)
        {
            Kind = kind;
            Funding = funding;
            RewardedAdWasShown = rewardedAdWasShown;
        }

        public override string ToString()
        {
            return Kind == RewardedGateStepKind.StartExport ? "StartExport(" + Funding + ")" : Kind.ToString();
        }
    }
}
