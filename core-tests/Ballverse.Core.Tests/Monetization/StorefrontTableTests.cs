using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// One test per row of the spec §5.2 "Умная витрина" table (full catalog), plus neighbouring cases; NO_WATERMARK by the
    /// permanent rule of spec §5.0.2 (Ultimate, or both ad rights).
    /// </summary>
    public class StorefrontTableTests
    {
        static readonly string[] SingleIds = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode).Select(p => p.Id).ToArray();

        static string[] VisibleIds(StorefrontState s)
        {
            return s.VisibleOffers.Select(o => o.ProductId).ToArray();
        }

        [Fact]
        public void DisplayOrderIsUltimateAllModesNoAdsRemoveExportRemoveInterstitialThenModes()
        {
            var expected = new[] { ProductIds.Ultimate, ProductIds.AllModes, ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }
                .Concat(SingleIds).ToArray();
            Assert.Equal(expected, Storefront.DisplayOrder.Select(p => p.Id).ToArray());
            Assert.Equal(expected, Storefront.Build(new string[0], MonetizationConfig.FullCatalog, null).Offers.Select(o => o.ProductId).ToArray());
        }

        /// <summary>Row "ничего": Ultimate (+ без водяного знака), All Modes, No Ads, singles.</summary>
        [Fact]
        public void Row1_Nothing()
        {
            var s = Storefront.Build(new string[0], MonetizationConfig.FullCatalog, null);
            Assert.Equal(Storefront.DisplayOrder.Select(p => p.Id).ToArray(), VisibleIds(s));
            var ultimate = s.Get(ProductIds.Ultimate);
            Assert.True(ultimate.RemovesWatermark);
            Assert.False(ultimate.IsLastStep);
            Assert.Equal(EntitlementSet.AllDefined, ultimate.Gain);
            Assert.False(s.AllPurchased);
            Assert.All(s.Offers, o => Assert.False(o.IsLastStep));
            Assert.All(s.Offers, o => Assert.False(o.Owned));
            Assert.False(s.Get(ProductIds.AllModes).PartiallyOwned);
            Assert.Equal(0, s.Get(ProductIds.AllModes).OwnedModeCount);
        }

        /// <summary>Row "1+ отдельных режимов": All Modes with "У вас уже N режимов", price unchanged.</summary>
        [Fact]
        public void Row2_SomeSingleModes()
        {
            var s = Storefront.Build(new[] { ProductIds.ModeBallBattle, ProductIds.ModeOrbit }, MonetizationConfig.FullCatalog, null);
            var all = s.Get(ProductIds.AllModes);
            Assert.True(all.Visible);
            Assert.True(all.PartiallyOwned);
            Assert.Equal(2, all.OwnedModeCount);
            Assert.Equal(499, all.Product.BasePriceUsdCents);
            Assert.Equal(8, all.Gain.ModeCount);

            var owned = s.Get(ProductIds.ModeBallBattle);
            Assert.False(owned.Visible);
            Assert.True(owned.Owned);
            Assert.Equal(OfferHiddenReason.Owned, owned.HiddenReason);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.ModeOrbit).HiddenReason);

            Assert.True(s.Get(ProductIds.Ultimate).Visible);
            Assert.True(s.Get(ProductIds.NoAds).Visible);
            Assert.True(s.Get(ProductIds.RemoveExportAds).Visible);
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).Visible);
            Assert.True(s.Get(ProductIds.ModeColorWar).Visible);
        }

        [Fact]
        public void Row2_NineSingleModes_AllModesHiddenBecauseLastSingleIsCheaperAndSame()
        {
            var owned = SingleIds.Take(9).ToArray();
            var s = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
            var all = s.Get(ProductIds.AllModes);
            Assert.False(all.Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, all.HiddenReason);
            Assert.Equal(ProductIds.ModeLiquidSand, all.CheaperAlternativeId);
            Assert.Equal(9, all.OwnedModeCount);
            Assert.True(s.Get(ProductIds.ModeLiquidSand).Visible);
        }

        /// <summary>
        /// 9 singles, no ads: Ultimate ($11.99) is dearer than the missing part (M15 $1.99 + No Ads $8.99 = $10.98), which
        /// gives the same, NO_WATERMARK included through both ad rights (§5.0.2, §5.2 "он дороже, чем недостающая часть").
        /// </summary>
        [Fact]
        public void Row2_NineSingleModes_UltimateHiddenBecauseMissingPartIsCheaper()
        {
            var owned = SingleIds.Take(9).ToArray();
            var s = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.ModeLiquidSand },
                VisibleIds(s));
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.ModeLiquidSand }, Storefront.CheapestPartsToComplete(s.Entitlements, MonetizationConfig.FullCatalog).ToArray());
            // The export sheet's watermark row opens No Ads (both ad rights remove the watermark), not Ultimate.
            Assert.Equal(new[] { ProductIds.NoAds },
                Storefront.WatermarkOffers(owned, MonetizationConfig.FullCatalog, null).Select(o => o.ProductId).ToArray());
            Assert.False(s.Get(ProductIds.ModeLiquidSand).RemovesWatermark);
        }

        /// <summary>8 singles: the missing part costs 2 × $1.99 + $8.99 = $12.97 &gt; $11.99, so Ultimate stays; All Modes too (row 2).</summary>
        [Fact]
        public void Row2_EightSingleModes_UltimateAndAllModesVisible()
        {
            var s = Storefront.Build(SingleIds.Take(8).ToArray(), MonetizationConfig.FullCatalog, null);
            Assert.True(s.Get(ProductIds.Ultimate).Visible);
            Assert.True(s.Get(ProductIds.AllModes).Visible);
            Assert.Equal(8, s.Get(ProductIds.AllModes).OwnedModeCount);
        }

        /// <summary>Row "All Modes": No Ads as last step; ad singles; hides Ultimate and single modes.</summary>
        [Fact]
        public void Row3_AllModes()
        {
            var s = Storefront.Build(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));

            var noAds = s.Get(ProductIds.NoAds);
            Assert.True(noAds.IsLastStep);
            Assert.True(noAds.RemovesWatermark);
            Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark), noAds.Gain);

            var ultimate = s.Get(ProductIds.Ultimate);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, ultimate.HiddenReason);
            Assert.Equal(ProductIds.NoAds, ultimate.CheaperAlternativeId);

            foreach (var id in SingleIds) Assert.Equal(OfferHiddenReason.NothingNew, s.Get(id).HiddenReason);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.False(s.Get(ProductIds.RemoveExportAds).IsLastStep);
        }

        /// <summary>Row "No Ads": All Modes as last step ("все режимы", the watermark already gone); hides Ultimate and Remove *.</summary>
        [Fact]
        public void Row4_NoAds()
        {
            var s = Storefront.Build(new[] { ProductIds.NoAds }, MonetizationConfig.FullCatalog, null);
            Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark));
            var all = s.Get(ProductIds.AllModes);
            Assert.True(all.Visible);
            Assert.True(all.IsLastStep);
            Assert.False(all.RemovesWatermark);
            Assert.Empty(Storefront.WatermarkOffers(new[] { ProductIds.NoAds }, MonetizationConfig.FullCatalog, null));

            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Equal(ProductIds.AllModes, s.Get(ProductIds.Ultimate).CheaperAlternativeId);
            Assert.Equal(OfferHiddenReason.NothingNew, s.Get(ProductIds.RemoveExportAds).HiddenReason);
            Assert.Equal(OfferHiddenReason.NothingNew, s.Get(ProductIds.RemoveInterstitialAds).HiddenReason);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.NoAds).HiddenReason);
            // Single modes still grant something new and stay visible (not last steps: 9 modes would remain).
            foreach (var id in SingleIds)
            {
                Assert.True(s.Get(id).Visible);
                Assert.False(s.Get(id).IsLastStep);
            }
        }

        /// <summary>Row "Remove Export Ads": Remove Interstitial; hides No Ads; Ultimate shown only without modes.</summary>
        [Fact]
        public void Row5_RemoveExportAds()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.FullCatalog, null);
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.NoAds).HiddenReason);
            Assert.Equal(ProductIds.RemoveInterstitialAds, s.Get(ProductIds.NoAds).CheaperAlternativeId);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.RemoveExportAds).HiddenReason);
            // "Ultimate показывается только если нет режимов", although All Modes + Remove Interstitial = $10.98.
            Assert.True(s.Get(ProductIds.Ultimate).Visible);
            Assert.Equal(1098, Storefront.CheapestPartsToComplete(s.Entitlements, MonetizationConfig.FullCatalog).Sum(id => ProductCatalog.Get(id).BasePriceUsdCents));
            Assert.True(s.Get(ProductIds.AllModes).Visible);
            // Watermark row: Ultimate and the missing ad removal both remove it (spec §5.0.2), in display order.
            Assert.Equal(new[] { ProductIds.Ultimate, ProductIds.RemoveInterstitialAds },
                Storefront.WatermarkOffers(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.FullCatalog, null).Select(o => o.ProductId).ToArray());
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).RemovesWatermark);
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
        }

        [Fact]
        public void Row5_RemoveExportAdsWithNineModes_HidesUltimate_WatermarkRowOpensMissingAdRemoval()
        {
            var owned = SingleIds.Take(9).Concat(new[] { ProductIds.RemoveExportAds }).ToArray();
            var s = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds },
                Storefront.WatermarkOffers(owned, MonetizationConfig.FullCatalog, null).Select(o => o.ProductId).ToArray());
        }

        [Fact]
        public void Row5_RemoveExportAdsWithAMode_HidesUltimate()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds, ProductIds.ModeSmash }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.True(s.Get(ProductIds.AllModes).Visible);
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).Visible);
            Assert.False(s.Get(ProductIds.NoAds).Visible);
        }

        [Fact]
        public void Row5_RemoveExportAdsWithAllModes_RemoveInterstitialIsLastStep()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds, ProductIds.AllModes }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.Ultimate).HiddenReason);
        }

        /// <summary>Row "Remove Interstitial": Remove Export; hides No Ads; Ultimate shown only without modes.</summary>
        [Fact]
        public void Row6_RemoveInterstitial()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds }, MonetizationConfig.FullCatalog, null);
            Assert.True(s.Get(ProductIds.RemoveExportAds).Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.NoAds).HiddenReason);
            Assert.Equal(ProductIds.RemoveExportAds, s.Get(ProductIds.NoAds).CheaperAlternativeId);
            Assert.True(s.Get(ProductIds.Ultimate).Visible); // no modes: shown although All Modes + Remove Export = $11.98

            var withMode = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds, ProductIds.ModeOrbit }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(OfferHiddenReason.CheaperViaParts, withMode.Get(ProductIds.Ultimate).HiddenReason);

            var withAll = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds, ProductIds.AllModes }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.RemoveExportAds }, VisibleIds(withAll));
            Assert.True(withAll.Get(ProductIds.RemoveExportAds).IsLastStep);
        }

        /// <summary>Row "Ultimate или All Modes + No Ads": "Всё куплено", everything hidden.</summary>
        [Fact]
        public void Row7_UltimateOrAllModesPlusNoAds_AllPurchased()
        {
            foreach (var owned in new[]
            {
                new[] { ProductIds.Ultimate },
                new[] { ProductIds.AllModes, ProductIds.NoAds },
                SingleIds.Concat(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }).ToArray(),
            })
            {
                var s = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
                Assert.True(s.AllPurchased);
                Assert.Empty(s.VisibleOffers);
                Assert.Equal(EntitlementSet.AllDefined, s.Entitlements);
            }
        }

        [Fact]
        public void CheapestPartsToComplete_Examples()
        {
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds }, Storefront.CheapestPartsToComplete(EntitlementSet.Empty, MonetizationConfig.FullCatalog).ToArray());
            Assert.Empty(Storefront.CheapestPartsToComplete(EntitlementSet.AllDefined, MonetizationConfig.FullCatalog));
            Assert.Equal(new[] { ProductIds.RemoveExportAds, ProductIds.ModeOrbit, ProductIds.ModeLiquidSand },
                Storefront.CheapestPartsToComplete(Entitlements.From(SingleIds.Take(8).Concat(new[] { ProductIds.RemoveInterstitialAds }), MonetizationConfig.FullCatalog), MonetizationConfig.FullCatalog).ToArray());
        }

        [Fact]
        public void DisplayOrderIsReadOnly()
        {
            Assert.False(Storefront.DisplayOrder is ProductInfo[]);
            Assert.False(Storefront.Build(new string[0], MonetizationConfig.FullCatalog, null).Offers is StorefrontOffer[]);
            Assert.False(Storefront.Build(new string[0], MonetizationConfig.FullCatalog, null).VisibleOffers is StorefrontOffer[]);
        }

        [Fact]
        public void UnknownAndDuplicateIdsAreIgnored()
        {
            var s = Storefront.Build(new[] { "x", null, ProductIds.AllModes, ProductIds.AllModes }, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.Null(s.Get("x"));
            Assert.Throws<System.ArgumentNullException>(() => Storefront.Build(null, MonetizationConfig.FullCatalog, null));
        }

        [Fact]
        public void NineModesAndNoAds_LastSingleIsLastStep()
        {
            var owned = SingleIds.Take(9).Concat(new[] { ProductIds.NoAds }).ToArray();
            var s = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand }, VisibleIds(s));
            Assert.True(s.Get(ProductIds.ModeLiquidSand).IsLastStep);
        }
    }
}
