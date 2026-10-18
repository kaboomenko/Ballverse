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
            Assert.Equal(EntitlementSet.Empty, Entitlements.From(new string[0]));
        }

        [Fact]
        public void UltimateGivesEverything()
        {
            Assert.Equal(EntitlementSet.AllDefined, Entitlements.From(new[] { ProductIds.Ultimate }));
        }

        [Fact]
        public void AllModesPlusNoAdsGivesNoWatermarkByParts()
        {
            var e = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds });
            Assert.Equal(EntitlementSet.AllDefined, e);
        }

        [Fact]
        public void TenSinglesPlusBothAdRemovalsGivesNoWatermark()
        {
            var ids = ProductCatalog.All.Where(p => p.Kind != ProductKind.Bundle).Select(p => p.Id).ToList();
            Assert.Equal(12, ids.Count);
            Assert.True(Entitlements.From(ids).Contains(Entitlement.NoWatermark));
            ids.Remove(ProductIds.ModeOrbit);
            Assert.False(Entitlements.From(ids).Contains(Entitlement.NoWatermark));
        }

        [Fact]
        public void PartialContentsDoNotGiveNoWatermark()
        {
            Assert.False(Entitlements.From(new[] { ProductIds.AllModes, ProductIds.RemoveExportAds }).Contains(Entitlement.NoWatermark));
            Assert.False(Entitlements.From(new[] { ProductIds.NoAds }).Contains(Entitlement.NoWatermark));
        }

        [Fact]
        public void WatermarkByPartsCanBeDisabledByConfig()
        {
            var config = new MonetizationConfig(MonetizationConfig.Default.FreeModes, watermarkByParts: false);
            var parts = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds }, config);
            Assert.Equal(Full, parts);
            Assert.True(Entitlements.From(new[] { ProductIds.Ultimate }, config).Contains(Entitlement.NoWatermark));
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
            var e = Entitlements.From(new[] { "ballverse.mode.future_mode", null, "", ProductIds.ModeSmash, ProductIds.ModeSmash, "BALLVERSE.BUNDLE.ULTIMATE" });
            Assert.Equal(Subsets.Mode(ModeCode.M11), e);
        }

        [Fact]
        public void NullListThrows()
        {
            Assert.Throws<ArgumentNullException>(() => Entitlements.From(null));
        }

        [Fact]
        public void OrderDoesNotMatter()
        {
            var a = Entitlements.From(new[] { ProductIds.NoAds, ProductIds.ModeOrbit, ProductIds.AllModes });
            var b = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.ModeOrbit, ProductIds.NoAds });
            Assert.Equal(a, b);
        }

        /// <summary>Spec §5.1: exhaustive check of all 2^15 product combinations.</summary>
        [Fact]
        public void ExhaustiveAllSubsetsInvariants()
        {
            var noParts = new MonetizationConfig(MonetizationConfig.Default.FreeModes, false);
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var ids = Subsets.Ids(mask);
                var e = Entitlements.From(ids);

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

                // NO_WATERMARK iff Ultimate or the full contents.
                bool nw = Subsets.Has(mask, ProductIds.Ultimate) || union.IsSupersetOf(Full);
                Assert.Equal(nw, e.Contains(Entitlement.NoWatermark));
                Assert.Equal(Subsets.Has(mask, ProductIds.Ultimate), Entitlements.From(ids, noParts).Contains(Entitlement.NoWatermark));

                // Monotone: owning more never removes a right.
                if (mask != 0)
                {
                    int lower = mask & (mask - 1);
                    Assert.True(e.IsSupersetOf(Entitlements.From(Subsets.Ids(lower))));
                }
            }
        }
    }
}
