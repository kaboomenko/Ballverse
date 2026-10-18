using System;
using System.Linq;
using System.Reflection;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// No implicit catalog: the release config is a required argument everywhere, null is rejected, and the default preset
    /// is the shipping release (owner decision "the store must NOT sell products that promise unavailable modes").
    /// </summary>
    public class MonetizationConfigRequiredTests
    {
        static readonly string[] None = new string[0];
        static readonly string[] NoAds = { ProductIds.NoAds };

        [Fact]
        public void DefaultIsTheShippingRelease1()
        {
            Assert.Same(MonetizationConfig.Release1, MonetizationConfig.Default);
        }

        /// <summary>
        /// Every public method of the module that takes a <see cref="MonetizationConfig"/> takes it as a required parameter, so
        /// forgetting it in the App layer fails to compile instead of silently selling the full catalog.
        /// </summary>
        [Fact]
        public void NoPublicMethodHasAnOptionalConfig()
        {
            var methods = typeof(MonetizationConfig).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == typeof(MonetizationConfig).Namespace)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(MonetizationConfig)))
                .ToArray();
            Assert.True(methods.Length >= 15, "found " + methods.Length);
            foreach (var m in methods)
            {
                foreach (var p in m.GetParameters().Where(p => p.ParameterType == typeof(MonetizationConfig)))
                    Assert.False(p.IsOptional, m.DeclaringType.Name + "." + m.Name + "(" + p.Name + ")");
            }
        }

        [Fact]
        public void NullConfigThrowsEverywhere()
        {
            const MonetizationConfig n = null;
            var e = EntitlementSet.Empty;
            Assert.Throws<ArgumentNullException>(() => Entitlements.From(None, n));
            Assert.Throws<ArgumentNullException>(() => Entitlements.ApplyRules(e, n));
            Assert.Throws<ArgumentNullException>(() => Storefront.Build(None, n));
            Assert.Throws<ArgumentNullException>(() => Storefront.ModeSheet(ModeCode.M08, None, n));
            Assert.Throws<ArgumentNullException>(() => Storefront.WatermarkOffers(None, n));
            Assert.Throws<ArgumentNullException>(() => Storefront.CheapestPartsToComplete(e, n));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.DoneScreenLineOffers(3, null, None, n));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.CanStartTrialPreview(ModeCode.M08, e, null, 0, n));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(false, None, n));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(true, None, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.GetAccess(ModeCode.M01, e, null, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.IsAvailable(ModeCode.M01, e, null, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.RecordUse(null, ModeCode.M01, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.AvailableModes(e, null, n));
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.Update(false, None, n));
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.Update(true, None, n));
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.Update(false, e, n));
            Assert.Throws<ArgumentNullException>(() => WatermarkGrandfathering.Update(false, new PurchaseRecord[0], n));

            var key = new byte[] { 1, 2, 3 };
            var entry = EntitlementCache.Load(EntitlementCache.Serialize(NoAds, 1, key), key);
            Assert.Throws<ArgumentNullException>(() => entry.GetEntitlements(n));
        }

        /// <summary>The QA probes for a forgotten argument, rewritten against the default preset: release-1 behaviour.</summary>
        [Fact]
        public void DefaultPreset_SellsOnlyAdProducts_NoAdsRemovesWatermark()
        {
            var d = MonetizationConfig.Default;
            Assert.Equal(new[] { ProductIds.NoAds, ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds },
                Storefront.Build(None, d).VisibleOffers.Select(o => o.ProductId).ToArray());
            Assert.True(Entitlements.From(NoAds, d).Contains(Entitlement.NoWatermark));
            Assert.Equal(new[] { ProductIds.NoAds }, Storefront.WatermarkOffers(None, d).Select(o => o.ProductId).ToArray());
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 },
                ModeAvailability.AvailableModes(EntitlementSet.Empty, null, d).ToArray());
        }
    }
}
