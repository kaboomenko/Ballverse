namespace Ballverse.Core.Ads
{
    /// <summary>Where an export credit came from (spec §5.3.1).</summary>
    public enum RewardedCreditSource
    {
        /// <summary>RW2/RW5: earned by watching a rewarded ad to <c>OnUserEarnedReward</c>.</summary>
        Reward = 0,
        /// <summary>RW4/§3.8: an ad-funded export failed or was cancelled; a retry of the same project needs no new ad.</summary>
        Retry = 1,
    }

    /// <summary>
    /// One ad-funded export of one project (spec §5.3.1 RW2, RW4, RW5). A credit belongs to exactly one project and never
    /// transfers. At most one credit exists per project.
    /// </summary>
    public sealed class RewardedExportCredit
    {
        /// <summary>Project the credit is bound to (ordinal comparison).</summary>
        public string ProjectId { get; set; }

        public RewardedCreditSource Source { get; set; }

        /// <summary>
        /// Last valid moment, unix ms, inclusive (a retry at exactly failure time + window is still free).
        /// Null while the credit is held by an export (earned and not yet used, or claimed by <see cref="RewardedExportGate.Decide"/>):
        /// such a credit lasts until that export reports its result, so a long export or a resumed export never loses it.
        /// </summary>
        public long? ExpiresAtMs { get; set; }

        public RewardedExportCredit()
        {
        }

        public RewardedExportCredit(string projectId, RewardedCreditSource source, long? expiresAtMs)
        {
            ProjectId = projectId;
            Source = source;
            ExpiresAtMs = expiresAtMs;
        }

        /// <summary>True if the credit can fund an export at <paramref name="nowMs"/>.</summary>
        public bool IsValidAt(long nowMs)
        {
            return !ExpiresAtMs.HasValue || nowMs <= ExpiresAtMs.Value;
        }

        public RewardedExportCredit Clone()
        {
            return (RewardedExportCredit)MemberwiseClone();
        }
    }
}
