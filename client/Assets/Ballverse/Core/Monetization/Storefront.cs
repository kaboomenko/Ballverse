using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Smart storefront: protection from paying twice (spec §5.2 "Умная витрина", owner decisions Р6, Р13). Pure functions.
    /// <para>
    /// Paid modes the user can already play without an entitlement (free by config or grandfathered, spec §5.1) are
    /// "available". An offer's <em>value</em> is its <see cref="StorefrontOffer.Gain"/> minus the entitlements of available
    /// modes: what the user would actually get that they do not already have.
    /// </para>
    /// <para>Rules, applied per product in display order:</para>
    /// <list type="number">
    /// <item>Owned product → hidden (<see cref="OfferHiddenReason.Owned"/>).</item>
    /// <item>Gain (new entitlements incl. NO_WATERMARK by parts) empty → hidden (<see cref="OfferHiddenReason.NothingNew"/>).</item>
    /// <item>Value empty (only re-sells available modes and does not complete NO_WATERMARK) → hidden
    /// (<see cref="OfferHiddenReason.AlreadyAvailable"/>). A single mode that would complete the Ultimate contents stays
    /// visible as a last step, flagged <see cref="StorefrontOffer.AlreadyAvailable"/>.</item>
    /// <item>A cheaper product (base USD price; ties broken by display order) has exactly the same value → hidden
    /// (<see cref="OfferHiddenReason.CheaperEquivalent"/>). E.g. Ultimate after All Modes (No Ads gives the same),
    /// No Ads after Remove Export (Remove Interstitial gives the same), All Modes when only one mode is missing.</item>
    /// <item>Ultimate when the "by parts" rule is on, the user owns at least one mode entitlement, and the cheapest set of
    /// other products completing the Ultimate contents (<see cref="CheapestPartsToComplete"/>) costs strictly less →
    /// hidden (<see cref="OfferHiddenReason.CheaperViaParts"/>): "Ultimate (он дороже, чем недостающая часть)". Without
    /// modes Ultimate stays visible, per the §5.2 Remove Export / Remove Interstitial rows ("Ultimate показывается только
    /// если нет режимов"). Covers e.g. a mode + an ad removal, and 9 single modes ($1.99 + No Ads $8.99 &lt; $11.99).</item>
    /// </list>
    /// Repeatedly buying every visible offer always ends with every entitlement except those of available modes, and
    /// with NO_WATERMARK, so nothing becomes unreachable.
    /// </summary>
    public static class Storefront
    {
        static readonly ProductInfo[] s_displayOrder = BuildDisplayOrder();
        static readonly IReadOnlyList<ProductInfo> s_displayOrderView = Array.AsReadOnly(s_displayOrder);

        /// <summary>Shop tab order (spec §5.2): Ultimate, All Modes, No Ads, Remove Export, Remove Interstitial, then M06…M15.</summary>
        public static IReadOnlyList<ProductInfo> DisplayOrder => s_displayOrderView;

        /// <summary>
        /// The Shop tab for a user owning <paramref name="ownedProductIds"/> (spec §5.2). Unknown, null and duplicate ids are ignored.
        /// </summary>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static StorefrontState Build(IEnumerable<string> ownedProductIds, MonetizationConfig config = null,
            IEnumerable<ModeCode> grandfathered = null)
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
            EntitlementSet availableModes = AvailableModeEntitlements(config, grandfathered);

            int n = s_displayOrder.Length;
            var gains = new EntitlementSet[n];
            var values = new EntitlementSet[n];
            var isOwned = new bool[n];
            var reasons = new OfferHiddenReason[n];
            var alternatives = new string[n];

            for (int i = 0; i < n; i++)
            {
                var p = s_displayOrder[i];
                isOwned[i] = owned.Contains(p.Id);
                gains[i] = Entitlements.ApplyRules(direct.Union(p.Grants), config).Except(current);
                values[i] = gains[i].Except(availableModes);
                if (isOwned[i]) reasons[i] = OfferHiddenReason.Owned;
                else if (gains[i].IsEmpty) reasons[i] = OfferHiddenReason.NothingNew;
                else if (values[i].IsEmpty) reasons[i] = OfferHiddenReason.AlreadyAvailable;
            }

            for (int i = 0; i < n; i++)
            {
                if (reasons[i] != OfferHiddenReason.None) continue;
                int cheaper = FindCheaperEquivalent(i, values, reasons);
                if (cheaper >= 0)
                {
                    reasons[i] = OfferHiddenReason.CheaperEquivalent;
                    alternatives[i] = s_displayOrder[cheaper].Id;
                }
            }

            int ultimate = IndexOf(ProductIds.Ultimate);
            if (reasons[ultimate] == OfferHiddenReason.None && config.WatermarkByParts && current.ModeCount > 0
                && PartsCostCents(current) < s_displayOrder[ultimate].BasePriceUsdCents)
            {
                reasons[ultimate] = OfferHiddenReason.CheaperViaParts;
            }

            int haveModes = current.Union(availableModes).ModeCount;
            var offers = new StorefrontOffer[n];
            for (int i = 0; i < n; i++)
            {
                var p = s_displayOrder[i];
                bool allModes = p.Id == ProductIds.AllModes;
                bool visible = reasons[i] == OfferHiddenReason.None;
                bool lastStep = visible && i != ultimate && gains[i].Contains(Entitlement.NoWatermark);
                bool alreadyAvailable = p.Kind == ProductKind.SingleMode && p.Mode.HasValue
                    && !current.ContainsMode(p.Mode.Value) && IsModeAvailable(p.Mode.Value, availableModes);
                offers[i] = new StorefrontOffer(p, reasons[i], alternatives[i], isOwned[i], gains[i],
                    allModes ? haveModes : 0,
                    allModes && haveModes > 0 && haveModes < EntitlementSet.PaidModes10.Count,
                    lastStep, false, alreadyAvailable);
            }
            return new StorefrontState(offers, current);
        }

        /// <summary>
        /// Offers on the paid-mode sheet (spec §5.2 "Тап по платной карточке"): the single mode, then All Modes emphasized.
        /// Empty when the mode has no product (M01–M05), is available without an entitlement (free by config or
        /// grandfathered: no paid card) or its entitlement is already owned.
        /// <para>
        /// Decision (open owner question): All Modes is left out when the smart storefront hides it, i.e. owned or only this
        /// one mode is missing (the single mode at $1.99 gives the same as All Modes at $4.99, §5.2 "защита от переплаты").
        /// The "Всегда" in the §5.2 sale-point table is read as "the sheet opens on every tap".
        /// </para>
        /// </summary>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static IReadOnlyList<StorefrontOffer> ModeSheet(ModeCode mode, IEnumerable<string> ownedProductIds,
            MonetizationConfig config = null, IEnumerable<ModeCode> grandfathered = null)
        {
            var product = ProductCatalog.ForMode(mode);
            var state = Build(ownedProductIds, config, grandfathered);
            var result = new List<StorefrontOffer>();
            if (product == null || state.Entitlements.ContainsMode(mode)) return result;
            if (ModeAvailability.GetAccess(mode, state.Entitlements, grandfathered, config) != ModeAccess.Locked) return result;
            var single = state.Get(product.Id);
            if (single.Visible) result.Add(single);
            var all = state.Get(ProductIds.AllModes);
            if (all.Visible) result.Add(all.WithEmphasis(true));
            return result;
        }

        /// <summary>
        /// What the export sheet's locked "Водяной знак" row opens (spec §3.10, §5.2 "Лист Ultimate (или недостающая часть по
        /// умной витрине)"), in display order. Empty if NO_WATERMARK is owned. Otherwise the visible offers that grant
        /// NO_WATERMARK on their own (Ultimate or a last step); when there are none (Ultimate hidden because the parts are
        /// cheaper), the products of <see cref="CheapestPartsToComplete"/>, which together complete the Ultimate contents.
        /// Such a part may be hidden in the Shop (e.g. <see cref="OfferHiddenReason.AlreadyAvailable"/> for a mode playable
        /// for free that is still needed for the contents); its <see cref="StorefrontOffer.HiddenReason"/> says so.
        /// </summary>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static IReadOnlyList<StorefrontOffer> WatermarkOffers(IEnumerable<string> ownedProductIds,
            MonetizationConfig config = null, IEnumerable<ModeCode> grandfathered = null)
        {
            var state = Build(ownedProductIds, config, grandfathered);
            var result = new List<StorefrontOffer>();
            if (state.Entitlements.Contains(Entitlement.NoWatermark)) return result;
            foreach (var o in state.VisibleOffers)
            {
                if (o.RemovesWatermark) result.Add(o);
            }
            if (result.Count > 0) return result;

            var parts = new HashSet<string>(PartsToComplete(state.Entitlements), StringComparer.Ordinal);
            foreach (var o in state.Offers)
            {
                if (parts.Contains(o.ProductId)) result.Add(o);
            }
            return result;
        }

        /// <summary>
        /// The cheapest products, other than Ultimate, that together complete <see cref="Entitlements.FullUltimateContents"/>
        /// for a user with <paramref name="entitlements"/> (base USD prices; spec §5.1 NO_WATERMARK by parts, §5.2): All Modes
        /// or the missing single modes, whichever is cheaper, plus No Ads or the one missing ad removal. Display order;
        /// empty when the contents are complete.
        /// </summary>
        public static IReadOnlyList<string> CheapestPartsToComplete(EntitlementSet entitlements)
        {
            var parts = PartsToComplete(entitlements);
            var result = new List<string>();
            foreach (var p in s_displayOrder)
            {
                if (parts.Contains(p.Id)) result.Add(p.Id);
            }
            return result;
        }

        static List<string> PartsToComplete(EntitlementSet entitlements)
        {
            var result = new List<string>();
            var missingModes = EntitlementSet.PaidModes10.Except(entitlements);
            if (!missingModes.IsEmpty)
            {
                int singles = 0;
                var singleIds = new List<string>();
                foreach (var p in s_displayOrder)
                {
                    if (p.Kind == ProductKind.SingleMode && !p.Grants.Intersect(missingModes).IsEmpty)
                    {
                        singles += p.BasePriceUsdCents;
                        singleIds.Add(p.Id);
                    }
                }
                if (singles < ProductCatalog.AllModes.BasePriceUsdCents) result.AddRange(singleIds);
                else result.Add(ProductIds.AllModes);
            }

            var missingAds = EntitlementSet.AdFree.Except(entitlements);
            if (!missingAds.IsEmpty)
            {
                int separate = 0;
                var separateIds = new List<string>();
                foreach (var p in new[] { ProductCatalog.RemoveExportAds, ProductCatalog.RemoveInterstitialAds })
                {
                    if (!p.Grants.Intersect(missingAds).IsEmpty)
                    {
                        separate += p.BasePriceUsdCents;
                        separateIds.Add(p.Id);
                    }
                }
                if (separate <= ProductCatalog.NoAds.BasePriceUsdCents) result.AddRange(separateIds);
                else result.Add(ProductIds.NoAds);
            }
            return result;
        }

        static int PartsCostCents(EntitlementSet entitlements)
        {
            int total = 0;
            foreach (var id in PartsToComplete(entitlements)) total += ProductCatalog.Get(id).BasePriceUsdCents;
            return total;
        }

        static EntitlementSet AvailableModeEntitlements(MonetizationConfig config, IEnumerable<ModeCode> grandfathered)
        {
            var set = EntitlementSet.Empty;
            foreach (var m in config.FreeModes)
            {
                if (EntitlementInfo.TryGetModeEntitlement(m, out var e)) set = set.With(e);
            }
            if (grandfathered != null)
            {
                foreach (var m in grandfathered)
                {
                    if (EntitlementInfo.TryGetModeEntitlement(m, out var e)) set = set.With(e);
                }
            }
            return set;
        }

        static bool IsModeAvailable(ModeCode mode, EntitlementSet availableModes)
        {
            return EntitlementInfo.TryGetModeEntitlement(mode, out var e) && availableModes.Contains(e);
        }

        static int FindCheaperEquivalent(int i, EntitlementSet[] values, OfferHiddenReason[] reasons)
        {
            int price = s_displayOrder[i].BasePriceUsdCents;
            int best = -1;
            for (int j = 0; j < values.Length; j++)
            {
                if (j == i || values[j] != values[i]) continue;
                if (reasons[j] != OfferHiddenReason.None && reasons[j] != OfferHiddenReason.CheaperEquivalent) continue;
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
