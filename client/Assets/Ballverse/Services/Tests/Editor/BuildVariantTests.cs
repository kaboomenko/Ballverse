using System;
using Ballverse.Services.Editor;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    /// <summary>Build variants (spec §7.9, §8.9) and the build steps that go with them.</summary>
    public sealed class BuildVariantTests
    {
        static readonly AdUnitIds RealUnits = new AdUnitIds("ca-app-pub-1234567890123456/1111111111", "ca-app-pub-1234567890123456/2222222222");

        static ServicesPlan Resolve(ServicesBuildVariant variant, ServicesMode mode, bool editor,
            TestBuildStore store = TestBuildStore.Fake, ServicesMode editorMode = ServicesMode.Fake)
        {
            var plan = ServicesPolicy.Resolve(variant, mode, editor, editorMode, store, out string warning);
            if (variant != ServicesBuildVariant.Store || mode != ServicesMode.Fake) Assert.IsNull(warning);
            return plan;
        }

        // ---- which implementations ----

        [Test]
        public void StoreBuildsAlwaysUseTheRealServices()
        {
            foreach (ServicesMode mode in Enum.GetValues(typeof(ServicesMode)))
            {
                foreach (bool editor in new[] { false, true })
                {
                    var plan = ServicesPolicy.Resolve(ServicesBuildVariant.Store, mode, editor, ServicesMode.Fake, TestBuildStore.Fake, out string warning);
                    Assert.IsFalse(plan.FakeAds || plan.FakeConsent || plan.FakeStore || plan.FakeFirebase, mode + " editor=" + editor);
                    Assert.AreEqual(ServicesMode.Real, plan.Mode);
                    Assert.IsFalse(plan.TestAdUnitsOnly);
                    Assert.IsFalse(plan.AllowConsentDebug);
                    Assert.AreEqual(mode == ServicesMode.Fake, warning != null);
                }
            }
            var sandbox = Resolve(ServicesBuildVariant.Store, ServicesMode.Auto, false, TestBuildStore.Fake);
            Assert.IsFalse(sandbox.FakeStore, "the test-build store setting does not apply to Store");
        }

        [Test]
        public void DevAndOwnerTestDevicesUseTestAdsRealSdksAndTheFakeStoreByDefault()
        {
            foreach (var variant in new[] { ServicesBuildVariant.Dev, ServicesBuildVariant.OwnerTest })
            {
                var plan = Resolve(variant, ServicesMode.Auto, editor: false);
                Assert.IsFalse(plan.FakeAds, variant + ": the Google SDK with test units");
                Assert.IsFalse(plan.FakeConsent, variant + ": UMP");
                Assert.IsTrue(plan.FakeStore, variant + ": fake store until the store apps exist");
                Assert.IsFalse(plan.FakeFirebase);
                Assert.IsTrue(plan.TestAdUnitsOnly);
                Assert.IsTrue(plan.AllowConsentDebug, variant + ": UMP EEA test geography allowed");
                Assert.AreEqual(ServicesMode.Real, plan.Mode);

                var sandbox = Resolve(variant, ServicesMode.Auto, editor: false, store: TestBuildStore.Sandbox);
                Assert.IsFalse(sandbox.FakeStore, variant + ": sandbox opt-in");
                Assert.IsTrue(sandbox.TestAdUnitsOnly);

                var fake = Resolve(variant, ServicesMode.Fake, editor: false);
                Assert.IsTrue(fake.FakeAds && fake.FakeConsent && fake.FakeStore && fake.FakeFirebase);
                Assert.AreEqual(ServicesMode.Fake, fake.Mode);

                var real = Resolve(variant, ServicesMode.Real, editor: false);
                Assert.IsFalse(real.FakeAds || real.FakeConsent || real.FakeStore || real.FakeFirebase);
                Assert.IsTrue(real.TestAdUnitsOnly, variant + ": still test ad units");
            }
        }

        [Test]
        public void EditorFollowsTheConfiguredEditorMode()
        {
            Assert.AreEqual(ServicesMode.Fake, Resolve(ServicesBuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Fake).Mode);
            Assert.AreEqual(ServicesMode.Fake, Resolve(ServicesBuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Auto).Mode);
            var real = Resolve(ServicesBuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Real);
            Assert.IsFalse(real.FakeStore);
            Assert.IsTrue(real.TestAdUnitsOnly);
        }

        [Test]
        public void CompiledVariantWithoutASymbolIsDev()
        {
            // The editor and test assemblies get no variant symbol.
            Assert.AreEqual(ServicesBuildVariant.Dev, ServicesPolicy.CurrentVariant);
        }

        // ---- ad units ----

        [Test]
        public void DevAndOwnerTestAlwaysUseGoogleTestUnits()
        {
            foreach (var variant in new[] { ServicesBuildVariant.Dev, ServicesBuildVariant.OwnerTest })
            {
                var units = ServicesPolicy.SelectAdUnits(variant, RealUnits, AdUnitIds.AndroidTest, out string problem);
                Assert.IsTrue(units.AreTestIds, variant + ": configured real ids are ignored");
                Assert.IsNotNull(problem);
                units = ServicesPolicy.SelectAdUnits(variant, null, AdUnitIds.IosTest, out problem);
                Assert.AreEqual(AdUnitIds.TestIosRewarded, units.Rewarded);
                Assert.IsNull(problem);
            }
        }

        [Test]
        public void StoreUsesOnlyRealUnitsAndOtherwiseSwitchesAdsOff()
        {
            Assert.AreSame(RealUnits, ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, RealUnits, null, out string problem));
            Assert.IsNull(problem);

            Assert.IsNull(ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, null, AdUnitIds.AndroidTest, out problem), "no fallback to test units");
            Assert.IsNotNull(problem);
            Assert.IsNull(ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, AdUnitIds.AndroidTest, null, out problem));
            Assert.IsNotNull(problem);
            var mixed = new AdUnitIds(RealUnits.Rewarded, AdUnitIds.TestAndroidInterstitial);
            Assert.IsNull(ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, mixed, null, out problem), "one test unit is enough to refuse");
            Assert.IsNull(ServicesPolicy.SelectAdUnits(ServicesBuildVariant.Store, new AdUnitIds("rewarded", "interstitial"), null, out problem));
            Assert.IsNotNull(problem);
        }

        [Test]
        public void ConfigHasNoTestUnitFallback()
        {
            Assert.IsNull(ServicesConfig.UnitsOrNull("", AdUnitIds.TestAndroidInterstitial));
            Assert.IsNull(ServicesConfig.UnitsOrNull(" ", "x"));
            var units = ServicesConfig.UnitsOrNull(" " + RealUnits.Rewarded + " ", RealUnits.Interstitial);
            Assert.AreEqual(RealUnits.Rewarded, units.Rewarded);
        }

        // ---- installer ----

        [Test]
        public void StoreVariantInstallerWiresNoFakesEvenWhenAskedTo()
        {
            var log = new MemoryServiceLog();
            var s = ServicesInstaller.Create(new ServicesOptions
            {
                Variant = ServicesBuildVariant.Store,
                Mode = ServicesMode.Fake,
                Clock = new ManualClock(),
                Store = new MemoryKeyValueStore(),
                Log = log,
                Connectivity = new FakeConnectivity(),
            });
            Assert.AreEqual(ServicesBuildVariant.Store, s.Plan.Variant);
            Assert.AreEqual(ServicesMode.Real, s.Mode);
            Assert.IsNull(s.FakeAds);
            Assert.IsNull(s.FakeStore);
            Assert.IsInstanceOf<NoConsentService>(s.Consent, "no UMP package in this test build");
            Assert.Greater(log.ErrorCount, 0, "the fake request is reported");

            s.Tick();
            Assert.AreEqual(PurchaseServiceState.Unavailable, s.Purchases.State, "no store SDK here: unavailable, never fake");
        }

        // ---- build steps ----

        [Test]
        public void StoreBuildCheckReadsTheVariantFromTheCommandLine()
        {
            Assert.IsTrue(StoreBuildCheck.IsStoreVariant(new[] { "Unity", "-batchmode", "-ballverseVariant", "store" }));
            Assert.IsFalse(StoreBuildCheck.IsStoreVariant(new[] { "-ballverseVariant", "OwnerTest" }));
            Assert.IsFalse(StoreBuildCheck.IsStoreVariant(new[] { "-ballverseVariant" }));
            Assert.IsFalse(StoreBuildCheck.IsStoreVariant(null));

            Assert.IsNull(StoreBuildCheck.StoreAdUnitsProblem(RealUnits.Rewarded, RealUnits.Interstitial));
            Assert.IsNotNull(StoreBuildCheck.StoreAdUnitsProblem("", ""));
            Assert.IsNotNull(StoreBuildCheck.StoreAdUnitsProblem(AdUnitIds.TestIosRewarded, AdUnitIds.TestIosInterstitial));
        }

        const string Manifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.unity3d.player\">\n" +
            "  <application>\n" +
            "    <activity android:name=\"com.unity3d.player.UnityPlayerActivity\" />\n" +
            "    <meta-data android:name=\"google_analytics_default_allow_ad_storage\" android:value=\"true\" />\n" +
            "  </application>\n" +
            "</manifest>\n";

        [Test]
        public void FirebaseCollectionAndConsentDefaultToOffInTheAndroidManifest()
        {
            string patched = FirebaseConsentDefaults.ApplyToManifest(Manifest);
            Assert.AreEqual("false", AndroidManifestText.GetMetaData(patched, "firebase_analytics_collection_enabled"));
            Assert.AreEqual("false", AndroidManifestText.GetMetaData(patched, "google_analytics_default_allow_analytics_storage"));
            Assert.AreEqual("false", AndroidManifestText.GetMetaData(patched, "google_analytics_default_allow_ad_user_data"));
            Assert.AreEqual("false", AndroidManifestText.GetMetaData(patched, "google_analytics_default_allow_ad_personalization_signals"));
            Assert.AreEqual("true", AndroidManifestText.GetMetaData(patched, "google_analytics_default_allow_ad_storage"), "owner value kept");
            StringAssert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", patched);
            StringAssert.Contains("<meta-data android:name=\"firebase_analytics_collection_enabled\" android:value=\"false\" />", patched);
            StringAssert.DoesNotContain("xmlns:android=\"http://schemas.android.com/apk/res/android\" xmlns", patched);
            Assert.AreEqual(patched, FirebaseConsentDefaults.ApplyToManifest(patched), "idempotent");
        }

        [Test]
        public void FirebaseCollectionAndConsentDefaultToOffInInfoPlist()
        {
            const string plist = "<?xml version=\"1.0\"?>\n<plist version=\"1.0\">\n<dict>\n\t<key>FIREBASE_ANALYTICS_COLLECTION_ENABLED</key>\n\t<true/>\n</dict>\n</plist>\n";
            string patched = FirebaseConsentDefaults.ApplyToPlist(plist);
            StringAssert.Contains("<key>FIREBASE_ANALYTICS_COLLECTION_ENABLED</key>\n\t<true/>", patched, "owner value kept");
            foreach (string key in FirebaseConsentDefaults.IosPlistKeys)
            {
                Assert.IsTrue(InfoPlistText.HasKey(patched, key), key);
                if (key != "FIREBASE_ANALYTICS_COLLECTION_ENABLED") StringAssert.Contains("<key>" + key + "</key>\n\t<false/>", patched);
            }
            Assert.AreEqual(patched, FirebaseConsentDefaults.ApplyToPlist(patched));
        }
    }
}
