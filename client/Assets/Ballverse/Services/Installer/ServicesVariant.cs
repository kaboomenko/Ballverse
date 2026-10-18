namespace Ballverse.Services
{
    /// <summary>
    /// Build variant of spec §7.9, mirroring <c>Ballverse.Platform.BuildVariant</c> (unity/bootstrap), which this assembly
    /// cannot reference. The build script compiles exactly one symbol into the player: <c>BALLVERSE_DEV</c>,
    /// <c>BALLVERSE_OWNERTEST</c> or <c>BALLVERSE_STORE</c>; none (editor, players built without the build script) counts
    /// as <see cref="Dev"/>.
    /// </summary>
    public enum ServicesBuildVariant
    {
        /// <summary>Development player: Google test ad units, fake store, developer menu.</summary>
        Dev = 0,

        /// <summary>Release-optimised player for the owner's phones: Google test ad units, fake store or sandbox.</summary>
        OwnerTest = 1,

        /// <summary>Store release: real AdMob units and store billing; fakes and test ids are not compiled in (spec §8.9).</summary>
        Store = 2,
    }

    /// <summary>Purchases in <see cref="ServicesBuildVariant.Dev"/> and <see cref="ServicesBuildVariant.OwnerTest"/> device builds.</summary>
    public enum TestBuildStore
    {
        /// <summary>The fake store (spec §7.9: until the apps exist in the stores).</summary>
        Fake = 0,

        /// <summary>Unity IAP against the store sandbox / license testers (spec §5.5), once the store apps exist.</summary>
        Sandbox = 1,
    }

    /// <summary>What <see cref="ServicesInstaller.Create"/> wires for one build (<see cref="ServicesPolicy.Resolve"/>).</summary>
    public sealed class ServicesPlan
    {
        public ServicesPlan(ServicesBuildVariant variant, bool fakeAds, bool fakeConsent, bool fakeStore, bool fakeFirebase)
        {
            Variant = variant;
            FakeAds = fakeAds;
            FakeConsent = fakeConsent;
            FakeStore = fakeStore;
            FakeFirebase = fakeFirebase;
        }

        public ServicesBuildVariant Variant { get; }

        /// <summary>Instant scriptable ads (<c>FakeAdNetwork</c>) instead of the Google Mobile Ads SDK.</summary>
        public bool FakeAds { get; }

        /// <summary>Scriptable consent (<c>FakeConsentService</c>) instead of UMP.</summary>
        public bool FakeConsent { get; }

        /// <summary>The fake store instead of Unity IAP.</summary>
        public bool FakeStore { get; }

        /// <summary>Log-based analytics / crash reporting and build-default Remote Config instead of Firebase.</summary>
        public bool FakeFirebase { get; }

        /// <summary>Dev and OwnerTest always use Google's test ad units, whatever is configured (spec §7.9, §8 "Реклама").</summary>
        public bool TestAdUnitsOnly => Variant != ServicesBuildVariant.Store;

        /// <summary>UMP debug settings (EEA test geography, test devices) apply in Dev and OwnerTest, never in Store.</summary>
        public bool AllowConsentDebug => Variant != ServicesBuildVariant.Store;

        /// <summary><see cref="ServicesMode.Fake"/> when everything is fake, otherwise <see cref="ServicesMode.Real"/>.</summary>
        public ServicesMode Mode => FakeAds && FakeConsent && FakeStore && FakeFirebase ? ServicesMode.Fake : ServicesMode.Real;

        public override string ToString()
        {
            return Variant + " (ads " + (FakeAds ? "fake" : "real") + ", consent " + (FakeConsent ? "fake" : "real")
                   + ", store " + (FakeStore ? "fake" : "real") + ", firebase " + (FakeFirebase ? "fake" : "real") + ")";
        }
    }

    /// <summary>
    /// Build-variant policy of spec §7.9 / §8.9 as pure functions (the installer applies them; tests cover every variant):
    /// <list type="table">
    /// <item><term>Store</term><description>always the real services and the configured AdMob units; missing or test units
    /// switch ads off (exports run without ads, Р14) instead of serving test ads; fakes are not even compiled in.</description></item>
    /// <item><term>Dev, OwnerTest</term><description>Google test ad units only. Auto on a device: real ads SDK and UMP (with the
    /// debug geography allowed), the fake store unless <see cref="TestBuildStore.Sandbox"/> is chosen. Fake: everything
    /// fake. Real: real store (sandbox) too.</description></item>
    /// <item><term>Editor</term><description>Auto follows <c>ServicesConfig.editorMode</c> (fakes by default).</description></item>
    /// </list>
    /// </summary>
    public static class ServicesPolicy
    {
        /// <summary>The variant this assembly was compiled as.</summary>
        public static ServicesBuildVariant CurrentVariant
        {
            get
            {
#if BALLVERSE_STORE
                return ServicesBuildVariant.Store;
#elif BALLVERSE_OWNERTEST
                return ServicesBuildVariant.OwnerTest;
#else
                return ServicesBuildVariant.Dev;
#endif
            }
        }

        /// <summary>
        /// The implementations to wire. <paramref name="warning"/> is set when the request was overridden (fakes asked for in
        /// a Store build).
        /// </summary>
        public static ServicesPlan Resolve(ServicesBuildVariant variant, ServicesMode requested, bool isEditor, ServicesMode editorMode,
            TestBuildStore testStore, out string warning)
        {
            warning = null;
            if (variant == ServicesBuildVariant.Store)
            {
                if (requested == ServicesMode.Fake) warning = "Fake services requested in a Store build; using the real ones (spec §8.9).";
                return new ServicesPlan(variant, false, false, false, false);
            }
            ServicesMode mode = requested;
            if (mode == ServicesMode.Auto && isEditor) mode = editorMode == ServicesMode.Auto ? ServicesMode.Fake : editorMode;
            switch (mode)
            {
                case ServicesMode.Fake:
                    return new ServicesPlan(variant, true, true, true, true);
                case ServicesMode.Real:
                    return new ServicesPlan(variant, false, false, false, false);
                default:
                    // Auto on a Dev / OwnerTest device: real SDKs with test ad units, purchases from the fake store or sandbox.
                    return new ServicesPlan(variant, false, false, testStore != TestBuildStore.Sandbox, false);
            }
        }

        /// <summary>
        /// Ad units for the real ads SDK. Dev / OwnerTest: <paramref name="testUnits"/> (Google's, for the platform), configured
        /// real ids are ignored. Store: <paramref name="configured"/> when complete, well-formed and not Google's test units;
        /// otherwise null (ads off) with <paramref name="problem"/> set.
        /// </summary>
        public static AdUnitIds SelectAdUnits(ServicesBuildVariant variant, AdUnitIds configured, AdUnitIds testUnits, out string problem)
        {
            problem = null;
            if (variant != ServicesBuildVariant.Store)
            {
                if (testUnits == null)
                {
                    problem = "No Google test ad units for this platform; ads are off.";
                    return null;
                }
                if (configured != null && !configured.AreTestIds)
                    problem = "Configured AdMob units are used only in Store builds; " + variant + " uses Google's test units (spec §7.9).";
                return testUnits;
            }
            if (configured == null)
            {
                problem = "Store build without AdMob ad unit ids (ServicesConfig); ads are off and exports run without ads.";
                return null;
            }
            if (configured.AnyTestId)
            {
                problem = "Store build configured with Google's test ad units; ads are off (spec §8.9). Set the real ids in ServicesConfig.";
                return null;
            }
            if (!configured.AreWellFormed)
            {
                problem = "Store build AdMob ad unit ids are not of the form ca-app-pub-…/…; ads are off.";
                return null;
            }
            return configured;
        }
    }
}
