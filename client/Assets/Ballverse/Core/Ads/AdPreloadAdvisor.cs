using System;

namespace Ballverse.Core.Ads
{
    /// <summary>Pure hints for the ads service on when to load ads in advance (spec §5.3.1 RW1, §5.3.2 IN4, IN7).</summary>
    public static class AdPreloadAdvisor
    {
        /// <summary>
        /// RW1: preload the rewarded ad when the user enters (or is in) the studio, so it is ready by export time.
        /// Not needed with <c>NO_EXPORT_ADS</c> or when the gate is switched off (RW7).
        /// </summary>
        public static bool ShouldPreloadRewarded(bool inStudio, bool hasNoExportAdsEntitlement, AdPolicyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return inStudio && !hasNoExportAdsEntitlement && config.RewardedExportEnabled;
        }

        /// <summary>
        /// Keep an interstitial loaded unless the user owns <c>NO_INTERSTITIALS</c> (IN7 disables loading too) or is
        /// deeper in the grace period than one full cycle (more than N games from pending). Because an ad that is not
        /// ready at the break forfeits the cycle (IN4), loading starts a full cycle ahead rather than one game ahead.
        /// </summary>
        public static bool ShouldPreloadInterstitial(InterstitialPolicy policy, bool hasNoInterstitialsEntitlement)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (hasNoInterstitialsEntitlement) return false;
            return policy.GamesUntilPending <= policy.Config.InterstitialEveryNGames;
        }
    }
}
