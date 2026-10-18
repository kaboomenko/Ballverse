using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Keeps NO_WATERMARK for users who obtained it under the release-1 No Ads rule
    /// (<see cref="MonetizationConfig.WatermarkWithNoAds"/>) after the rule is turned off, like grandfathered free modes
    /// (spec §5.1 <c>grandfathered_modes</c>). Pure functions.
    /// <para>
    /// The flag is derived from store data (authoritative, restorable anywhere), so every device with the same purchases
    /// and a build that knows the cutoff gets the same flag: ad products whose store purchase time
    /// (<see cref="PurchaseRecord.PurchaseTimeUnixMs"/>) is before <see cref="MonetizationConfig.WatermarkWithNoAdsEndedAtUnixMs"/>
    /// were bought under the rule (<see cref="FromStore"/>). The caller persists the result (e.g.
    /// <c>watermark_no_ads_grandfathered</c>) only as a cache for launches without a complete store read.
    /// </para>
    /// <para>
    /// Lifecycle: after every complete store read (launch, purchase, restore finished) persist <see cref="FromStore"/>,
    /// replacing the stored value; without one (offline, store unavailable, restore not finished) keep the stored value with
    /// <see cref="Update(bool, IEnumerable{string}, MonetizationConfig)"/>, which never clears it. Pass the flag to
    /// <see cref="Entitlements.From"/>, <see cref="Storefront.Build"/> and the other storefront functions.
    /// </para>
    /// <para>
    /// Decisions: the flag keeps the rule, not the right: NO_WATERMARK still requires owning NO_EXPORT_ADS +
    /// NO_INTERSTITIALS, so a refund brings the watermark back (spec §3.10, §5.4). Re-buying restores it only where the
    /// rule still applies to the new purchase (before the cutoff, or while no cutoff is known): a refund followed by a
    /// post-cutoff re-purchase gives no NO_WATERMARK on any device, because the store no longer returns the refunded
    /// pre-cutoff transaction. Overwriting the cache on a complete read is safe: the flag never grants anything without
    /// current ad ownership, and a pre-cutoff purchase derives it again on the next read. Pending purchases never qualify,
    /// because they grant nothing (spec §5.4). A purchase without a store time cannot prove it was made before a known
    /// cutoff (the bridges must pass the original purchase time). The local flag is not signed: forging it gives
    /// NO_WATERMARK only to a user who already owns both ad rights, the release-1 price of the right.
    /// </para>
    /// </summary>
    public static class WatermarkGrandfathering
    {
        /// <summary>
        /// The flag to keep without a complete store read (offline launch from the entitlement cache): true if it already
        /// was, or if <paramref name="config"/> has <see cref="MonetizationConfig.WatermarkWithNoAds"/> on with no known
        /// cutoff and the user owns NO_EXPORT_ADS + NO_INTERSTITIALS (No Ads, both ad removals, or Ultimate). Otherwise false.
        /// Never clears the flag. Product ids carry no purchase time, so once a cutoff is known
        /// (<see cref="MonetizationConfig.WatermarkWithNoAdsEndedAtUnixMs"/>) only <see cref="FromStore"/> can set it.
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
            return !config.WatermarkWithNoAdsEndedAtUnixMs.HasValue
                && Entitlements.QualifiesByNoAds(directGrants, config.WatermarkWithNoAds);
        }

        /// <summary>
        /// The flag to persist after a complete store read (spec §5.4), derived from the transactions alone (the stored value
        /// is replaced). With a known cutoff (<see cref="MonetizationConfig.WatermarkWithNoAdsEndedAtUnixMs"/>, rule on or
        /// off): true if the currently purchased transactions with a store purchase time strictly before the cutoff together
        /// grant NO_EXPORT_ADS + NO_INTERSTITIALS. Without one: true if the rule is on and the currently purchased products
        /// grant both ad rights. A fresh install restoring a pre-cutoff No Ads purchase therefore keeps NO_WATERMARK; a
        /// post-cutoff purchase, or one ad removal before and the other after the cutoff, does not, on every device.
        /// </summary>
        /// <param name="records">Store transactions as passed to <see cref="OwnedProducts.From"/>, with original purchase times.</param>
        /// <param name="config">Release catalog and rules (required).</param>
        public static bool FromStore(IEnumerable<PurchaseRecord> records, MonetizationConfig config)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            if (config == null) throw new ArgumentNullException(nameof(config));
            var current = OwnedProducts.CurrentPurchased(records);
            long? cutoff = config.WatermarkWithNoAdsEndedAtUnixMs;
            var qualifying = EntitlementSet.Empty;
            // Unions are order-independent, so the unspecified record order cannot leak.
            foreach (var r in current)
            {
                if (!ProductCatalog.TryGet(r.ProductId, out var p)) continue;
                if (!cutoff.HasValue || (r.PurchaseTimeUnixMs.HasValue && r.PurchaseTimeUnixMs.Value < cutoff.Value))
                    qualifying = qualifying.Union(p.Grants);
            }
            return Entitlements.QualifiesByNoAds(qualifying, cutoff.HasValue || config.WatermarkWithNoAds);
        }
    }
}
