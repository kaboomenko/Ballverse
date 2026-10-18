using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// QA round 2: the cutoff of the release-1 No Ads watermark rule must reach release-1 binaries (Remote Config key,
    /// <see cref="MonetizationConfig.WithWatermarkNoAdsCutoff"/>), so that the rule is on before and off after the cutoff in
    /// every build and every device derives the same NO_WATERMARK from the same purchases (owner decision: keep
    /// NO_WATERMARK obtained under the rule; spec §5.1, §5.4).
    /// </summary>
    public class WatermarkNoAdsCutoffTests
    {
        const long Cutoff = 1_800_000_000_000L;
        const long Before = Cutoff - 86_400_000L;
        const long After = Cutoff + 86_400_000L;

        static readonly MonetizationConfig R1 = MonetizationConfig.Release1;
        static readonly MonetizationConfig Later = MonetizationConfig.FullCatalog.WithWatermarkWithNoAdsEnded(Cutoff);
        /// <summary>A release-1 binary that fetched the key, before and after the cutoff by its clock.</summary>
        static readonly MonetizationConfig R1KeyBefore = R1.WithWatermarkNoAdsCutoff(Cutoff, Before);
        static readonly MonetizationConfig R1KeyAfter = R1.WithWatermarkNoAdsCutoff(Cutoff, After);

        static PurchaseRecord P(string id, long? time, string tx = null, PurchaseState state = PurchaseState.Purchased)
        {
            return new PurchaseRecord(id, state, tx, time);
        }

        static EntitlementSet Ent(IEnumerable<PurchaseRecord> records, MonetizationConfig config, bool gf)
        {
            return Entitlements.From(OwnedProducts.From(records), config, gf);
        }

        [Fact]
        public void RemoteKeyName()
        {
            Assert.Equal("watermark_no_ads_ended_at_ms", MonetizationConfig.WatermarkNoAdsEndedAtRemoteKey);
        }

        [Fact]
        public void WithWatermarkNoAdsCutoff_Transitions()
        {
            // Key absent: unchanged.
            Assert.Same(R1, R1.WithWatermarkNoAdsCutoff(null, After));
            // Rule already off (later build with its hard-coded cutoff, or a config where the rule never applied): unchanged.
            Assert.Same(Later, Later.WithWatermarkNoAdsCutoff(Cutoff + 5, After));
            Assert.Same(MonetizationConfig.FullCatalog, MonetizationConfig.FullCatalog.WithWatermarkNoAdsCutoff(Cutoff, After));

            // Before the cutoff: rule still on, cutoff recorded.
            Assert.True(R1KeyBefore.WatermarkWithNoAds);
            Assert.Equal(Cutoff, R1KeyBefore.WatermarkWithNoAdsEndedAtUnixMs);
            Assert.True(R1.WithWatermarkNoAdsCutoff(Cutoff, Cutoff - 1).WatermarkWithNoAds);
            // From the cutoff on: rule off.
            Assert.False(R1.WithWatermarkNoAdsCutoff(Cutoff, Cutoff).WatermarkWithNoAds);
            Assert.False(R1KeyAfter.WatermarkWithNoAds);
            Assert.Equal(Cutoff, R1KeyAfter.WatermarkWithNoAdsEndedAtUnixMs);

            // Catalog and other rules kept.
            foreach (var c in new[] { R1KeyBefore, R1KeyAfter })
            {
                Assert.Equal(R1.SellableProductIds.ToArray(), c.SellableProductIds.ToArray());
                Assert.Equal(R1.ReleasedModes.ToArray(), c.ReleasedModes.ToArray());
                Assert.Equal(R1.FreeModes.ToArray(), c.FreeModes.ToArray());
                Assert.Equal(R1.WatermarkByParts, c.WatermarkByParts);
            }
            // Free modes from Remote Config can be applied before or after.
            var widened = R1.WithFreeModes(new[] { ModeCode.M06 }).WithWatermarkNoAdsCutoff(Cutoff, After);
            Assert.Equal(Cutoff, widened.WithFreeModes(new ModeCode[0]).WatermarkWithNoAdsEndedAtUnixMs);
            Assert.False(widened.WatermarkWithNoAds);
        }

        /// <summary>
        /// The QA N1 scenario with the key: a user who never updates buys No Ads on the release-1 binary after the cutoff.
        /// The build no longer promises NO_WATERMARK, does not grant it, and the same device after the update, a reinstall and
        /// a second device all agree.
        /// </summary>
        [Fact]
        public void OldBuildAfterCutoff_NoPromise_AllDevicesAgree()
        {
            var shop = Storefront.Build(new string[0], R1KeyAfter, null, false);
            Assert.False(shop.Get(ProductIds.NoAds).RemovesWatermark);
            Assert.Empty(Storefront.WatermarkOffers(new string[0], R1KeyAfter, null, false));

            var records = new[] { P(ProductIds.NoAds, After, "t1") };
            bool oldGf = WatermarkGrandfathering.FromStore(records, R1KeyAfter);
            Assert.False(oldGf);
            var oldBuild = Ent(records, R1KeyAfter, oldGf);
            Assert.False(oldBuild.Contains(Entitlement.NoWatermark));
            // Offline launch on the old build: the product-id path cannot set the flag once the cutoff is known.
            Assert.False(WatermarkGrandfathering.Update(oldGf, OwnedProducts.From(records), R1KeyAfter));

            bool updatedGf = WatermarkGrandfathering.FromStore(records, Later);
            bool freshGf = WatermarkGrandfathering.FromStore(records, Later);
            Assert.Equal(oldBuild, Ent(records, Later, updatedGf));
            Assert.Equal(oldBuild, Ent(records, Later, freshGf));
        }

        /// <summary>
        /// Key set ahead of the cutoff: the release-1 binary keeps the rule (and its promise) until the cutoff; the purchase
        /// before it is grandfathered everywhere.
        /// </summary>
        [Fact]
        public void OldBuildBeforeCutoff_KeepsPromise_PurchaseGrandfatheredEverywhere()
        {
            var noAds = Storefront.Build(new string[0], R1KeyBefore, null, false).Get(ProductIds.NoAds);
            Assert.True(noAds.Visible);
            Assert.True(noAds.RemovesWatermark);

            var records = new[] { P(ProductIds.NoAds, Before, "t1") };
            bool oldGf = WatermarkGrandfathering.FromStore(records, R1KeyBefore);
            Assert.True(oldGf);
            Assert.True(Ent(records, R1KeyBefore, oldGf).Contains(Entitlement.NoWatermark));
            // The same binary after the cutoff passes, and a new device on the later release.
            Assert.True(Ent(records, R1KeyAfter, WatermarkGrandfathering.FromStore(records, R1KeyAfter)).Contains(Entitlement.NoWatermark));
            Assert.True(Ent(records, Later, WatermarkGrandfathering.FromStore(records, Later)).Contains(Entitlement.NoWatermark));
        }

        /// <summary>
        /// Exhaustive over every combination of the bundles and ad products (the only products granting ad rights; ProductIds
        /// bits 10–14) with several single-mode sets, all bought before or all after the cutoff: a release-1 binary with the key
        /// (before or after the cutoff by its clock) derives the same flag as the later release, and the flag it persisted
        /// gives the same entitlements after the update as a fresh install (QA control probe N1b turned into a test).
        /// </summary>
        [Fact]
        public void OldBuildWithKey_MatchesLaterRelease_AllSubsets()
        {
            var laterConfigs = new[] { Later, Later.WithWatermarkByParts(false) };
            var singleSets = new[] { 0, 0x3FF, 0x155, 0x2AA, 0x1FF };
            for (int high = 0; high < 32; high++)
            foreach (int low in singleSets)
            {
                var owned = Subsets.Ids((high << 10) | low);
                foreach (long t in new[] { Before, After })
                {
                    var records = owned.Select((id, i) => P(id, t + i, "t" + i)).ToArray();
                    bool expected = WatermarkGrandfathering.FromStore(records, Later);
                    bool qualifies = t == Before && Entitlements.DirectGrants(owned).IsSupersetOf(EntitlementSet.AdFree);
                    Assert.Equal(qualifies, expected);
                    foreach (var old in new[] { R1KeyBefore, R1KeyAfter })
                    {
                        bool oldGf = WatermarkGrandfathering.FromStore(records, old);
                        Assert.Equal(expected, oldGf);
                        foreach (var c in laterConfigs)
                            Assert.Equal(Entitlements.From(owned, c, WatermarkGrandfathering.FromStore(records, c)), Entitlements.From(owned, c, oldGf));
                    }
                    // After the cutoff the old binary grants exactly what the later release grants.
                    Assert.Equal(Entitlements.From(owned, Later, expected), Entitlements.From(owned, R1KeyAfter, expected));
                }
            }
        }

        /// <summary>
        /// Why the key is needed: without it a release-1 binary cannot know the cutoff and treats a post-cutoff purchase as made
        /// under the rule (documented residual risk for a binary that never fetched the key).
        /// </summary>
        [Fact]
        public void WithoutKey_Release1CannotTellPostCutoffPurchases()
        {
            var records = new[] { P(ProductIds.NoAds, After, "t1") };
            Assert.True(WatermarkGrandfathering.FromStore(records, R1));
            Assert.False(WatermarkGrandfathering.FromStore(records, Later));
        }

        /// <summary>
        /// QA N2: refund of the release-1 No Ads, re-buy after the cutoff. The old device's cached flag is replaced by the
        /// store-derived one on the next complete store read, so it agrees with a new device: no NO_WATERMARK.
        /// </summary>
        [Fact]
        public void RefundThenRebuyAfterCutoff_DevicesAgree()
        {
            var r1Records = new[] { P(ProductIds.NoAds, Before, "t1") };
            bool cached = WatermarkGrandfathering.FromStore(r1Records, R1);
            Assert.True(cached);

            var laterRecords = new[]
            {
                P(ProductIds.NoAds, Before, "t1"),
                P(ProductIds.NoAds, Before, "t1", PurchaseState.Revoked),
                P(ProductIds.NoAds, After, "t2"),
            };
            var owned = OwnedProducts.From(laterRecords);
            bool oldDevice = WatermarkGrandfathering.FromStore(laterRecords, Later);
            bool newDevice = WatermarkGrandfathering.FromStore(laterRecords, Later);
            Assert.False(oldDevice);
            Assert.Equal(Entitlements.From(owned, Later, newDevice), Entitlements.From(owned, Later, oldDevice));
            Assert.False(Entitlements.From(owned, Later, oldDevice).Contains(Entitlement.NoWatermark));
            // The same answer whether or not the store still lists the refunded transaction.
            Assert.False(WatermarkGrandfathering.FromStore(new[] { P(ProductIds.NoAds, After, "t2") }, Later));
        }

        /// <summary>A refund followed by a re-purchase still before the cutoff keeps the rule on every device.</summary>
        [Fact]
        public void RefundThenRebuyBeforeCutoff_DevicesAgree()
        {
            var records = new[]
            {
                P(ProductIds.NoAds, Before - 10, "t1"),
                P(ProductIds.NoAds, Before - 10, "t1", PurchaseState.Revoked),
                P(ProductIds.NoAds, Before, "t2"),
            };
            Assert.True(WatermarkGrandfathering.FromStore(records, Later));
            Assert.True(WatermarkGrandfathering.FromStore(records, R1KeyAfter));
            Assert.True(WatermarkGrandfathering.FromStore(records, R1));
        }

        /// <summary>Without a complete store read (offline) the cached flag is kept, never cleared.</summary>
        [Fact]
        public void Offline_KeepsCachedFlag()
        {
            foreach (var c in new[] { R1, R1KeyBefore, R1KeyAfter, Later, MonetizationConfig.FullCatalog })
            {
                Assert.True(WatermarkGrandfathering.Update(true, new string[0], c));
                Assert.True(WatermarkGrandfathering.Update(true, new[] { ProductIds.NoAds }, c));
            }
            // Rule on, no cutoff known yet: the product-id path still sets the flag (release 1 before the key).
            Assert.True(WatermarkGrandfathering.Update(false, new[] { ProductIds.NoAds }, R1));
            // A known cutoff needs purchase times: only FromStore can set it.
            Assert.False(WatermarkGrandfathering.Update(false, new[] { ProductIds.NoAds }, R1KeyBefore));
            Assert.True(Entitlements.From(new[] { ProductIds.NoAds }, R1KeyBefore, false).Contains(Entitlement.NoWatermark));
        }
    }
}
