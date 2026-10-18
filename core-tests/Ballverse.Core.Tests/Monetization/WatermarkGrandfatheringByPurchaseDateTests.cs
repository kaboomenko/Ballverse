using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Grandfathering of the release-1 No Ads watermark rule from restorable store data (purchase time before the release that
    /// turns the rule off), so a reinstall or a second device keeps the paid NO_WATERMARK (owner decision; spec §5.1, §5.4).
    /// </summary>
    public class WatermarkGrandfatheringByPurchaseDateTests
    {
        const long Cutoff = 1_800_000_000_000L;
        const long Before = Cutoff - 86_400_000L;
        const long After = Cutoff + 86_400_000L;

        static readonly MonetizationConfig R1 = MonetizationConfig.Release1;
        static readonly MonetizationConfig Later = MonetizationConfig.FullCatalog.WithWatermarkWithNoAdsEnded(Cutoff);

        static PurchaseRecord P(string id, long? time, string tx = null, PurchaseState state = PurchaseState.Purchased)
        {
            return new PurchaseRecord(id, state, tx, time);
        }

        static bool Gf(MonetizationConfig config, params PurchaseRecord[] records)
        {
            return WatermarkGrandfathering.FromStore(records, config);
        }

        static bool HasNw(MonetizationConfig config, params PurchaseRecord[] records)
        {
            bool gf = Gf(config, records);
            return Entitlements.From(OwnedProducts.From(records), config, gf).Contains(Entitlement.NoWatermark);
        }

        [Fact]
        public void Config_Cutoff()
        {
            Assert.Null(MonetizationConfig.Release1.WatermarkWithNoAdsEndedAtUnixMs);
            Assert.Null(MonetizationConfig.FullCatalog.WatermarkWithNoAdsEndedAtUnixMs);
            Assert.False(Later.WatermarkWithNoAds);
            Assert.Equal(Cutoff, Later.WatermarkWithNoAdsEndedAtUnixMs);
            Assert.Equal(Cutoff, Later.WithFreeModes(new[] { ModeCode.M06 }).WatermarkWithNoAdsEndedAtUnixMs);
            Assert.Equal(Cutoff, Later.WithWatermarkByParts(false).WatermarkWithNoAdsEndedAtUnixMs);
            Assert.Equal(Cutoff, Later.WithWatermarkWithNoAds(true).WatermarkWithNoAdsEndedAtUnixMs);
            var r1Off = R1.WithWatermarkWithNoAdsEnded(Cutoff);
            Assert.False(r1Off.WatermarkWithNoAds);
            Assert.Equal(R1.SellableProductIds.ToArray(), r1Off.SellableProductIds.ToArray());
        }

        /// <summary>The QA reinstall scenario: a new device under the later release, flag starting false.</summary>
        [Fact]
        public void FreshInstallUnderLaterRelease_PreCutoffNoAds_KeepsNoWatermark()
        {
            var records = new[] { P(ProductIds.NoAds, Before, "tx1") };
            bool oldDevice = WatermarkGrandfathering.Update(false, OwnedProducts.From(records), R1);
            Assert.True(oldDevice);
            var oldEnt = Entitlements.From(OwnedProducts.From(records), Later, oldDevice);

            bool newDevice = WatermarkGrandfathering.FromStore(records, Later);
            Assert.True(newDevice);
            var newEnt = Entitlements.From(OwnedProducts.From(records), Later, newDevice);
            Assert.Equal(oldEnt, newEnt);
            Assert.True(newEnt.Contains(Entitlement.NoWatermark));
            Assert.Empty(Storefront.WatermarkOffers(OwnedProducts.From(records), Later, null, newDevice));
        }

        [Fact]
        public void PostCutoffPurchase_DoesNotQualify()
        {
            Assert.False(Gf(Later, P(ProductIds.NoAds, After)));
            Assert.False(HasNw(Later, P(ProductIds.NoAds, After)));
            Assert.False(Gf(Later, P(ProductIds.NoAds, Cutoff)));  // strictly before
            Assert.True(Gf(Later, P(ProductIds.NoAds, Cutoff - 1)));
        }

        [Fact]
        public void BothRemovals_BothBeforeCutoffQualify_SplitAcrossCutoffDoesNot()
        {
            Assert.True(Gf(Later, P(ProductIds.RemoveExportAds, Before), P(ProductIds.RemoveInterstitialAds, Before - 5)));
            Assert.False(Gf(Later, P(ProductIds.RemoveExportAds, Before), P(ProductIds.RemoveInterstitialAds, After)));
            Assert.False(Gf(Later, P(ProductIds.RemoveExportAds, After), P(ProductIds.RemoveInterstitialAds, Before)));
            Assert.False(Gf(Later, P(ProductIds.RemoveExportAds, Before)));
            // Pre-cutoff Remove Export + pre-cutoff No Ads still covers both.
            Assert.True(Gf(Later, P(ProductIds.RemoveExportAds, Before), P(ProductIds.NoAds, Before)));
            // A pre-cutoff removal plus a post-cutoff No Ads: only one ad right existed under the rule.
            Assert.False(Gf(Later, P(ProductIds.RemoveExportAds, Before), P(ProductIds.NoAds, After)));
        }

        [Fact]
        public void MissingPurchaseTime_CannotProveTheRule()
        {
            Assert.False(Gf(Later, P(ProductIds.NoAds, null)));
            Assert.False(Gf(Later, P(ProductIds.RemoveExportAds, Before), P(ProductIds.RemoveInterstitialAds, null)));
        }

        [Fact]
        public void OnlyCurrentlyPurchasedTransactionsCount()
        {
            // Refunded before the new device: no right to keep (spec §5.4).
            Assert.False(Gf(Later, P(ProductIds.NoAds, Before, "t1"), P(ProductIds.NoAds, Before, "t1", PurchaseState.Revoked)));
            // Pending never grants (spec §5.4), even with a token and a pre-cutoff time.
            Assert.False(Gf(Later, P(ProductIds.NoAds, Before, "GPA.1", PurchaseState.Pending)));
            // Pending, then confirmed after the cutoff: purchase time of the confirmed record decides.
            Assert.False(Gf(Later, P(ProductIds.NoAds, Before, "GPA.2", PurchaseState.Pending), P(ProductIds.NoAds, After, "GPA.2")));
            Assert.True(Gf(Later, P(ProductIds.NoAds, Before, "GPA.3", PurchaseState.Pending), P(ProductIds.NoAds, Before, "GPA.3")));
        }

        [Fact]
        public void RuleOffWithoutCutoff_NeverQualifiesByDate()
        {
            Assert.False(Gf(MonetizationConfig.FullCatalog, P(ProductIds.NoAds, 0)));
            Assert.False(Gf(MonetizationConfig.FullCatalog, P(ProductIds.NoAds, Before)));
        }

        [Fact]
        public void RuleOn_DateIrrelevant_MatchesProductIdOverload()
        {
            Assert.True(Gf(R1, P(ProductIds.NoAds, null)));
            Assert.True(Gf(R1, P(ProductIds.NoAds, After)));
            Assert.True(Gf(R1.WithWatermarkWithNoAds(true), P(ProductIds.RemoveExportAds, After), P(ProductIds.RemoveInterstitialAds, null)));
            Assert.False(Gf(R1, P(ProductIds.RemoveExportAds, Before)));
            Assert.False(Gf(R1, P(ProductIds.NoAds, Before, "x", PurchaseState.Pending)));
        }

        [Fact]
        public void DerivedFromStoreDataAlone_UnknownIdsIgnored_OrderIndependent()
        {
            // A complete store read replaces the cached flag: no ad purchase, no flag (it never granted anything alone).
            Assert.False(WatermarkGrandfathering.FromStore(new PurchaseRecord[0], Later));
            Assert.False(WatermarkGrandfathering.FromStore(new PurchaseRecord[0], R1));
            Assert.False(Gf(Later, P("ballverse.future.ads", Before), P(null, Before), P("", Before)));

            var records = new[]
            {
                P(ProductIds.RemoveInterstitialAds, Before, "a"), P(ProductIds.ModeOrbit, After, "b"),
                P(ProductIds.RemoveExportAds, Before - 1, "c"), P(ProductIds.NoAds, After, "d", PurchaseState.Revoked),
            };
            Assert.True(Gf(Later, records));
            Assert.True(Gf(Later, records.Reverse().ToArray()));
        }

        [Fact]
        public void NullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.FromStore((IEnumerable<PurchaseRecord>)null, Later));
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.FromStore(new PurchaseRecord[0], null));
        }

        [Fact]
        public void PurchaseRecord_TimeDefaultsToNull()
        {
            Assert.Null(new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased).PurchaseTimeUnixMs);
            Assert.Null(new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "t").PurchaseTimeUnixMs);
            Assert.Equal(Before, P(ProductIds.NoAds, Before).PurchaseTimeUnixMs);
        }

        /// <summary>
        /// Exhaustive over all 2^15 owned sets, every product bought before the cutoff: a new device under the later release
        /// gets exactly the flag and the entitlements the release-1 device had (and keeps); all bought after: no flag.
        /// </summary>
        [Fact]
        public void NewDeviceMatchesOldDevice_AllSubsets()
        {
            var laterConfigs = new[] { Later, Later.WithWatermarkByParts(false), R1.WithWatermarkWithNoAdsEnded(Cutoff) };
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var owned = Subsets.Ids(mask);
                var pre = owned.Select((id, i) => P(id, Before - i, "t" + i)).ToArray();
                var post = owned.Select((id, i) => P(id, After + i, "t" + i)).ToArray();
                bool oldGf = WatermarkGrandfathering.Update(false, owned, R1);
                foreach (var c in laterConfigs)
                {
                    bool newGf = WatermarkGrandfathering.FromStore(pre, c);
                    Assert.Equal(oldGf, newGf);
                    Assert.False(WatermarkGrandfathering.FromStore(post, c));
                    if ((mask & 7) == 0)
                        Assert.Equal(Entitlements.From(owned, c, oldGf), Entitlements.From(OwnedProducts.From(pre), c, newGf));
                }
            }
        }
    }
}
