using System;
using Ballverse.Platform;
using Ballverse.Services.Editor;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    /// <summary>Build variants (spec §7.9, §8.9) and the build steps that go with them.</summary>
    public sealed class BuildVariantTests
    {
        static readonly AdUnitIds RealUnits = new AdUnitIds("ca-app-pub-1234567890123456/1111111111", "ca-app-pub-1234567890123456/2222222222");

        static ServicesPlan Resolve(BuildVariant variant, ServicesMode mode, bool editor,
            TestBuildStore store = TestBuildStore.Fake, ServicesMode editorMode = ServicesMode.Fake)
        {
            var plan = ServicesPolicy.Resolve(variant, mode, editor, editorMode, store, out string warning);
            if (variant != BuildVariant.Store || mode != ServicesMode.Fake) Assert.IsNull(warning);
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
                    var plan = ServicesPolicy.Resolve(BuildVariant.Store, mode, editor, ServicesMode.Fake, TestBuildStore.Fake, out string warning);
                    Assert.IsFalse(plan.FakeAds || plan.FakeConsent || plan.FakeStore || plan.FakeFirebase, mode + " editor=" + editor);
                    Assert.AreEqual(ServicesMode.Real, plan.Mode);
                    Assert.IsFalse(plan.TestAdUnitsOnly);
                    Assert.IsFalse(plan.AllowConsentDebug);
                    Assert.AreEqual(mode == ServicesMode.Fake, warning != null);
                }
            }
            var sandbox = Resolve(BuildVariant.Store, ServicesMode.Auto, false, TestBuildStore.Fake);
            Assert.IsFalse(sandbox.FakeStore, "the test-build store setting does not apply to Store");
        }

        [Test]
        public void DevAndOwnerTestDevicesUseTestAdsRealSdksAndTheFakeStoreByDefault()
        {
            foreach (var variant in new[] { BuildVariant.Dev, BuildVariant.OwnerTest })
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
            Assert.AreEqual(ServicesMode.Fake, Resolve(BuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Fake).Mode);
            Assert.AreEqual(ServicesMode.Fake, Resolve(BuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Auto).Mode);
            var real = Resolve(BuildVariant.Dev, ServicesMode.Auto, true, editorMode: ServicesMode.Real);
            Assert.IsFalse(real.FakeStore);
            Assert.IsTrue(real.TestAdUnitsOnly);
        }

        [Test]
        public void TheVariantIsTheAppsBuildVariant()
        {
            // One definition of the variant (Ballverse.Platform); the editor and test assemblies get no variant symbol.
            Assert.AreEqual(BuildVariant.Dev, BuildVariants.Current);
            var s = ServicesInstaller.Create(new ServicesOptions
            {
                Mode = ServicesMode.Fake,
                Clock = new ManualClock(),
                Store = new MemoryKeyValueStore(),
                Log = new MemoryServiceLog(),
                Connectivity = new FakeConnectivity(),
            });
            Assert.AreEqual(BuildVariants.Current, s.Plan.Variant, "the installer defaults to the compiled-in variant");
        }

        // ---- ad units ----

        [Test]
        public void DevAndOwnerTestAlwaysUseGoogleTestUnits()
        {
            foreach (var variant in new[] { BuildVariant.Dev, BuildVariant.OwnerTest })
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
            Assert.AreSame(RealUnits, ServicesPolicy.SelectAdUnits(BuildVariant.Store, RealUnits, null, out string problem));
            Assert.IsNull(problem);

            Assert.IsNull(ServicesPolicy.SelectAdUnits(BuildVariant.Store, null, AdUnitIds.AndroidTest, out problem), "no fallback to test units");
            Assert.IsNotNull(problem);
            Assert.IsNull(ServicesPolicy.SelectAdUnits(BuildVariant.Store, AdUnitIds.AndroidTest, null, out problem));
            Assert.IsNotNull(problem);
            var mixed = new AdUnitIds(RealUnits.Rewarded, AdUnitIds.TestAndroidInterstitial);
            Assert.IsNull(ServicesPolicy.SelectAdUnits(BuildVariant.Store, mixed, null, out problem), "one test unit is enough to refuse");
            Assert.IsNull(ServicesPolicy.SelectAdUnits(BuildVariant.Store, new AdUnitIds("rewarded", "interstitial"), null, out problem));
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
            // Must hold whichever SDK packages are installed: the project manifest has Google Mobile Ads 11.6.0 and Unity IAP
            // 5.4.4 (BALLVERSE_ADMOB / BALLVERSE_UNITY_IAP defined), the dotnet harness also builds without them. Tick() is
            // not called, so no real SDK is started from an EditMode test.
            var log = new MemoryServiceLog();
            var s = ServicesInstaller.Create(new ServicesOptions
            {
                Variant = BuildVariant.Store,
                Mode = ServicesMode.Fake,
                Clock = new ManualClock(),
                Store = new MemoryKeyValueStore(),
                Log = log,
                Connectivity = new FakeConnectivity(),
                AdUnits = AdUnitIds.AndroidTest, // test units handed to a Store build are refused
            });
            Assert.AreEqual(BuildVariant.Store, s.Plan.Variant);
            Assert.AreEqual(ServicesMode.Real, s.Mode);
            Assert.IsFalse(s.Plan.FakeAds || s.Plan.FakeConsent || s.Plan.FakeStore || s.Plan.FakeFirebase);
            Assert.IsNull(s.FakeAds);
            Assert.IsNull(s.FakeStore);
            Assert.IsNotInstanceOf<FakeConsentService>(s.Consent);
            CollectionAssert.Contains(new[] { "UmpConsentService", "NoConsentService" }, s.Consent.GetType().Name,
                "UMP when the ads package is installed, otherwise consent not required");
            Assert.IsInstanceOf<BuildDefaultsRemoteConfigService>(s.RemoteConfig, "build defaults while Firebase is off, no test values");
            Assert.AreSame(Ballverse.Core.Config.RemoteConfigValues.Defaults, s.RemoteConfig.Current);
            Assert.IsTrue(MonetizationConfigMapper.SameCatalog(Ballverse.Core.Monetization.MonetizationConfig.Release1, s.Purchases.Config));
            Assert.IsTrue(new System.Collections.Generic.List<string>(log.Lines)
                .Exists(l => l.StartsWith("E [Installer] Fake services requested in a Store build", StringComparison.Ordinal)),
                "the fake request is reported");
            Assert.AreEqual(PurchaseServiceState.NotInitialized, s.Purchases.State, "nothing connects before the first Tick");

            s.Ads.Initialize();
            Assert.AreEqual(AdsServiceState.Unavailable, s.Ads.State, "test units in a Store build: ads off (exports without ads), never test ads");
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

        [Test]
        public void StoreBuildCheckFindsTheVariantWithoutTheCommandLineFlag()
        {
            string[] none = { "Unity", "-batchmode", "-executeMethod", "Ballverse.Editor.BuildScript.BuildAndroidAab" };

            // The bootstrap App Bundle default: the variant is only in the build's extra defines, or nowhere at all.
            Assert.AreEqual(BuildVariant.Store, StoreBuildCheck.ResolveVariant(new[] { BuildVariants.StoreSymbol }, "", none, true));
            Assert.AreEqual(BuildVariant.Store, StoreBuildCheck.ResolveVariant(null, "", none, androidAppBundle: true));
            // Menu builds pass the variant in the extra defines only.
            Assert.AreEqual(BuildVariant.Store, StoreBuildCheck.ResolveVariant(new[] { BuildVariants.StoreSymbol }, null, null, false));
            Assert.AreEqual(BuildVariant.OwnerTest, StoreBuildCheck.ResolveVariant(new[] { BuildVariants.OwnerTestSymbol }, "", none, true),
                "an explicit OwnerTest App Bundle is not a Store build");
            Assert.AreEqual(BuildVariant.Dev, StoreBuildCheck.ResolveVariant(new[] { BuildVariants.DevSymbol }, "", null, true));
            // Player Settings symbols compile into the player too; Store wins as in BuildVariants.Current.
            Assert.AreEqual(BuildVariant.Store, StoreBuildCheck.ResolveVariant(new[] { BuildVariants.OwnerTestSymbol },
                "FOO; " + BuildVariants.StoreSymbol, none, false));
            Assert.AreEqual(BuildVariant.OwnerTest, StoreBuildCheck.ResolveVariant(null, "FOO;" + BuildVariants.OwnerTestSymbol, none, true));
            // Command line when no symbol is known; an APK or Xcode project without any of these is a Dev player.
            Assert.AreEqual(BuildVariant.Store, StoreBuildCheck.ResolveVariant(null, "", new[] { "-ballverseVariant", "Store" }, false));
            Assert.AreEqual(BuildVariant.OwnerTest, StoreBuildCheck.ResolveVariant(null, "", new[] { "-ballverseVariant", "OwnerTest" }, true));
            Assert.IsNull(StoreBuildCheck.ResolveVariant(null, null, none, androidAppBundle: false));
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
