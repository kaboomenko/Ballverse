using System.Linq;
using Ballverse.Core.Monetization;
using Ballverse.Core.Projects;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Spec §3.10 "Поведение" (Р13). The row presentation is <see cref="WatermarkRowPolicy"/>; whether the watermark is drawn
    /// is the single canonical rule <see cref="WatermarkPolicy.Resolve"/> (Projects), fed with NO_WATERMARK from
    /// <see cref="Entitlements.From"/>. This file imports both namespaces and uses <c>WatermarkPolicy</c> unqualified on
    /// purpose: it stops compiling (CS0104) if Monetization ever exports a second <c>WatermarkPolicy</c>.
    /// </summary>
    public class WatermarkRowPolicyTests
    {
        static readonly EntitlementSet Without = Entitlements.From(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog);
        static readonly EntitlementSet With = Entitlements.From(new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog);

        static bool Apply(EntitlementSet e, bool toggle = false, bool enabled = true)
        {
            return WatermarkPolicy.Resolve(toggle, e.Contains(Entitlement.NoWatermark), enabled);
        }

        [Fact]
        public void WithoutRight_WatermarkAlwaysOnAndRowLocked()
        {
            Assert.True(Apply(Without));
            Assert.True(Apply(Without, true));
            Assert.Equal(WatermarkRow.Locked, WatermarkRowPolicy.GetRow(Without));
        }

        [Fact]
        public void WithRight_DefaultOffButUserMayKeepIt()
        {
            Assert.False(Apply(With));
            Assert.True(Apply(With, true));
            Assert.Equal(WatermarkRow.Toggle, WatermarkRowPolicy.GetRow(With));
        }

        [Fact]
        public void ByPartsOwnerAlsoGetsToggle()
        {
            var parts = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds }, MonetizationConfig.FullCatalog);
            Assert.Equal(WatermarkRow.Toggle, WatermarkRowPolicy.GetRow(parts));
            Assert.False(Apply(parts));
        }

        [Fact]
        public void KillSwitchRemovesWatermarkForEveryone()
        {
            Assert.False(Apply(Without, false, false));
            Assert.False(Apply(With, true, false));
            Assert.Equal(WatermarkRow.Hidden, WatermarkRowPolicy.GetRow(Without, false));
            Assert.Equal(WatermarkRow.Hidden, WatermarkRowPolicy.GetRow(With, false));
        }

        [Fact]
        public void RefundBringsWatermarkBack()
        {
            var before = Entitlements.From(OwnedProducts.From(new[] { new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Purchased) }), MonetizationConfig.FullCatalog);
            var after = Entitlements.From(OwnedProducts.From(new[] { new PurchaseRecord(ProductIds.Ultimate, PurchaseState.Revoked) }), MonetizationConfig.FullCatalog);
            Assert.False(Apply(before));
            Assert.True(Apply(after));
        }

        /// <summary>
        /// Ownership (main INTEGRATION.md "One watermark rule"): no public top-level Monetization type shares its name with a
        /// type of another Ballverse.Core namespace, so importing Monetization next to any other Core namespace never fails
        /// with CS0104.
        /// </summary>
        [Fact]
        public void MonetizationTypeNamesAreUniqueAcrossCoreNamespaces()
        {
            const string ns = "Ballverse.Core.Monetization";
            var types = typeof(MonetizationConfig).Assembly.GetExportedTypes().Where(t => !t.IsNested && t.Namespace != null).ToArray();
            var ours = types.Where(t => t.Namespace == ns || t.Namespace.StartsWith(ns + ".", System.StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(ours);
            Assert.DoesNotContain(ours, t => t.Name == "WatermarkPolicy");
            foreach (var t in ours)
            {
                var clash = types.Where(o => o.Name == t.Name && o.Namespace != t.Namespace).Select(o => o.FullName).ToArray();
                Assert.True(clash.Length == 0, t.FullName + " clashes with " + string.Join(", ", clash));
            }
        }
    }
}
