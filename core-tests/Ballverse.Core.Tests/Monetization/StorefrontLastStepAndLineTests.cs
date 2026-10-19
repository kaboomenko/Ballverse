using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// "Последний шаг" badge, the Done-screen line target and "Всё куплено ✓" in states the presets reach rarely
    /// (spec §5.0.3, §5.2; NO_WATERMARK by the permanent rule of spec §5.0.2).
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
            var noAds = Storefront.Build(None, MonetizationConfig.Release1, null).Get(ProductIds.NoAds);
            Assert.True(noAds.RemovesWatermark);
            Assert.False(noAds.IsLastStep);

            var full = MonetizationConfig.FullCatalog;
            var s = Storefront.Build(None, full, null);
            Assert.True(s.Get(ProductIds.NoAds).RemovesWatermark);
            Assert.False(s.Get(ProductIds.NoAds).IsLastStep);
            // Owning a mode does not start the ad set: No Ads is still a first ad purchase.
            Assert.False(Storefront.Build(new[] { ProductIds.ModeOrbit }, full, null).Get(ProductIds.NoAds).IsLastStep);
        }

        [Fact]
        public void LastStep_CompletesAStartedSet()
        {
            // Release 1: the missing ad removal.
            var r1 = Storefront.Build(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.Release1, null);
            Assert.True(r1.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            // Full catalog §5.2 "All Modes | No Ads как «последний шаг: реклама и водяной знак уйдут»".
            var full = Storefront.Build(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog, null);
            Assert.True(full.Get(ProductIds.NoAds).IsLastStep);
            Assert.True(full.Get(ProductIds.NoAds).RemovesWatermark);
            // §5.2 "No Ads | All Modes как «последний шаг: все режимы» (знак уже снят)".
            var noAds = Storefront.Build(new[] { ProductIds.NoAds }, MonetizationConfig.FullCatalog, null);
            Assert.True(noAds.Get(ProductIds.AllModes).IsLastStep);
            Assert.False(noAds.Get(ProductIds.AllModes).RemovesWatermark);
            // Later release, one ad removal owned: the missing removal completes the ad set.
            var one = Storefront.Build(new[] { ProductIds.RemoveExportAds }, MonetizationConfig.FullCatalog, null);
            Assert.True(one.Get(ProductIds.RemoveInterstitialAds).IsLastStep);
            Assert.False(one.Get(ProductIds.Ultimate).IsLastStep);
        }

        /// <summary>
        /// Explicit table of the "последний шаг" label (spec §5.0.3 rows, §5.2 rows): owned products → the visible offers that
        /// carry the label. Written by hand, independent of the implementation's formula.
        /// </summary>
        [Theory]
        [InlineData("r1", "", "")]
        [InlineData("r1", ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds)]
        [InlineData("r1", ProductIds.RemoveInterstitialAds, ProductIds.RemoveExportAds)]
        [InlineData("r1", ProductIds.NoAds, "")]
        [InlineData("full", "", "")]
        [InlineData("full", ProductIds.AllModes, ProductIds.NoAds)]
        [InlineData("full", ProductIds.NoAds, ProductIds.AllModes)]
        [InlineData("full", ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds)]
        [InlineData("full", ProductIds.RemoveInterstitialAds, ProductIds.RemoveExportAds)]
        [InlineData("full", ProductIds.ModeOrbit, "")]
        [InlineData("full", ProductIds.NoAds + "," + ProductIds.ModeOrbit, ProductIds.AllModes)]
        [InlineData("full", ProductIds.AllModes + "," + ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds)]
        [InlineData("full", ProductIds.AllModes + "," + ProductIds.NoAds, "")]
        [InlineData("full", ProductIds.Ultimate, "")]
        public void LastStep_Table(string preset, string owned, string expectedLastSteps)
        {
            var config = preset == "r1" ? MonetizationConfig.Release1 : MonetizationConfig.FullCatalog;
            var ownedIds = owned.Length == 0 ? None : owned.Split(',');
            var expected = expectedLastSteps.Length == 0 ? None : expectedLastSteps.Split(',');
            var s = Storefront.Build(ownedIds, config, null);
            Assert.Equal(expected, Ids(s.VisibleOffers.Where(o => o.IsLastStep)));
        }

        /// <summary>
        /// QA probe P3: No Ads owned and M06–M14 playable without entitlements (free by config or grandfathered): the M15
        /// single completes "все режимы" (§5.2 "No Ads | All Modes как «последний шаг: все режимы»"), so it carries the label,
        /// and All Modes is hidden as the dearer equivalent.
        /// </summary>
        [Fact]
        public void LastStep_CountsModesTheUserAlreadyPlays()
        {
            var nine = Enumerable.Range(6, 9).Select(m => (ModeCode)m).ToArray();
            var free = MonetizationConfig.FullCatalog.WithFreeModes(nine);
            foreach (var (config, grandfathered) in new[] { (free, (ModeCode[])null), (MonetizationConfig.FullCatalog, nine) })
            {
                var s = Storefront.Build(new[] { ProductIds.NoAds }, config, grandfathered);
                Assert.Equal(new[] { ProductIds.ModeLiquidSand }, Ids(s.VisibleOffers));
                Assert.True(s.Get(ProductIds.ModeLiquidSand).IsLastStep);
                Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.AllModes).HiddenReason);
            }
            // Without No Ads the M15 single does not complete the contents: no label.
            var noAdsMissing = Storefront.Build(None, free, null);
            Assert.False(noAdsMissing.Get(ProductIds.ModeLiquidSand).IsLastStep);
            // Available modes alone do not "start" the ad pair: No Ads stays a first ad purchase unless it completes all 12.
            Assert.False(noAdsMissing.Get(ProductIds.NoAds).IsLastStep);
        }

        /// <summary>
        /// Later release, user owning one ad removal: the row sheet keeps display order, the Done-screen line names the $5.99
        /// missing part before Ultimate ($11.99).
        /// </summary>
        [Fact]
        public void DoneScreenLine_CheapestStandaloneRemoverFirst()
        {
            var owned = new[] { ProductIds.RemoveExportAds };
            var c = MonetizationConfig.FullCatalog;
            Assert.Equal(new[] { ProductIds.Ultimate, ProductIds.RemoveInterstitialAds }, Ids(Storefront.WatermarkOffers(owned, c, null)));
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds, ProductIds.Ultimate },
                Ids(SalePointPolicy.DoneScreenLineOffers(3, null, owned, c, null)));

            // Nothing owned: No Ads ($8.99) before Ultimate ($11.99), the row sheet in display order.
            Assert.Equal(new[] { ProductIds.Ultimate, ProductIds.NoAds }, Ids(Storefront.WatermarkOffers(None, c, null)));
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.Ultimate }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, c, null)));
            // Release 1: No Ads only.
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, MonetizationConfig.Release1, null)));
        }

        /// <summary>
        /// Every remaining gain is a mode the user already plays for free, so nothing is visible: that is "Всё куплено ✓"
        /// (M06 widened to free, No Ads + M07…M15 owned).
        /// </summary>
        [Fact]
        public void EmptyShopIsAllPurchased_WhenOnlyAvailableModesRemain()
        {
            var c = MonetizationConfig.FullCatalog.WithFreeModes(new[] { ModeCode.M06 });
            var owned = new[]
            {
                ProductIds.NoAds, ProductIds.ModeLastStanding, ProductIds.ModeColorWar, ProductIds.ModeMultiply,
                ProductIds.ModeMazeEscape, ProductIds.ModeSmash, ProductIds.ModeChainReaction, ProductIds.ModePendulumWave,
                ProductIds.ModeOrbit, ProductIds.ModeLiquidSand,
            };
            var s = Storefront.Build(owned, c, null);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark));

            // Without the free widening M06 is still for sale: not all purchased.
            var s2 = Storefront.Build(owned, MonetizationConfig.FullCatalog, null);
            Assert.Equal(new[] { ProductIds.ModeBallBattle }, Ids(s2.VisibleOffers));
            Assert.True(s2.VisibleOffers[0].IsLastStep);
            Assert.False(s2.AllPurchased);
        }
    }
}
