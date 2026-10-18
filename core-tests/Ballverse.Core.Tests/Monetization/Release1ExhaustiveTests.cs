using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release-1 storefront invariants over all 2^15 owned-product combinations (owned unsellable products included, e.g.
    /// restored from a store test account), with and without the NO_WATERMARK grandfathering flag (owner decisions; spec §5.2).
    /// </summary>
    public class Release1ExhaustiveTests
    {
        static readonly string[] AdProducts = { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds };

        [Fact]
        public void InvariantsHoldForEverySubset_Release1()
        {
            for (int mask = 0; mask < Subsets.Count; mask++) Check(mask, MonetizationConfig.Release1, false);
        }

        /// <summary>
        /// The grandfathering flag is redundant while the rule is on: the storefront is identical (every other subset, cheap
        /// comparison), and the full invariants also hold on a strided sample.
        /// </summary>
        [Fact]
        public void GrandfatheredFlagChangesNothingWhileRuleIsOn()
        {
            var r1 = MonetizationConfig.Release1;
            for (int mask = 1; mask < Subsets.Count; mask += 2)
            {
                var owned = Subsets.Ids(mask);
                var a = Storefront.Build(owned, r1, null, false);
                var b = Storefront.Build(owned, r1, null, true);
                Assert.Equal(a.Entitlements, b.Entitlements);
                Assert.Equal(a.AllPurchased, b.AllPurchased);
                for (int i = 0; i < a.Offers.Count; i++)
                {
                    Assert.Equal(a.Offers[i].HiddenReason, b.Offers[i].HiddenReason);
                    Assert.Equal(a.Offers[i].Gain, b.Offers[i].Gain);
                    Assert.Equal(a.Offers[i].IsLastStep, b.Offers[i].IsLastStep);
                }
                if (mask % 8 == 1) Check(mask, r1, true);
            }
        }

        internal static void Check(int mask, MonetizationConfig config, bool gf)
        {
            var owned = Subsets.Ids(mask);
            var s = Storefront.Build(owned, config, null, gf);
            var current = Entitlements.From(owned, config, gf);
            Assert.Equal(current, s.Entitlements);
            Assert.Equal(15, s.Offers.Count);

            bool noAdsRule = config.WatermarkWithNoAds || gf;
            var nonOwnedSellable = new List<StorefrontOffer>();
            foreach (var o in s.Offers)
            {
                owned.Add(o.ProductId);
                Assert.Equal(Entitlements.From(owned, config, gf).Except(current), o.Gain);
                owned.RemoveAt(owned.Count - 1);
                Assert.Equal(Subsets.Has(mask, o.ProductId), o.Owned);
                bool sellable = AdProducts.Contains(o.ProductId);
                Assert.Equal(sellable, config.IsSellable(o.ProductId));

                if (o.Owned) Assert.Equal(OfferHiddenReason.Owned, o.HiddenReason);
                else if (!sellable) Assert.Equal(OfferHiddenReason.NotSellable, o.HiddenReason);
                else if (o.Gain.IsEmpty) Assert.Equal(OfferHiddenReason.NothingNew, o.HiddenReason);
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
                    Assert.Equal(o.Gain, alt.Gain);
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
                Assert.Equal(o.Visible && o.Gain.Contains(Entitlement.NoWatermark), o.IsLastStep);
            }

            // No redundant offers: exactly the cheapest sellable product of each gain class is visible.
            foreach (var group in nonOwnedSellable.GroupBy(o => o.Gain.Mask))
            {
                int min = group.Min(o => o.Product.BasePriceUsdCents);
                foreach (var o in group) Assert.Equal(o.Product.BasePriceUsdCents == min, o.Visible);
            }

            // "Всё куплено": nothing sellable adds anything.
            bool nwObtainable = noAdsRule || (config.WatermarkByParts && current.IsSupersetOf(EntitlementSet.PaidModes10));
            bool expectAll = current.IsSupersetOf(EntitlementSet.AdFree) && (current.Contains(Entitlement.NoWatermark) || !nwObtainable);
            Assert.Equal(expectAll, s.AllPurchased);
            Assert.Equal(s.AllPurchased, s.VisibleOffers.Count == 0);

            // Buying every visible offer yields every release-1 entitlement, NO_WATERMARK included under the rule. The
            // resulting set is itself a subset checked above, so "Всё куплено" with no visible offers follows from its
            // own iteration; here the entitlements are checked directly (one storefront build less per subset).
            var all = new List<string>(owned);
            all.AddRange(s.VisibleOffers.Select(o => o.ProductId));
            var final = Entitlements.From(all, config, gf);
            Assert.True(final.IsSupersetOf(EntitlementSet.AdFree));
            if (noAdsRule) Assert.True(final.Contains(Entitlement.NoWatermark));
            bool finalNwObtainable = noAdsRule || (config.WatermarkByParts && final.IsSupersetOf(EntitlementSet.PaidModes10));
            Assert.True(final.Contains(Entitlement.NoWatermark) || !finalNwObtainable);

            // Watermark row target: only sellable products, and it leads to NO_WATERMARK when a path exists.
            var wm = Storefront.WatermarkOffers(owned, config, null, gf);
            Assert.All(wm, o => Assert.True(config.IsSellable(o.ProductId)));
            if (current.Contains(Entitlement.NoWatermark)) Assert.Empty(wm);
            else if (nwObtainable)
            {
                Assert.NotEmpty(wm);
                Assert.All(wm, o => Assert.True(o.Visible && o.RemovesWatermark));
            }
            else Assert.Empty(wm);
        }

        /// <summary>Sale points never offer unsold products (sampled: each call rebuilds the storefront).</summary>
        [Fact]
        public void SalePointsNeverOfferUnsoldProducts()
        {
            foreach (bool gf in new[] { false, true })
            {
                for (int mask = gf ? 1 : 0; mask < Subsets.Count; mask += 29)
                {
                    var owned = Subsets.Ids(mask);
                    Assert.False(SalePointPolicy.FirstVideoQuest(false, owned, false, MonetizationConfig.Release1, null, gf).ShowOffer);
                    for (int m = 1; m <= 15; m++) Assert.Empty(Storefront.ModeSheet((ModeCode)m, owned, MonetizationConfig.Release1, null, gf));
                    Assert.All(SalePointPolicy.DoneScreenLineOffers(3, null, owned, MonetizationConfig.Release1, null, gf),
                        o => Assert.True(MonetizationConfig.Release1.IsSellable(o.ProductId)));
                }
            }
        }

        [Fact]
        public void NothingOwned_ExactlyTheThreeAdProducts_BothFlagValues()
        {
            foreach (bool gf in new[] { false, true })
            {
                var s = Storefront.Build(new string[0], MonetizationConfig.Release1, null, gf);
                Assert.Equal(AdProducts, s.VisibleOffers.Select(o => o.ProductId).ToArray());
            }
        }

        /// <summary>
        /// Turning the No Ads rule off (a later release) never revokes NO_WATERMARK for the same owned products, given the
        /// persisted grandfathering flag; exhaustive over owned sets and target configs.
        /// </summary>
        [Fact]
        public void TurningRuleOffNeverRevokesWatermark_AllSubsets()
        {
            var later = new[]
            {
                MonetizationConfig.FullCatalog,
                MonetizationConfig.FullCatalog.WithWatermarkByParts(false),
                MonetizationConfig.Release1.WithWatermarkWithNoAds(false),
            };
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var owned = Subsets.Ids(mask);
                bool gf = WatermarkGrandfathering.Update(false, owned, MonetizationConfig.Release1);
                var before = Entitlements.From(owned, MonetizationConfig.Release1, gf);
                bool hasAdFree = Entitlements.DirectGrants(owned).IsSupersetOf(EntitlementSet.AdFree);
                Assert.Equal(hasAdFree, gf);
                Assert.Equal(hasAdFree || Subsets.Has(mask, ProductIds.Ultimate)
                    || Entitlements.DirectGrants(owned).IsSupersetOf(Entitlements.FullUltimateContents), before.Contains(Entitlement.NoWatermark));
                foreach (var config in later)
                {
                    Assert.Equal(gf, WatermarkGrandfathering.Update(gf, owned, config));
                    var after = Entitlements.From(owned, config, WatermarkGrandfathering.Update(gf, owned, config));
                    Assert.Equal(before, after);
                }
            }
        }
    }
}
