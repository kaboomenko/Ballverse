using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Storefront invariants over all 2^15 owned-product combinations (spec §5.1, §5.2).</summary>
    public class StorefrontExhaustiveTests
    {
        [Fact]
        public void InvariantsHoldForEverySubset_FullCatalog()
        {
            for (int mask = 0; mask < Subsets.Count; mask++) Check(mask, MonetizationConfig.FullCatalog);
        }

        internal static void Check(int mask, MonetizationConfig config)
        {
            var owned = Subsets.Ids(mask);
            var s = Storefront.Build(owned, config, null);
            var current = Entitlements.From(owned, config);
            Assert.True(current == s.Entitlements);
            Assert.True(15 == s.Offers.Count);

            // "All purchased" iff every entitlement is owned; then nothing is shown.
            Assert.Equal(current == EntitlementSet.AllDefined, s.AllPurchased);
            Assert.Equal(s.AllPurchased, s.VisibleOffers.Count == 0);

            var nonOwnedWithGain = new List<StorefrontOffer>();
            foreach (var o in s.Offers)
            {
                owned.Add(o.ProductId);
                Assert.True(Entitlements.From(owned, config).Except(current) == o.Gain);
                owned.RemoveAt(owned.Count - 1);
                Assert.NotEqual(OfferHiddenReason.NotSellable, o.HiddenReason);
                Assert.True(Subsets.Has(mask, o.ProductId) == o.Owned);
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

                if (o.Owned) Assert.True(OfferHiddenReason.Owned == o.HiddenReason);
                else if (o.Gain.IsEmpty) Assert.True(OfferHiddenReason.NothingNew == o.HiddenReason);

                if (o.HiddenReason == OfferHiddenReason.CheaperEquivalent)
                {
                    var alt = s.Get(o.CheaperAlternativeId);
                    Assert.True(alt.Visible);
                    Assert.True(o.Gain == alt.Gain);
                    Assert.True(alt.Product.BasePriceUsdCents < o.Product.BasePriceUsdCents);
                }
                else
                {
                    Assert.Null(o.CheaperAlternativeId);
                }

                Assert.True(ExpectedLastStep(o, owned) == o.IsLastStep);
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

            // Ultimate overpay rule (§5.2 "Ultimate (он дороже, чем недостающая часть)", "только если нет режимов"), checked
            // against an independent brute-force oracle over every combination of the other 14 products.
            int partsCost = BruteForcePartsCost(current);
            bool viaParts = current.ModeCount > 0 && partsCost < ProductCatalog.UltimatePriceCents;
            bool cheaperSame = nonOwnedWithGain.Any(q => q != ultimate && q.Gain == ultimate.Gain);
            Assert.Equal(!ultimate.Owned && !ultimate.Gain.IsEmpty && !cheaperSame && !viaParts, ultimate.Visible);
            if (ultimate.HiddenReason == OfferHiddenReason.CheaperViaParts) Assert.True(viaParts);
            Assert.Equal(partsCost, Storefront.CheapestPartsToComplete(current, MonetizationConfig.FullCatalog).Sum(id => ProductCatalog.Get(id).BasePriceUsdCents));

            // The cheapest product of each gain class is visible (Ultimate excepted when the missing parts are cheaper).
            foreach (var group in nonOwnedWithGain.GroupBy(o => o.Gain.Mask))
            {
                int min = group.Min(o => o.Product.BasePriceUsdCents);
                var cheapest = group.First(o => o.Product.BasePriceUsdCents == min);
                if (cheapest != ultimate || !viaParts) Assert.True(cheapest.Visible);
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
                    Assert.True(0 == o.OwnedModeCount);
                    Assert.False(o.PartiallyOwned);
                }
                Assert.False(o.Emphasized);
                Assert.False(o.AlreadyAvailable);
            }
        }

        /// <summary>
        /// "Последний шаг" (spec §5.0.3, §5.2): a visible non-Ultimate offer that completes a set the user has already
        /// started: both ad rights (which removes the watermark, spec §5.0.2) or the full Ultimate contents. A first purchase
        /// that completes a set on its own is not a last step.
        /// </summary>
        internal static bool ExpectedLastStep(StorefrontOffer o, IEnumerable<string> owned)
        {
            if (!o.Visible || o.ProductId == ProductIds.Ultimate) return false;
            var direct = Entitlements.DirectGrants(owned);
            var after = direct.Union(o.Product.Grants);
            var sets = new[] { EntitlementSet.AdFree, Entitlements.FullUltimateContents };
            bool result = sets.Any(set => after.IsSupersetOf(set) && !direct.IsSupersetOf(set) && !direct.Intersect(set).IsEmpty);
            // Completing the ad pair always removes the watermark; completing the contents does so unless already removed.
            if (result && !direct.IsSupersetOf(EntitlementSet.AdFree)) Assert.True(o.RemovesWatermark);
            return result;
        }

        // --- Independent oracle: cheapest set of non-Ultimate products completing the Ultimate contents. ---
        // Entitlements are packed into 12 bits: M06..M15 → bits 0..9, NO_EXPORT_ADS → 10, NO_INTERSTITIALS → 11.

        static readonly int[] s_bestCover = BuildBestCover();

        static int Pack(EntitlementSet e)
        {
            int bits = 0;
            for (int m = 6; m <= 15; m++)
            {
                if (e.Contains((Entitlement)m)) bits |= 1 << (m - 6);
            }
            if (e.Contains(Entitlement.NoExportAds)) bits |= 1 << 10;
            if (e.Contains(Entitlement.NoInterstitials)) bits |= 1 << 11;
            return bits;
        }

        static int BruteForcePartsCost(EntitlementSet current)
        {
            return s_bestCover[0xFFF & ~Pack(current)];
        }

        static int[] BuildBestCover()
        {
            var others = ProductCatalog.All.Where(p => p.Id != ProductIds.Ultimate).ToArray();
            Assert.Equal(14, others.Length);
            var best = Enumerable.Repeat(int.MaxValue, 1 << 12).ToArray();
            for (int sub = 0; sub < 1 << others.Length; sub++)
            {
                int bits = 0, cost = 0;
                for (int i = 0; i < others.Length; i++)
                {
                    if ((sub & (1 << i)) == 0) continue;
                    bits |= Pack(others[i].Grants);
                    cost += others[i].BasePriceUsdCents;
                }
                if (cost < best[bits]) best[bits] = cost;
            }
            // best[need] = min over covered supersets of need.
            for (int b = 0; b < 12; b++)
            {
                for (int need = 0; need < 1 << 12; need++)
                {
                    if ((need & (1 << b)) == 0 && best[need | (1 << b)] < best[need]) best[need] = best[need | (1 << b)];
                }
            }
            return best;
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
                var a = Storefront.Build(ids, MonetizationConfig.FullCatalog, null);
                var b = Storefront.Build(shuffled, MonetizationConfig.FullCatalog, null);
                Assert.Equal(a.Offers.Select(Describe), b.Offers.Select(Describe));
                var r1a = Storefront.Build(ids, MonetizationConfig.Release1, null);
                var r1b = Storefront.Build(shuffled, MonetizationConfig.Release1, null);
                Assert.Equal(r1a.Offers.Select(Describe), r1b.Offers.Select(Describe));
            }
        }

        static string Describe(StorefrontOffer o)
        {
            return o.ProductId + "|" + o.HiddenReason + "|" + o.CheaperAlternativeId + "|" + o.Gain.Mask + "|" + o.IsLastStep + "|" + o.OwnedModeCount;
        }
    }
}
