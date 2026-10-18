using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    public class EntitlementsTests
    {
        static readonly EntitlementSet Full = Entitlements.FullUltimateContents;

        [Fact]
        public void NothingOwnedGivesNothing()
        {
            Assert.Equal(EntitlementSet.Empty, Entitlements.From(new string[0], MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void UltimateGivesEverything()
        {
            Assert.Equal(EntitlementSet.AllDefined, Entitlements.From(new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void AllModesPlusNoAdsGivesNoWatermarkByParts()
        {
            var e = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds }, MonetizationConfig.FullCatalog);
            Assert.Equal(EntitlementSet.AllDefined, e);
        }

        [Fact]
        public void TenSinglesPlusBothAdRemovalsGivesNoWatermark()
        {
            var ids = ProductCatalog.All.Where(p => p.Kind != ProductKind.Bundle).Select(p => p.Id).ToList();
            Assert.Equal(12, ids.Count);
            Assert.True(Entitlements.From(ids, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
            // The modes are not needed (spec §5.0.2: the by-parts rule is absorbed by the ad rule) ...
            ids.Remove(ProductIds.ModeOrbit);
            Assert.True(Entitlements.From(ids, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
            // ... but both ad rights are.
            ids.Remove(ProductIds.RemoveInterstitialAds);
            Assert.False(Entitlements.From(ids, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
        }

        [Fact]
        public void OneAdRightDoesNotGiveNoWatermark()
        {
            Assert.False(Entitlements.From(new[] { ProductIds.AllModes, ProductIds.RemoveExportAds }, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
            Assert.False(Entitlements.From(new[] { ProductIds.RemoveInterstitialAds }, MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
        }

        /// <summary>Spec §5.0.2: NO_WATERMARK = Ultimate ∨ (NO_EXPORT_ADS ∧ NO_INTERSTITIALS), in every catalog.</summary>
        [Fact]
        public void BothAdRightsGiveNoWatermarkInBothPresets()
        {
            foreach (var c in new[] { MonetizationConfig.Release1, MonetizationConfig.FullCatalog })
            {
                Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark), Entitlements.From(new[] { ProductIds.NoAds }, c));
                Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark),
                    Entitlements.From(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, c));
                Assert.Equal(EntitlementSet.AllDefined, Entitlements.From(new[] { ProductIds.Ultimate }, c));
            }
        }

        [Fact]
        public void RuleDoesNotDependOnConfig()
        {
            var configs = new[]
            {
                MonetizationConfig.Release1, MonetizationConfig.FullCatalog, MonetizationConfig.Default,
                MonetizationConfig.Release1.WithFreeModes(new[] { ModeCode.M06 }),
                new MonetizationConfig(new ModeCode[0], new string[0], new[] { ModeCode.M01 }),
            };
            for (int mask = 0; mask < Subsets.Count; mask += 37)
            {
                var ids = Subsets.Ids(mask);
                var expected = Entitlements.From(ids, MonetizationConfig.FullCatalog);
                foreach (var c in configs) Assert.Equal(expected, Entitlements.From(ids, c));
            }
        }

        [Fact]
        public void NullConfigThrows()
        {
            Assert.Throws<ArgumentNullException>(() => Entitlements.From(new[] { ProductIds.NoAds }, null));
        }

        [Fact]
        public void ApplyRulesAddsNoWatermarkOnlyForBothAdRights()
        {
            Assert.Equal(EntitlementSet.Empty, Entitlements.ApplyRules(EntitlementSet.Empty));
            Assert.Equal(EntitlementSet.Of(Entitlement.NoExportAds), Entitlements.ApplyRules(EntitlementSet.Of(Entitlement.NoExportAds)));
            Assert.Equal(EntitlementSet.PaidModes10, Entitlements.ApplyRules(EntitlementSet.PaidModes10));
            Assert.Equal(EntitlementSet.AdFree.With(Entitlement.NoWatermark), Entitlements.ApplyRules(EntitlementSet.AdFree));
            Assert.Equal(EntitlementSet.AllDefined, Entitlements.ApplyRules(Full));
        }

        [Fact]
        public void FullContentsAreFixedTo1Point0Catalog()
        {
            Assert.Equal(12, Full.Count);
            Assert.Equal(EntitlementSet.PaidModes10.Union(EntitlementSet.AdFree), Full);
            Assert.False(Full.Contains(Entitlement.NoWatermark));
        }

        [Fact]
        public void UnknownNullAndDuplicateIdsAreIgnored()
        {
            var e = Entitlements.From(new[] { "ballverse.mode.future_mode", null, "", ProductIds.ModeSmash, ProductIds.ModeSmash, "BALLVERSE.BUNDLE.ULTIMATE" }, MonetizationConfig.FullCatalog);
            Assert.Equal(Subsets.Mode(ModeCode.M11), e);
        }

        [Fact]
        public void NullListThrows()
        {
            Assert.Throws<ArgumentNullException>(() => Entitlements.From(null, MonetizationConfig.FullCatalog));
        }

        [Fact]
        public void OrderDoesNotMatter()
        {
            var a = Entitlements.From(new[] { ProductIds.NoAds, ProductIds.ModeOrbit, ProductIds.AllModes }, MonetizationConfig.FullCatalog);
            var b = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.ModeOrbit, ProductIds.NoAds }, MonetizationConfig.FullCatalog);
            Assert.Equal(a, b);
        }

        /// <summary>Spec §5.1: exhaustive check of all 2^15 product combinations.</summary>
        [Fact]
        public void ExhaustiveAllSubsetsInvariants()
        {
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var ids = Subsets.Ids(mask);
                var e = Entitlements.From(ids, MonetizationConfig.FullCatalog);

                // Exact union of direct grants, recomputed independently.
                var union = EntitlementSet.Empty;
                foreach (var id in ids) union = union.Union(ProductCatalog.Get(id).Grants);
                Assert.Equal(union.Intersect(Full), e.Intersect(Full));

                // Mode entitlements: MODE_Mx iff All Modes, Ultimate or the single mode is owned.
                for (int m = 6; m <= 15; m++)
                {
                    var single = ProductCatalog.ForMode((ModeCode)m).Id;
                    bool expected = Subsets.Has(mask, ProductIds.AllModes) || Subsets.Has(mask, ProductIds.Ultimate) || Subsets.Has(mask, single);
                    Assert.Equal(expected, e.ContainsMode((ModeCode)m));
                }

                bool ads = Subsets.Has(mask, ProductIds.NoAds) || Subsets.Has(mask, ProductIds.Ultimate);
                Assert.Equal(ads || Subsets.Has(mask, ProductIds.RemoveExportAds), e.Contains(Entitlement.NoExportAds));
                Assert.Equal(ads || Subsets.Has(mask, ProductIds.RemoveInterstitialAds), e.Contains(Entitlement.NoInterstitials));

                // NO_WATERMARK iff Ultimate or both ad rights (spec §5.0.2); the full contents imply both.
                bool nw = Subsets.Has(mask, ProductIds.Ultimate) || union.IsSupersetOf(EntitlementSet.AdFree);
                Assert.Equal(nw, e.Contains(Entitlement.NoWatermark));
                if (union.IsSupersetOf(Full)) Assert.True(e.Contains(Entitlement.NoWatermark));

                // Monotone: owning more never removes a right.
                if (mask != 0)
                {
                    int lower = mask & (mask - 1);
                    Assert.True(e.IsSupersetOf(Entitlements.From(Subsets.Ids(lower), MonetizationConfig.FullCatalog)));
                }
            }
        }
    }
}
