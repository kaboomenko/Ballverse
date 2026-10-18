using System.Linq;
using Ballverse.Core;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    public class ProductCatalogTests
    {
        [Fact]
        public void HasExactlyTheFifteenSpecIdsInTableOrder()
        {
            var expected = new[]
            {
                "ballverse.mode.ball_battle", "ballverse.mode.last_standing", "ballverse.mode.color_war",
                "ballverse.mode.multiply", "ballverse.mode.maze_escape", "ballverse.mode.smash",
                "ballverse.mode.chain_reaction", "ballverse.mode.pendulum_wave", "ballverse.mode.orbit",
                "ballverse.mode.liquid_sand", "ballverse.bundle.all_modes", "ballverse.ads.remove_export",
                "ballverse.ads.remove_interstitial", "ballverse.bundle.no_ads", "ballverse.bundle.ultimate",
            };
            Assert.Equal(expected, ProductIds.All.ToArray());
            Assert.Equal(expected, ProductCatalog.All.Select(p => p.Id).ToArray());
        }

        [Fact]
        public void SingleModeIdsMatchModeCatalogIdsForM06ToM15()
        {
            var singles = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode).ToArray();
            Assert.Equal(10, singles.Length);
            for (int i = 0; i < 10; i++)
            {
                var mode = (ModeCode)(6 + i);
                Assert.Equal(mode, singles[i].Mode);
                Assert.Equal(ProductIds.ModePrefix + ModeCatalog.Get(mode).Id, singles[i].Id);
                Assert.False(ModeCatalog.Get(mode).FreeByDefault);
                Assert.Same(singles[i], ProductCatalog.ForMode(mode));
                Assert.Equal(Subsets.Mode(mode), singles[i].Grants);
            }
            for (int m = 1; m <= 5; m++) Assert.Null(ProductCatalog.ForMode((ModeCode)m));
        }

        [Theory]
        [InlineData("ballverse.mode.ball_battle", 199)]
        [InlineData("ballverse.mode.liquid_sand", 199)]
        [InlineData("ballverse.bundle.all_modes", 499)]
        [InlineData("ballverse.ads.remove_export", 699)]
        [InlineData("ballverse.ads.remove_interstitial", 599)]
        [InlineData("ballverse.bundle.no_ads", 899)]
        [InlineData("ballverse.bundle.ultimate", 1199)]
        public void BasePricesMatchDecisionR5(string id, int cents)
        {
            Assert.Equal(cents, ProductCatalog.Get(id).BasePriceUsdCents);
            Assert.Equal(cents / 100m, ProductCatalog.BasePricesUsd[id]);
        }

        [Fact]
        public void BundleAndAdGrantsMatchSpecTable()
        {
            Assert.Equal(EntitlementSet.PaidModes10, ProductCatalog.AllModes.Grants);
            Assert.Equal(EntitlementSet.Of(Entitlement.NoExportAds), ProductCatalog.RemoveExportAds.Grants);
            Assert.Equal(EntitlementSet.Of(Entitlement.NoInterstitials), ProductCatalog.RemoveInterstitialAds.Grants);
            Assert.Equal(EntitlementSet.AdFree, ProductCatalog.NoAds.Grants);
            Assert.Equal(EntitlementSet.AllDefined, ProductCatalog.Ultimate.Grants);
            Assert.True(ProductCatalog.Ultimate.Grants.Contains(Entitlement.NoWatermark));
            Assert.Equal(13, EntitlementSet.AllDefined.Count);
        }

        [Fact]
        public void OnlyUltimateGrantsNoWatermarkDirectly()
        {
            foreach (var p in ProductCatalog.All)
            {
                Assert.Equal(p.Id == ProductIds.Ultimate, p.Grants.Contains(Entitlement.NoWatermark));
            }
        }

        [Fact]
        public void LookupIsOrdinalAndRejectsUnknown()
        {
            Assert.True(ProductCatalog.TryGet(ProductIds.NoAds, out var p));
            Assert.Equal(ProductKind.Bundle, p.Kind);
            Assert.False(ProductCatalog.TryGet("BALLVERSE.BUNDLE.NO_ADS", out _));
            Assert.False(ProductCatalog.TryGet(null, out _));
            Assert.False(ProductCatalog.TryGet("", out _));
            Assert.Throws<System.ArgumentException>(() => ProductCatalog.Get("nope"));
        }

        [Fact]
        public void KindsAreAssigned()
        {
            Assert.Equal(ProductKind.AdRemoval, ProductCatalog.RemoveExportAds.Kind);
            Assert.Equal(ProductKind.AdRemoval, ProductCatalog.RemoveInterstitialAds.Kind);
            Assert.Equal(ProductKind.Bundle, ProductCatalog.Ultimate.Kind);
            Assert.Equal(ProductKind.Bundle, ProductCatalog.AllModes.Kind);
            Assert.Null(ProductCatalog.Ultimate.Mode);
            Assert.Equal(11.99m, ProductCatalog.Ultimate.BasePriceUsd);
        }

        [Fact]
        public void SharedListsAreReadOnly()
        {
            Assert.False(ProductIds.All is string[]);
            Assert.False(ProductCatalog.All is ProductInfo[]);
            Assert.False(EntitlementInfo.All is Entitlement[]);
            Assert.True(((System.Collections.Generic.ICollection<string>)ProductIds.All).IsReadOnly);
            Assert.Throws<System.NotSupportedException>(() => ((System.Collections.Generic.IList<string>)ProductIds.All)[0] = "x");
            Assert.Throws<System.NotSupportedException>(() => ((System.Collections.Generic.IDictionary<string, decimal>)ProductCatalog.BasePricesUsd)[ProductIds.Ultimate] = 0m);
        }
    }
}
