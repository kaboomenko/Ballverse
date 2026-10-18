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
        /// unknown, null and duplicate ids are ignored. NO_WATERMARK = owns Ultimate, or (when
        /// <see cref="MonetizationConfig.WatermarkByParts"/>) owns <see cref="FullUltimateContents"/> assembled from any products.
        /// </summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        public static EntitlementSet From(IEnumerable<string> ownedProductIds, MonetizationConfig config = null)
        {
            return ApplyRules(DirectGrants(ownedProductIds), config);
        }

        /// <summary>Union of the products' own grants, without the "by parts" rule.</summary>
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

        /// <summary>Applies the derived-right rules (spec §5.1 NO_WATERMARK by parts) to a union of direct grants.</summary>
        public static EntitlementSet ApplyRules(EntitlementSet directGrants, MonetizationConfig config = null)
        {
            config = config ?? MonetizationConfig.Default;
            if (config.WatermarkByParts && directGrants.IsSupersetOf(FullUltimateContents))
                return directGrants.With(Entitlement.NoWatermark);
            return directGrants;
        }
    }
}
