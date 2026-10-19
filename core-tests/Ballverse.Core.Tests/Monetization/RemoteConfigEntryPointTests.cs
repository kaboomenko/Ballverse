using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Config;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// The App's single entry point for a Remote Config snapshot, <see cref="MonetizationConfig.WithRemote"/>: it takes
    /// <c>free_modes</c> and the <c>modes_disabled</c> kill switch together, so the kill switch cannot be lost (spec §5.0.4
    /// «Remote Config не может добавить товар в продажу (только скрыть режим через modes_disabled)», §7.6, §9.3).
    /// </summary>
    public class RemoteConfigEntryPointTests
    {
        static readonly ModeCode[] None = new ModeCode[0];

        static RemoteConfigValues Snapshot(params ModeCode[] disabled)
        {
            return new RemoteConfigBuilder { ModesDisabled = new List<ModeCode>(disabled) }.Build();
        }

        [Fact]
        public void Release1_DisabledDefaultFreeModeIsNotPlayable()
        {
            var values = Snapshot(ModeCode.M02);
            // Both inputs the config module offers give the same result.
            foreach (var free in new[] { values.FreeModes, values.EffectiveFreeModes })
            {
                var c = MonetizationConfig.Default.WithRemote(free, values.ModesDisabled);
                Assert.Equal(new[] { ModeCode.M02 }, c.DisabledModes.ToArray());
                Assert.False(c.IsReleased(ModeCode.M02));
                Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M02, EntitlementSet.Empty, null, c));
                Assert.False(ModeAvailability.IsAvailable(ModeCode.M02, EntitlementSet.Empty, new[] { ModeCode.M02 }, c));
                Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M01, EntitlementSet.Empty, null, c));
                Assert.Equal(ModeAccess.Free, ModeAvailability.GetAccess(ModeCode.M05, EntitlementSet.Empty, null, c));
                // The ad products grant no mode and stay on sale.
                Assert.Equal(MonetizationConfig.Release1.SellableProductIds.ToArray(), c.SellableProductIds.ToArray());
            }
        }

        [Fact]
        public void Release1_DefaultSnapshotEqualsThePreset()
        {
            var values = RemoteConfigValues.Defaults;
            var c = MonetizationConfig.Default.WithRemote(values.FreeModes, values.ModesDisabled);
            Assert.Empty(c.DisabledModes);
            Assert.Equal(MonetizationConfig.Release1.FreeModes.ToArray(), c.FreeModes.ToArray());
            Assert.Equal(MonetizationConfig.Release1.ReleasedModes.ToArray(), c.ReleasedModes.ToArray());
            Assert.Equal(MonetizationConfig.Release1.SellableProductIds.ToArray(), c.SellableProductIds.ToArray());
        }

        [Fact]
        public void FullCatalog_DisabledPaidModeStopsItsProducts()
        {
            var c = MonetizationConfig.FullCatalog.WithRemote(None, new[] { ModeCode.M12 });
            Assert.False(c.IsReleased(ModeCode.M12));
            Assert.False(c.IsModeSold(ModeCode.M12));
            foreach (var id in new[] { ProductIds.ModeChainReaction, ProductIds.AllModes, ProductIds.Ultimate })
            {
                Assert.False(c.IsSellable(id));
                Assert.Contains(id, c.UnsoldProductIds);
            }
            Assert.Equal(3, c.UnsoldProductIds.Count);
            Assert.True(c.IsSellable(ProductIds.NoAds));
            Assert.True(c.IsSellable(ProductIds.ModeOrbit));

            var s = Storefront.Build(new string[0], c, null);
            Assert.All(new[] { ProductIds.ModeChainReaction, ProductIds.AllModes, ProductIds.Ultimate },
                id => Assert.Equal(OfferHiddenReason.NotSellable, s.Get(id).HiddenReason));
            Assert.Empty(Storefront.ModeSheet(ModeCode.M12, new string[0], c, null));
        }

        [Fact]
        public void WithRemote_ReplacesBothKeys()
        {
            var c = MonetizationConfig.FullCatalog.WithRemote(new[] { ModeCode.M06 }, new[] { ModeCode.M09 });
            var d = c.WithRemote(new[] { ModeCode.M07 }, None);
            Assert.False(d.IsFree(ModeCode.M06));
            Assert.True(d.IsFree(ModeCode.M07));
            Assert.Empty(d.DisabledModes);
            Assert.True(d.IsReleased(ModeCode.M09));
            Assert.Equal(ProductIds.All.ToArray(), d.SellableProductIds.ToArray());

            var chained = MonetizationConfig.FullCatalog.WithFreeModes(new[] { ModeCode.M06 }).WithDisabledModes(new[] { ModeCode.M09 });
            Assert.Equal(chained.FreeModes.ToArray(), c.FreeModes.ToArray());
            Assert.Equal(chained.ReleasedModes.ToArray(), c.ReleasedModes.ToArray());
            Assert.Equal(chained.SellableProductIds.ToArray(), c.SellableProductIds.ToArray());
            Assert.Equal(chained.UnsoldProductIds.ToArray(), c.UnsoldProductIds.ToArray());
        }

        [Fact]
        public void WithRemote_RejectsNullAndUnknownModes()
        {
            Assert.Throws<ArgumentNullException>(() => MonetizationConfig.Default.WithRemote(null, None));
            Assert.Throws<ArgumentNullException>(() => MonetizationConfig.Default.WithRemote(None, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => MonetizationConfig.Default.WithRemote(None, new[] { (ModeCode)16 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => MonetizationConfig.Default.WithRemote(new[] { (ModeCode)0 }, None));
        }

        /// <summary>The remote keys never widen the release catalog: a free unreleased mode stays unplayable and unsold.</summary>
        [Fact]
        public void WithRemote_NeverAddsProductsOrModes()
        {
            var c = MonetizationConfig.Release1.WithRemote(new[] { ModeCode.M06, ModeCode.M03 }, None);
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M06, EntitlementSet.Empty, null, c));
            Assert.Equal(ModeAccess.NotReleased, ModeAvailability.GetAccess(ModeCode.M03, EntitlementSet.Empty, null, c));
            Assert.Equal(MonetizationConfig.Release1.SellableProductIds.ToArray(), c.SellableProductIds.ToArray());
        }
    }
}
