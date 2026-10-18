using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// "Only sellable products are ever offered" for partial releases (between <see cref="MonetizationConfig.Release1"/> and
    /// <see cref="MonetizationConfig.FullCatalog"/>), whose code paths the two presets never reach (owner decisions; spec §5.2).
    /// </summary>
    public class PartialReleaseSellabilityTests
    {
        static readonly string[] None = new string[0];
        static readonly ModeCode[] NoModes = new ModeCode[0];
        static readonly ModeCode[] AllModeCodes = Enumerable.Range(1, 15).Select(m => (ModeCode)m).ToArray();

        static string[] Ids(IEnumerable<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        /// <summary>A released paid mode whose single is not sold: the sheet offers only All Modes.</summary>
        [Fact]
        public void ModeSheet_LeavesOutTheUnsoldSingle()
        {
            var c = new MonetizationConfig(NoModes, true, ProductIds.All.Where(id => id != ProductIds.ModeOrbit), AllModeCodes, false);
            var sheet = Storefront.ModeSheet(ModeCode.M14, None, c, null, false);
            Assert.Equal(new[] { ProductIds.AllModes }, Ids(sheet));
            Assert.True(sheet[0].Emphasized);
            Assert.Equal(OfferHiddenReason.NotSellable, Storefront.Build(None, c, null, false).Get(ProductIds.ModeOrbit).HiddenReason);
            // Only M14 missing: All Modes would be the cheaper-equivalent victim of the single, but the single is not sold.
            var owned = ProductIds.All.Where(id => ProductCatalog.Get(id).Kind == ProductKind.SingleMode && id != ProductIds.ModeOrbit).ToArray();
            Assert.Equal(new[] { ProductIds.AllModes }, Ids(Storefront.ModeSheet(ModeCode.M14, owned, c, null, false)));
        }

        /// <summary>
        /// A released, non-free mode with no sellable product (a misconfigured release, accepted so a broken free_modes payload
        /// never makes the config unconstructible): the card is locked and the sheet is empty, never an unsold product.
        /// </summary>
        [Fact]
        public void ModeSheet_EmptyForReleasedUnsoldMode()
        {
            var released = new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05, ModeCode.M14 };
            var c = new MonetizationConfig(NoModes, true, new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds },
                released, true);
            Assert.Equal(ModeAccess.Locked, ModeAvailability.GetAccess(ModeCode.M14, EntitlementSet.Empty, null, c));
            Assert.False(c.IsModeSold(ModeCode.M14));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M14, None, c, null, false));
            Assert.Equal(TrialPreviewAccess.NotSold, SalePointPolicy.CanStartTrialPreview(ModeCode.M14, EntitlementSet.Empty, null, 0, c));
        }

        /// <summary>No Ads not sold, rule on: the watermark row falls back to both ad removals (spec §5.2 "недостающая часть").</summary>
        [Fact]
        public void WatermarkOffers_AdRemovalsFallbackWhenNoAdsNotSold()
        {
            var c = new MonetizationConfig(NoModes, true, new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds },
                new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, true);
            var expected = new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds };
            Assert.Equal(expected, Ids(Storefront.WatermarkOffers(None, c, null, false)));
            Assert.Equal(expected, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, c, null, false)));
            Assert.Equal(expected, Ids(Storefront.Build(None, c, null, false).VisibleOffers));
            // After one removal the other one is the last step and removes the watermark on its own.
            var last = Storefront.WatermarkOffers(new[] { ProductIds.RemoveExportAds }, c, null, false);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, Ids(last));
            Assert.True(last[0].IsLastStep);
        }

        /// <summary>Only No Ads sold: a missing ad right is covered by No Ads, never by an unsold ad removal.</summary>
        [Fact]
        public void CheapestParts_UseNoAdsWhenRemovalsNotSold()
        {
            var c = new MonetizationConfig(NoModes, true, new[] { ProductIds.NoAds }, AllModeCodes, false);
            Assert.Equal(new[] { ProductIds.NoAds },
                Storefront.CheapestPartsToComplete(EntitlementSet.PaidModes10.With(Entitlement.NoExportAds), c).ToArray());
            Assert.Equal(new[] { ProductIds.NoAds },
                Storefront.CheapestPartsToComplete(EntitlementSet.PaidModes10.With(Entitlement.NoInterstitials), c).ToArray());
            // Modes missing and no mode product sold: cannot be completed.
            Assert.Empty(Storefront.CheapestPartsToComplete(EntitlementSet.Empty, c));

            // Same through the watermark row for a grandfathered user who owns one ad removal (No Ads rule applies to them).
            var g = new MonetizationConfig(NoModes, false, new[] { ProductIds.NoAds }, AllModeCodes, false);
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(Storefront.WatermarkOffers(new[] { ProductIds.RemoveExportAds }, g, null, true)));
        }

        /// <summary>Only the singles sold (no All Modes, no Ultimate): the parts are the missing singles and an ad product.</summary>
        [Fact]
        public void CheapestParts_UseSinglesWhenAllModesNotSold()
        {
            var sellable = ProductIds.All.Where(id => id != ProductIds.AllModes && id != ProductIds.Ultimate);
            var c = new MonetizationConfig(NoModes, true, sellable, AllModeCodes, false);
            var parts = Storefront.CheapestPartsToComplete(EntitlementSet.AdFree, c);
            Assert.Equal(10, parts.Count);
            Assert.All(parts, id => Assert.Equal(ProductKind.SingleMode, ProductCatalog.Get(id).Kind));
        }

        // ---- Seeded random-config invariants ----

        /// <param name="biased">
        /// Favours the states behind the QA round-2 stuck Shop: All Modes often unsold, many modes free or grandfathered.
        /// </param>
        static MonetizationConfig RandomConfig(ref Rng rng, bool biased, out ModeCode[] grandfathered)
        {
            uint availableOdds = biased ? 4u : 10u;
            var released = new List<ModeCode> { ModeCode.M01, ModeCode.M02, ModeCode.M05 };
            bool allReleased = rng.NextUInt(3) == 0;
            for (int m = 3; m <= 15; m++)
            {
                if (m != 5 && (allReleased || rng.NextUInt(2) == 0)) released.Add((ModeCode)m);
            }
            var sellable = new List<string>();
            foreach (var id in ProductIds.All)
            {
                bool ok = true;
                foreach (var e in ProductCatalog.Get(id).Grants.ToArray())
                {
                    if (EntitlementInfo.TryGetMode(e, out var mode) && !released.Contains(mode)) ok = false;
                }
                if (ok && rng.NextUInt(4) != 0 && !(biased && id == ProductIds.AllModes && rng.NextBool())) sellable.Add(id);
            }
            var free = new List<ModeCode>();
            var g = new List<ModeCode>();
            for (int m = 6; m <= 15; m++)
            {
                if (rng.NextUInt(availableOdds) == 0) free.Add((ModeCode)m);
                if (rng.NextUInt(availableOdds) == 0) g.Add((ModeCode)m);
            }
            grandfathered = g.ToArray();
            return new MonetizationConfig(free, rng.NextBool(), sellable, released, rng.NextBool());
        }

        static EntitlementSet AvailableModes(MonetizationConfig c, ModeCode[] grandfathered)
        {
            var set = EntitlementSet.Empty;
            for (int m = 6; m <= 15; m++)
            {
                var mode = (ModeCode)m;
                if (c.IsReleased(mode) && (c.IsFree(mode) || grandfathered.Contains(mode))) set = set.Union(Subsets.Mode(mode));
            }
            return set;
        }

        [Fact]
        public void RandomPartialReleases_OnlySellableOffers_EverythingReachable()
        {
            CheckRandomPartialReleases(0x5E11AB1EUL, 7UL, 300, 20, false);
        }

        /// <summary>
        /// QA round 2: the generator biased toward two or more available modes with All Modes unsold, where an empty Shop
        /// without "Всё куплено" used to appear (the unbiased run needed 60000 samples to hit it).
        /// </summary>
        [Fact]
        public void RandomPartialReleases_ManyAvailableModes_EverythingReachable()
        {
            CheckRandomPartialReleases(0xC0FFEE1234UL, 99UL, 200, 15, true);
        }

        static void CheckRandomPartialReleases(ulong seed, ulong stream, int configs, int samples, bool biased)
        {
            var rng = Rng.FromSeed(seed, stream);
            for (int ci = 0; ci < configs; ci++)
            {
                var c = RandomConfig(ref rng, biased, out var g);
                var available = AvailableModes(c, g);
                for (int k = 0; k < samples; k++)
                {
                    int mask = (int)rng.NextUInt((uint)Subsets.Count);
                    bool gf = rng.NextBool();
                    var owned = Subsets.Ids(mask);
                    var direct = Entitlements.DirectGrants(owned);
                    var s = Storefront.Build(owned, c, g, gf);
                    string ctx = "config " + ci + " sample " + k + " sell=" + string.Join(",", c.SellableProductIds) + " rel=" + string.Join(",", c.ReleasedModes)
                        + " free=" + string.Join(",", c.FreeModes) + " g=" + string.Join(",", g) + " gf=" + gf + " owned=" + string.Join(",", owned)
                        + " byParts=" + c.WatermarkByParts + " rule=" + c.WatermarkWithNoAds;

                    foreach (var o in s.VisibleOffers)
                    {
                        Assert.True(c.IsSellable(o.ProductId), ctx);
                        Assert.False(o.Owned, ctx);
                        Assert.False(o.Gain.IsEmpty, ctx);
                    }
                    Assert.True(s.AllPurchased == (s.VisibleOffers.Count == 0), ctx);

                    // Repeatedly buying every visible offer reaches every obtainable right except available modes, and
                    // NO_WATERMARK whenever a sellable path to it exists.
                    var obtainable = Entitlements.ApplyRules(direct.Union(c.SellableGrants), c, gf);
                    var bought = new List<string>(owned);
                    for (int round = 0; round < 6; round++)
                    {
                        var visible = Storefront.Build(bought, c, g, gf).VisibleOffers;
                        if (visible.Count == 0) break;
                        bought.AddRange(visible.Select(o => o.ProductId));
                    }
                    var final = Entitlements.From(bought, c, gf);
                    Assert.True(final.IsSupersetOf(obtainable.Except(available)), ctx);
                    Assert.True(!obtainable.Contains(Entitlement.NoWatermark) || final.Contains(Entitlement.NoWatermark), ctx);

                    // Watermark row: sellable only; non-empty exactly when a sellable path exists; buying it all removes it.
                    bool has = s.Entitlements.Contains(Entitlement.NoWatermark);
                    bool reachable = !has && obtainable.Contains(Entitlement.NoWatermark);
                    var wm = Storefront.WatermarkOffers(owned, c, g, gf);
                    Assert.All(wm, o => Assert.True(c.IsSellable(o.ProductId), ctx));
                    Assert.True(reachable == (wm.Count > 0), ctx);
                    if (reachable)
                        Assert.True(Entitlements.From(owned.Concat(Ids(wm)), c, gf).Contains(Entitlement.NoWatermark), ctx);

                    // Done-screen line: the same offers, standalone removers first and cheapest first; the first one alone
                    // removes the watermark when it is a standalone remover.
                    var line = SalePointPolicy.DoneScreenLineOffers(3, null, owned, c, g, gf);
                    Assert.Equal(Ids(wm).OrderBy(id => id), Ids(line).OrderBy(id => id));
                    for (int i = 1; i < line.Count; i++)
                    {
                        Assert.False(!line[i - 1].RemovesWatermark && line[i].RemovesWatermark, ctx);
                        if (line[i - 1].RemovesWatermark && line[i].RemovesWatermark)
                            Assert.True(line[i - 1].Product.BasePriceUsdCents <= line[i].Product.BasePriceUsdCents, ctx);
                    }
                    if (line.Count > 0 && line[0].RemovesWatermark)
                        Assert.True(Entitlements.From(owned.Concat(new[] { line[0].ProductId }), c, gf).Contains(Entitlement.NoWatermark), ctx);

                    // Mode sheet: sellable only; non-empty exactly for a locked mode whose single or All Modes is sold (the
                    // sheet never offers Ultimate, spec §5.2 "этот режим + All Modes").
                    for (int m = 6; m <= 15; m++)
                    {
                        var mode = (ModeCode)m;
                        var sheet = Storefront.ModeSheet(mode, owned, c, g, gf);
                        Assert.All(sheet, o => Assert.True(c.IsSellable(o.ProductId) && o.Visible, ctx));
                        bool locked = ModeAvailability.GetAccess(mode, s.Entitlements, g, c) == ModeAccess.Locked;
                        bool sheetProductSold = c.IsSellable(ProductCatalog.ForMode(mode).Id) || c.IsSellable(ProductIds.AllModes);
                        Assert.True((locked && sheetProductSold) == (sheet.Count > 0), ctx + " M" + m);
                    }
                }
            }
        }
    }
}
