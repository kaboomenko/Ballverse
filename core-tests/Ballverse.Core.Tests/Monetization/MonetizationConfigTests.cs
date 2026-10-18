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
        public void Release1_ShipsOnlyM01M02M05_AllFreeByDefault()
        {
            var c = MonetizationConfig.Release1;
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, c.ReleasedModes.ToArray());
            // Spec §5.0.2, §9.3: free_modes defaults to M01, M02, M05; M03/M04 only via free_modes at their release.
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, c.FreeModes.ToArray());
            Assert.Empty(c.DisabledModes);
            Assert.Empty(c.UnsoldProductIds);
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
            foreach (var ctor in t.GetConstructors())
            {
                foreach (var p in ctor.GetParameters())
                    Assert.DoesNotContain("watermark", p.Name, StringComparison.OrdinalIgnoreCase);
            }
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
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05, ModeCode.M09 }, widened.FreeModes.ToArray());
        }

        [Fact]
        public void SellableOrderAndDuplicatesDoNotDependOnInput()
        {
            var a = new MonetizationConfig(new ModeCode[0], new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.NoAds },
                new[] { ModeCode.M05, ModeCode.M01, ModeCode.M05 });
            Assert.Equal(new[] { ProductIds.RemoveExportAds, ProductIds.NoAds }, a.SellableProductIds.ToArray());
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M05 }, a.ReleasedModes.ToArray());
        }

        /// <summary>Spec §5.0.4 rule (b) is a filter: a listed product promising an unreleased mode is simply not sold.</summary>
        [Fact]
        public void AProductThatPromisesAnUnreleasedModeIsNotSold()
        {
            var r1Modes = MonetizationConfig.Release1.ReleasedModes;
            foreach (var id in new[] { ProductIds.AllModes, ProductIds.Ultimate, ProductIds.ModeOrbit })
            {
                var c = new MonetizationConfig(new ModeCode[0], new[] { id, ProductIds.NoAds }, r1Modes);
                Assert.False(c.IsSellable(id));
                Assert.Equal(new[] { ProductIds.NoAds }, c.SellableProductIds.ToArray());
                Assert.Equal(new[] { id }, c.UnsoldProductIds.ToArray());
                var shop = Storefront.Build(new string[0], c, null);
                Assert.Equal(OfferHiddenReason.NotSellable, shop.Get(id).HiddenReason);
            }
            // Releasing M14 sells its single, but not the bundles that also promise M06–M15.
            var withM14 = r1Modes.Concat(new[] { ModeCode.M14 }).ToArray();
            var m14 = new MonetizationConfig(new ModeCode[0], new[] { ProductIds.ModeOrbit, ProductIds.AllModes, ProductIds.Ultimate }, withM14);
            Assert.True(m14.IsModeSold(ModeCode.M14));
            Assert.Equal(new[] { ProductIds.ModeOrbit }, m14.SellableProductIds.ToArray());
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.Ultimate }, m14.UnsoldProductIds.ToArray());
        }

        /// <summary>
        /// QA probe P5: the build list of the full catalog with M08 missing from the build no longer throws; the M08 single,
        /// All Modes and Ultimate are unsold, everything else is sold.
        /// </summary>
        [Fact]
        public void FullListWithOneModeMissingFromTheBuildDropsItsProducts()
        {
            var build = AllModes.Where(m => m != ModeCode.M08).ToArray();
            var c = new MonetizationConfig(new ModeCode[0], ProductIds.All, build);
            var dropped = new[] { ProductIds.ModeColorWar, ProductIds.AllModes, ProductIds.Ultimate };
            Assert.Equal(ProductIds.All.Where(id => dropped.Contains(id)).ToArray(), c.UnsoldProductIds.ToArray());
            Assert.Equal(ProductIds.All.Where(id => !dropped.Contains(id)).ToArray(), c.SellableProductIds.ToArray());
            Assert.False(c.IsModeSold(ModeCode.M08));
            Assert.True(c.IsModeSold(ModeCode.M09));
        }

        /// <summary>Spec §5.0.4, §7.6: modes_disabled hides the mode and stops selling every product that grants it.</summary>
        [Fact]
        public void DisabledModeHidesModeAndUnsellsAllModesUltimateAndItsSingle()
        {
            var c = MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M12, ModeCode.M12 });
            Assert.Equal(new[] { ModeCode.M12 }, c.DisabledModes.ToArray());
            Assert.False(c.IsReleased(ModeCode.M12));
            Assert.Equal(AllModes.Where(m => m != ModeCode.M12).ToArray(), c.ReleasedModes.ToArray());
            var dropped = new[] { ProductIds.ModeChainReaction, ProductIds.AllModes, ProductIds.Ultimate };
            Assert.Equal(ProductIds.All.Where(id => dropped.Contains(id)).ToArray(), c.UnsoldProductIds.ToArray());
            foreach (var id in dropped) Assert.False(c.IsSellable(id));

            var shop = Storefront.Build(new string[0], c, null);
            foreach (var id in dropped) Assert.Equal(OfferHiddenReason.NotSellable, shop.Get(id).HiddenReason);
            Assert.True(shop.Get(ProductIds.NoAds).Visible);
            Assert.True(shop.Get(ProductIds.ModeOrbit).Visible);
            // An owner of M12 keeps the right, but the disabled mode is not playable (kill switch).
            var owned = Entitlements.From(new[] { ProductIds.ModeChainReaction }, c);
            Assert.True(owned.ContainsMode(ModeCode.M12));
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M12, owned, null, c));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M12, new string[0], c, null));

            // Disabling a default free mode hides it too; the free set itself is unchanged.
            var r1 = MonetizationConfig.Release1.WithDisabledModes(new[] { ModeCode.M02 });
            Assert.True(r1.IsFree(ModeCode.M02));
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M02, EntitlementSet.Empty, null, r1));
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M05 }, r1.ReleasedModes.ToArray());
            Assert.Equal(MonetizationConfig.Release1.SellableProductIds.ToArray(), r1.SellableProductIds.ToArray());
        }

        /// <summary>
        /// Spec §5.0.4 as a property over random build lists, build modes and modes_disabled: sold = build list ∩ products
        /// whose modes are all released and not disabled; the rest of the list is unsold; the Shop never shows an unsold one.
        /// </summary>
        [Fact]
        public void SellableIsBuildListFilteredByReleasedAndNotDisabled_Random()
        {
            var rng = Rng.FromSeed(0x5E11AB1EUL, 11UL);
            for (int iter = 0; iter < 400; iter++)
            {
                var list = ProductIds.All.Where(_ => rng.NextUInt(3) != 0).ToArray();
                var build = AllModes.Where(_ => rng.NextUInt(4) != 0).ToArray();
                var disabled = AllModes.Where(_ => rng.NextUInt(8) == 0).ToArray();
                var c = new MonetizationConfig(new ModeCode[0], list.Reverse(), build, disabled);
                var playable = build.Except(disabled).OrderBy(m => m).ToArray();
                Assert.Equal(playable, c.ReleasedModes.ToArray());
                var expectedSold = ProductIds.All.Where(id => list.Contains(id) && ProductCatalog.Get(id).Grants.ToArray()
                    .All(e => !EntitlementInfo.TryGetMode(e, out var m) || playable.Contains(m))).ToArray();
                Assert.Equal(expectedSold, c.SellableProductIds.ToArray());
                Assert.Equal(ProductIds.All.Where(id => list.Contains(id) && !expectedSold.Contains(id)).ToArray(), c.UnsoldProductIds.ToArray());
                var shop = Storefront.Build(new string[0], c, null);
                foreach (var o in shop.VisibleOffers) Assert.Contains(o.ProductId, expectedSold);
            }
        }

        [Fact]
        public void DisabledModesAreReplacedNotAccumulatedAndSurviveWithFreeModes()
        {
            var c = MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M06 }).WithFreeModes(new[] { ModeCode.M07 });
            Assert.Equal(new[] { ModeCode.M06 }, c.DisabledModes.ToArray());
            Assert.True(c.IsFree(ModeCode.M07));
            Assert.False(c.IsSellable(ProductIds.AllModes));

            var reenabled = c.WithDisabledModes(new ModeCode[0]);
            Assert.Empty(reenabled.DisabledModes);
            Assert.Equal(ProductIds.All.ToArray(), reenabled.SellableProductIds.ToArray());
            Assert.Equal(AllModes, reenabled.ReleasedModes.ToArray());
            Assert.True(reenabled.IsFree(ModeCode.M07));

            var other = c.WithDisabledModes(new[] { ModeCode.M09 });
            Assert.Equal(new[] { ModeCode.M09 }, other.DisabledModes.ToArray());
            Assert.True(other.IsReleased(ModeCode.M06));
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
            Assert.Throws<ArgumentNullException>(() => new MonetizationConfig(new ModeCode[0], AdIds, modes, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonetizationConfig(new ModeCode[0], AdIds, modes, new[] { (ModeCode)16 }));
            Assert.Throws<ArgumentNullException>(() => MonetizationConfig.Release1.WithDisabledModes(null));
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
            var d = MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M06 });
            Assert.Throws<NotSupportedException>(() => ((IList<ModeCode>)d.DisabledModes)[0] = ModeCode.M07);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)d.UnsoldProductIds)[0] = ProductIds.NoAds);
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
