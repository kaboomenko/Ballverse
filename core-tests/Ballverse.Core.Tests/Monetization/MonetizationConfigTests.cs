using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Release catalog presets and validation (owner decisions for release 1; spec §5.1).</summary>
    public class MonetizationConfigTests
    {
        static readonly string[] AdIds = { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.NoAds };
        static readonly ModeCode[] AllModes = Enumerable.Range(1, 15).Select(m => (ModeCode)m).ToArray();

        [Fact]
        public void Release1_SellsOnlyTheThreeAdProductsInCatalogOrder()
        {
            var c = MonetizationConfig.Release1;
            Assert.Equal(AdIds, c.SellableProductIds.ToArray());
            foreach (var id in ProductIds.All) Assert.Equal(AdIds.Contains(id), c.IsSellable(id));
            Assert.Equal(new[] { 699, 599, 899 }, c.SellableProductIds.Select(id => ProductCatalog.Get(id).BasePriceUsdCents).ToArray());
            Assert.Equal(EntitlementSet.AdFree, c.SellableGrants);
        }

        [Fact]
        public void Release1_ShipsOnlyM01M02M05_FreeSetStaysM01ToM05()
        {
            var c = MonetizationConfig.Release1;
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, c.ReleasedModes.ToArray());
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05 }, c.FreeModes.ToArray());
            foreach (var m in AllModes)
            {
                Assert.Equal(m == ModeCode.M01 || m == ModeCode.M02 || m == ModeCode.M05, c.IsReleased(m));
                Assert.False(c.IsModeSold(m));
            }
        }

        /// <summary>Spec §5.0.2: the watermark rule has no config switch; both presets grant it the same way.</summary>
        [Fact]
        public void WatermarkRuleIsNotConfigurable()
        {
            var t = typeof(MonetizationConfig);
            foreach (var m in t.GetMembers())
                Assert.DoesNotContain("Watermark", m.Name);
            Assert.Single(t.GetConstructors());
            Assert.Equal(3, t.GetConstructors()[0].GetParameters().Length);
        }

        [Fact]
        public void FullCatalog_EverythingReleasedAndSellable_NotTheDefault()
        {
            var c = MonetizationConfig.FullCatalog;
            Assert.NotSame(MonetizationConfig.Default, c);
            Assert.Equal(ProductIds.All.ToArray(), c.SellableProductIds.ToArray());
            Assert.Equal(AllModes, c.ReleasedModes.ToArray());
            for (int m = 6; m <= 15; m++) Assert.True(c.IsModeSold((ModeCode)m));
            for (int m = 1; m <= 5; m++) Assert.False(c.IsModeSold((ModeCode)m));
            Assert.Equal(EntitlementSet.AllDefined, c.SellableGrants);
        }

        [Fact]
        public void LegacyConstructorBuildsFullCatalog()
        {
            var c = new MonetizationConfig(new[] { ModeCode.M07 });
            Assert.Equal(ProductIds.All.ToArray(), c.SellableProductIds.ToArray());
            Assert.Equal(AllModes, c.ReleasedModes.ToArray());
            Assert.True(c.IsFree(ModeCode.M07));
        }

        [Fact]
        public void WithFreeModes_KeepsReleaseCatalogAndExtendsOnly()
        {
            var c = MonetizationConfig.Release1.WithFreeModes(new ModeCode[0]);
            Assert.Equal(MonetizationConfig.Release1.FreeModes.ToArray(), c.FreeModes.ToArray());
            Assert.Equal(AdIds, c.SellableProductIds.ToArray());
            Assert.Equal(MonetizationConfig.Release1.ReleasedModes.ToArray(), c.ReleasedModes.ToArray());

            var widened = MonetizationConfig.Release1.WithFreeModes(new[] { ModeCode.M09 });
            Assert.True(widened.IsFree(ModeCode.M09));
            Assert.False(widened.IsReleased(ModeCode.M09));
            Assert.Equal(6, widened.FreeModes.Count);
        }

        [Fact]
        public void SellableOrderAndDuplicatesDoNotDependOnInput()
        {
            var a = new MonetizationConfig(new ModeCode[0], new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.NoAds },
                new[] { ModeCode.M05, ModeCode.M01, ModeCode.M05 });
            Assert.Equal(new[] { ProductIds.RemoveExportAds, ProductIds.NoAds }, a.SellableProductIds.ToArray());
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M05 }, a.ReleasedModes.ToArray());
        }

        [Fact]
        public void SellingAProductThatPromisesAnUnreleasedModeThrows()
        {
            var r1Modes = MonetizationConfig.Release1.ReleasedModes;
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { ProductIds.AllModes }, r1Modes));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { ProductIds.Ultimate }, r1Modes));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { ProductIds.ModeOrbit }, r1Modes));
            // Releasing M14 allows its single, but not the bundles that also promise M06–M15.
            var withM14 = r1Modes.Concat(new[] { ModeCode.M14 }).ToArray();
            var c = new MonetizationConfig(new ModeCode[0], new[] { ProductIds.ModeOrbit }, withM14);
            Assert.True(c.IsModeSold(ModeCode.M14));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { ProductIds.AllModes }, withM14));
        }

        [Fact]
        public void InvalidInputThrows()
        {
            var modes = MonetizationConfig.Release1.ReleasedModes;
            Assert.Throws<ArgumentNullException>(() => new MonetizationConfig(null, AdIds, modes));
            Assert.Throws<ArgumentNullException>(() => new MonetizationConfig(new ModeCode[0], null, modes));
            Assert.Throws<ArgumentNullException>(() => new MonetizationConfig(new ModeCode[0], AdIds, null));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { "ballverse.unknown" }, modes));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new string[] { null }, modes));
            Assert.Throws<ArgumentException>(() => new MonetizationConfig(new ModeCode[0], new[] { "BALLVERSE.BUNDLE.NO_ADS" }, modes));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new ModeCode[0], AdIds, new[] { (ModeCode)16 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new ModeCode[0], AdIds, new[] { (ModeCode)0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new[] { (ModeCode)99 }, AdIds, modes));
        }

        [Fact]
        public void IsSellableIsOrdinalAndRejectsNull()
        {
            var c = MonetizationConfig.Release1;
            Assert.False(c.IsSellable(null));
            Assert.False(c.IsSellable(""));
            Assert.False(c.IsSellable("BALLVERSE.BUNDLE.NO_ADS"));
            Assert.False(c.IsModeSold((ModeCode)0));
        }

        [Fact]
        public void ListsAreReadOnly()
        {
            var c = MonetizationConfig.Release1;
            Assert.Throws<NotSupportedException>(() => ((IList<string>)c.SellableProductIds)[0] = ProductIds.Ultimate);
            Assert.Throws<NotSupportedException>(() => ((IList<ModeCode>)c.ReleasedModes)[0] = ModeCode.M06);
            Assert.Throws<NotSupportedException>(() => ((IList<ModeCode>)c.FreeModes)[0] = ModeCode.M06);
        }

        [Fact]
        public void EmptySellableCatalogIsAllowed()
        {
            var c = new MonetizationConfig(new ModeCode[0], new string[0], new[] { ModeCode.M01 });
            Assert.Empty(c.SellableProductIds);
            Assert.Equal(EntitlementSet.Empty, c.SellableGrants);
            var s = Storefront.Build(new string[0], c, null);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.All(s.Offers, o => Assert.Equal(OfferHiddenReason.NotSellable, o.HiddenReason));
            Assert.Empty(Storefront.WatermarkOffers(new string[0], c, null));
            Assert.Empty(SalePointPolicy.DoneScreenLineOffers(3, null, new string[0], c, null));
        }
    }
}
