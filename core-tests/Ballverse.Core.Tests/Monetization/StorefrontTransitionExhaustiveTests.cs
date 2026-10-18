using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Full-catalog storefront invariants (<see cref="StorefrontExhaustiveTests.Check"/>) for releases after release 1: the No
    /// Ads watermark rule still on, or off with users grandfathered under it (owner decision; spec §5.1, §5.2). Strided over
    /// the 2^15 subsets (disjoint residues per test) to keep the suite fast; the default full catalog stays exhaustive in
    /// <see cref="StorefrontExhaustiveTests"/>.
    /// </summary>
    public class StorefrontTransitionExhaustiveTests
    {
        [Fact]
        public void InvariantsHold_FullCatalogWithNoAdsRule()
        {
            var config = MonetizationConfig.FullCatalog.WithWatermarkWithNoAds(true);
            for (int mask = 0; mask < Subsets.Count; mask += 4) StorefrontExhaustiveTests.Check(mask, config);
        }

        /// <summary>The by-parts rule off, the No Ads rule on.</summary>
        [Fact]
        public void InvariantsHold_FullCatalogNoAdsRuleWithoutByParts()
        {
            var config = MonetizationConfig.FullCatalog.WithWatermarkWithNoAds(true).WithWatermarkByParts(false);
            for (int mask = 1; mask < Subsets.Count; mask += 8) StorefrontExhaustiveTests.Check(mask, config);
        }

        /// <summary>A full-catalog release for a user grandfathered under the release-1 No Ads rule.</summary>
        [Fact]
        public void InvariantsHold_FullCatalogWatermarkGrandfathered()
        {
            for (int mask = 2; mask < Subsets.Count; mask += 4) StorefrontExhaustiveTests.Check(mask, MonetizationConfig.FullCatalog, true);
        }

        /// <summary>Grandfathered user, by-parts rule revoked too.</summary>
        [Fact]
        public void InvariantsHold_WithoutByPartsWatermarkGrandfathered()
        {
            var config = MonetizationConfig.FullCatalog.WithWatermarkByParts(false);
            for (int mask = 3; mask < Subsets.Count; mask += 8) StorefrontExhaustiveTests.Check(mask, config, true);
        }
    }
}
