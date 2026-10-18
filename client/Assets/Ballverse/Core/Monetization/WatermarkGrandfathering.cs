using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Keeps NO_WATERMARK for users who obtained it under the release-1 No Ads rule
    /// (<see cref="MonetizationConfig.WatermarkWithNoAds"/>) after a later release turns the rule off, like grandfathered free
    /// modes (spec §5.1 <c>grandfathered_modes</c>). The flag is persisted by the caller (local flag, e.g.
    /// <c>watermark_no_ads_grandfathered</c>); these functions are pure.
    /// <para>
    /// Lifecycle: call <see cref="Update(bool, IEnumerable{string}, MonetizationConfig)"/> whenever entitlements are recomputed
    /// (launch, purchase, restore) and persist the result; pass the persisted flag to <see cref="Entitlements.From"/>,
    /// <see cref="Storefront.Build"/> and the other storefront functions.
    /// </para>
    /// <para>
    /// Decisions: the flag is never cleared, so a transiently empty owned list (store unavailable, restore not finished)
    /// cannot wipe it. It keeps the rule, not the right: NO_WATERMARK still requires owning NO_EXPORT_ADS + NO_INTERSTITIALS,
    /// so a refund brings the watermark back (spec §3.10, §5.4) and re-buying the ad removals restores it. Pending purchases
    /// never qualify, because <see cref="OwnedProducts.From"/> drops them (spec §5.4).
    /// </para>
    /// </summary>
    public static class WatermarkGrandfathering
    {
        /// <summary>
        /// The updated flag to persist: true if it already was, or if <paramref name="config"/> has
        /// <see cref="MonetizationConfig.WatermarkWithNoAds"/> on and the user owns NO_EXPORT_ADS + NO_INTERSTITIALS (No Ads,
        /// both ad removals, or Ultimate). Otherwise false.
        /// </summary>
        /// <param name="grandfathered">The persisted flag (false on first run).</param>
        /// <param name="ownedProductIds">Current owned products (<see cref="OwnedProducts.From"/>); unknown ids ignored.</param>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        public static bool Update(bool grandfathered, IEnumerable<string> ownedProductIds, MonetizationConfig config = null)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            return Update(grandfathered, Entitlements.DirectGrants(ownedProductIds), config);
        }

        /// <summary>Same as the product-list overload, from the union of direct grants (<see cref="Entitlements.DirectGrants"/>).</summary>
        public static bool Update(bool grandfathered, EntitlementSet directGrants, MonetizationConfig config = null)
        {
            if (grandfathered) return true;
            config = config ?? MonetizationConfig.Default;
            return Entitlements.QualifiesByNoAds(directGrants, config.WatermarkWithNoAds);
        }
    }
}
