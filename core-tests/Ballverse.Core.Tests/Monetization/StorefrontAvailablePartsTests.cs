using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Modes the user already plays (free by a widened <c>free_modes</c> or grandfathered) are never re-sold (spec §5.2
    /// "Покупатель не должен платить за то, что у него уже есть"): under the permanent NO_WATERMARK rule (spec §5.0.2) a
    /// single mode never completes the watermark right, so the Shop never needs them to reach NO_WATERMARK.
    /// </summary>
    public class StorefrontAvailablePartsTests
    {
        static readonly ModeCode[] AllModeCodes = Enumerable.Range(1, 15).Select(m => (ModeCode)m).ToArray();
        static readonly ModeCode[] M06M07 = { ModeCode.M06, ModeCode.M07 };
        static readonly string[] NoAdsAndM08ToM15 =
        {
            ProductIds.NoAds, ProductIds.ModeColorWar, ProductIds.ModeMultiply, ProductIds.ModeMazeEscape, ProductIds.ModeSmash,
            ProductIds.ModeChainReaction, ProductIds.ModePendulumWave, ProductIds.ModeOrbit, ProductIds.ModeLiquidSand,
        };

        /// <summary>Singles and ad products sold, no All Modes and no Ultimate.</summary>
        static readonly MonetizationConfig NoBundles = new MonetizationConfig(M06M07,
            ProductIds.All.Where(id => id != ProductIds.AllModes && id != ProductIds.Ultimate), AllModeCodes);

        /// <summary>Everything but All Modes; M06/M07 grandfathered by the caller.</summary>
        static readonly MonetizationConfig NoAllModes = new MonetizationConfig(new ModeCode[0],
            ProductIds.All.Where(id => id != ProductIds.AllModes), AllModeCodes);

        static string[] Ids(IEnumerable<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        /// <summary>No Ads already gives NO_WATERMARK, so the missing available modes leave nothing to buy.</summary>
        [Fact]
        public void FreeModesMissing_NothingToBuy_AllPurchased()
        {
            var s = Storefront.Build(NoAdsAndM08ToM15, NoBundles, null);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark));
            foreach (var id in new[] { ProductIds.ModeBallBattle, ProductIds.ModeLastStanding })
            {
                var o = s.Get(id);
                Assert.Equal(OfferHiddenReason.AlreadyAvailable, o.HiddenReason);
                Assert.True(o.AlreadyAvailable);
                Assert.False(o.RemovesWatermark);
            }
            Assert.Empty(Storefront.WatermarkOffers(NoAdsAndM08ToM15, NoBundles, null));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M06, NoAdsAndM08ToM15, NoBundles, null));
        }

        /// <summary>Ultimate sold but would only re-sell the grandfathered modes; the singles stay hidden too.</summary>
        [Fact]
        public void GrandfatheredModesMissing_NothingToBuy()
        {
            var s = Storefront.Build(NoAdsAndM08ToM15, NoAllModes, M06M07);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
        }

        /// <summary>Without No Ads the watermark comes from the ad rights, not from the available singles.</summary>
        [Fact]
        public void AvailableModesMissing_AdRightMissing_OnlyAdOffersShown()
        {
            var owned = NoAdsAndM08ToM15.Where(id => id != ProductIds.NoAds).ToArray();
            var s = Storefront.Build(owned, NoBundles, null);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, Ids(s.VisibleOffers));
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(Storefront.WatermarkOffers(owned, NoBundles, null)));
            Assert.False(s.VisibleOffers[0].IsLastStep);
            var u = Storefront.Build(owned, NoAllModes, M06M07);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, u.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Equal(ProductIds.NoAds, u.Get(ProductIds.Ultimate).CheaperAlternativeId);
        }

        /// <summary>All Modes sold: it would only re-sell the free M06/M07, so it is hidden like the singles.</summary>
        [Fact]
        public void AllModesSold_AllModesAndAvailableSinglesStayHidden()
        {
            var c = MonetizationConfig.FullCatalog.WithFreeModes(M06M07);
            var s = Storefront.Build(NoAdsAndM08ToM15, c, null);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.ModeBallBattle).HiddenReason);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.ModeLastStanding).HiddenReason);
        }

        /// <summary>An ad right missing: only the missing ad removal (last step); the available singles never follow.</summary>
        [Fact]
        public void AdRightAlsoMissing_SinglesWaitForTheAdRights()
        {
            var owned = NoAdsAndM08ToM15.Where(id => id != ProductIds.NoAds).Concat(new[] { ProductIds.RemoveExportAds }).ToArray();
            var s = Storefront.Build(owned, NoBundles, null);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, Ids(s.VisibleOffers));
            Assert.True(s.VisibleOffers[0].IsLastStep);
            Assert.True(s.VisibleOffers[0].RemovesWatermark);
            var after = Storefront.Build(owned.Concat(new[] { ProductIds.RemoveInterstitialAds }), NoBundles, null);
            Assert.Empty(after.VisibleOffers);
            Assert.True(after.AllPurchased);
        }

        /// <summary>
        /// Exhaustive over all 2^15 owned sets for both configs: "Всё куплено" exactly
        /// when nothing is visible, and it includes NO_WATERMARK (so buying visible offers always reaches it).
        /// </summary>
        [Fact]
        public void Exhaustive_AllPurchasedIffNothingVisible_WatermarkReachable()
        {
            var cases = new[]
            {
                (NoBundles, (ModeCode[])null),
                (NoAllModes, M06M07),
            };
            foreach (var (c, g) in cases)
            {
                for (int mask = 0; mask < Subsets.Count; mask++)
                {
                    var owned = Subsets.Ids(mask);
                    var s = Storefront.Build(owned, c, g);
                    // Every visible offer adds something, so buying visible offers always reaches the empty Shop, which
                    // is "Всё куплено"; NO_WATERMARK is obtainable in both configs, so it is reached too.
                    Assert.True(s.AllPurchased == (s.VisibleOffers.Count == 0), string.Join(",", owned));
                    Assert.All(s.VisibleOffers, o => Assert.False(o.Gain.IsEmpty));
                    Assert.All(s.VisibleOffers, o => Assert.False(o.AlreadyAvailable));
                    if (s.AllPurchased) Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark), string.Join(",", owned));
                }
            }
        }
    }
}
