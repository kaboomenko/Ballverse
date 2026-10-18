using System;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release-1 NO_WATERMARK rule (No Ads or both ad removals) and its grandfathering when a later release turns it off
    /// (owner decision; spec §3.10, §5.1, §5.4).
    /// </summary>
    public class WatermarkGrandfatheringTests
    {
        static readonly MonetizationConfig R1 = MonetizationConfig.Release1;
        static readonly MonetizationConfig Later = MonetizationConfig.FullCatalog; // Ultimate back, rule off
        static readonly string[] NoAds = { ProductIds.NoAds };
        static readonly string[] BothRemovals = { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds };
        static readonly string[] Nothing = new string[0];

        static bool HasNw(string[] owned, MonetizationConfig config, bool gf)
        {
            return Entitlements.From(owned, config, gf).Contains(Entitlement.NoWatermark);
        }

        // ---- The rule itself ----

        [Fact]
        public void Release1_NoAdsOrBothRemovalsGrantNoWatermark()
        {
            Assert.True(HasNw(NoAds, R1, false));
            Assert.True(HasNw(BothRemovals, R1, false));
            Assert.False(HasNw(new[] { ProductIds.RemoveExportAds }, R1, false));
            Assert.False(HasNw(new[] { ProductIds.RemoveInterstitialAds }, R1, false));
            Assert.False(HasNw(Nothing, R1, false));
            Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark), Entitlements.From(NoAds, R1));
        }

        [Fact]
        public void FullCatalog_NoAdsAloneDoesNotGrantNoWatermark()
        {
            Assert.False(HasNw(NoAds, Later, false));
            Assert.False(HasNw(BothRemovals, Later, false));
            Assert.True(HasNw(new[] { ProductIds.Ultimate }, Later, false));
            Assert.True(HasNw(new[] { ProductIds.AllModes, ProductIds.NoAds }, Later, false));
        }

        // ---- Update transitions ----

        [Fact]
        public void Update_RuleOn_QualifiesWithAdFree()
        {
            Assert.True(WatermarkGrandfathering.Update(false, NoAds, R1));
            Assert.True(WatermarkGrandfathering.Update(false, BothRemovals, R1));
            Assert.True(WatermarkGrandfathering.Update(false, new[] { ProductIds.Ultimate }, R1));
            Assert.False(WatermarkGrandfathering.Update(false, new[] { ProductIds.RemoveExportAds }, R1));
            Assert.False(WatermarkGrandfathering.Update(false, Nothing, R1));
            Assert.False(WatermarkGrandfathering.Update(false, new[] { ProductIds.AllModes }, R1));
        }

        [Fact]
        public void Update_RuleOff_NeverSetsTheFlag()
        {
            Assert.False(WatermarkGrandfathering.Update(false, NoAds, Later));
            Assert.False(WatermarkGrandfathering.Update(false, BothRemovals, Later));
            Assert.False(WatermarkGrandfathering.Update(false, new[] { ProductIds.Ultimate }, Later));
        }

        [Fact]
        public void Update_NeverClearsTheFlag()
        {
            foreach (var config in new[] { R1, Later, R1.WithWatermarkWithNoAds(false) })
            {
                Assert.True(WatermarkGrandfathering.Update(true, Nothing, config));
                Assert.True(WatermarkGrandfathering.Update(true, NoAds, config));
                Assert.True(WatermarkGrandfathering.Update(true, EntitlementSet.Empty, config));
            }
        }

        [Fact]
        public void Update_SecondAdRemovalBoughtLater()
        {
            bool gf = WatermarkGrandfathering.Update(false, new[] { ProductIds.RemoveExportAds }, R1);
            Assert.False(gf);
            gf = WatermarkGrandfathering.Update(gf, BothRemovals, R1);
            Assert.True(gf);
        }

        [Fact]
        public void Update_EntitlementOverloadMatchesProductOverload()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 5)
            {
                var owned = Subsets.Ids(mask);
                foreach (var config in new[] { R1, Later })
                {
                    foreach (bool gf in new[] { false, true })
                    {
                        Assert.Equal(WatermarkGrandfathering.Update(gf, owned, config),
                            WatermarkGrandfathering.Update(gf, Entitlements.DirectGrants(owned), config));
                    }
                }
            }
        }

        [Fact]
        public void Update_NullOwnedThrows()
        {
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.Update(false, (string[])null, R1));
        }

        // ---- Release 1 → later release (rule turned off) ----

        [Fact]
        public void RuleTurnedOff_GrandfatheredUserKeepsNoWatermark()
        {
            bool gf = WatermarkGrandfathering.Update(false, NoAds, R1);
            Assert.True(HasNw(NoAds, R1, gf));
            gf = WatermarkGrandfathering.Update(gf, NoAds, Later);
            Assert.True(gf);
            var e = Entitlements.From(NoAds, Later, gf);
            Assert.True(e.Contains(Entitlement.NoWatermark));
            Assert.Equal(WatermarkRow.Toggle, WatermarkPolicy.GetRow(e));
            Assert.False(WatermarkPolicy.ShouldApply(e));
            Assert.Empty(Storefront.WatermarkOffers(NoAds, Later, null, gf));
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(9, null, e));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(9, null, NoAds, Later, null, gf));

            // The later shop does not re-sell the watermark: Ultimate's gain lacks it and All Modes (cheaper) gives the same.
            var s = Storefront.Build(NoAds, Later, null, gf);
            Assert.False(s.Get(ProductIds.Ultimate).Gain.Contains(Entitlement.NoWatermark));
            Assert.Equal(OfferHiddenReason.CheaperEquivalent, s.Get(ProductIds.Ultimate).HiddenReason);
            Assert.False(s.Get(ProductIds.AllModes).IsLastStep);
        }

        [Fact]
        public void RuleTurnedOff_BothRemovalsUserKeepsNoWatermark()
        {
            bool gf = WatermarkGrandfathering.Update(false, BothRemovals, R1);
            Assert.True(HasNw(BothRemovals, Later, WatermarkGrandfathering.Update(gf, BothRemovals, Later)));
        }

        [Fact]
        public void RuleTurnedOff_NonGrandfatheredUserDoesNotGetIt()
        {
            // Bought No Ads only after the rule was turned off.
            bool gf = WatermarkGrandfathering.Update(false, NoAds, Later);
            Assert.False(gf);
            Assert.False(HasNw(NoAds, Later, gf));
            Assert.Equal(new[] { ProductIds.Ultimate }, Storefront.WatermarkOffers(Nothing, Later).Select(o => o.ProductId).ToArray());
            // With No Ads, All Modes is the last step to the watermark (by parts).
            var wm = Storefront.WatermarkOffers(NoAds, Later, null, gf).Select(o => o.ProductId).ToArray();
            Assert.Equal(new[] { ProductIds.AllModes }, wm);
        }

        [Fact]
        public void RuleTurnedOff_UserWithOnlyOneRemovalUnderRule_DoesNotGetItLater()
        {
            bool gf = WatermarkGrandfathering.Update(false, new[] { ProductIds.RemoveExportAds }, R1);
            Assert.False(gf);
            // Buys the second removal after the rule is off: no watermark right.
            gf = WatermarkGrandfathering.Update(gf, BothRemovals, Later);
            Assert.False(gf);
            Assert.False(HasNw(BothRemovals, Later, gf));
        }

        [Fact]
        public void Grandfathered_RefundBringsWatermarkBack_RebuyRestoresIt()
        {
            bool gf = WatermarkGrandfathering.Update(false, NoAds, R1);
            // Refund under the later release (spec §3.10 / §5.4): the right follows the products.
            gf = WatermarkGrandfathering.Update(gf, Nothing, Later);
            Assert.True(gf);
            var refunded = Entitlements.From(Nothing, Later, gf);
            Assert.False(refunded.Contains(Entitlement.NoWatermark));
            Assert.True(WatermarkPolicy.ShouldApply(refunded));
            // Re-buying the ad removals restores it; the flag survived the refund.
            Assert.True(HasNw(NoAds, Later, gf));
            Assert.True(HasNw(BothRemovals, Later, gf));
            Assert.False(HasNw(new[] { ProductIds.RemoveExportAds }, Later, gf));
        }

        [Fact]
        public void Grandfathered_TransientlyEmptyOwnedListDoesNotWipeTheFlag()
        {
            bool gf = WatermarkGrandfathering.Update(false, NoAds, R1);
            gf = WatermarkGrandfathering.Update(gf, Nothing, R1); // e.g. store unavailable, restore not finished
            Assert.True(gf);
            Assert.True(HasNw(NoAds, Later, gf));
        }

        [Fact]
        public void RuleOffThenOnAgain()
        {
            var off = R1.WithWatermarkWithNoAds(false);
            bool gf = WatermarkGrandfathering.Update(false, NoAds, off);
            Assert.False(gf);
            Assert.False(HasNw(NoAds, off, gf));
            gf = WatermarkGrandfathering.Update(gf, NoAds, R1);
            Assert.True(gf);
            Assert.True(HasNw(NoAds, off, gf));
        }

        [Fact]
        public void GrandfatheredFlagIsHarmlessWhileRuleIsOn()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 3)
            {
                var owned = Subsets.Ids(mask);
                Assert.Equal(Entitlements.From(owned, R1, false), Entitlements.From(owned, R1, true));
            }
        }

        [Fact]
        public void PendingNoAdsDoesNotQualify()
        {
            var owned = OwnedProducts.From(new[] { new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.1") }).ToArray();
            Assert.False(WatermarkGrandfathering.Update(false, owned, R1));
            Assert.False(HasNw(owned, R1, false));
        }

        [Fact]
        public void LaterShopForGrandfatheredUser_BuyingEverythingVisibleCompletesEverything()
        {
            bool gf = true;
            var owned = NoAds.ToList();
            for (int round = 0; round < 3; round++)
            {
                var visible = Storefront.Build(owned, Later, null, gf).VisibleOffers;
                if (visible.Count == 0) break;
                owned.AddRange(visible.Select(o => o.ProductId));
            }
            var final = Storefront.Build(owned, Later, null, gf);
            Assert.True(final.AllPurchased);
            Assert.Equal(EntitlementSet.AllDefined, final.Entitlements);
        }

        [Fact]
        public void CacheEntryPassesTheFlag()
        {
            var key = new byte[32];
            var json = EntitlementCache.Serialize(NoAds, 0L, key);
            var entry = EntitlementCache.Load(json, key);
            Assert.False(entry.GetEntitlements(Later).Contains(Entitlement.NoWatermark));
            Assert.True(entry.GetEntitlements(Later, true).Contains(Entitlement.NoWatermark));
            Assert.True(entry.GetEntitlements(R1).Contains(Entitlement.NoWatermark));
        }
    }
}
