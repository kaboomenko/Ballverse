using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// QA round 2: when the only missing Ultimate parts are two or more modes the user already plays (free by a widened
    /// <c>free_modes</c> or grandfathered) and nothing visible removes the watermark, the Shop shows the cheapest singles
    /// instead of an empty Shop without "Всё куплено" (spec §5.2, Storefront rule 4 exception).
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
        static readonly MonetizationConfig NoBundles = new MonetizationConfig(M06M07, true,
            ProductIds.All.Where(id => id != ProductIds.AllModes && id != ProductIds.Ultimate), AllModeCodes, false);

        /// <summary>Everything but All Modes; M06/M07 grandfathered by the caller.</summary>
        static readonly MonetizationConfig NoAllModes = new MonetizationConfig(new ModeCode[0], true,
            ProductIds.All.Where(id => id != ProductIds.AllModes), AllModeCodes, false);

        static string[] Ids(IEnumerable<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        /// <summary>QA S1.</summary>
        [Fact]
        public void FreeModesMissing_SinglesShown_ThenLastStep_ThenAllPurchased()
        {
            var s = Storefront.Build(NoAdsAndM08ToM15, NoBundles, null, false);
            Assert.Equal(new[] { ProductIds.ModeBallBattle, ProductIds.ModeLastStanding }, Ids(s.VisibleOffers));
            Assert.False(s.AllPurchased);
            foreach (var o in s.VisibleOffers)
            {
                Assert.True(o.AlreadyAvailable);
                Assert.False(o.IsLastStep);
                Assert.False(o.RemovesWatermark);
            }
            Assert.Equal(Ids(s.VisibleOffers), Ids(Storefront.WatermarkOffers(NoAdsAndM08ToM15, NoBundles, null, false)));
            // The paid-card sheet stays empty: the user plays these modes already.
            Assert.Empty(Storefront.ModeSheet(ModeCode.M06, NoAdsAndM08ToM15, NoBundles, null, false));

            var one = NoAdsAndM08ToM15.Concat(new[] { ProductIds.ModeBallBattle }).ToArray();
            var s1 = Storefront.Build(one, NoBundles, null, false);
            Assert.Equal(new[] { ProductIds.ModeLastStanding }, Ids(s1.VisibleOffers));
            Assert.True(s1.VisibleOffers[0].IsLastStep);
            Assert.True(s1.VisibleOffers[0].RemovesWatermark);
            Assert.True(s1.VisibleOffers[0].AlreadyAvailable);

            var both = one.Concat(new[] { ProductIds.ModeLastStanding }).ToArray();
            var s2 = Storefront.Build(both, NoBundles, null, false);
            Assert.Empty(s2.VisibleOffers);
            Assert.True(s2.AllPurchased);
            Assert.True(s2.Entitlements.Contains(Entitlement.NoWatermark));
        }

        /// <summary>QA S2: Ultimate sold but hidden as dearer than the parts; the parts are shown, not an empty Shop.</summary>
        [Fact]
        public void GrandfatheredModesMissing_UltimateCheaperViaParts_SinglesShown()
        {
            var s = Storefront.Build(NoAdsAndM08ToM15, NoAllModes, M06M07, false);
            Assert.Equal(OfferHiddenReason.CheaperViaParts, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.Equal(new[] { ProductIds.ModeBallBattle, ProductIds.ModeLastStanding }, Ids(s.VisibleOffers));
            Assert.False(s.AllPurchased);
        }

        /// <summary>When All Modes is sold it is the visible remover, and the available singles stay hidden.</summary>
        [Fact]
        public void AllModesSold_AvailableSinglesStayHidden()
        {
            var c = MonetizationConfig.FullCatalog.WithFreeModes(M06M07);
            var s = Storefront.Build(NoAdsAndM08ToM15, c, null, false);
            Assert.Equal(new[] { ProductIds.AllModes }, Ids(s.VisibleOffers));
            Assert.True(s.Get(ProductIds.AllModes).IsLastStep);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.ModeBallBattle).HiddenReason);
            Assert.Equal(OfferHiddenReason.AlreadyAvailable, s.Get(ProductIds.ModeLastStanding).HiddenReason);
        }

        /// <summary>Without NO_WATERMARK by parts the available singles give nothing new: hidden, and all purchased.</summary>
        [Fact]
        public void ByPartsOff_AvailableSinglesStayHidden_AllPurchased()
        {
            var s = Storefront.Build(NoAdsAndM08ToM15, NoBundles.WithWatermarkByParts(false), null, false);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
        }

        /// <summary>While an ad right is also missing, the ad offers are shown first; the singles follow once they are owned.</summary>
        [Fact]
        public void AdRightAlsoMissing_SinglesWaitForTheAdRights()
        {
            var owned = NoAdsAndM08ToM15.Where(id => id != ProductIds.NoAds).Concat(new[] { ProductIds.RemoveExportAds }).ToArray();
            var s = Storefront.Build(owned, NoBundles, null, false);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, Ids(s.VisibleOffers));
            var after = Storefront.Build(owned.Concat(new[] { ProductIds.RemoveInterstitialAds }), NoBundles, null, false);
            Assert.Equal(new[] { ProductIds.ModeBallBattle, ProductIds.ModeLastStanding }, Ids(after.VisibleOffers));
        }

        /// <summary>
        /// Exhaustive over all 2^15 owned sets for both QA configs, with and without the watermark flag: "Всё куплено" exactly
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
                foreach (bool gf in new[] { false, true })
                {
                    for (int mask = 0; mask < Subsets.Count; mask++)
                    {
                        var owned = Subsets.Ids(mask);
                        var s = Storefront.Build(owned, c, g, gf);
                        // Every visible offer adds something, so buying visible offers always reaches the empty Shop, which
                        // is "Всё куплено"; NO_WATERMARK is obtainable in both configs, so it is reached too.
                        Assert.True(s.AllPurchased == (s.VisibleOffers.Count == 0), string.Join(",", owned));
                        Assert.All(s.VisibleOffers, o => Assert.False(o.Gain.IsEmpty));
                        if (s.AllPurchased) Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark), string.Join(",", owned));
                    }
                }
            }
        }
    }
}
