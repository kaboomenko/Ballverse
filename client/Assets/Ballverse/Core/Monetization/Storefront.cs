using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Smart storefront: protection from paying twice (spec §5.2 "Умная витрина", owner decisions Р6, Р13). Pure functions.
    /// <para>Rules, applied per product in display order:</para>
    /// <list type="number">
    /// <item>Owned product → hidden (<see cref="OfferHiddenReason.Owned"/>).</item>
    /// <item>Gain (new entitlements incl. NO_WATERMARK by parts) empty → hidden (<see cref="OfferHiddenReason.NothingNew"/>).</item>
    /// <item>A cheaper product (base USD price; ties broken by display order) has exactly the same gain → hidden
    /// (<see cref="OfferHiddenReason.CheaperEquivalent"/>). E.g. Ultimate after All Modes (No Ads gives the same),
    /// No Ads after Remove Export (Remove Interstitial gives the same), All Modes when only one mode is missing.</item>
    /// <item>Ultimate when the user owns at least one mode and at least one ad-removal right and the "by parts" rule is on →
    /// hidden (<see cref="OfferHiddenReason.CheaperViaParts"/>), per the §5.2 Remove Export / Remove Interstitial rows.</item>
    /// </list>
    /// Buying every visible offer always yields every entitlement, so nothing becomes unreachable.
    /// </summary>
    public static class Storefront
    {
        static readonly ProductInfo[] s_displayOrder = BuildDisplayOrder();

        /// <summary>Shop tab order (spec §5.2): Ultimate, All Modes, No Ads, Remove Export, Remove Interstitial, then M06…M15.</summary>
        public static IReadOnlyList<ProductInfo> DisplayOrder => s_displayOrder;

        /// <summary>
        /// The Shop tab for a user owning <paramref name="ownedProductIds"/> (spec §5.2). Unknown, null and duplicate ids are ignored.
        /// </summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        public static StorefrontState Build(IEnumerable<string> ownedProductIds, MonetizationConfig config = null)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            config = config ?? MonetizationConfig.Default;

            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ownedProductIds)
            {
                if (id != null) owned.Add(id);
            }

            EntitlementSet direct = Entitlements.DirectGrants(owned);
            EntitlementSet current = Entitlements.ApplyRules(direct, config);

            int n = s_displayOrder.Length;
            var gains = new EntitlementSet[n];
            var isOwned = new bool[n];
            var reasons = new OfferHiddenReason[n];
            var alternatives = new string[n];

            for (int i = 0; i < n; i++)
            {
                var p = s_displayOrder[i];
                isOwned[i] = owned.Contains(p.Id);
                gains[i] = Entitlements.ApplyRules(direct.Union(p.Grants), config).Except(current);
                if (isOwned[i]) reasons[i] = OfferHiddenReason.Owned;
                else if (gains[i].IsEmpty) reasons[i] = OfferHiddenReason.NothingNew;
            }

            for (int i = 0; i < n; i++)
            {
                if (reasons[i] != OfferHiddenReason.None) continue;
                int cheaper = FindCheaperEquivalent(i, gains);
                if (cheaper >= 0)
                {
                    reasons[i] = OfferHiddenReason.CheaperEquivalent;
                    alternatives[i] = s_displayOrder[cheaper].Id;
                }
            }

            int ultimate = IndexOf(ProductIds.Ultimate);
            if (reasons[ultimate] == OfferHiddenReason.None && config.WatermarkByParts
                && current.ModeCount > 0 && !current.Intersect(EntitlementSet.AdFree).IsEmpty)
            {
                reasons[ultimate] = OfferHiddenReason.CheaperViaParts;
            }

            int ownedModes = current.ModeCount;
            var offers = new StorefrontOffer[n];
            for (int i = 0; i < n; i++)
            {
                var p = s_displayOrder[i];
                bool allModes = p.Id == ProductIds.AllModes;
                bool visible = reasons[i] == OfferHiddenReason.None;
                bool lastStep = visible && i != ultimate && gains[i].Contains(Entitlement.NoWatermark);
                offers[i] = new StorefrontOffer(p, reasons[i], alternatives[i], isOwned[i], gains[i],
                    allModes ? ownedModes : 0,
                    allModes && ownedModes > 0 && ownedModes < EntitlementSet.PaidModes10.Count,
                    lastStep, false);
            }
            return new StorefrontState(offers, current);
        }

        /// <summary>
        /// Offers on the paid-mode sheet (spec §5.2 "Тап по платной карточке"): the single mode, then All Modes emphasized.
        /// Empty when the mode has no product (M01–M05) or its entitlement is already owned. All Modes is left out when the
        /// smart storefront hides it (owned, or only this one mode is missing so the single mode gives the same).
        /// Free/grandfathered availability is the caller's concern (an available mode shows no paid card).
        /// </summary>
        public static IReadOnlyList<StorefrontOffer> ModeSheet(ModeCode mode, IEnumerable<string> ownedProductIds,
            MonetizationConfig config = null)
        {
            var product = ProductCatalog.ForMode(mode);
            var state = Build(ownedProductIds, config);
            var result = new List<StorefrontOffer>();
            if (product == null || state.Entitlements.ContainsMode(mode)) return result;
            var single = state.Get(product.Id);
            if (single.Visible) result.Add(single);
            var all = state.Get(ProductIds.AllModes);
            if (all.Visible) result.Add(all.WithEmphasis(true));
            return result;
        }

        /// <summary>
        /// What the export sheet's locked "Водяной знак" row opens (spec §3.10, §5.2 "Лист Ultimate (или недостающая часть по
        /// умной витрине)"): visible offers that grant NO_WATERMARK on their own (Ultimate or a last step). When Ultimate is
        /// hidden because the parts are cheaper, the cheapest visible offer covering the missing modes and the cheapest
        /// covering the missing ad rights, which together complete the Ultimate contents. Empty if NO_WATERMARK is owned.
        /// </summary>
        public static IReadOnlyList<StorefrontOffer> WatermarkOffers(IEnumerable<string> ownedProductIds,
            MonetizationConfig config = null)
        {
            var state = Build(ownedProductIds, config);
            var result = new List<StorefrontOffer>();
            if (state.Entitlements.Contains(Entitlement.NoWatermark)) return result;
            foreach (var o in state.VisibleOffers)
            {
                if (o.RemovesWatermark) result.Add(o);
            }
            if (result.Count > 0) return result;

            var modesOffer = CheapestCovering(state, EntitlementSet.PaidModes10.Except(state.Entitlements));
            var adsOffer = CheapestCovering(state, EntitlementSet.AdFree.Except(state.Entitlements));
            foreach (var o in state.VisibleOffers)
            {
                if (o == modesOffer || o == adsOffer) result.Add(o);
            }
            return result;
        }

        static StorefrontOffer CheapestCovering(StorefrontState state, EntitlementSet needed)
        {
            if (needed.IsEmpty) return null;
            StorefrontOffer best = null;
            foreach (var o in state.VisibleOffers)
            {
                if (!o.Gain.IsSupersetOf(needed)) continue;
                if (best == null || o.Product.BasePriceUsdCents < best.Product.BasePriceUsdCents) best = o;
            }
            return best;
        }

        static int FindCheaperEquivalent(int i, EntitlementSet[] gains)
        {
            int price = s_displayOrder[i].BasePriceUsdCents;
            int best = -1;
            for (int j = 0; j < gains.Length; j++)
            {
                if (j == i || gains[j] != gains[i]) continue;
                int pj = s_displayOrder[j].BasePriceUsdCents;
                bool cheaper = pj < price || (pj == price && j < i);
                if (!cheaper) continue;
                if (best < 0 || pj < s_displayOrder[best].BasePriceUsdCents) best = j;
            }
            return best;
        }

        static int IndexOf(string productId)
        {
            for (int i = 0; i < s_displayOrder.Length; i++)
            {
                if (s_displayOrder[i].Id == productId) return i;
            }
            throw new InvalidOperationException(productId);
        }

        static ProductInfo[] BuildDisplayOrder()
        {
            var list = new List<ProductInfo>
            {
                ProductCatalog.Ultimate,
                ProductCatalog.AllModes,
                ProductCatalog.NoAds,
                ProductCatalog.RemoveExportAds,
                ProductCatalog.RemoveInterstitialAds,
            };
            foreach (var p in ProductCatalog.All)
            {
                if (p.Kind == ProductKind.SingleMode) list.Add(p);
            }
            return list.ToArray();
        }
    }
}
