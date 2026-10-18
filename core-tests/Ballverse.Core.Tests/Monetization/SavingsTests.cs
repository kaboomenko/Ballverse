using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.1 "Экономия в наборах" (Р6).</summary>
    public class SavingsTests
    {
        static readonly IReadOnlyDictionary<string, decimal> Base = ProductCatalog.BasePricesUsd;

        [Fact]
        public void AllModesSaves75PercentInsteadOf1990()
        {
            var s = Savings.ForBundle(ProductIds.AllModes, Base);
            Assert.Equal(4.99m, s.BundlePrice);
            Assert.Equal(19.90m, s.InsteadOf);
            Assert.Equal(14.91m, s.Amount);
            Assert.Equal(75, s.Percent);
            Assert.Equal(10, s.PartIds.Count);
        }

        [Fact]
        public void NoAdsSaves31PercentInsteadOf1298()
        {
            var s = Savings.ForBundle(ProductIds.NoAds, Base);
            Assert.Equal(12.98m, s.InsteadOf);
            Assert.Equal(31, s.Percent);
            Assert.Equal(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds }, s.PartIds.ToArray());
        }

        [Fact]
        public void UltimateSaves64PercentVsEverythingSeparately()
        {
            var s = Savings.ForBundle(ProductIds.Ultimate, Base);
            Assert.Equal(32.88m, s.InsteadOf);
            Assert.Equal(20.89m, s.Amount);
            Assert.Equal(64, s.Percent);
            Assert.Equal(12, s.PartIds.Count);
        }

        [Fact]
        public void UltimateSaves14PercentVsCheapestParts()
        {
            var s = Savings.UltimateVsCheapestParts(Base);
            Assert.Equal(new[] { ProductIds.AllModes, ProductIds.NoAds }, s.PartIds.ToArray());
            Assert.Equal(13.98m, s.InsteadOf);
            Assert.Equal(1.99m, s.Amount);
            Assert.Equal(14, s.Percent);
            Assert.Equal(ProductIds.Ultimate, s.BundleId);
        }

        [Fact]
        public void UsesLocalizedPricesAsGiven()
        {
            var prices = ProductCatalog.All.ToDictionary(p => p.Id, p => p.Kind == ProductKind.SingleMode ? 199m : 0m);
            prices[ProductIds.AllModes] = 499m;
            var s = Savings.ForBundle(ProductIds.AllModes, prices);
            Assert.Equal(1990m, s.InsteadOf);
            Assert.Equal(75, s.Percent);
        }

        [Fact]
        public void NonBundlesAndUnknownIdsGiveNull()
        {
            Assert.Null(Savings.ForBundle(ProductIds.ModeOrbit, Base));
            Assert.Null(Savings.ForBundle(ProductIds.RemoveExportAds, Base));
            Assert.Null(Savings.ForBundle("nope", Base));
            Assert.Null(Savings.ForBundle(null, Base));
            Assert.Empty(Savings.SeparatePartsOf(ProductIds.ModeOrbit));
        }

        [Fact]
        public void MissingOrNonPositivePricesGiveNull()
        {
            var prices = new Dictionary<string, decimal>(Base.ToDictionary(p => p.Key, p => p.Value));
            prices.Remove(ProductIds.ModeOrbit);
            Assert.Null(Savings.ForBundle(ProductIds.AllModes, prices));
            Assert.Null(Savings.ForBundle(ProductIds.AllModes, null));

            var zero = new Dictionary<string, decimal>(Base.ToDictionary(p => p.Key, p => p.Value)) { [ProductIds.RemoveExportAds] = 0m };
            Assert.Null(Savings.ForBundle(ProductIds.NoAds, zero));
            var negative = new Dictionary<string, decimal>(Base.ToDictionary(p => p.Key, p => p.Value)) { [ProductIds.NoAds] = -1m };
            Assert.Null(Savings.ForBundle(ProductIds.NoAds, negative));
        }

        [Fact]
        public void NoSavingGivesNull()
        {
            var prices = new Dictionary<string, decimal>(Base.ToDictionary(p => p.Key, p => p.Value)) { [ProductIds.NoAds] = 12.98m };
            Assert.Null(Savings.ForBundle(ProductIds.NoAds, prices));
            prices[ProductIds.NoAds] = 20m;
            Assert.Null(Savings.ForBundle(ProductIds.NoAds, prices));
        }

        [Fact]
        public void HugePricesDoNotThrow()
        {
            var prices = new Dictionary<string, decimal>(Base.ToDictionary(p => p.Key, p => decimal.MaxValue / 2));
            Assert.Null(Savings.ForBundle(ProductIds.Ultimate, prices));
        }

        [Fact]
        public void ExplicitPartsAndEmptyList()
        {
            Assert.Null(Savings.Compute(ProductIds.NoAds, new string[0], Base));
            Assert.Null(Savings.Compute(ProductIds.NoAds, null, Base));
            var s = Savings.Compute(ProductIds.AllModes, new[] { ProductIds.ModeOrbit, ProductIds.ModeOrbit, ProductIds.ModeOrbit }, Base);
            Assert.Equal(5.97m, s.InsteadOf);
            Assert.Equal(16, s.Percent); // 0.98 / 5.97 = 16.4%
        }
    }
}
