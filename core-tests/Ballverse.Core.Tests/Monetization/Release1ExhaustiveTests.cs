using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release-1 storefront invariants over all 2^15 owned-product combinations (owned unsellable products included, e.g.
    /// restored from a store test account; owner decisions; spec §5.0.2, §5.0.3).
    /// </summary>
    public class Release1ExhaustiveTests
    {
        static readonly string[] AdProducts = { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds };

        [Fact]
        public void InvariantsHoldForEverySubset_Release1()
        {
            for (int mask = 0; mask < Subsets.Count; mask++) Check(mask, MonetizationConfig.Release1);
        }

        internal static void Check(int mask, MonetizationConfig config)
        {
            var owned = Subsets.Ids(mask);
            var s = Storefront.Build(owned, config, null);
            var current = Entitlements.From(owned, config);
            Assert.True(current == s.Entitlements);
            Assert.True(15 == s.Offers.Count);

            var nonOwnedSellable = new List<StorefrontOffer>();
            foreach (var o in s.Offers)
            {
                owned.Add(o.ProductId);
                Assert.True(Entitlements.From(owned, config).Except(current) == o.Gain);
                owned.RemoveAt(owned.Count - 1);
                Assert.True(Subsets.Has(mask, o.ProductId) == o.Owned);
                bool sellable = AdProducts.Contains(o.ProductId);
                Assert.True(sellable == config.IsSellable(o.ProductId));

                if (o.Owned) Assert.True(OfferHiddenReason.Owned == o.HiddenReason);
                else if (!sellable) Assert.True(OfferHiddenReason.NotSellable == o.HiddenReason);
                else if (o.Gain.IsEmpty) Assert.True(OfferHiddenReason.NothingNew == o.HiddenReason);
                if (!o.Owned && sellable && !o.Gain.IsEmpty) nonOwnedSellable.Add(o);

                // Never an unsellable product, an owned one or one that adds nothing.
                if (o.Visible)
                {
                    Assert.True(sellable);
                    Assert.False(o.Owned);
                    Assert.False(o.Gain.IsEmpty);
                }

                if (o.HiddenReason == OfferHiddenReason.CheaperEquivalent)
                {
                    var alt = s.Get(o.CheaperAlternativeId);
                    Assert.True(alt.Visible);
                    Assert.True(config.IsSellable(alt.ProductId));
                    Assert.True(o.Gain == alt.Gain);
                    Assert.True(alt.Product.BasePriceUsdCents < o.Product.BasePriceUsdCents);
                }
                else
                {
                    Assert.Null(o.CheaperAlternativeId);
                }
                Assert.NotEqual(OfferHiddenReason.CheaperViaParts, o.HiddenReason);
                Assert.NotEqual(OfferHiddenReason.AlreadyAvailable, o.HiddenReason);
                Assert.False(o.AlreadyAvailable);
                Assert.False(o.Emphasized);
                Assert.True(StorefrontExhaustiveTests.ExpectedLastStep(o, owned) == o.IsLastStep);
                // Release 1 (no restored full mode set): a last step is exactly the missing ad removal after the other one.
                if (o.IsLastStep && !Entitlements.DirectGrants(owned).IsSupersetOf(EntitlementSet.PaidModes10))
                    Assert.True(o.Product.Kind == ProductKind.AdRemoval && Entitlements.DirectGrants(owned).Intersect(EntitlementSet.AdFree).Count == 1);
            }

            // No redundant offers: exactly the cheapest sellable product of each gain class is visible.
            foreach (var group in nonOwnedSellable.GroupBy(o => o.Gain.Mask))
            {
                int min = group.Min(o => o.Product.BasePriceUsdCents);
                foreach (var o in group) Assert.Equal(o.Product.BasePriceUsdCents == min, o.Visible);
            }

            // "Всё куплено" (spec §5.0.3 "No Ads или обе отдельные"): both ad rights, which also give NO_WATERMARK.
            bool expectAll = current.IsSupersetOf(EntitlementSet.AdFree);
            Assert.Equal(expectAll, s.AllPurchased);
            if (expectAll) Assert.True(current.Contains(Entitlement.NoWatermark));
            Assert.Equal(s.AllPurchased, s.VisibleOffers.Count == 0);

            // Buying every visible offer yields every release-1 entitlement, NO_WATERMARK included (spec §5.0.2). The
            // resulting set is itself a subset checked above, so "Всё куплено" with no visible offers follows from its
            // own iteration; here the entitlements are checked directly (one storefront build less per subset).
            var all = new List<string>(owned);
            all.AddRange(s.VisibleOffers.Select(o => o.ProductId));
            var final = Entitlements.From(all, config);
            Assert.True(final.IsSupersetOf(EntitlementSet.AdFree));
            Assert.True(final.Contains(Entitlement.NoWatermark));

            // Watermark row target (spec §5.0.3): No Ads, or the missing ad removal once one is owned.
            var wm = Storefront.WatermarkOffers(owned, config, null);
            Assert.All(wm, o => Assert.True(config.IsSellable(o.ProductId)));
            if (current.Contains(Entitlement.NoWatermark)) Assert.Empty(wm);
            else
            {
                Assert.NotEmpty(wm);
                Assert.All(wm, o => Assert.True(o.Visible && o.RemovesWatermark));
                var adCount = current.Intersect(EntitlementSet.AdFree).Count;
                var expectedWm = adCount == 0 ? new[] { ProductIds.NoAds }
                    : current.Contains(Entitlement.NoExportAds) ? new[] { ProductIds.RemoveInterstitialAds } : new[] { ProductIds.RemoveExportAds };
                Assert.Equal(expectedWm, wm.Select(o => o.ProductId).ToArray());
            }
        }

        /// <summary>Sale points never offer unsold products (sampled: each call rebuilds the storefront).</summary>
        [Fact]
        public void SalePointsNeverOfferUnsoldProducts()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 13)
            {
                var owned = Subsets.Ids(mask);
                Assert.False(SalePointPolicy.FirstVideoQuest(false, owned, MonetizationConfig.Release1, false, null).ShowOffer);
                for (int m = 1; m <= 15; m++) Assert.Empty(Storefront.ModeSheet((ModeCode)m, owned, MonetizationConfig.Release1, null));
                Assert.All(SalePointPolicy.DoneScreenLineOffers(3, null, owned, MonetizationConfig.Release1, null),
                    o => Assert.True(MonetizationConfig.Release1.IsSellable(o.ProductId)));
            }
        }

        [Fact]
        public void NothingOwned_ExactlyTheThreeAdProducts()
        {
            var s = Storefront.Build(new string[0], MonetizationConfig.Release1, null);
            Assert.Equal(AdProducts, s.VisibleOffers.Select(o => o.ProductId).ToArray());
        }
    }
}
