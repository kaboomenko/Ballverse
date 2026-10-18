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
    }
}
