using System.Collections.Generic;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// The permanent NO_WATERMARK rule (spec §5.0.2, Р21): owns Ultimate, OR owns the full Ultimate contents from any
    /// products, OR owns both NO_EXPORT_ADS and NO_INTERSTITIALS (No Ads Bundle or the two single removals). Checked for all
    /// 2^15 owned-product subsets in both catalog presets, against an oracle that looks only at which products are owned.
    /// </summary>
    public class NoWatermarkRuleExhaustiveTests
    {
        static readonly string[] SingleModeIds =
        {
            ProductIds.ModeBallBattle, ProductIds.ModeLastStanding, ProductIds.ModeColorWar, ProductIds.ModeMultiply,
            ProductIds.ModeMazeEscape, ProductIds.ModeSmash, ProductIds.ModeChainReaction, ProductIds.ModePendulumWave,
            ProductIds.ModeOrbit, ProductIds.ModeLiquidSand,
        };

        /// <summary>Independent oracle written from the brief, by product ownership only.</summary>
        static bool Oracle(int mask)
        {
            bool ultimate = Subsets.Has(mask, ProductIds.Ultimate);
            bool noAds = Subsets.Has(mask, ProductIds.NoAds);
            bool exportAds = noAds || Subsets.Has(mask, ProductIds.RemoveExportAds);
            bool interstitials = noAds || Subsets.Has(mask, ProductIds.RemoveInterstitialAds);
            bool allModes = Subsets.Has(mask, ProductIds.AllModes);
            if (!allModes)
            {
                allModes = true;
                foreach (var id in SingleModeIds) allModes &= Subsets.Has(mask, id);
            }
            bool fullContents = allModes && exportAds && interstitials;
            return ultimate || fullContents || (exportAds && interstitials);
        }

        static void CheckAllSubsets(MonetizationConfig config)
        {
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var e = Entitlements.From(Subsets.Ids(mask), config);
                bool expected = Oracle(mask);
                Assert.True(expected == e.Contains(Entitlement.NoWatermark), "mask " + mask);
                Assert.Equal(expected ? WatermarkRow.Toggle : WatermarkRow.Locked, WatermarkRowPolicy.GetRow(e));
                Assert.Equal(!expected, global::Ballverse.Core.Projects.WatermarkPolicy.Resolve(false, e.Contains(Entitlement.NoWatermark)));
            }
        }

        [Fact]
        public void AllSubsets_Release1()
        {
            CheckAllSubsets(MonetizationConfig.Release1);
        }

        [Fact]
        public void AllSubsets_FullCatalog()
        {
            CheckAllSubsets(MonetizationConfig.FullCatalog);
        }

        /// <summary>
        /// Same rights in every release: the storefront of both presets reports the same entitlements for the same purchases,
        /// so buying No Ads in release 1 keeps NO_WATERMARK after the catalog grows (spec §5.0.4).
        /// </summary>
        [Fact]
        public void AllSubsets_StorefrontEntitlementsIdenticalInBothPresets()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 7)
            {
                var ids = Subsets.Ids(mask);
                var r1 = Storefront.Build(ids, MonetizationConfig.Release1, null).Entitlements;
                var full = Storefront.Build(ids, MonetizationConfig.FullCatalog, null).Entitlements;
                Assert.Equal(r1, full);
                Assert.Equal(Oracle(mask), r1.Contains(Entitlement.NoWatermark));
            }
        }

        /// <summary>
        /// Restores from the store alone (spec §5.4): store transactions → <see cref="OwnedProducts.From"/> → entitlements,
        /// with no local flag; refunded and pending transactions grant nothing. Rng-driven, deterministic seed.
        /// </summary>
        [Fact]
        public void RestoreFromStoreRecordsAlone_MatchesOracle()
        {
            var rng = Rng.FromSeed(0x0A7E12AAUL, 5UL);
            for (int iter = 0; iter < 4000; iter++)
            {
                int mask = (int)rng.NextUInt((uint)Subsets.Count);
                var records = new List<PurchaseRecord>();
                int tx = 0;
                foreach (var id in Subsets.Ids(mask))
                {
                    // An owned product may carry an earlier refunded or pending transaction of the same product.
                    if (rng.NextUInt(4) == 0) records.Add(new PurchaseRecord(id, PurchaseState.Revoked, "r" + tx++));
                    records.Add(new PurchaseRecord(id, PurchaseState.Purchased, "t" + tx++));
                }
                // Products not owned appear only as pending or refunded transactions.
                foreach (var id in ProductIds.All)
                {
                    if (Subsets.Has(mask, id) || rng.NextUInt(3) != 0) continue;
                    records.Add(new PurchaseRecord(id, rng.NextBool() ? PurchaseState.Pending : PurchaseState.Revoked, "x" + tx++));
                }
                for (int i = records.Count - 1; i > 0; i--)
                {
                    int j = (int)rng.NextUInt((uint)(i + 1));
                    var t = records[i];
                    records[i] = records[j];
                    records[j] = t;
                }
                var owned = OwnedProducts.From(records);
                foreach (var config in new[] { MonetizationConfig.Release1, MonetizationConfig.FullCatalog })
                    Assert.Equal(Oracle(mask), Entitlements.From(owned, config).Contains(Entitlement.NoWatermark));
            }
        }

        /// <summary>The offline cache (spec §5.4, §7.9) stores only product ids and yields the same NO_WATERMARK.</summary>
        [Fact]
        public void CachedProductsAlone_MatchOracle()
        {
            var key = new byte[] { 9, 8, 7, 6 };
            for (int mask = 0; mask < Subsets.Count; mask += 61)
            {
                var entry = EntitlementCache.Load(EntitlementCache.Serialize(Subsets.Ids(mask), 1000, key), key);
                Assert.Equal(Oracle(mask), entry.GetEntitlements(MonetizationConfig.Release1).Contains(Entitlement.NoWatermark));
                Assert.Equal(Oracle(mask), entry.GetEntitlements(MonetizationConfig.FullCatalog).Contains(Entitlement.NoWatermark));
            }
        }

        /// <summary>Monotone: buying more never removes NO_WATERMARK (spec §5.0.2 "купленное раньше право никогда не отнимается").</summary>
        [Fact]
        public void AllSubsets_BuyingMoreNeverRemovesTheRight()
        {
            var has = new bool[Subsets.Count];
            for (int mask = 0; mask < Subsets.Count; mask++)
                has[mask] = Entitlements.From(Subsets.Ids(mask), MonetizationConfig.Release1).Contains(Entitlement.NoWatermark);
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                if (!has[mask]) continue;
                for (int bit = 0; bit < 15; bit++) Assert.True(has[mask | (1 << bit)]);
            }
        }
    }
}
