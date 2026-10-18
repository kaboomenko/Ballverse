namespace Ballverse.Core.Ads
{
    /// <summary>Final result of one export run (spec §3.8).</summary>
    public enum ExportResult
    {
        Succeeded = 0,
        /// <summary>Encoder failure or any other error that ended the export.</summary>
        Failed = 1,
        /// <summary>The user pressed «Отмена» during export.</summary>
        Cancelled = 2,
    }

    public enum ExportFinishKind
    {
        /// <summary>The export used no credit; nothing changed.</summary>
        None = 0,
        /// <summary>RW5: a successful export used up the project's credit.</summary>
        CreditConsumed = 1,
        /// <summary>RW4: the ad-funded export failed or was cancelled; the project may retry without an ad until the expiry.</summary>
        RetryCreditGranted = 2,
        /// <summary>The ad-funded export failed or was cancelled and the retry window is 0: the view is burned (§3.8 without В5).</summary>
        CreditBurned = 3,
    }

    /// <summary>Result of <see cref="RewardedExportGate.OnExportFinished"/>.</summary>
    public readonly struct ExportFinishOutcome
    {
        public ExportFinishKind Kind { get; }

        /// <summary>For <see cref="ExportFinishKind.RetryCreditGranted"/>: last free retry moment, unix ms, inclusive.</summary>
        public long? RetryExpiresAtMs { get; }

        public ExportFinishOutcome(ExportFinishKind kind, long? retryExpiresAtMs)
        {
            Kind = kind;
            RetryExpiresAtMs = retryExpiresAtMs;
        }

        public override string ToString()
        {
            return Kind == ExportFinishKind.RetryCreditGranted ? "RetryCreditGranted(" + RetryExpiresAtMs + ")" : Kind.ToString();
        }
    }
}
