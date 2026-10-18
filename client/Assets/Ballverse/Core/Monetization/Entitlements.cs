using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>The pure entitlement function of spec §5.1 (<c>Entitlements.From(ownedProducts, config)</c>).</summary>
    public static class Entitlements
    {
        /// <summary>
        /// The full Ultimate contents that unlock NO_WATERMARK "by parts" (spec §5.1, Р13): MODE_M06…MODE_M15 +
        /// NO_EXPORT_ADS + NO_INTERSTITIALS. Fixed to the 1.0 catalog, so future paid modes never revoke the right.
        /// </summary>
        public static readonly EntitlementSet FullUltimateContents = EntitlementSet.PaidModes10.Union(EntitlementSet.AdFree);

        /// <summary>
        /// Entitlements of a user owning <paramref name="ownedProductIds"/> (spec §5.1): the union of the products' grants;
        /// unknown, null and duplicate ids are ignored. Rights come from every catalog product, sellable in this release or
        /// not. NO_WATERMARK = owns Ultimate, or (<see cref="MonetizationConfig.WatermarkByParts"/>) owns
        /// <see cref="FullUltimateContents"/> assembled from any products, or (<see cref="MonetizationConfig.WatermarkWithNoAds"/>
        /// or <paramref name="watermarkGrandfathered"/>) owns NO_EXPORT_ADS + NO_INTERSTITIALS.
        /// </summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        /// <param name="watermarkGrandfathered">
        /// Persisted result of <see cref="WatermarkGrandfathering.Update(bool, IEnumerable{string}, MonetizationConfig)"/>: the
        /// user obtained NO_WATERMARK under the release-1 No Ads rule, which keeps applying to them after the rule is turned off.
        /// </param>
        public static EntitlementSet From(IEnumerable<string> ownedProductIds, MonetizationConfig config = null,
            bool watermarkGrandfathered = false)
        {
            return ApplyRules(DirectGrants(ownedProductIds), config, watermarkGrandfathered);
        }

        /// <summary>Union of the products' own grants, without the derived NO_WATERMARK rules.</summary>
        public static EntitlementSet DirectGrants(IEnumerable<string> ownedProductIds)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            var set = EntitlementSet.Empty;
            foreach (var id in ownedProductIds)
            {
                if (ProductCatalog.TryGet(id, out var p)) set = set.Union(p.Grants);
            }
            return set;
        }

        /// <summary>
        /// Applies the derived-right rules to a union of direct grants: NO_WATERMARK by parts (spec §5.1) and the release-1
        /// No Ads rule (see <see cref="From"/>).
        /// </summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        /// <param name="watermarkGrandfathered">See <see cref="From"/>.</param>
        public static EntitlementSet ApplyRules(EntitlementSet directGrants, MonetizationConfig config = null,
            bool watermarkGrandfathered = false)
        {
            config = config ?? MonetizationConfig.Default;
            if (config.WatermarkByParts && directGrants.IsSupersetOf(FullUltimateContents))
                return directGrants.With(Entitlement.NoWatermark);
            if (QualifiesByNoAds(directGrants, config.WatermarkWithNoAds || watermarkGrandfathered))
                return directGrants.With(Entitlement.NoWatermark);
            return directGrants;
        }

        internal static bool QualifiesByNoAds(EntitlementSet directGrants, bool ruleApplies)
        {
            return ruleApplies && directGrants.IsSupersetOf(EntitlementSet.AdFree);
        }
    }
}
