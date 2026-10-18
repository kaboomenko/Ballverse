using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Spec §5.2 "Покупатель не должен платить за то, что у него уже есть" for paid modes playable without an entitlement:
    /// free by config or grandfathered (spec §5.1).
    /// </summary>
    public class StorefrontAvailableModesTests
    {
        static readonly ModeCode[] None = new ModeCode[0];
        static readonly MonetizationConfig M06Free = new MonetizationConfig(new[] { ModeCode.M06 });
        static readonly string[] SinglesM07ToM15 = ProductCatalog.All
            .Where(p => p.Kind == ProductKind.SingleMode && p.Mode != ModeCode.M06).Select(p => p.Id).ToArray();

        static string[] VisibleIds(StorefrontState s)
        {
            return s.VisibleOffers.Select(o => o.ProductId).ToArray();
        }

        [Fact]
        public void ModeFreeByConfig_SingleHiddenAndCountedInAllModes()
        {
            var s = Storefront.Build(new string[0], M06Free);
            var single = s.Get(ProductIds.ModeBallBattle);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, single.HiddenReason);
            Assert.True(single.AlreadyAvailable);
            Assert.Equal(Subsets.Mode(ModeCode.M06), single.Gain);

            var all = s.Get(ProductIds.AllModes);
            Assert.True(all.Visible);
            Assert.Equal(1, all.OwnedModeCount);
            Assert.True(all.PartiallyOwned);
            Assert.True(s.Get(ProductIds.Ultimate).Visible);
            Assert.False(s.Get(ProductIds.ModeLastStanding).AlreadyAvailable);

            Assert.Empty(Storefront.ModeSheet(ModeCode.M06, new string[0], M06Free));
            Assert.Equal(new[] { ProductIds.ModeLastStanding, ProductIds.AllModes },
                Storefront.ModeSheet(ModeCode.M07, new string[0], M06Free).Select(o => o.ProductId).ToArray());
        }

        [Fact]
        public void GrandfatheredMode_SingleHiddenAndModeSheetEmpty()
        {
            var g = new[] { ModeCode.M06 };
            var s = Storefront.Build(new string[0], null, g);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.ModeBallBattle).HiddenReason);
            Assert.Equal(1, s.Get(ProductIds.AllModes).OwnedModeCount);
            Assert.Empty(Storefront.ModeSheet(ModeCode.M06, new string[0], null, g));

            // Grandfathered M01–M05 entries have no product and change nothing.
            var s2 = Storefront.Build(new string[0], null, new[] { ModeCode.M02 });
            Assert.Equal(VisibleIds(Storefront.Build(new string[0])), VisibleIds(s2));
            Assert.Equal(0, s2.Get(ProductIds.AllModes).OwnedModeCount);
        }

        [Fact]
        public void AvailableModeStillOfferedAsLastStepForWatermark()
        {
            var owned = SinglesM07ToM15.Concat(new[] { ProductIds.NoAds }).ToArray();
            var s = Storefront.Build(owned, null, new[] { ModeCode.M06 });
            Assert.Equal(new[] { ProductIds.ModeBallBattle }, VisibleIds(s));
            var single = s.Get(ProductIds.ModeBallBattle);
            Assert.True(single.AlreadyAvailable);
            Assert.True(single.IsLastStep);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.Equal(ProductIds.ModeBallBattle, s.Get(ProductIds.Ultimate).CheaperAlternativeId);
            Assert.Equal(new[] { ProductIds.ModeBallBattle },
                Storefront.WatermarkOffers(owned, null, new[] { ModeCode.M06 }).Select(o => o.ProductId).ToArray());
        }

        [Fact]
        public void WatermarkPathIncludesAvailableModeHiddenInShop()
        {
            var owned = SinglesM07ToM15.Concat(new[] { ProductIds.RemoveExportAds }).ToArray();
            var g = new[] { ModeCode.M06 };
            var s = Storefront.Build(owned, null, g);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);

            var offers = Storefront.WatermarkOffers(owned, null, g);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds, ProductIds.ModeBallBattle }, offers.Select(o => o.ProductId).ToArray());
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, offers[1].HiddenReason);
            Assert.True(offers[1].AlreadyAvailable);
        }

        [Fact]
        public void AllPaidModesFree_AllModesAndSinglesHidden_UltimateStillSellsAdsAndWatermark()
        {
            var config = new MonetizationConfig(Enumerable.Range(6, 10).Select(m => (ModeCode)m));
            var s = Storefront.Build(new string[0], config);
            Assert.Equal(new[] { ProductIds.Ultimate, ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            var all = s.Get(ProductIds.AllModes);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, all.HiddenReason);
            Assert.Equal(10, all.OwnedModeCount);
            Assert.False(all.PartiallyOwned);
        }

        /// <summary>
        /// CheaperEquivalent compares value (gain minus available modes), not gain: with M06–M14 playable, All Modes adds only
        /// M15, exactly what the $1.99 single adds, so the $4.99 bundle is hidden (spec §5.2 "защита от переплаты").
        /// </summary>
        [Fact]
        public void AllModesHiddenWhenItsValueEqualsTheSingle_FreeByConfig()
        {
            var config = new MonetizationConfig(Enumerable.Range(6, 9).Select(m => (ModeCode)m));
            var s = Storefront.Build(new string[0], config);
            var all = s.Get(ProductIds.AllModes);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, all.HiddenReason);
            Assert.Equal(ProductIds.ModeLiquidSand, all.CheaperAlternativeId);
            Assert.Equal(9, all.OwnedModeCount);
            Assert.NotEqual(all.Gain, s.Get(ProductIds.ModeLiquidSand).Gain);
            Assert.True(s.Get(ProductIds.ModeLiquidSand).Visible);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand },
                Storefront.ModeSheet(ModeCode.M15, new string[0], config).Select(o => o.ProductId).ToArray());
        }

        [Fact]
        public void AllModesHiddenWhenItsValueEqualsTheSingle_Grandfathered()
        {
            var g = Enumerable.Range(6, 9).Select(m => (ModeCode)m).ToArray();
            var s = Storefront.Build(new string[0], null, g);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.Equal(ProductIds.ModeLiquidSand, s.Get(ProductIds.AllModes).CheaperAlternativeId);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand },
                Storefront.ModeSheet(ModeCode.M15, new string[0], null, g).Select(o => o.ProductId).ToArray());
        }

        /// <summary>Rng-driven property test over random free sets, grandfathered sets and owned products.</summary>
        [Fact]
        public void InvariantsHoldUnderRandomFreeAndGrandfatheredModes()
        {
            var rng = Rng.FromSeed(0xA11AB1EUL, 11UL);
            for (int iter = 0; iter < 3000; iter++)
            {
                var free = Enumerable.Range(6, 10).Where(_ => rng.NextUInt(4) == 0).Select(m => (ModeCode)m).ToArray();
                var g = Enumerable.Range(1, 15).Where(_ => rng.NextUInt(5) == 0).Select(m => (ModeCode)m).ToArray();
                var config = new MonetizationConfig(free, rng.NextUInt(4) != 0);
                var owned = Subsets.Ids((int)rng.NextUInt((uint)Subsets.Count));
                var available = EntitlementSet.Of(free.Concat(g).Where(m => (int)m >= 6).Select(m => (Entitlement)(int)m));

                var s = Storefront.Build(owned, config, g);
                foreach (var o in s.Offers)
                {
                    bool expectFlag = o.Product.Kind == ProductKind.SingleMode && available.ContainsMode(o.Product.Mode.Value)
                        && !s.Entitlements.ContainsMode(o.Product.Mode.Value);
                    Assert.Equal(expectFlag, o.AlreadyAvailable);
                    if (!o.Visible) continue;
                    Assert.False(o.Gain.Except(available).IsEmpty);
                    if (o.AlreadyAvailable) Assert.True(o.IsLastStep);
                }
                // Value-based equivalence: a hidden CheaperEquivalent offer points at a visible, not dearer product with the
                // same value, and no visible offer has a strictly cheaper non-owned product with the same value.
                foreach (var o in s.Offers)
                {
                    var value = o.Gain.Except(available);
                    if (o.HiddenReason == OfferHiddenReason.CheaperEquivalent)
                    {
                        var alt = s.Get(o.CheaperAlternativeId);
                        Assert.True(alt.Visible);
                        Assert.True(alt.Product.BasePriceUsdCents <= o.Product.BasePriceUsdCents);
                        Assert.Equal(value, alt.Gain.Except(available));
                    }
                    if (!o.Visible) continue;
                    foreach (var other in s.Offers)
                    {
                        if (other.Owned || other.Product.BasePriceUsdCents >= o.Product.BasePriceUsdCents) continue;
                        Assert.NotEqual(value, other.Gain.Except(available));
                    }
                }
                Assert.Equal(s.Entitlements.Union(available).ModeCount, s.Get(ProductIds.AllModes).OwnedModeCount);
                if (!s.AllPurchased) Assert.NotEmpty(s.VisibleOffers);

                for (int m = 6; m <= 15; m++)
                {
                    if (ModeAvailability.IsAvailable((ModeCode)m, s.Entitlements, g, config))
                        Assert.Empty(Storefront.ModeSheet((ModeCode)m, owned, config, g));
                }

                var wm = Storefront.WatermarkOffers(owned, config, g);
                if (s.Entitlements.Contains(Entitlement.NoWatermark)) Assert.Empty(wm);
                else Assert.Contains(Entitlement.NoWatermark,
                    Entitlements.From(owned.Concat(wm.Select(o => o.ProductId)), config).ToArray());

                // Buying every visible offer, repeatedly, ends with everything not already available, plus NO_WATERMARK.
                var have = new List<string>(owned);
                for (int round = 0; round < 5; round++)
                {
                    var visible = Storefront.Build(have, config, g).VisibleOffers;
                    if (visible.Count == 0) break;
                    have.AddRange(visible.Select(o => o.ProductId));
                }
                var final = Storefront.Build(have, config, g);
                Assert.Empty(final.VisibleOffers);
                Assert.True(final.Entitlements.Union(available).IsSupersetOf(EntitlementSet.AllDefined));
                Assert.True(final.Entitlements.Contains(Entitlement.NoWatermark));
            }
        }
    }
}
