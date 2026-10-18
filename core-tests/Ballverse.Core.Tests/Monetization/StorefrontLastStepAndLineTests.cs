using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// "Последний шаг" badge, the Done-screen line target and "Всё куплено ✓" in states the presets reach rarely
    /// (spec §5.2; owner decisions for release 1 and the grandfathered No Ads rule).
    /// </summary>
    public class StorefrontLastStepAndLineTests
    {
        static readonly string[] None = new string[0];

        static string[] Ids(System.Collections.Generic.IEnumerable<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        [Fact]
        public void LastStep_NotOnAFirstPurchase()
        {
            var noAds = Storefront.Build(None, MonetizationConfig.Release1).Get(ProductIds.NoAds);
            Assert.True(noAds.RemovesWatermark);
            Assert.False(noAds.IsLastStep);

            var ruleOn = MonetizationConfig.FullCatalog.WithWatermarkWithNoAds(true);
            var s = Storefront.Build(None, ruleOn);
            Assert.True(s.Get(ProductIds.NoAds).RemovesWatermark);
            Assert.False(s.Get(ProductIds.NoAds).IsLastStep);
            // Owning a mode does not start the ad set: No Ads is still a first ad purchase.
            Assert.False(Storefront.Build(new[] { ProductIds.ModeOrbit }, ruleOn).Get(ProductIds.NoAds).IsLastStep);
        }

        [Fact]
        public void LastStep_CompletesAStartedSet()
        {
            // Release 1: the missing ad removal.
            var r1 = Storefront.Build(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.Release1);
            Assert.True(r1.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            // Full catalog §5.2 "All Modes | No Ads как «последний шаг»".
            var full = Storefront.Build(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog);
            Assert.True(full.Get(ProductIds.NoAds).IsLastStep);
            // Grandfathered user in a later release: the missing removal completes the ad set.
            var gf = Storefront.Build(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.FullCatalog, null, true);
            Assert.True(gf.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            Assert.False(gf.Get(ProductIds.Ultimate).IsLastStep);
        }

        /// <summary>
        /// Later release, grandfathered user owning one ad removal: the row sheet keeps display order, the Done-screen line
        /// names the $5.99 missing part before Ultimate ($11.99).
        /// </summary>
        [Fact]
        public void DoneScreenLine_CheapestStandaloneRemoverFirst()
        {
            var owned = new[] { ProductIds.RemoveExportAds };
            var c = MonetizationConfig.FullCatalog;
            Assert.Equal(new[] { ProductIds.Ultimate, ProductIds.RemoveInterstitialAds }, Ids(Storefront.WatermarkOffers(owned, c, null, true)));
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds, ProductIds.Ultimate },
                Ids(SalePointPolicy.DoneScreenLineOffers(3, null, owned, c, null, true)));

            var ruleOn = MonetizationConfig.FullCatalog.WithWatermarkWithNoAds(true);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.Ultimate }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, ruleOn)));
            // Single standalone remover: unchanged.
            Assert.Equal(new[] { ProductIds.Ultimate }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, c)));
        }

        /// <summary>
        /// Every remaining gain is a mode the user already plays for free, so nothing is visible: that is "Всё куплено ✓"
        /// (QA probe: by-parts off, M06 widened to free, grandfathered No Ads rule, No Ads + M07…M15 owned).
        /// </summary>
        [Fact]
        public void EmptyShopIsAllPurchased_WhenOnlyAvailableModesRemain()
        {
            var c = MonetizationConfig.FullCatalog.WithWatermarkByParts(false).WithFreeModes(new[] { ModeCode.M06 });
            var owned = new[]
            {
                ProductIds.NoAds, ProductIds.ModeLastStanding, ProductIds.ModeColorWar, ProductIds.ModeMultiply,
                ProductIds.ModeMazeEscape, ProductIds.ModeSmash, ProductIds.ModeChainReaction, ProductIds.ModePendulumWave,
                ProductIds.ModeOrbit, ProductIds.ModeLiquidSand,
            };
            var s = Storefront.Build(owned, c, null, true);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark));

            // Without the free widening M06 is still for sale: not all purchased.
            var s2 = Storefront.Build(owned, MonetizationConfig.FullCatalog.WithWatermarkByParts(false), null, true);
            Assert.Equal(new[] { ProductIds.ModeBallBattle }, Ids(s2.VisibleOffers));
            Assert.False(s2.AllPurchased);
        }
    }
}
