using System;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// Initialization order of spec §7.9, as a non-blocking state machine:
    /// <code>
    /// Frame 0  : Crashlytics → cached rights and ad policy state (already loaded by the constructors) → UI and simulation start
    /// Frame 1+ : Remote Config (activate cached, background fetch, 3 s) · IAP connect → purchases → rights → cache
    ///            UMP consent (form on top if required)
    ///            Ads SDK: only once consent allows ads and not earlier than 3 s after the first frame; then preload
    ///            ATT: deferred to the first ad show (IAdsService.BeforeShow → IConsentService.EnsureTrackingDecision)
    /// </code>
    /// Nothing here waits on the main thread; <see cref="Tick"/> advances it every frame.
    /// </summary>
    public sealed class ServicesStartup : ITickable
    {
        /// <summary>Spec §7.9: the ads SDK starts no earlier than 3 s after the first frame.</summary>
        public const long AdsStartDelayMs = 3000;

        const string Tag = "Startup";

        readonly ICrashReporter _crash;
        readonly IRemoteConfigService _remoteConfig;
        readonly IPurchaseService _purchases;
        readonly IConsentService _consent;
        readonly IAdsService _ads;
        readonly IAnalyticsService _analytics;
        readonly AdPolicyStore _adPolicy;
        readonly InterstitialCoordinator _interstitials;
        readonly RewardedExportCoordinator _rewarded;
        readonly IClock _clock;
        readonly IServiceLog _log;
        bool _frame0Done;
        bool _started;
        long _firstTickMs;

        public ServicesStartup(ICrashReporter crash, IRemoteConfigService remoteConfig, IPurchaseService purchases,
            IConsentService consent, IAdsService ads, IAnalyticsService analytics, AdPolicyStore adPolicy,
            InterstitialCoordinator interstitials, RewardedExportCoordinator rewarded, IClock clock, IServiceLog log)
        {
            _crash = crash ?? throw new ArgumentNullException(nameof(crash));
            _remoteConfig = remoteConfig ?? throw new ArgumentNullException(nameof(remoteConfig));
            _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _consent = consent ?? throw new ArgumentNullException(nameof(consent));
            _ads = ads ?? throw new ArgumentNullException(nameof(ads));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _adPolicy = adPolicy ?? throw new ArgumentNullException(nameof(adPolicy));
            _interstitials = interstitials ?? throw new ArgumentNullException(nameof(interstitials));
            _rewarded = rewarded ?? throw new ArgumentNullException(nameof(rewarded));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? NullServiceLog.Instance;
        }

        /// <summary>Frame-1 work has been started.</summary>
        public bool Started => _started;

        /// <summary>The ads SDK has been asked to start.</summary>
        public bool AdsStarted { get; private set; }

        /// <summary>The consent flow has finished (completed or failed).</summary>
        public bool ConsentSettled { get; private set; }

        /// <summary>Frame 0: synchronous and cheap. Called by the installer.</summary>
        public void RunFrame0()
        {
            if (_frame0Done) return;
            _frame0Done = true;
            _crash.Initialize();
            _crash.Log("services: frame 0");

            _adPolicy.ApplyConfig(AdPolicyConfigMapper.From(_remoteConfig.Current));
            _remoteConfig.Updated += values =>
            {
                _adPolicy.ApplyConfig(AdPolicyConfigMapper.From(values));
                RefreshAdPreload();
            };
            _purchases.EntitlementsChanged += entitlements =>
            {
                RefreshAdPreload();
                PublishEntitlementProperties(entitlements);
            };
            _ads.BeforeShow = _consent.EnsureTrackingDecision;
            PublishEntitlementProperties(_purchases.Entitlements);
        }

        public void Tick()
        {
            if (!_frame0Done) RunFrame0();
            long now = _clock.MonotonicMs;
            if (!_started)
            {
                _started = true;
                _firstTickMs = now;
                _remoteConfig.Initialize();
                _purchases.Initialize();
                _consent.Gather(OnConsentGathered);
            }
            if (!AdsStarted && _consent.CanRequestAds && now - _firstTickMs >= AdsStartDelayMs)
            {
                AdsStarted = true;
                _log.Info(Tag, "Starting the ads SDK.");
                _ads.Initialize();
                RefreshAdPreload();
            }
        }

        void OnConsentGathered(ConsentGatherResult result)
        {
            ConsentSettled = true;
            if (result.Error != null) _crash.Log("consent: " + result.Error);

            // Consent Mode (spec §9.1). Outside consent regions everything is granted. After a UMP answer analytics storage is
            // granted and the TCF string written by UMP governs the ad signals (not overridden). Without a decision (form not
            // answered, offline first launch) analytics storage is denied: Firebase then sends cookieless signals only.
            switch (result.Requirement)
            {
                case ConsentRequirement.NotRequired:
                    _analytics.SetConsent(AnalyticsConsent.AllGranted);
                    break;
                case ConsentRequirement.Obtained:
                    _analytics.SetConsent(new AnalyticsConsent(true, null, null, null));
                    break;
                default:
                    _analytics.SetConsent(new AnalyticsConsent(false, null, null, null));
                    break;
            }
            _analytics.SetCollectionEnabled(true);
        }

        void RefreshAdPreload()
        {
            _interstitials.UpdatePreload();
            _rewarded.UpdatePreload();
        }

        void PublishEntitlementProperties(EntitlementSet entitlements)
        {
            _analytics.SetUserProperty(AnalyticsUserProperties.HasNoExportAds, entitlements.Contains(Entitlement.NoExportAds) ? "true" : "false");
            _analytics.SetUserProperty(AnalyticsUserProperties.HasNoInterstitials, entitlements.Contains(Entitlement.NoInterstitials) ? "true" : "false");
            _analytics.SetUserProperty(AnalyticsUserProperties.OwnedModesCount,
                entitlements.ModeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
