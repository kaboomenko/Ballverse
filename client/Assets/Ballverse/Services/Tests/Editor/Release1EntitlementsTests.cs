using Ballverse.Core.Monetization;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class Release1EntitlementsTests
    {
        [Test]
        public void CatalogSellsOnlyTheThreeAdProducts()
        {
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.NoAds },
                Release1Catalog.SoldProductIds);
            Assert.IsFalse(Release1Catalog.IsSold(ProductIds.Ultimate));
            Assert.IsFalse(Release1Catalog.IsSold(ProductIds.AllModes));
            Assert.IsFalse(Release1Catalog.IsSold(ProductIds.ModeBallBattle));
        }

        [Test]
        public void NoAdsBundleRemovesTheWatermark()
        {
            var e = Release1Entitlements.From(new[] { ProductIds.NoAds });
            Assert.IsTrue(e.Contains(Entitlement.NoExportAds));
            Assert.IsTrue(e.Contains(Entitlement.NoInterstitials));
            Assert.IsTrue(e.Contains(Entitlement.NoWatermark));
        }

        [Test]
        public void BothAdRemovalsRemoveTheWatermark()
        {
            var e = Release1Entitlements.From(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds });
            Assert.IsTrue(e.Contains(Entitlement.NoWatermark));
        }

        [Test]
        public void OneAdRemovalKeepsTheWatermark()
        {
            Assert.IsFalse(Release1Entitlements.From(new[] { ProductIds.RemoveExportAds }).Contains(Entitlement.NoWatermark));
            Assert.IsFalse(Release1Entitlements.From(new[] { ProductIds.RemoveInterstitialAds }).Contains(Entitlement.NoWatermark));
            Assert.IsTrue(Release1Entitlements.From(new string[0]).IsEmpty);
        }

        [Test]
        public void UltimateStillGrantsEverything()
        {
            var e = Release1Entitlements.From(new[] { ProductIds.Ultimate });
            Assert.IsTrue(e.Contains(Entitlement.NoWatermark));
            Assert.AreEqual(10, e.ModeCount);
        }

        [Test]
        public void RedundantPurchasesAreDetected()
        {
            Assert.IsTrue(Release1Entitlements.IsRedundantPurchase(new[] { ProductIds.NoAds }, ProductIds.RemoveExportAds));
            Assert.IsTrue(Release1Entitlements.IsRedundantPurchase(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, ProductIds.NoAds));
            Assert.IsFalse(Release1Entitlements.IsRedundantPurchase(new[] { ProductIds.RemoveExportAds }, ProductIds.NoAds));
            Assert.IsFalse(Release1Entitlements.IsRedundantPurchase(new string[0], ProductIds.RemoveInterstitialAds));
        }
    }
}
