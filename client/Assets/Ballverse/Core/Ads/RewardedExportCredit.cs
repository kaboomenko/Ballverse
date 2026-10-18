using Newtonsoft.Json;

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

        /// <summary>
        /// True while the credit funds the export that is running now: set when a reward is earned (the export starts
        /// right away) and when <see cref="RewardedExportGate.Decide"/> uses the credit; cleared when the project is
        /// exported without using it (entitlement, RW7 kill switch). Only a claimed credit turns into a retry credit on a
        /// failed or cancelled export (RW4, RW5 «один просмотр = один экспорт»). Always false for a timed credit.
        /// Omitted from JSON when false; states written before this flag existed read as unclaimed, which only ever keeps
        /// a held credit held.
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Claimed { get; set; }

        public RewardedExportCredit()
        {
        }

        public RewardedExportCredit(string projectId, RewardedCreditSource source, long? expiresAtMs, bool claimed = false)
        {
            ProjectId = projectId;
            Source = source;
            ExpiresAtMs = expiresAtMs;
            Claimed = claimed;
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
