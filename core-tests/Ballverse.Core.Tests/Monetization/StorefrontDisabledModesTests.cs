using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// The Shop with disabled or unsold modes (spec §5.0.4 rule (b), §7.6 <c>modes_disabled</c>): "Всё куплено" is
    /// computed from the products actually sold, and a disabled mode is never "available" (free or grandfathered modes
    /// are playable only when released, spec §5.1). Also the "последний шаг" label when available modes alone would
    /// complete the Ultimate contents (spec §5.0.3, §5.2).
    /// </summary>
    public class StorefrontDisabledModesTests
    {
        static readonly ModeCode[] All15 = Enumerable.Range(1, 15).Select(m => (ModeCode)m).ToArray();
        static readonly ModeCode[] Paid10 = Enumerable.Range(6, 10).Select(m => (ModeCode)m).ToArray();

        static string[] SinglesExcept(params ModeCode[] modes)
        {
            return ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode && !modes.Contains(p.Mode.Value))
                .Select(p => p.Id).ToArray();
        }

        /// <summary>M12 disabled: owning every product still sold (9 singles + No Ads) empties the Shop: "Всё куплено".</summary>
        [Fact]
        public void DisabledMode_OwningEverythingSellable_AllPurchased()
        {
            var c = MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M12 });
            var owned = SinglesExcept(ModeCode.M12).Concat(new[] { ProductIds.NoAds }).ToArray();
            var s = Storefront.Build(owned, c, null);
            Assert.Empty(s.VisibleOffers);
            Assert.True(s.AllPurchased);
            Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.ModeChainReaction).HiddenReason);
            Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.AllModes).HiddenReason);
            Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.Ultimate).HiddenReason);

            // One sold product missing: not all purchased.
            var s2 = Storefront.Build(owned.Where(id => id != ProductIds.ModeOrbit), c, null);
            Assert.Equal(new[] { ProductIds.ModeOrbit }, s2.VisibleOffers.Select(o => o.ProductId).ToArray());
            Assert.False(s2.AllPurchased);
        }

        /// <summary>
        /// M09 free by config or grandfathered, but disabled: not playable, so not "available" in the Shop. Its single is
        /// unsold (not AlreadyAvailable), and the M15 single cannot complete "все режимы" with a mode nobody can play.
        /// </summary>
        [Fact]
        public void FreeOrGrandfatheredDisabledMode_IsNotAvailableInShop()
        {
            var owned = SinglesExcept(ModeCode.M09, ModeCode.M15).Concat(new[] { ProductIds.NoAds }).ToArray();
            var disabledFree = MonetizationConfig.FullCatalog.WithRemote(new[] { ModeCode.M09 }, new[] { ModeCode.M09 });
            var disabled = MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M09 });
            var cases = new[] { (disabledFree, (ModeCode[])null), (disabled, new[] { ModeCode.M09 }) };
            foreach (var (c, g) in cases)
            {
                var s = Storefront.Build(owned, c, g);
                Assert.False(ModeAvailability.IsAvailable(ModeCode.M09, s.Entitlements, g, c));
                var m09 = s.Get(ProductIds.ModeMultiply);
                Assert.False(m09.AlreadyAvailable);
                Assert.Equal(OfferHiddenReason.NotSellable, m09.HiddenReason);
                var m15 = s.Get(ProductIds.ModeLiquidSand);
                Assert.True(m15.Visible);
                Assert.False(m15.IsLastStep);
                Assert.Equal(OfferHiddenReason.NotSellable, s.Get(ProductIds.AllModes).HiddenReason);
                Assert.Empty(Storefront.ModeSheet(ModeCode.M09, owned, c, g));
            }

            // Control: the same mode free and enabled is available, so the M15 single is the last step.
            var enabled = MonetizationConfig.FullCatalog.WithRemote(new[] { ModeCode.M09 }, new ModeCode[0]);
            var e = Storefront.Build(owned, enabled, null);
            Assert.True(e.Get(ProductIds.ModeMultiply).AlreadyAvailable);
            Assert.True(e.Get(ProductIds.ModeLiquidSand).IsLastStep);
        }

        /// <summary>
        /// Nothing owned and every paid mode playable without an entitlement (free by config, or grandfathered): No Ads is a
        /// first purchase, not a "последний шаг", exactly like release 1 where every mode is free (spec §5.0.3 row «ничего |
        /// No Ads Bundle первым»). Available modes alone do not start the Ultimate set.
        /// </summary>
        [Fact]
        public void AllPaidModesAvailable_NothingOwned_NoAdsIsNotALastStep()
        {
            var free = MonetizationConfig.FullCatalog.WithFreeModes(Paid10);
            foreach (var (c, g) in new[] { (free, (ModeCode[])null), (MonetizationConfig.FullCatalog, Paid10) })
            {
                var s = Storefront.Build(new string[0], c, g);
                var noAds = s.Get(ProductIds.NoAds);
                Assert.True(noAds.Visible);
                Assert.True(noAds.RemovesWatermark);
                Assert.False(noAds.IsLastStep);
                Assert.All(s.Offers, o => Assert.False(o.IsLastStep));
            }
            Assert.False(Storefront.Build(new string[0], MonetizationConfig.Release1, null).Get(ProductIds.NoAds).IsLastStep);

            // Started by a purchase (the M06 single owned, M07–M15 free): No Ads completes the contents, a last step.
            var rest = MonetizationConfig.FullCatalog.WithFreeModes(Paid10.Skip(1));
            var started = Storefront.Build(new[] { ProductIds.ModeBallBattle }, rest, null);
            Assert.True(started.Get(ProductIds.NoAds).IsLastStep);
        }

        /// <summary>
        /// Random property over build lists, build modes, disabled, free, grandfathered and owned sets (deterministic seed):
        /// "Всё куплено" exactly when nothing is visible; every visible, watermark or mode-sheet offer is sellable; a disabled
        /// mode has an empty mode sheet, is not playable and not sold; buying visible offers always reaches the empty Shop.
        /// </summary>
        [Fact]
        public void RandomConfigs_DisabledAndUnsoldInvariants()
        {
            var rng = Rng.FromSeed(0xA11CE5UL, 7UL);
            for (int iter = 0; iter < 1500; iter++)
            {
                var list = ProductIds.All.Where(_ => rng.NextUInt(4) != 0).ToArray();
                var build = All15.Where(_ => rng.NextUInt(5) != 0).ToArray();
                var disabled = All15.Where(_ => rng.NextUInt(10) == 0).ToArray();
                var free = All15.Where(_ => rng.NextUInt(6) == 0).ToArray();
                var gf = All15.Where(_ => rng.NextUInt(8) == 0).ToArray();
                var owned = ProductIds.All.Where(_ => rng.NextUInt(5) == 0).ToArray();
                var c = new MonetizationConfig(free, list, build, disabled);
                var s = Storefront.Build(owned, c, gf);

                Assert.True(s.AllPurchased == (s.VisibleOffers.Count == 0), "iteration " + iter);
                foreach (var o in s.VisibleOffers)
                {
                    Assert.True(c.IsSellable(o.ProductId));
                    Assert.DoesNotContain(o.ProductId, c.UnsoldProductIds);
                    Assert.False(o.AlreadyAvailable);
                }
                foreach (var o in Storefront.WatermarkOffers(owned, c, gf)) Assert.True(c.IsSellable(o.ProductId));
                foreach (var m in All15)
                {
                    var sheet = Storefront.ModeSheet(m, owned, c, gf);
                    foreach (var o in sheet) Assert.True(c.IsSellable(o.ProductId));
                    if (!c.IsReleased(m)) Assert.Empty(sheet);
                    if (disabled.Contains(m))
                    {
                        Assert.Empty(sheet);
                        Assert.False(ModeAvailability.IsAvailable(m, s.Entitlements, gf, c));
                        Assert.False(c.IsModeSold(m));
                        var single = ProductCatalog.ForMode(m);
                        if (single != null) Assert.False(s.Get(single.Id).AlreadyAvailable);
                    }
                }

                var have = new List<string>(owned);
                for (int round = 0; ; round++)
                {
                    var st = Storefront.Build(have, c, gf);
                    if (st.VisibleOffers.Count == 0)
                    {
                        Assert.True(st.AllPurchased);
                        break;
                    }
                    Assert.True(round < 15, "did not converge, iteration " + iter);
                    have.Add(st.VisibleOffers[0].ProductId);
                }
            }
        }
    }
}
