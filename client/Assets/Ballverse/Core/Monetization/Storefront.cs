using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Smart storefront: protection from paying twice (spec §5.0.3, §5.2 "Умная витрина", owner decisions Р6, Р13, Р21)
    /// restricted to the products sold in this release (<see cref="MonetizationConfig.SellableProductIds"/>; release 1: only
    /// the three ad products). NO_WATERMARK follows the permanent rule of spec §5.0.2 (<see cref="Entitlements"/>). Pure
    /// functions.
    /// <para>
    /// Paid modes the user can already play without an entitlement (free by config or grandfathered, and released;
    /// spec §5.1) are "available". An offer's <em>value</em> is its <see cref="StorefrontOffer.Gain"/> minus the entitlements
    /// of available modes: what the user would actually get that they do not already have.
    /// </para>
    /// <para>Rules, applied per product in display order:</para>
    /// <list type="number">
    /// <item>Owned product → hidden (<see cref="OfferHiddenReason.Owned"/>).</item>
    /// <item>Not sold in this release → hidden (<see cref="OfferHiddenReason.NotSellable"/>); never visible, never a cheaper
    /// alternative, never a watermark part.</item>
    /// <item>Gain (new entitlements incl. derived NO_WATERMARK) empty → hidden (<see cref="OfferHiddenReason.NothingNew"/>).</item>
    /// <item>Value empty (only re-sells available modes) → hidden (<see cref="OfferHiddenReason.AlreadyAvailable"/>). A single
    /// mode never grants NO_WATERMARK (spec §5.0.2 needs both ad rights), so an available single mode is always hidden.</item>
    /// <item>A cheaper sellable product (base USD price; ties broken by display order) has exactly the same value → hidden
    /// (<see cref="OfferHiddenReason.CheaperEquivalent"/>). E.g. Ultimate after All Modes (No Ads gives the same),
    /// No Ads after Remove Export (Remove Interstitial gives the same), All Modes when only one mode is missing.</item>
    /// <item>Ultimate when the user owns at least one mode entitlement and the cheapest set of other sellable products
    /// completing the Ultimate contents (<see cref="CheapestPartsToComplete"/>, which then also grant NO_WATERMARK through
    /// both ad rights, spec §5.0.2) costs strictly less →
    /// hidden (<see cref="OfferHiddenReason.CheaperViaParts"/>): "Ultimate (он дороже, чем недостающая часть)". Without
    /// modes Ultimate stays visible, per the §5.2 Remove Export / Remove Interstitial rows ("Ultimate показывается только
    /// если нет режимов"). Covers e.g. a mode + an ad removal, and 9 single modes ($1.99 + No Ads $8.99 &lt; $11.99).</item>
    /// </list>
    /// Repeatedly buying every visible offer always ends with every entitlement the sellable products can give, except
    /// those of available modes, including NO_WATERMARK whenever a sellable path to it exists, so nothing becomes
    /// unreachable. Release 1 with nothing owned shows No Ads (removes the watermark, not a last step), Remove Export Ads,
    /// Remove Interstitial Ads; after one ad removal only the other one (last step); after both or No Ads "Всё куплено".
    /// <para>Every function takes the release config as a required argument and throws on null (no implicit catalog).</para>
    /// </summary>
    public static class Storefront
    {
        static readonly ProductInfo[] s_displayOrder = BuildDisplayOrder();
        static readonly IReadOnlyList<ProductInfo> s_displayOrderView = Array.AsReadOnly(s_displayOrder);
        static readonly int s_ultimateIndex = IndexOf(ProductIds.Ultimate);

        /// <summary>Shop tab order (spec §5.2): Ultimate, All Modes, No Ads, Remove Export, Remove Interstitial, then M06…M15.</summary>
        public static IReadOnlyList<ProductInfo> DisplayOrder => s_displayOrderView;

        /// <summary>
        /// The Shop tab for a user owning <paramref name="ownedProductIds"/> (spec §5.2). Unknown, null and duplicate ids are ignored.
        /// Only <see cref="MonetizationConfig.SellableProductIds"/> can be visible; with <see cref="MonetizationConfig.Release1"/>
        /// at most No Ads, Remove Export Ads and Remove Interstitial Ads.
        /// </summary>
        /// <param name="config">Release catalog (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static StorefrontState Build(IEnumerable<string> ownedProductIds, MonetizationConfig config,
            IEnumerable<ModeCode> grandfathered)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in ownedProductIds)
            {
                if (id != null) owned.Add(id);
            }

            EntitlementSet direct = Entitlements.DirectGrants(owned);
            EntitlementSet current = Entitlements.ApplyRules(direct);
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
                gains[i] = Entitlements.ApplyRules(direct.Union(p.Grants)).Except(current);
                values[i] = gains[i].Except(availableModes);
                if (isOwned[i]) reasons[i] = OfferHiddenReason.Owned;
                else if (!config.IsSellable(p.Id)) reasons[i] = OfferHiddenReason.NotSellable;
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

            int ultimate = s_ultimateIndex;
            if (reasons[ultimate] == OfferHiddenReason.None && current.ModeCount > 0
                && TryCover(current, Entitlements.FullUltimateContents, config, out _, out int partsCost)
                && partsCost < s_displayOrder[ultimate].BasePriceUsdCents)
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
                bool lastStep = visible && i != ultimate && CompletesStartedSet(direct, availableModes, p);
                bool alreadyAvailable = p.Kind == ProductKind.SingleMode && p.Mode.HasValue
                    && !current.ContainsMode(p.Mode.Value) && IsModeAvailable(p.Mode.Value, availableModes);
                offers[i] = new StorefrontOffer(p, reasons[i], alternatives[i], isOwned[i], gains[i],
                    allModes ? haveModes : 0,
                    allModes && haveModes > 0 && haveModes < EntitlementSet.PaidModes10.Count,
                    lastStep, false, alreadyAvailable);
            }
            // Available modes count as had, like in the offer values: otherwise an empty Shop could report "not all
            // purchased" when the only missing rights are modes the user already plays for free.
            var obtainable = Entitlements.ApplyRules(direct.Union(config.SellableGrants));
            return new StorefrontState(offers, current, current.Union(availableModes).IsSupersetOf(obtainable));
        }

        /// <summary>
        /// Offers on the paid-mode sheet (spec §5.2 "Тап по платной карточке"): the single mode, then All Modes emphasized.
        /// Empty when the mode has no product (M01–M05), is not released or disabled (<see cref="ModeAccess.NotReleased"/>, release 1:
        /// M03, M04, M06–M15), is available without an entitlement (free by config or grandfathered: no paid card) or its
        /// entitlement is already owned. Only sellable products appear: a product outside
        /// <see cref="MonetizationConfig.SellableProductIds"/> is left out.
        /// <para>
        /// Decision (open owner question): All Modes is left out when the smart storefront hides it, i.e. owned or only this
        /// one mode is missing (the single mode at $1.99 gives the same as All Modes at $4.99, §5.2 "защита от переплаты").
        /// The "Всегда" in the §5.2 sale-point table is read as "the sheet opens on every tap".
        /// </para>
        /// </summary>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static IReadOnlyList<StorefrontOffer> ModeSheet(ModeCode mode, IEnumerable<string> ownedProductIds,
            MonetizationConfig config, IEnumerable<ModeCode> grandfathered)
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
        /// What the export sheet's locked "Водяной знак" row opens (spec §3.10, §5.0.3 "Лист No Ads Bundle …, а если одна
        /// реклама уже куплена — недостающая покупка", §5.2), in display order. Empty if NO_WATERMARK is owned. Otherwise
        /// the visible offers that grant NO_WATERMARK on their own (Ultimate, No Ads, or the missing ad removal as the last
        /// step); when there are none, the cheapest sellable products that together grant both ad rights (spec §5.0.2), e.g.
        /// both single ad removals in a catalog that sells them without No Ads. Never contains an unsellable product; empty
        /// when no sellable product can remove the watermark.
        /// </summary>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static IReadOnlyList<StorefrontOffer> WatermarkOffers(IEnumerable<string> ownedProductIds,
            MonetizationConfig config, IEnumerable<ModeCode> grandfathered)
        {
            var state = Build(ownedProductIds, config, grandfathered);
            var result = new List<StorefrontOffer>();
            if (state.Entitlements.Contains(Entitlement.NoWatermark)) return result;
            foreach (var o in state.VisibleOffers)
            {
                if (o.RemovesWatermark) result.Add(o);
            }
            if (result.Count > 0) return result;

            if (!TryCover(state.Entitlements, EntitlementSet.AdFree, config, out var adIds, out _)) return result;
            var parts = new HashSet<string>(adIds, StringComparer.Ordinal);
            foreach (var o in state.Offers)
            {
                if (parts.Contains(o.ProductId)) result.Add(o);
            }
            return result;
        }

        /// <summary>
        /// The cheapest sellable products, other than Ultimate, that together complete <see cref="Entitlements.FullUltimateContents"/>
        /// for a user with <paramref name="entitlements"/> (base USD prices; spec §5.1, §5.2 "Ultimate (он дороже, чем недостающая часть)"): All Modes
        /// or the missing single modes, whichever is cheaper, plus No Ads or the missing ad removal(s). Display order;
        /// empty when the contents are complete or the sellable products cannot complete them (release 1 sells no mode).
        /// </summary>
        /// <param name="config">Release catalog (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        public static IReadOnlyList<string> CheapestPartsToComplete(EntitlementSet entitlements, MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var result = new List<string>();
            if (!TryCover(entitlements, Entitlements.FullUltimateContents, config, out var parts, out _))
                return result;
            foreach (var p in s_displayOrder)
            {
                if (parts.Contains(p.Id)) result.Add(p.Id);
            }
            return result;
        }

        /// <summary>
        /// Cheapest sellable non-Ultimate products that add the mode and ad rights of <paramref name="target"/> missing from
        /// <paramref name="have"/>. Modes: the missing singles if all are sold and cheaper than All Modes, else All Modes if
        /// sold. Ads: the missing ad removals if sold and not dearer than No Ads, else No Ads if sold. False when a part
        /// cannot be bought.
        /// </summary>
        static bool TryCover(EntitlementSet have, EntitlementSet target, MonetizationConfig config,
            out List<string> ids, out int cost)
        {
            ids = new List<string>();
            cost = 0;

            var missingModes = target.Intersect(EntitlementSet.PaidModes10).Except(have);
            if (!missingModes.IsEmpty)
            {
                int singles = 0;
                var singleIds = new List<string>();
                var covered = EntitlementSet.Empty;
                foreach (var p in s_displayOrder)
                {
                    if (p.Kind == ProductKind.SingleMode && config.IsSellable(p.Id) && !p.Grants.Intersect(missingModes).IsEmpty)
                    {
                        singles += p.BasePriceUsdCents;
                        singleIds.Add(p.Id);
                        covered = covered.Union(p.Grants);
                    }
                }
                bool singlesOk = covered.IsSupersetOf(missingModes);
                bool allModesOk = config.IsSellable(ProductIds.AllModes);
                if (singlesOk && (!allModesOk || singles < ProductCatalog.AllModes.BasePriceUsdCents))
                {
                    ids.AddRange(singleIds);
                    cost += singles;
                }
                else if (allModesOk)
                {
                    ids.Add(ProductIds.AllModes);
                    cost += ProductCatalog.AllModes.BasePriceUsdCents;
                }
                else
                {
                    return false;
                }
            }

            var missingAds = target.Intersect(EntitlementSet.AdFree).Except(have);
            if (!missingAds.IsEmpty)
            {
                int separate = 0;
                var separateIds = new List<string>();
                var covered = EntitlementSet.Empty;
                foreach (var p in new[] { ProductCatalog.RemoveExportAds, ProductCatalog.RemoveInterstitialAds })
                {
                    if (config.IsSellable(p.Id) && !p.Grants.Intersect(missingAds).IsEmpty)
                    {
                        separate += p.BasePriceUsdCents;
                        separateIds.Add(p.Id);
                        covered = covered.Union(p.Grants);
                    }
                }
                bool separateOk = covered.IsSupersetOf(missingAds);
                bool noAdsOk = config.IsSellable(ProductIds.NoAds);
                if (separateOk && (!noAdsOk || separate <= ProductCatalog.NoAds.BasePriceUsdCents))
                {
                    ids.AddRange(separateIds);
                    cost += separate;
                }
                else if (noAdsOk)
                {
                    ids.Add(ProductIds.NoAds);
                    cost += ProductCatalog.NoAds.BasePriceUsdCents;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Whether buying <paramref name="p"/> completes a set the user has already started (spec §5.0.3, §5.2 "последний
        /// шаг"): both ad rights (e.g. Remove Interstitial after Remove Export, which removes the watermark, §5.0.2), or the
        /// full Ultimate contents (e.g. No Ads after All Modes). For the full contents the modes the user already plays
        /// without an entitlement (<paramref name="availableModes"/>) count as had, before and after: with No Ads owned and
        /// M06–M14 free or grandfathered, the M15 single is the §5.2 "последний шаг: все режимы". The set must have been
        /// started by a purchase, though: available modes alone do not start it. A first purchase that completes a set on its
        /// own is not a "last step": No Ads with nothing owned, also when every paid mode is free by config or grandfathered
        /// (the same situation as release 1, where every mode is free and No Ads is shown first, spec §5.0.3).
        /// </summary>
        static bool CompletesStartedSet(EntitlementSet direct, EntitlementSet availableModes, ProductInfo p)
        {
            var after = direct.Union(p.Grants);
            var had = direct.Union(availableModes);
            var full = Entitlements.FullUltimateContents;
            return CompletesStarted(direct, after, EntitlementSet.AdFree)
                || (CompletesStarted(had, after.Union(availableModes), full) && !direct.Intersect(full).IsEmpty);
        }

        static bool CompletesStarted(EntitlementSet before, EntitlementSet after, EntitlementSet set)
        {
            return after.IsSupersetOf(set) && !before.IsSupersetOf(set) && !before.Intersect(set).IsEmpty;
        }

        static EntitlementSet AvailableModeEntitlements(MonetizationConfig config, IEnumerable<ModeCode> grandfathered)
        {
            // Paid modes playable without their entitlement: (free by config or grandfathered) and released.
            var set = EntitlementSet.Empty;
            foreach (var m in config.FreeModes)
            {
                if (config.IsReleased(m) && EntitlementInfo.TryGetModeEntitlement(m, out var e)) set = set.With(e);
            }
            if (grandfathered != null)
            {
                foreach (var m in grandfathered)
                {
                    if (config.IsReleased(m) && EntitlementInfo.TryGetModeEntitlement(m, out var e)) set = set.With(e);
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
