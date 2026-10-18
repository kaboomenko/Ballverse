using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Storefront invariants over all 2^15 owned-product combinations (spec §5.1, §5.2).</summary>
    public class StorefrontExhaustiveTests
    {
        static readonly MonetizationConfig NoParts = new MonetizationConfig(MonetizationConfig.Default.FreeModes, false);

        [Fact]
        public void InvariantsHoldForEverySubset_DefaultConfig()
        {
            for (int mask = 0; mask < Subsets.Count; mask++) Check(mask, MonetizationConfig.Default);
        }

        [Fact]
        public void InvariantsHoldForEverySubset_WithoutWatermarkByParts()
        {
            for (int mask = 0; mask < Subsets.Count; mask++) Check(mask, NoParts);
        }

        static void Check(int mask, MonetizationConfig config)
        {
            var owned = Subsets.Ids(mask);
            var s = Storefront.Build(owned, config);
            var current = Entitlements.From(owned, config);
            Assert.Equal(current, s.Entitlements);
            Assert.Equal(15, s.Offers.Count);

            // "All purchased" iff every entitlement is owned; then nothing is shown.
            Assert.Equal(current == EntitlementSet.AllDefined, s.AllPurchased);
            if (s.AllPurchased) Assert.Empty(s.VisibleOffers);

            var nonOwnedWithGain = new List<StorefrontOffer>();
            foreach (var o in s.Offers)
            {
                var withP = new List<string>(owned) { o.ProductId };
                Assert.Equal(Entitlements.From(withP, config).Except(current), o.Gain);
                Assert.Equal(Subsets.Has(mask, o.ProductId), o.Owned);
                if (!o.Owned && !o.Gain.IsEmpty) nonOwnedWithGain.Add(o);

                if (o.Visible)
                {
                    // Never show something that grants nothing new, nor an owned product.
                    Assert.False(o.Gain.IsEmpty);
                    Assert.False(o.Owned);
                }
                else
                {
                    Assert.NotEqual(OfferHiddenReason.None, o.HiddenReason);
                }

                if (o.Owned) Assert.Equal(OfferHiddenReason.Owned, o.HiddenReason);
                else if (o.Gain.IsEmpty) Assert.Equal(OfferHiddenReason.NothingNew, o.HiddenReason);

                if (o.HiddenReason == OfferHiddenReason.CheaperEquivalent)
                {
                    var alt = s.Get(o.CheaperAlternativeId);
                    Assert.True(alt.Visible);
                    Assert.Equal(o.Gain, alt.Gain);
                    Assert.True(alt.Product.BasePriceUsdCents < o.Product.BasePriceUsdCents);
                }
                else
                {
                    Assert.Null(o.CheaperAlternativeId);
                }

                Assert.Equal(o.Visible && o.ProductId != ProductIds.Ultimate && o.Gain.Contains(Entitlement.NoWatermark), o.IsLastStep);
            }

            // A visible offer never has a strictly cheaper product granting the same missing set.
            foreach (var o in s.VisibleOffers)
            {
                foreach (var q in nonOwnedWithGain)
                {
                    if (q != o && q.Gain == o.Gain) Assert.True(q.Product.BasePriceUsdCents >= o.Product.BasePriceUsdCents);
                }
            }

            // In particular Ultimate is never shown next to a cheaper visible offer with the same gain.
            var ultimate = s.Get(ProductIds.Ultimate);
            if (ultimate.Visible)
            {
                Assert.DoesNotContain(s.VisibleOffers, q => q != ultimate && q.Gain == ultimate.Gain);
                Assert.True(ultimate.RemovesWatermark);
            }

            // Ultimate special rule (§5.2 rows Remove Export / Remove Interstitial).
            bool special = config.WatermarkByParts && current.ModeCount > 0 && !current.Intersect(EntitlementSet.AdFree).IsEmpty;
            bool cheaperSame = nonOwnedWithGain.Any(q => q != ultimate && q.Gain == ultimate.Gain);
            Assert.Equal(!ultimate.Owned && !ultimate.Gain.IsEmpty && !cheaperSame && !special, ultimate.Visible);
            if (!config.WatermarkByParts)
                Assert.Equal(!current.Contains(Entitlement.NoWatermark), ultimate.Visible);

            // The cheapest product of each gain class is visible (Ultimate excepted by the special rule).
            foreach (var group in nonOwnedWithGain.GroupBy(o => o.Gain.Mask))
            {
                int min = group.Min(o => o.Product.BasePriceUsdCents);
                var cheapest = group.First(o => o.Product.BasePriceUsdCents == min);
                if (cheapest != ultimate || !special) Assert.True(cheapest.Visible);
            }

            // Buying everything visible completes every entitlement: nothing becomes unreachable.
            var all = new List<string>(owned);
            all.AddRange(s.VisibleOffers.Select(o => o.ProductId));
            Assert.Equal(EntitlementSet.AllDefined, Entitlements.From(all, config));

            // All Modes partial-ownership marker.
            var allModes = s.Get(ProductIds.AllModes);
            Assert.Equal(current.ModeCount, allModes.OwnedModeCount);
            Assert.Equal(current.ModeCount > 0 && current.ModeCount < 10, allModes.PartiallyOwned);
            foreach (var o in s.Offers)
            {
                if (o != allModes)
                {
                    Assert.Equal(0, o.OwnedModeCount);
                    Assert.False(o.PartiallyOwned);
                }
                Assert.False(o.Emphasized);
            }
        }

        [Fact]
        public void ResultDoesNotDependOnInputOrder()
        {
            var rng = Rng.FromSeed(0x5EED5EEDUL, 7UL);
            for (int iter = 0; iter < 2000; iter++)
            {
                int mask = (int)rng.NextUInt((uint)Subsets.Count);
                var ids = Subsets.Ids(mask);
                var shuffled = new List<string>(ids);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = (int)rng.NextUInt((uint)(i + 1));
                    var t = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = t;
                }
                if (shuffled.Count > 0) shuffled.Add(shuffled[0]);
                var a = Storefront.Build(ids);
                var b = Storefront.Build(shuffled);
                Assert.Equal(a.Offers.Select(Describe), b.Offers.Select(Describe));
            }
        }

        static string Describe(StorefrontOffer o)
        {
            return o.ProductId + "|" + o.HiddenReason + "|" + o.CheaperAlternativeId + "|" + o.Gain.Mask + "|" + o.IsLastStep + "|" + o.OwnedModeCount;
        }
    }
}
