namespace Ballverse.Core.Ads
{
    public enum ExportGateDecisionKind
    {
        /// <summary>Start the export right away, no ad (see <see cref="ExportGateDecision.Reason"/>).</summary>
        ExportWithoutAd = 0,
        /// <summary>Show the rewarded offer dialog: watch an ad / buy Remove Export Ads / cancel (spec §5.3.1).</summary>
        ShowOffer = 1,
    }

    /// <summary>Why an export needs no ad (spec §5.3.1).</summary>
    public enum ExportWithoutAdReason
    {
        None = 0,
        /// <summary>The user owns <c>NO_EXPORT_ADS</c> (Remove Export Ads, No Ads or Ultimate, spec §5.1).</summary>
        Entitlement = 1,
        /// <summary>RW7 kill switch: <c>rewarded_export_enabled = false</c>.</summary>
        GateDisabled = 2,
        /// <summary>RW4: an ad-funded export of this project failed or was cancelled within the retry window.</summary>
        RetryCredit = 3,
        /// <summary>RW2/RW5: a reward earned for this project has not been used by a successful export yet.</summary>
        EarnedCredit = 4,
    }

    /// <summary>Result of <see cref="RewardedExportGate.Decide"/>.</summary>
    public readonly struct ExportGateDecision
    {
        public ExportGateDecisionKind Kind { get; }
        public ExportWithoutAdReason Reason { get; }

        ExportGateDecision(ExportGateDecisionKind kind, ExportWithoutAdReason reason)
        {
            Kind = kind;
            Reason = reason;
        }

        public bool IsShowOffer => Kind == ExportGateDecisionKind.ShowOffer;

        public static ExportGateDecision ShowOffer => new ExportGateDecision(ExportGateDecisionKind.ShowOffer, ExportWithoutAdReason.None);

        public static ExportGateDecision ExportWithoutAd(ExportWithoutAdReason reason)
        {
            return new ExportGateDecision(ExportGateDecisionKind.ExportWithoutAd, reason);
        }

        public override string ToString()
        {
            return Kind == ExportGateDecisionKind.ExportWithoutAd ? "ExportWithoutAd(" + Reason + ")" : Kind.ToString();
        }
    }
}
