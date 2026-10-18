using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Keeps NO_WATERMARK for users who obtained it under the release-1 No Ads rule
    /// (<see cref="MonetizationConfig.WatermarkWithNoAds"/>) after a later release turns the rule off, like grandfathered free
    /// modes (spec §5.1 <c>grandfathered_modes</c>). Pure functions.
    /// <para>
    /// Two sources, so a reinstall or a second device never loses the paid right:
    /// <list type="bullet">
    /// <item>Store data (authoritative, restorable anywhere): ad products whose store purchase time
    /// (<see cref="PurchaseRecord.PurchaseTimeUnixMs"/>) is before <see cref="MonetizationConfig.WatermarkWithNoAdsEndedAtUnixMs"/>
    /// were bought under the rule; see <see cref="Update(bool, IEnumerable{PurchaseRecord}, MonetizationConfig)"/>.</item>
    /// <item>A caller-persisted local flag (e.g. <c>watermark_no_ads_grandfathered</c>), a cache of the result that also
    /// covers offline launches from the entitlement cache.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Lifecycle: on every store read (launch, purchase, restore) call the <see cref="PurchaseRecord"/> overload with the
    /// persisted flag and persist the result; offline, the product-id overload keeps the flag. Pass the flag to
    /// <see cref="Entitlements.From"/>, <see cref="Storefront.Build"/> and the other storefront functions.
    /// </para>
    /// <para>
    /// Decisions: the flag is never cleared, so a transiently empty owned list (store unavailable, restore not finished)
    /// cannot wipe it. It keeps the rule, not the right: NO_WATERMARK still requires owning NO_EXPORT_ADS + NO_INTERSTITIALS,
    /// so a refund brings the watermark back (spec §3.10, §5.4) and re-buying the ad removals restores it. Pending purchases
    /// never qualify, because they grant nothing (spec §5.4). A purchase without a store time cannot prove it was made under
    /// the rule and does not qualify by date (the bridges must pass the time). The local flag is not signed: forging it
    /// gives NO_WATERMARK only to a user who already owns both ad rights, the release-1 price of the right.
    /// </para>
    /// </summary>
    public static class WatermarkGrandfathering
    {
        /// <summary>
        /// The updated flag to persist: true if it already was, or if <paramref name="config"/> has
        /// <see cref="MonetizationConfig.WatermarkWithNoAds"/> on and the user owns NO_EXPORT_ADS + NO_INTERSTITIALS (No Ads,
        /// both ad removals, or Ultimate). Otherwise false. Cannot restore the flag on a new device after the rule is off:
        /// use the <see cref="PurchaseRecord"/> overload whenever store data is at hand.
        /// </summary>
        /// <param name="grandfathered">The persisted flag (false on first run).</param>
        /// <param name="ownedProductIds">Current owned products (<see cref="OwnedProducts.From"/>); unknown ids ignored.</param>
        /// <param name="config">Release catalog and rules (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        public static bool Update(bool grandfathered, IEnumerable<string> ownedProductIds, MonetizationConfig config)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            return Update(grandfathered, Entitlements.DirectGrants(ownedProductIds), config);
        }

        /// <summary>Same as the product-list overload, from the union of direct grants (<see cref="Entitlements.DirectGrants"/>).</summary>
        public static bool Update(bool grandfathered, EntitlementSet directGrants, MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (grandfathered) return true;
            return Entitlements.QualifiesByNoAds(directGrants, config.WatermarkWithNoAds);
        }

        /// <summary>
        /// The updated flag to persist, from store transactions (spec §5.4): the product-id rule of
        /// <see cref="Update(bool, IEnumerable{string}, MonetizationConfig)"/> on the owned products
        /// (<see cref="OwnedProducts.From"/>), or, when the rule is off and <see cref="MonetizationConfig.WatermarkWithNoAdsEndedAtUnixMs"/>
        /// is set, true if the currently purchased transactions with a store purchase time strictly before that cutoff
        /// together grant NO_EXPORT_ADS + NO_INTERSTITIALS (the user had NO_WATERMARK under the rule). A fresh install
        /// restoring a pre-cutoff No Ads purchase therefore keeps NO_WATERMARK; a post-cutoff purchase, or one ad removal
        /// before and the other after the cutoff, does not.
        /// </summary>
        /// <param name="grandfathered">The persisted flag (false on first run or a new device).</param>
        /// <param name="records">Store transactions as passed to <see cref="OwnedProducts.From"/>.</param>
        /// <param name="config">Release catalog and rules (required).</param>
        public static bool Update(bool grandfathered, IEnumerable<PurchaseRecord> records, MonetizationConfig config)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (grandfathered) return true;
            var current = OwnedProducts.CurrentPurchased(records);
            var all = EntitlementSet.Empty;
            var beforeCutoff = EntitlementSet.Empty;
            long? cutoff = config.WatermarkWithNoAdsEndedAtUnixMs;
            // Unions are order-independent, so the unspecified record order cannot leak.
            foreach (var r in current)
            {
                if (!ProductCatalog.TryGet(r.ProductId, out var p)) continue;
                all = all.Union(p.Grants);
                if (cutoff.HasValue && r.PurchaseTimeUnixMs.HasValue && r.PurchaseTimeUnixMs.Value < cutoff.Value)
                    beforeCutoff = beforeCutoff.Union(p.Grants);
            }
            if (Entitlements.QualifiesByNoAds(all, config.WatermarkWithNoAds)) return true;
            return !config.WatermarkWithNoAds && Entitlements.QualifiesByNoAds(beforeCutoff, cutoff.HasValue);
        }
    }
}
