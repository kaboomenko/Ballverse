using System;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.4: pending purchases grant nothing, refunds remove rights.</summary>
    public class OwnedProductsTests
    {
        [Fact]
        public void OnlyPurchasedRecordsCount()
        {
            var owned = OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending),
                new PurchaseRecord(ProductIds.ModeOrbit, PurchaseState.Purchased),
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked),
                new PurchaseRecord(ProductIds.AllModes, PurchaseState.Purchased),
                new PurchaseRecord(ProductIds.AllModes, PurchaseState.Purchased),
                new PurchaseRecord(null, PurchaseState.Purchased),
                new PurchaseRecord("", PurchaseState.Purchased),
            });
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.ModeOrbit }, owned.ToArray());
        }

        [Fact]
        public void RepurchaseAfterRefundCounts()
        {
            var owned = OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Revoked),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased),
            });
            Assert.Equal(new[] { ProductIds.NoAds }, owned.ToArray());
        }

        [Fact]
        public void NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => OwnedProducts.From(null));
        }

        [Fact]
        public void LaterStateOfSameTransactionWins_RefundRemovesRight()
        {
            Assert.Empty(OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased, "t1"),
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked, "t1"),
            }));
            Assert.Equal(new[] { ProductIds.NoAds }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "t2"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "t2"),
            }).ToArray());
        }

        [Fact]
        public void DistinctTransactionsAreIndependent()
        {
            // Refund of one transaction, a separate purchase of the same product stays.
            Assert.Equal(new[] { ProductIds.Ultimate }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased, "t1"),
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked, "t1"),
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased, "t2"),
            }).ToArray());
            // Records without an id are independent current states.
            Assert.Equal(new[] { ProductIds.Ultimate }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked, "t1"),
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased),
            }).ToArray());
        }

        [Fact]
        public void OwnedProducts_EmptyTransactionIdIsTreatedAsMissing()
        {
            // Native bridges may marshal a missing id as "": such records must not share one transaction slot (spec §5.4).
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.AllModes, PurchaseState.Purchased, ""),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, ""),
                new PurchaseRecord(ProductIds.ModeOrbit, PurchaseState.Pending, ""),
            }).ToArray());
            // A pending single mode (Ask to Buy, no transaction yet) does not wipe a purchased Ultimate.
            Assert.Equal(new[] { ProductIds.Ultimate }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased, ""),
                new PurchaseRecord(ProductIds.ModeOrbit, PurchaseState.Pending, ""),
            }).ToArray());
        }


        // ---- Spec §5.4 "Покупка в статусе «ожидает» (Android: оплата наличными, одобрение родителем)": no rights until
        // confirmed. Play Billing reports PENDING purchases WITH a purchaseToken, so these cases carry a transaction id. ----

        [Fact]
        public void PendingWithTransactionId_GrantsNothing()
        {
            var records = new[] { new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.pending-1") };
            Assert.Empty(OwnedProducts.From(records));
            Assert.Equal(EntitlementSet.Empty, Entitlements.From(OwnedProducts.From(records), MonetizationConfig.FullCatalog, false));
            Assert.Equal(EntitlementSet.Empty, Entitlements.From(OwnedProducts.From(records), MonetizationConfig.Release1, false));
        }

        [Theory]
        [InlineData(ProductIds.NoAds)]
        [InlineData(ProductIds.RemoveExportAds)]
        [InlineData(ProductIds.RemoveInterstitialAds)]
        [InlineData(ProductIds.Ultimate)]
        [InlineData(ProductIds.AllModes)]
        [InlineData(ProductIds.ModeOrbit)]
        public void PendingWithTransactionId_GrantsNothing_EveryProductKind(string productId)
        {
            var owned = OwnedProducts.From(new[] { new PurchaseRecord(productId, PurchaseState.Pending, "token-" + productId) });
            Assert.Empty(owned);
            Assert.Equal(EntitlementSet.Empty, Entitlements.From(owned, MonetizationConfig.Release1, false));
            // The shop still offers it (Release 1 ad products), so the user is not told it is owned.
            if (MonetizationConfig.Release1.IsSellable(productId))
                Assert.False(Storefront.Build(owned, MonetizationConfig.Release1, null, false).Get(productId).Owned);
        }

        [Fact]
        public void PendingThenPurchased_SameTransactionId_GrantsAfterConfirmation()
        {
            var pending = new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.1");
            Assert.Empty(OwnedProducts.From(new[] { pending }));
            var owned = OwnedProducts.From(new[] { pending, new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "GPA.1") });
            Assert.Equal(new[] { ProductIds.NoAds }, owned.ToArray());
            Assert.True(Entitlements.From(owned, MonetizationConfig.Release1, false).Contains(Entitlement.NoWatermark));
        }

        /// <summary>
        /// The last state of a transaction wins (documented contract), so an anomalous Purchased then Pending report of the
        /// same token grants nothing until a later Purchased: when in doubt, rights follow the store's latest state.
        /// </summary>
        [Fact]
        public void PurchasedThenPending_SameTransactionId_LatestStateWins()
        {
            Assert.Empty(OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "GPA.2"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.2"),
            }));
            Assert.Equal(new[] { ProductIds.NoAds }, OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "GPA.2"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.2"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "GPA.2"),
            }).ToArray());
        }

        [Fact]
        public void PendingThenRevoked_SameTransactionId_GrantsNothing()
        {
            // Declined cash payment / parent refused: the pending purchase is cancelled.
            Assert.Empty(OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.3"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Revoked, "GPA.3"),
            }));
            Assert.Empty(OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Revoked, "GPA.3"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.3"),
            }));
        }

        [Fact]
        public void PendingWithId_DoesNotAffectOtherTransactionsOrProducts()
        {
            var owned = OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.RemoveExportAds, PurchaseState.Purchased, "GPA.10"),
                new PurchaseRecord(ProductIds.RemoveInterstitialAds, PurchaseState.Pending, "GPA.11"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Pending, "GPA.12"),
                new PurchaseRecord(ProductIds.NoAds, PurchaseState.Purchased, "GPA.13"),
            });
            Assert.Equal(new[] { ProductIds.RemoveExportAds, ProductIds.NoAds }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), owned.ToArray());

            var onlyPendingSecond = OwnedProducts.From(new[]
            {
                new PurchaseRecord(ProductIds.RemoveExportAds, PurchaseState.Purchased, "GPA.20"),
                new PurchaseRecord(ProductIds.RemoveInterstitialAds, PurchaseState.Pending, "GPA.21"),
            });
            Assert.Equal(new[] { ProductIds.RemoveExportAds }, onlyPendingSecond.ToArray());
            // The pending second removal neither completes the ads nor unlocks NO_WATERMARK (release-1 rule), and the shop
            // keeps offering it as the last step.
            var r1 = MonetizationConfig.Release1;
            var e = Entitlements.From(onlyPendingSecond, r1, false);
            Assert.False(e.Contains(Entitlement.NoInterstitials));
            Assert.False(e.Contains(Entitlement.NoWatermark));
            Assert.Equal(new[] { ProductIds.RemoveInterstitialAds },
                Storefront.Build(onlyPendingSecond, r1, null, false).VisibleOffers.Select(o => o.ProductId).ToArray());
            Assert.False(WatermarkGrandfathering.Update(false, onlyPendingSecond, r1));
        }

        /// <summary>Rng-driven: whatever the interleaving, a product is owned iff some transaction's last state is Purchased.</summary>
        [Fact]
        public void RandomHistories_MatchLastStateOracle()
        {
            var rng = Rng.FromSeed(0x9E4D1A6UL, 3UL);
            var products = new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.Ultimate };
            for (int iter = 0; iter < 2000; iter++)
            {
                int count = (int)rng.NextUInt(8);
                var records = new PurchaseRecord[count];
                var last = new System.Collections.Generic.Dictionary<string, PurchaseRecord>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string tx = "t" + rng.NextUInt(3);
                    // A token always belongs to one product.
                    string product = products[int.Parse(tx.Substring(1))];
                    var state = (PurchaseState)rng.NextUInt(3);
                    records[i] = new PurchaseRecord(product, state, tx);
                    last[tx] = records[i];
                }
                var expected = last.Values.Where(r => r.State == PurchaseState.Purchased).Select(r => r.ProductId)
                    .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert.Equal(expected, OwnedProducts.From(records).ToArray());
            }
        }
    }
}
