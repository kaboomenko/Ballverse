using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §3.10 "Поведение" (Р13).</summary>
    public class WatermarkPolicyTests
    {
        static readonly EntitlementSet Without = Entitlements.From(new[] { ProductIds.AllModes });
        static readonly EntitlementSet With = Entitlements.From(new[] { ProductIds.Ultimate });

        [Fact]
        public void WithoutRight_WatermarkAlwaysOnAndRowLocked()
        {
            Assert.True(WatermarkPolicy.ShouldApply(Without));
            Assert.True(WatermarkPolicy.ShouldApply(Without, false));
            Assert.Equal(WatermarkRow.Locked, WatermarkPolicy.GetRow(Without));
        }

        [Fact]
        public void WithRight_DefaultOffButUserMayKeepIt()
        {
            Assert.False(WatermarkPolicy.ShouldApply(With));
            Assert.False(WatermarkPolicy.ShouldApply(With, false));
            Assert.True(WatermarkPolicy.ShouldApply(With, true));
            Assert.Equal(WatermarkRow.Toggle, WatermarkPolicy.GetRow(With));
        }

        [Fact]
        public void ByPartsOwnerAlsoGetsToggle()
        {
            var parts = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds });
            Assert.Equal(WatermarkRow.Toggle, WatermarkPolicy.GetRow(parts));
            Assert.False(WatermarkPolicy.ShouldApply(parts));
        }

        [Fact]
        public void KillSwitchRemovesWatermarkForEveryone()
        {
            Assert.False(WatermarkPolicy.ShouldApply(Without, null, false));
            Assert.False(WatermarkPolicy.ShouldApply(With, true, false));
            Assert.Equal(WatermarkRow.Hidden, WatermarkPolicy.GetRow(Without, false));
        }

        [Fact]
        public void RefundBringsWatermarkBack()
        {
            var before = Entitlements.From(OwnedProducts.From(new[] { new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased) }));
            var after = Entitlements.From(OwnedProducts.From(new[] { new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked) }));
            Assert.False(WatermarkPolicy.ShouldApply(before));
            Assert.True(WatermarkPolicy.ShouldApply(after));
        }
    }
}
