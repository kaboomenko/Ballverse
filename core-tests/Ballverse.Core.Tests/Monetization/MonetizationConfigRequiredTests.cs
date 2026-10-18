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
            Assert.True(methods.Length >= 13, "found " + methods.Length);
            foreach (var m in methods)
            {
                foreach (var p in m.GetParameters().Where(p => p.ParameterType == typeof(MonetizationConfig)))
                    Assert.False(p.IsOptional, m.DeclaringType.Name + "." + m.Name + "(" + p.Name + ")");
            }
        }

        /// <summary>
        /// The persisted <c>grandfathered_modes</c> input is required wherever it appears (including
        /// <see cref="SalePointPolicy.CanStartTrialPreview"/>), so a call site that forgets it fails to compile instead of
        /// locking a mode the user used while it was free (spec §5.1).
        /// </summary>
        [Fact]
        public void NoPublicMethodHasAnOptionalGrandfatheredModes()
        {
            var methods = PublicMethods().Where(m => m.GetParameters().Any(p => p.Name == "grandfathered")).ToArray();
            Assert.True(methods.Length >= 8, "found " + methods.Length);
            Assert.Contains(methods, m => m.Name == nameof(SalePointPolicy.CanStartTrialPreview));
            foreach (var m in methods)
            {
                var p = m.GetParameters().Single(x => x.Name == "grandfathered");
                Assert.False(p.IsOptional, m.DeclaringType.Name + "." + m.Name + "(" + p.Name + ")");
            }
        }

        /// <summary>
        /// Spec §5.0.2: NO_WATERMARK is a pure function of the owned products, so no public API takes a watermark
        /// grandfathering flag, a purchase time or a rule cutoff, and no grandfathering type exists.
        /// </summary>
        [Fact]
        public void NoWatermarkGrandfatheringInputsExist()
        {
            foreach (var m in PublicMethods())
            {
                foreach (var p in m.GetParameters())
                {
                    Assert.False(p.Name.IndexOf("watermarkGrandfathered", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.Name.IndexOf("cutoff", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.Name.IndexOf("purchaseTime", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.Name.IndexOf("endedAt", StringComparison.OrdinalIgnoreCase) >= 0,
                        m.DeclaringType.Name + "." + m.Name + "(" + p.Name + ")");
                }
            }
            Assert.DoesNotContain(ModuleTypes(), t => t.Name.Contains("Grandfathering"));
            Assert.Null(typeof(PurchaseRecord).GetProperty("PurchaseTimeUnixMs"));
        }

        static Type[] ModuleTypes()
        {
            return typeof(MonetizationConfig).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == typeof(MonetizationConfig).Namespace).ToArray();
        }

        static MethodBase[] PublicMethods()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            return ModuleTypes()
                .SelectMany(t => t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags)))
                .ToArray();
        }

        /// <summary>
        /// QA round 2: the App cannot build a full-catalog config by accident (e.g. <c>new MonetizationConfig(freeModes)</c>):
        /// the only public constructor names every catalog input and has no default.
        /// </summary>
        [Fact]
        public void OnlyPublicConstructorIsExplicit()
        {
            var ctors = typeof(MonetizationConfig).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            var ctor = Assert.Single(ctors);
            Assert.Equal(3, ctor.GetParameters().Length);
            Assert.DoesNotContain(ctor.GetParameters(), p => p.IsOptional);
        }

        [Fact]
        public void NullConfigThrowsEverywhere()
        {
            const MonetizationConfig n = null;
            var e = EntitlementSet.Empty;
            Assert.Throws<ArgumentNullException>(() => Entitlements.From(None, n));
            Assert.Throws<ArgumentNullException>(() => Storefront.Build(None, n, null));
            Assert.Throws<ArgumentNullException>(() => Storefront.ModeSheet(ModeCode.M08, None, n, null));
            Assert.Throws<ArgumentNullException>(() => Storefront.WatermarkOffers(None, n, null));
            Assert.Throws<ArgumentNullException>(() => Storefront.CheapestPartsToComplete(e, n));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.DoneScreenLineOffers(3, null, None, n, null));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.CanStartTrialPreview(ModeCode.M08, e, null, 0, n, null));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(false, None, n, false, null));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(true, None, n, false, null));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.GetAccess(ModeCode.M01, e, null, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.IsAvailable(ModeCode.M01, e, null, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.RecordUse(null, ModeCode.M01, n));
            Assert.Throws<ArgumentNullException>(() => ModeAvailability.AvailableModes(e, null, n));

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
                Storefront.Build(None, d, null).VisibleOffers.Select(o => o.ProductId).ToArray());
            Assert.True(Entitlements.From(NoAds, d).Contains(Entitlement.NoWatermark));
            Assert.Equal(new[] { ProductIds.NoAds }, Storefront.WatermarkOffers(None, d, null).Select(o => o.ProductId).ToArray());
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 },
                ModeAvailability.AvailableModes(EntitlementSet.Empty, null, d).ToArray());
        }
    }
}
