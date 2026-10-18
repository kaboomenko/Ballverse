using System.Collections.Generic;
using Ballverse.Core.Monetization;
using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// Registration entry point of the services layer for the App composition root (spec §7.3). Ballverse.Services cannot
    /// reference Ballverse.App, so the App's <c>IAppModule</c> contract (unity/bootstrap branch: <c>Ballverse.App.IAppModule</c>
    /// with <c>Register</c> / <c>Start</c> / <c>Shutdown</c>, <c>AppStartPhase</c>, <c>AppModuleCatalog</c>) wraps this class in
    /// a <c>ServicesModule</c> under <c>Assets/Ballverse/App/Modules/</c>:
    /// <code>
    /// public sealed class ServicesModule : IAppModule
    /// {
    ///     BallverseServices _services; ServicesRunner _runner;
    ///     public string Name => "Services";
    ///     public AppStartPhase Phase => AppStartPhase.Immediate;   // frame-0 part only: crash, cached rights, ad policy
    ///     public void Register(AppServices s) { _services = ServicesInstaller.Create(); /* publish _services.Ads, .Purchases, … */ }
    ///     public void Start(AppModuleContext c) { _runner = ServicesRunner.Attach(_services); }
    ///     public void Shutdown() { _runner?.Detach(); }
    /// }
    /// </code>
    /// The services do their own frame-1+ phasing (<see cref="ServicesStartup"/>: Remote Config → IAP → consent → ads after
    /// the first frame, as <c>AppModuleCatalog</c> intends), so they need one per-frame owner: either the
    /// <see cref="ServicesRunner"/> (as above) or the App's own update loop calling <see cref="BallverseServices.Tick"/> and
    /// <see cref="BallverseServices.OnApplicationResumed"/> (from <c>IAppLifecycle.Resumed</c>) and
    /// <see cref="BallverseServices.OnApplicationFocusChanged"/> (from <c>IAppLifecycle.FocusChanged</c>) — never both. Without the App
    /// (tests, sample scenes): <c>ServicesInstaller.CreateAndRun()</c>.
    /// <para>
    /// Which implementations a build gets depends on its variant (spec §7.9, §8.9; <see cref="ServicesPolicy"/>): Store builds
    /// always use the real services and contain no fakes or test ad ids; Dev and OwnerTest use Google's test ad units and,
    /// on a device, the fake store unless <see cref="ServicesConfig.testBuildStore"/> selects the sandbox. Within that, the
    /// SDKs compiled in decide (spec §7.1 "every external dependency behind an interface with a fake"):
    /// </para>
    /// <para>
    /// <list type="table">
    /// <item><term>Ads + consent</term><description><c>BALLVERSE_ADMOB</c> (set automatically when the
    /// <c>com.google.ads.mobile</c> package 9–11 is installed): Google Mobile Ads + UMP; otherwise no ads (exports run without
    /// ads, Р14) and consent "not required".</description></item>
    /// <item><term>Purchases</term><description><c>BALLVERSE_UNITY_IAP</c> (set automatically for <c>com.unity.purchasing</c>
    /// 5.4.2+): Unity IAP; otherwise the store is unavailable and rights come only from the cache.</description></item>
    /// <item><term>Analytics, Crashlytics, Remote Config</term><description><c>BALLVERSE_FIREBASE</c> (scripting define,
    /// set by hand once the Firebase SDK and config files are added): Firebase; otherwise log-based crash reports and analytics and
    /// build-default Remote Config (the release-1 production path, not test fakes).</description></item>
    /// <item><term>Editor</term><description>fakes (<see cref="ServicesConfig.editorMode"/>).</description></item>
    /// </list>
    /// </para>
    /// </summary>
    public static class ServicesInstaller
    {
        const string Tag = "Installer";

        /// <summary>Creates the services and attaches a <see cref="ServicesRunner"/> that ticks them.</summary>
        public static BallverseServices CreateAndRun(ServicesOptions options = null)
        {
            var services = Create(options);
            ServicesRunner.Attach(services);
            return services;
        }

        /// <summary>
        /// Builds and wires all services for the build variant (<see cref="ServicesPolicy"/>), runs the cheap frame-0 steps of
        /// spec §7.9 (crash reporter, cached rights, ad policy state) and returns. The rest starts on the first
        /// <see cref="BallverseServices.Tick"/>.
        /// </summary>
        public static BallverseServices Create(ServicesOptions options = null)
        {
            options = options ?? new ServicesOptions();
            ServicesConfig config = null;
            ServicesConfig Config() => config ?? (config = ServicesConfig.LoadOrDefault());

            bool developer = options.DeveloperBuild ?? Debug.isDebugBuild;
            var log = options.Log ?? new UnityServiceLog(developer);
            var clock = options.Clock ?? SystemClock.Instance;
            var store = options.Store ?? new PlayerPrefsKeyValueStore();
            var connectivity = options.Connectivity ?? new UnityConnectivity();
            var queue = new MainThreadQueue(log);
            var tickables = new List<ITickable>();

            var variant = options.Variant ?? ServicesPolicy.CurrentVariant;
            bool isEditor = Application.isEditor;
            bool auto = options.Mode == ServicesMode.Auto;
            // The config asset is read only when the answer depends on it.
            ServicesMode editorMode = auto && isEditor ? Config().editorMode : ServicesMode.Auto;
            TestBuildStore testStore = options.TestBuildStore
                                       ?? (auto && !isEditor && variant != ServicesBuildVariant.Store ? Config().testBuildStore : TestBuildStore.Fake);
            var plan = ServicesPolicy.Resolve(variant, options.Mode, isEditor, editorMode, testStore, out string planWarning);
            if (planWarning != null) log.Error(Tag, planWarning);

            // ---- Firebase: crash reporting, analytics, remote config ----
            ICrashReporter crash;
            IAnalyticsService analytics;
            IRemoteConfigService remoteConfig;
#if BALLVERSE_FIREBASE
            if (!plan.FakeFirebase)
            {
                var firebase = new FirebaseBootstrap(queue, log);
                crash = new FirebaseCrashReporter(firebase, log);
                analytics = new FirebaseAnalyticsService(firebase, log);
                var rc = new FirebaseRemoteConfigService(firebase, queue, clock, developer, log);
                tickables.Add(rc);
                remoteConfig = rc;
            }
            else
#endif
            {
                // Without Firebase (release 1): log-based crash reports and analytics, build-default Remote Config.
                crash = new LogCrashReporter(log);
                analytics = new RecordingAnalyticsService(log);
                remoteConfig = new FakeRemoteConfigService(log, options.FakeRemoteConfig);
            }

            // ---- Consent (UMP) and ATT ----
            var tracking = options.Tracking ?? CreateTracking(queue, plan.FakeConsent);
            IConsentService consent = options.Consent;
            if (consent == null)
            {
#if !BALLVERSE_STORE
                if (plan.FakeConsent)
                {
                    consent = new FakeConsentService(tracking, clock, log);
                }
                else
#endif
                {
#if BALLVERSE_ADMOB
                    ConsentDebugOptions debug = null;
                    if (plan.AllowConsentDebug)
                    {
                        debug = new ConsentDebugOptions { ForceEeaGeography = options.UmpForceEeaGeography || Config().umpForceEeaGeography };
                        debug.TestDeviceHashedIds.AddRange(options.UmpTestDeviceHashedIds);
                        debug.TestDeviceHashedIds.AddRange(Config().umpTestDeviceHashedIds);
                    }
                    var ump = new UmpConsentService(queue, clock, tracking, CreateTcfStorage(log), debug, log);
                    tickables.Add(ump);
                    consent = ump;
#else
                    consent = new NoConsentService(tracking, clock, log);
#endif
                }
            }

            // ---- Ads ----
            IAdNetwork network = null;
#if !BALLVERSE_STORE
            FakeAdNetwork fakeAds = null;
            if (plan.FakeAds)
            {
                fakeAds = new FakeAdNetwork(clock);
                tickables.Add(fakeAds);
                network = fakeAds;
            }
#endif
            if (network == null)
            {
#if BALLVERSE_ADMOB
#if BALLVERSE_STORE
                AdUnitIds testUnits = null;
#else
                AdUnitIds testUnits = AdUnitIds.CurrentPlatformTest;
#endif
                var units = ServicesPolicy.SelectAdUnits(variant, options.AdUnits ?? Config().CurrentPlatformAdUnits(), testUnits, out string adProblem);
                if (units != null)
                {
                    if (adProblem != null) log.Warning(Tag, adProblem);
                    var testDevices = new List<string>(options.AdTestDeviceIds);
                    testDevices.AddRange(Config().adTestDeviceIds);
                    network = new GoogleMobileAdsNetwork(queue, units, testDevices, log);
                }
                else
                {
                    log.Error(Tag, adProblem);
                    crash.Log("ads off: " + adProblem);
                    network = new UnavailableAdNetwork();
                }
#else
                network = new UnavailableAdNetwork();
#endif
            }
            var ads = new AdsService(network, clock, log);
            tickables.Add(ads);

            // ---- Purchases ----
            IStoreBackend backend = options.StoreBackend;
            IReceiptValidator validator = AcceptAllReceiptValidator.Instance;
#if !BALLVERSE_STORE
            FakeStoreBackend fakeStore = null;
            if (backend == null && plan.FakeStore)
            {
                fakeStore = new FakeStoreBackend(options.FakeStore);
                backend = fakeStore;
            }
#endif
            if (backend == null)
            {
#if BALLVERSE_UNITY_IAP
                backend = new UnityIapStoreBackend(queue, log);
#if UNITY_ANDROID
                string key = !string.IsNullOrEmpty(options.GooglePlayPublicKeyBase64) ? options.GooglePlayPublicKeyBase64 : Config().googlePlayPublicKeyBase64;
                validator = new GooglePlayReceiptValidator(key, options.ApplicationId ?? Application.identifier, log);
#endif
#else
                backend = new UnavailableStoreBackend();
#endif
            }
            var entitlementStore = new EntitlementStore(store, new PrefsDeviceKeyStore(store), clock, log);
            var purchases = new PurchaseService(backend, entitlementStore, validator, options.Monetization ?? MonetizationConfig.Release1,
                clock, analytics, log);
            tickables.Add(purchases);

            // ---- Ad policies (Core) ----
            var adPolicy = new AdPolicyStore(store, AdPolicyConfigMapper.From(remoteConfig.Current), log);
            System.Func<EntitlementSet> rights = () => purchases.Entitlements;
            var interstitials = new InterstitialCoordinator(ads, adPolicy, clock, analytics, rights, log);
            var rewarded = new RewardedExportCoordinator(ads, adPolicy, connectivity, clock, analytics, interstitials, rights, log);
            tickables.Add(rewarded);

            var startup = new ServicesStartup(crash, remoteConfig, purchases, consent, ads, analytics, adPolicy, interstitials, rewarded, clock, log);
            tickables.Insert(0, startup);
            startup.RunFrame0();

            log.Info(Tag, "Services created: " + plan + ".");
            return new BallverseServices(queue, clock, connectivity, crash, analytics, remoteConfig, consent, ads, purchases,
                interstitials, rewarded, startup, plan,
#if !BALLVERSE_STORE
                fakeAds, fakeStore,
#endif
                tickables);
        }

#if BALLVERSE_ADMOB
        static ITcfStorage CreateTcfStorage(IServiceLog log)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidTcfStorage(log);
#elif UNITY_IOS && !UNITY_EDITOR
            return new IosTcfStorage(log);
#else
            _ = log;
            return NoTcfStorage.Instance;
#endif
        }
#endif

        static ITrackingAuthorization CreateTracking(MainThreadQueue queue, bool fakeConsent)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!fakeConsent) return new IosTrackingAuthorization(queue);
#endif
            return new NoTrackingAuthorization();
        }
    }
}
