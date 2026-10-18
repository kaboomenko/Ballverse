using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>One test per row of the spec §5.2 "Умная витрина" table, plus neighbouring cases.</summary>
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
            Assert.Equal(expected, Storefront.Build(new string[0]).Offers.Select(o => o.ProductId).ToArray());
        }

        /// <summary>Row "ничего": Ultimate (+ без водяного знака), All Modes, No Ads, singles.</summary>
        [Fact]
        public void Row1_Nothing()
        {
            var s = Storefront.Build(new string[0]);
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
            var s = Storefront.Build(new[] { ProductIds.ModeBallBattle, ProductIds.ModeOrbit });
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
            var s = Storefront.Build(owned);
            var all = s.Get(ProductIds.AllModes);
            Assert.False(all.Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, all.HiddenReason);
            Assert.Equal(ProductIds.ModeLiquidSand, all.CheaperAlternativeId);
            Assert.Equal(9, all.OwnedModeCount);
            Assert.True(s.Get(ProductIds.ModeLiquidSand).Visible);
        }

        /// <summary>Row "All Modes": No Ads as last step; ad singles; hides Ultimate and single modes.</summary>
        [Fact]
        public void Row3_AllModes()
        {
            var s = Storefront.Build(new[] { ProductIds.AllModes });
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

        /// <summary>Row "No Ads": All Modes as last step; hides Ultimate and Remove *.</summary>
        [Fact]
        public void Row4_NoAds()
        {
            var s = Storefront.Build(new[] { ProductIds.NoAds });
            var all = s.Get(ProductIds.AllModes);
            Assert.True(all.Visible);
            Assert.True(all.IsLastStep);
            Assert.True(all.RemovesWatermark);

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
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds });
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.NoAds).HiddenReason);
            Assert.Equal(ProductIds.RemoveInterstitialAds, s.Get(ProductIds.NoAds).CheaperAlternativeId);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.RemoveExportAds).HiddenReason);
            Assert.True(s.Get(ProductIds.Ultimate).Visible);
            Assert.True(s.Get(ProductIds.AllModes).Visible);
        }

        [Fact]
        public void Row5_RemoveExportAdsWithAMode_HidesUltimate()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds, ProductIds.ModeSmash });
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.True(s.Get(ProductIds.AllModes).Visible);
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).Visible);
            Assert.False(s.Get(ProductIds.NoAds).Visible);
        }

        [Fact]
        public void Row5_RemoveExportAdsWithAllModes_RemoveInterstitialIsLastStep()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds, ProductIds.AllModes });
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.True(s.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.Ultimate).HiddenReason);
        }

        /// <summary>Row "Remove Interstitial": Remove Export; hides No Ads; Ultimate shown only without modes.</summary>
        [Fact]
        public void Row6_RemoveInterstitial()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds });
            Assert.True(s.Get(ProductIds.RemoveExportAds).Visible);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.NoAds).HiddenReason);
            Assert.Equal(ProductIds.RemoveExportAds, s.Get(ProductIds.NoAds).CheaperAlternativeId);
            Assert.True(s.Get(ProductIds.Ultimate).Visible);

            var withMode = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds, ProductIds.ModeOrbit });
            Assert.Equal(OfferHiddenReason.CheaperViaParts, withMode.Get(ProductIds.Ultimate).HiddenReason);

            var withAll = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds, ProductIds.AllModes });
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
                var s = Storefront.Build(owned);
                Assert.True(s.AllPurchased);
                Assert.Empty(s.VisibleOffers);
                Assert.Equal(EntitlementSet.AllDefined, s.Entitlements);
            }
        }

        [Fact]
        public void AllModesPlusNoAdsWithoutByPartsRule_StillOffersUltimateForWatermark()
        {
            var config = new MonetizationConfig(MonetizationConfig.Default.FreeModes, watermarkByParts: false);
            var s = Storefront.Build(new[] { ProductIds.AllModes, ProductIds.NoAds }, config);
            Assert.False(s.AllPurchased);
            Assert.Equal(new[] { ProductIds.Ultimate }, VisibleIds(s));
            Assert.Equal(EntitlementSet.Of(Entitlement.NoWatermark), s.Get(ProductIds.Ultimate).Gain);
            // Without the rule, No Ads is no longer a watermark-removing last step after All Modes.
            var allModes = Storefront.Build(new[] { ProductIds.AllModes }, config);
            Assert.False(allModes.Get(ProductIds.NoAds).IsLastStep);
            Assert.True(allModes.Get(ProductIds.Ultimate).Visible);
        }

        [Fact]
        public void UnknownAndDuplicateIdsAreIgnored()
        {
            var s = Storefront.Build(new[] { "x", null, ProductIds.AllModes, ProductIds.AllModes });
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.Null(s.Get("x"));
            Assert.Throws<System.ArgumentNullException>(() => Storefront.Build(null));
        }

        [Fact]
        public void NineModesAndNoAds_LastSingleIsLastStep()
        {
            var owned = SingleIds.Take(9).Concat(new[] { ProductIds.NoAds }).ToArray();
            var s = Storefront.Build(owned);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand }, VisibleIds(s));
            Assert.True(s.Get(ProductIds.ModeLiquidSand).IsLastStep);
        }
    }
}
