using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// The pure entitlement function of spec §5.0.2 / §5.1 (<c>Entitlements.From(ownedProducts, config)</c>). The config is
    /// required everywhere in this module: a null config throws <see cref="ArgumentNullException"/>.
    /// <para>
    /// NO_WATERMARK rule (spec §5.0.2, Р21; permanent, identical in every release and catalog preset):
    /// <c>NO_WATERMARK = owns Ultimate ∨ (NO_EXPORT_ADS ∧ NO_INTERSTITIALS)</c>, the ad rights counted from any products
    /// (No Ads Bundle, both single ad removals, Ultimate). The former "by parts" rule (the full Ultimate contents from any
    /// products, <see cref="FullUltimateContents"/>) is absorbed: those contents include both ad rights. The result is a
    /// pure function of the owned products, so it restores on any device or reinstall from the store alone (spec §5.4);
    /// no local flag, purchase date or cutoff is involved.
    /// </para>
    /// </summary>
    public static class Entitlements
    {
        /// <summary>
        /// The full Ultimate contents (spec §5.1, Р13): MODE_M06…MODE_M15 + NO_EXPORT_ADS + NO_INTERSTITIALS. Owning them
        /// grants NO_WATERMARK (absorbed by the ad rule of spec §5.0.2, since they include both ad rights); the storefront
        /// uses them to price Ultimate against its parts (spec §5.2). Fixed to the 1.0 catalog.
        /// </summary>
        public static readonly EntitlementSet FullUltimateContents = EntitlementSet.PaidModes10.Union(EntitlementSet.AdFree);

        /// <summary>
        /// Entitlements of a user owning <paramref name="ownedProductIds"/> (spec §5.0.2, §5.1): the union of the products'
        /// grants plus NO_WATERMARK by the permanent rule (see the class remarks); unknown, null and duplicate ids are
        /// ignored. Rights come from every catalog product, sellable in this release or not (spec §5.0.4).
        /// </summary>
        /// <param name="config">
        /// Release catalog (required; e.g. <see cref="MonetizationConfig.Release1"/>). Kept for the spec signature and to
        /// reject a missing config; the rights themselves do not depend on it.
        /// </param>
        public static EntitlementSet From(IEnumerable<string> ownedProductIds, MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return ApplyRules(DirectGrants(ownedProductIds));
        }

        /// <summary>Union of the products' own grants, without the derived NO_WATERMARK rule.</summary>
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
        /// Applies the derived NO_WATERMARK rule (spec §5.0.2) to a union of direct grants: adds NO_WATERMARK when both
        /// NO_EXPORT_ADS and NO_INTERSTITIALS are present. Ultimate grants NO_WATERMARK directly (spec §5.1).
        /// </summary>
        public static EntitlementSet ApplyRules(EntitlementSet directGrants)
        {
            return directGrants.IsSupersetOf(EntitlementSet.AdFree) ? directGrants.With(Entitlement.NoWatermark) : directGrants;
        }
    }
}
