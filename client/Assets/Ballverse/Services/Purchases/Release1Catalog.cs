using System;
using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// The products sold in release 1 (owner decision): Remove Export Ads ($6.99), Remove Interstitial Ads ($5.99), No Ads
    /// Bundle ($8.99). Mode products, All Modes and Ultimate stay in the Core catalog (<see cref="ProductIds"/>) for later
    /// releases but are not registered with the store nor sold, because the modes they unlock are not in release 1.
    /// </summary>
    public static class Release1Catalog
    {
        static readonly string[] s_sold =
        {
            ProductIds.RemoveExportAds,
            ProductIds.RemoveInterstitialAds,
            ProductIds.NoAds,
        };

        static readonly IReadOnlyList<string> s_soldView = Array.AsReadOnly(s_sold);

        /// <summary>Ids registered with the store and shown in the shop, in shop order.</summary>
        public static IReadOnlyList<string> SoldProductIds => s_soldView;

        public static bool IsSold(string productId)
        {
            return productId != null && Array.IndexOf(s_sold, productId) >= 0;
        }
    }

    /// <summary>
    /// Release-1 entitlement rule adapter. Core <see cref="Entitlements.From"/> grants <c>NO_WATERMARK</c> only for Ultimate or
    /// the full Ultimate contents (10 paid modes + both ad removals). Release 1 sells no modes, so the owner decided that
    /// <c>NO_WATERMARK</c> comes from the No Ads Bundle or from owning both ad removals (Ultimate keeps granting it later).
    /// This adapter adds exactly that on top of the Core result; it must be removed once Core encodes the rule.
    /// </summary>
    public static class Release1Entitlements
    {
        /// <param name="ownedProductIds">Owned store product ids (unknown ids are ignored, as in Core).</param>
        /// <param name="config">Null = <see cref="MonetizationConfig.Default"/>.</param>
        public static EntitlementSet From(IEnumerable<string> ownedProductIds, MonetizationConfig config = null)
        {
            var set = Entitlements.From(ownedProductIds, config);
            return ApplyRelease1Rules(set);
        }

        /// <summary>Adds <c>NO_WATERMARK</c> when both <c>NO_EXPORT_ADS</c> and <c>NO_INTERSTITIALS</c> are present.</summary>
        public static EntitlementSet ApplyRelease1Rules(EntitlementSet set)
        {
            return set.IsSupersetOf(EntitlementSet.AdFree) ? set.With(Entitlement.NoWatermark) : set;
        }

        /// <summary>True when owning <paramref name="ownedProductIds"/> already gives everything <paramref name="productId"/> grants.</summary>
        public static bool IsRedundantPurchase(IEnumerable<string> ownedProductIds, string productId)
        {
            if (!ProductCatalog.TryGet(productId, out var product)) return false;
            var owned = From(ownedProductIds);
            return owned.IsSupersetOf(product.Grants);
        }
    }
}
