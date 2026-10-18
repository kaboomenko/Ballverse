using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release 1 (owner decisions): the store sells only Remove Export Ads, Remove Interstitial Ads and No Ads Bundle, under the
    /// smart-storefront rules of spec §5.2; NO_WATERMARK comes with No Ads or both ad removals; no paid mode ships.
    /// </summary>
    public class Release1StorefrontTests
    {
        static readonly MonetizationConfig R1 = MonetizationConfig.Release1;
        static readonly string[] None = new string[0];

        static string[] VisibleIds(StorefrontState s)
        {
            return s.VisibleOffers.Select(o => o.ProductId).ToArray();
        }

        static string[] Ids(IReadOnlyList<StorefrontOffer> offers)
        {
            return offers.Select(o => o.ProductId).ToArray();
        }

        [Fact]
        public void Nothing_ShowsExactlyTheThreeAdProducts_NoAdsRemovesWatermark()
        {
            var s = Storefront.Build(None, R1, null);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.False(s.AllPurchased);
            var noAds = s.Get(ProductIds.NoAds);
            Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark), noAds.Gain);
            Assert.True(noAds.RemovesWatermark);
            // A first purchase is not a "последний шаг" (StorefrontOffer.IsLastStep); RemovesWatermark carries the badge.
            Assert.False(noAds.IsLastStep);
            Assert.False(s.Get(ProductIds.RemoveExportAds).RemovesWatermark);
            Assert.False(s.Get(ProductIds.RemoveInterstitialAds).RemovesWatermark);
            foreach (var o in s.Offers)
            {
                if (o.Product.Kind == ProductKind.SingleMode || o.ProductId == ProductIds.AllModes || o.ProductId == ProductIds.Ultimate)
                    Assert.Equal(OfferHiddenReason.NotSellable, o.HiddenReason);
            }
            Assert.Equal(15, s.Offers.Count);
        }

        [Fact]
        public void RemoveExportOwned_OnlyRemoveInterstitialAsLastStep()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveExportAds }, R1, null);
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            var ri = s.Get(ProductIds.RemoveInterstitialAds);
            Assert.True(ri.IsLastStep);
            Assert.Equal(EntitlementSet.Of(Entitlement.NoInterstitials, Entitlement.NoWatermark), ri.Gain);
            var noAds = s.Get(ProductIds.NoAds);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, noAds.HiddenReason);
            Assert.Equal(ProductIds.RemoveInterstitialAds, noAds.CheaperAlternativeId);
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.RemoveExportAds).HiddenReason);
            Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.Ultimate).HiddenReason);
        }

        [Fact]
        public void RemoveInterstitialOwned_OnlyRemoveExportAsLastStep()
        {
            var s = Storefront.Build(new[] { ProductIds.RemoveInterstitialAds }, R1, null);
            Assert.Equal(new[] { ProductIds.RemoveExportAds }, VisibleIds(s));
            Assert.True(s.Get(ProductIds.RemoveExportAds).IsLastStep);
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.NoAds).HiddenReason);
            Assert.Equal(ProductIds.RemoveExportAds, s.Get(ProductIds.NoAds).CheaperAlternativeId);
        }

        [Theory]
        [InlineData(ProductIds.NoAds)]
        [InlineData(ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds)]
        [InlineData(ProductIds.NoAds, ProductIds.RemoveExportAds)]
        [InlineData(ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds)]
        [InlineData(ProductIds.Ultimate)]
        public void AdFreeOwned_AllPurchasedWithWatermarkRemoved(params string[] owned)
        {
            var s = Storefront.Build(owned, R1, null);
            Assert.True(s.AllPurchased);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.Equal(WatermarkRow.Toggle, WatermarkRowPolicy.GetRow(s.Entitlements));
            Assert.False(global::Ballverse.Core.Projects.WatermarkPolicy.Resolve(false, s.Entitlements.Contains(Entitlement.NoWatermark)));
            Assert.Empty(Storefront.WatermarkOffers(owned, R1, null));
        }

        [Fact]
        public void OwnedUnsellableBundle_KeepsItsRights_ShopStillSellsAds()
        {
            // E.g. All Modes restored from a store test account: the rights stay, but nothing mode-related is offered.
            var s = Storefront.Build(new[] { ProductIds.AllModes }, R1, null);
            Assert.Equal(10, s.Entitlements.ModeCount);
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, VisibleIds(s));
            Assert.Equal(OfferHiddenReason.Owned, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.Ultimate).HiddenReason);
            // Modes are not released, so even owned ones are not playable in release 1.
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M06, s.Entitlements, null, R1));
        }

        [Fact]
        public void WatermarkOffers_Release1()
        {
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(Storefront.WatermarkOffers(None, R1, null)));
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds }, Ids(Storefront.WatermarkOffers(new[] { ProductIds.RemoveExportAds }, R1, null)));
            Assert.Equal(new[] { ProductIds.RemoveExportAds }, Ids(Storefront.WatermarkOffers(new[] { ProductIds.RemoveInterstitialAds }, R1, null)));
            Assert.DoesNotContain(ProductIds.Ultimate, Ids(Storefront.WatermarkOffers(new[] { ProductIds.ModeOrbit }, R1, null)));
        }

        /// <summary>
        /// Spec §5.0.2, §5.0.4: release-1 ad purchases keep NO_WATERMARK after the catalog grows; the full catalog applies the
        /// same rule, so the watermark row has nothing to open and No Ads still carries the "+ без водяного знака" badge.
        /// </summary>
        [Fact]
        public void Release1AdPurchasesKeepWatermarkInFullCatalog()
        {
            var full = MonetizationConfig.FullCatalog;
            foreach (var owned in new[] { new[] { ProductIds.NoAds }, new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds } })
            {
                Assert.Equal(Entitlements.From(owned, R1), Entitlements.From(owned, full));
                Assert.True(Storefront.Build(owned, full, null).Entitlements.Contains(Entitlement.NoWatermark));
                Assert.Empty(Storefront.WatermarkOffers(owned, full, null));
                Assert.Empty(SalePointPolicy.DoneScreenLineOffers(3, null, owned, full, null));
            }
            Assert.True(Storefront.Build(None, full, null).Get(ProductIds.NoAds).RemovesWatermark);
            Assert.True(Storefront.Build(None, R1, null).Get(ProductIds.NoAds).RemovesWatermark);
        }

        [Fact]
        public void CheapestPartsToComplete_ImpossibleInRelease1()
        {
            Assert.Empty(Storefront.CheapestPartsToComplete(EntitlementSet.Empty, R1));
            Assert.Equal(new[] { ProductIds.NoAds }, Storefront.CheapestPartsToComplete(EntitlementSet.PaidModes10, R1).ToArray());
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds }, Storefront.CheapestPartsToComplete(EntitlementSet.Empty, MonetizationConfig.FullCatalog).ToArray());
        }

        [Fact]
        public void ModeSheet_EmptyForEveryMode()
        {
            foreach (var owned in new[] { None, new[] { ProductIds.ModeOrbit }, new[] { ProductIds.NoAds } })
            {
                for (int m = 1; m <= 15; m++) Assert.Empty(Storefront.ModeSheet((ModeCode)m, owned, R1, null));
            }
        }

        [Fact]
        public void FirstVideoQuest_NoOfferBecauseAllModesIsNotSold()
        {
            var screen = SalePointPolicy.FirstVideoQuest(false, None, R1, false, null);
            Assert.False(screen.ShowOffer);
            Assert.Same(FirstVideoQuestScreen.None, screen);
        }

        [Fact]
        public void TrialPreview_PaidModesAreNotReleased()
        {
            for (int m = 6; m <= 15; m++)
            {
                Assert.Equal(TrialPreviewAccess.NotReleased,
                    SalePointPolicy.CanStartTrialPreview((ModeCode)m, EntitlementSet.Empty, null, 0, R1, null, true));
            }
            for (int m = 1; m <= 5; m++)
            {
                Assert.Equal(TrialPreviewAccess.NotPaid,
                    SalePointPolicy.CanStartTrialPreview((ModeCode)m, EntitlementSet.Empty, null, 0, R1, null, true));
            }
        }

        [Fact]
        public void TrialPreview_ReleasedButUnsoldMode_NotSold_Precedence()
        {
            // M14 released, but only ad products are sold.
            var c = new MonetizationConfig(new ModeCode[0], MonetizationConfig.Release1.SellableProductIds,
                new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05, ModeCode.M14 });
            var nothing = EntitlementSet.Empty;
            Assert.Equal(TrialPreviewAccess.NotSold, SalePointPolicy.CanStartTrialPreview(ModeCode.M14, nothing, 5, 5, c, null, false));
            Assert.Equal(TrialPreviewAccess.NotLocked,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M14, Entitlements.From(new[] { ProductIds.ModeOrbit }, MonetizationConfig.FullCatalog), 5, 5, c, null, false));
            Assert.Equal(TrialPreviewAccess.NotReleased, SalePointPolicy.CanStartTrialPreview(ModeCode.M13, nothing, 5, 5, c, null, false));
            Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview(ModeCode.M03, nothing, 5, 5, c, null, false));
            // Once its single is sold, the demo is allowed.
            var sold = new MonetizationConfig(new ModeCode[0], new[] { ProductIds.ModeOrbit }, c.ReleasedModes);
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M14, nothing, null, 5, sold, null, true));
            Assert.Equal(new[] { ProductIds.ModeOrbit }, Ids(Storefront.ModeSheet(ModeCode.M14, None, sold, null)));
        }

        [Fact]
        public void DoneScreenLine_Release1_TargetsAdProductsNeverUltimate()
        {
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(2, null, None, R1, null));
            Assert.Equal(new[] { ProductIds.NoAds }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, R1, null)));
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds },
                Ids(SalePointPolicy.DoneScreenLineOffers(6, 3, new[] { ProductIds.RemoveExportAds }, R1, null)));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(5, 3, None, R1, null));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(9, null, new[] { ProductIds.NoAds }, R1, null));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(9, null, None, R1, null, watermarkEnabled: false));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(9, null, None, R1, null, true, interstitialJustShown: true));
            Assert.Throws<System.ArgumentNullException>(() => SalePointPolicy.DoneScreenLineOffers(3, null, null, R1, null));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SalePointPolicy.DoneScreenLineOffers(-1, null, None, R1, null));
        }

        [Fact]
        public void DoneScreenLine_FullCatalogMatchesWatermarkOffers()
        {
            // Cheapest standalone remover first: No Ads ($8.99) before Ultimate ($11.99).
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.Ultimate }, Ids(SalePointPolicy.DoneScreenLineOffers(3, null, None, MonetizationConfig.FullCatalog, null)));
            var owned = new[] { ProductIds.RemoveExportAds, ProductIds.ModeSmash };
            Assert.Equal(Ids(Storefront.WatermarkOffers(owned, MonetizationConfig.FullCatalog, null)), Ids(SalePointPolicy.DoneScreenLineOffers(3, null, owned, MonetizationConfig.FullCatalog, null)));
        }

        [Fact]
        public void DoneScreenLine_OnlySellableOffersForEverySubset()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 3)
            {
                var owned = Subsets.Ids(mask);
                var offers = SalePointPolicy.DoneScreenLineOffers(3, null, owned, R1, null);
                Assert.All(offers, o => Assert.True(R1.IsSellable(o.ProductId)));
                bool nw = Entitlements.From(owned, R1).Contains(Entitlement.NoWatermark);
                Assert.Equal(!nw, offers.Count > 0);
            }
        }
    }
}
