using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.2 "Тап по платной карточке" and the watermark row of the export sheet (§3.10).</summary>
    public class ModeSheetTests
    {
        static string[] Ids(System.Collections.Generic.IReadOnlyList<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        [Fact]
        public void NothingOwned_SingleThenEmphasizedAllModes()
        {
            var offers = Storefront.ModeSheet(ModeCode.M08, new string[0], MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ProductIds.ModeColorWar, ProductIds.AllModes }, Ids(offers));
            Assert.False(offers[0].Emphasized);
            Assert.True(offers[1].Emphasized);
            Assert.Equal(199, offers[0].Product.BasePriceUsdCents);
            Assert.Equal(499, offers[1].Product.BasePriceUsdCents);
        }

        [Fact]
        public void ModeAlreadyOwned_NothingOffered()
        {
            Assert.Empty(Storefront.ModeSheet(ModeCode.M08, new[] { ProductIds.ModeColorWar }, MonetizationConfig.FullCatalog));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M08, new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M08, new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void FreeModeHasNoProduct()
        {
            Assert.Empty(Storefront.ModeSheet(ModeCode.M01, new string[0], MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void OtherModesOwned_AllModesShowsPartialCount()
        {
            var offers = Storefront.ModeSheet(ModeCode.M15, new[] { ProductIds.ModeOrbit, ProductIds.ModeSmash }, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand, ProductIds.AllModes }, Ids(offers));
            Assert.Equal(2, offers[1].OwnedModeCount);
            Assert.True(offers[1].PartiallyOwned);
        }

        [Fact]
        public void OnlyThisModeMissing_AllModesLeftOut()
        {
            var owned = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode && p.Mode != ModeCode.M15).Select(p => p.Id).ToArray();
            var offers = Storefront.ModeSheet(ModeCode.M15, owned, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ProductIds.ModeLiquidSand }, Ids(offers));
        }

        [Fact]
        public void ModeSheetNeverOffersSomethingUseless_AllSubsetsAllModes()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 7)
            {
                var owned = Subsets.Ids(mask);
                var e = Entitlements.From(owned, MonetizationConfig.FullCatalog);
                for (int m = 1; m <= 15; m++)
                {
                    var mode = (ModeCode)m;
                    var offers = Storefront.ModeSheet(mode, owned, MonetizationConfig.FullCatalog);
                    if (m <= 5 || e.ContainsMode(mode))
                    {
                        Assert.Empty(offers);
                        continue;
                    }
                    Assert.Equal(ProductCatalog.ForMode(mode).Id, offers[0].ProductId);
                    Assert.All(offers, o => Assert.True(o.Visible && o.Gain.ContainsMode(mode)));
                    Assert.Equal(e.ModeCount < 9, offers.Count == 2);
                }
            }
        }

        [Fact]
        public void WatermarkOffers_NothingOwned_Ultimate()
        {
            Assert.Equal(new[] { ProductIds.Ultimate }, Ids(Storefront.WatermarkOffers(new string[0], MonetizationConfig.FullCatalog)));
        }

        [Fact]
        public void WatermarkOffers_AllModes_NoAdsLastStep()
        {
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(Storefront.WatermarkOffers(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog)));
        }

        [Fact]
        public void WatermarkOffers_PartsPathWhenUltimateHidden()
        {
            var offers = Storefront.WatermarkOffers(new[] { ProductIds.RemoveExportAds, ProductIds.ModeSmash }, MonetizationConfig.FullCatalog);
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.RemoveInterstitialAds }, Ids(offers));
        }

        [Fact]
        public void WatermarkOffers_OwnedNoWatermark_Empty()
        {
            Assert.Empty(Storefront.WatermarkOffers(new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog));
            Assert.Empty(Storefront.WatermarkOffers(new[] { ProductIds.AllModes, ProductIds.NoAds }, MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void WatermarkOffers_AlwaysLeadToNoWatermark_AllSubsets()
        {
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var owned = Subsets.Ids(mask);
                var offers = Storefront.WatermarkOffers(owned, MonetizationConfig.FullCatalog);
                if (Entitlements.From(owned, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark))
                {
                    Assert.Empty(offers);
                    continue;
                }
                Assert.NotEmpty(offers);
                Assert.All(offers, o => Assert.True(o.Visible));
                if (offers.Any(o => o.RemovesWatermark))
                {
                    Assert.All(offers, o => Assert.True(o.RemovesWatermark));
                }
                else
                {
                    var all = owned.Concat(offers.Select(o => o.ProductId)).ToList();
                    Assert.True(Entitlements.From(all, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
                }
            }
        }
    }
}
