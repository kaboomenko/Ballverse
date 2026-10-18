using System;
using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// Everything the services layer gives the app (spec §7.3 <c>Ballverse.Services</c>), created by
    /// <see cref="ServicesInstaller.Create"/>. The app keeps one instance for the process and calls <see cref="Tick"/> every
    /// frame (done by <see cref="ServicesRunner"/>). Main thread only.
    /// </summary>
    public sealed class BallverseServices
    {
        readonly List<ITickable> _tickables;

        internal BallverseServices(
            MainThreadQueue queue,
            IClock clock,
            IConnectivity connectivity,
            ICrashReporter crash,
            IAnalyticsService analytics,
            IRemoteConfigService remoteConfig,
            IConsentService consent,
            IAdsService ads,
            IPurchaseService purchases,
            InterstitialCoordinator interstitials,
            RewardedExportCoordinator rewardedExport,
            ServicesStartup startup,
            ServicesPlan plan,
#if !BALLVERSE_STORE
            FakeAdNetwork fakeAds,
            FakeStoreBackend fakeStore,
#endif
            List<ITickable> tickables)
        {
            Queue = queue;
            Clock = clock;
            Connectivity = connectivity;
            Crash = crash;
            Analytics = analytics;
            RemoteConfig = remoteConfig;
            Consent = consent;
            Ads = ads;
            Purchases = purchases;
            Interstitials = interstitials;
            RewardedExport = rewardedExport;
            Startup = startup;
            Plan = plan;
#if !BALLVERSE_STORE
            FakeAds = fakeAds;
            FakeStore = fakeStore;
#endif
            _tickables = tickables ?? new List<ITickable>();
        }

        public MainThreadQueue Queue { get; }
        public IClock Clock { get; }
        public IConnectivity Connectivity { get; }
        public ICrashReporter Crash { get; }
        public IAnalyticsService Analytics { get; }
        public IRemoteConfigService RemoteConfig { get; }
        public IConsentService Consent { get; }
        public IAdsService Ads { get; }
        public IPurchaseService Purchases { get; }

        /// <summary>Interstitials after every N completed games (spec §5.3.2).</summary>
        public InterstitialCoordinator Interstitials { get; }

        /// <summary>Rewarded gate before export (spec §5.3.1).</summary>
        public RewardedExportCoordinator RewardedExport { get; }

        public ServicesStartup Startup { get; }

        /// <summary>Which implementations were wired, for which build variant.</summary>
        public ServicesPlan Plan { get; }

        /// <summary><see cref="ServicesMode.Fake"/> when every service is a fake, else <see cref="ServicesMode.Real"/>.</summary>
        public ServicesMode Mode => Plan.Mode;

#if !BALLVERSE_STORE
        /// <summary>The fake ad network when fakes are used (developer menu / tests), else null. Not in Store builds.</summary>
        public FakeAdNetwork FakeAds { get; }

        /// <summary>
        /// The fake store when it is used (developer menu "имитация покупок и возвратов", tests), else null. Its account is
        /// persisted across restarts (<see cref="FakeStoreBackend.IsPersistent"/>). Not in Store builds.
        /// </summary>
        public FakeStoreBackend FakeStore { get; }
#endif

        /// <summary>Current rights (release-1 rules).</summary>
        public EntitlementSet Entitlements => Purchases.Entitlements;

        /// <summary>Per-frame update: SDK callbacks, start-up sequence, timeouts, retries.</summary>
        public void Tick()
        {
            Queue.Drain();
            for (int i = 0; i < _tickables.Count; i++)
            {
                try
                {
                    _tickables[i].Tick();
                }
                catch (Exception e)
                {
                    Crash.RecordException(e, "services_tick");
                }
            }
        }

        /// <summary>
        /// The app returned to the foreground: re-read purchases shortly (pending approvals, refunds; spec §5.4,
        /// <see cref="ServicesStartup.ResumePurchasesReadDelayMs"/>) and retry a consent gather that has not produced a decision
        /// yet (offline first launch).
        /// </summary>
        public void OnApplicationResumed()
        {
            Startup.OnApplicationResumed();
        }

        /// <summary>Focus changed (Unity <c>OnApplicationFocus</c>): a gain moves the purchases re-read after it.</summary>
        public void OnApplicationFocusChanged(bool hasFocus)
        {
            Startup.OnApplicationFocusChanged(hasFocus);
        }
    }
}
