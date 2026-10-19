using System;
using Ballverse.Core.Integration;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// Initialization order of spec §7.9, as a non-blocking state machine:
    /// <code>
    /// Frame 0  : Crashlytics → cached rights and ad policy state (already loaded by the constructors) → UI and simulation start
    /// Frame 1+ : Remote Config (activate cached, background fetch, 3 s) · IAP connect → purchases → rights → cache
    ///            UMP consent (form on top if required); analytics collection and Consent Mode follow the user's decision
    ///            (spec §9.1, <see cref="ConsentModeRules"/>), also when it changes later in the privacy options form;
    ///            without a decision (offline, form failed) consent is gathered again with backoff and on resume
    ///            Ads SDK: only once consent allows ads and not earlier than 3 s after the first frame; then preload
    ///            ATT: deferred to the first ad show (IAdsService.BeforeShow → IConsentService.EnsureTrackingDecision)
    /// </code>
    /// Nothing here waits on the main thread; <see cref="Tick"/> advances it every frame.
    /// </summary>
    public sealed class ServicesStartup : ITickable
    {
        /// <summary>Spec §7.9: the ads SDK starts no earlier than 3 s after the first frame.</summary>
        public const long AdsStartDelayMs = 3000;

        /// <summary>First retry of a consent gather that ended without a decision allowing ads; doubles up to <see cref="ConsentRetryMaxDelayMs"/>.</summary>
        public const long ConsentRetryFirstDelayMs = 30_000;

        public const long ConsentRetryMaxDelayMs = 300_000;

        /// <summary>
        /// Purchases are re-read this long after the app returns to the foreground or regains focus (spec §5.4: pending
        /// approvals, refunds), the last of those events counting. On Google Play, Unity IAP 5 reads purchases itself on every
        /// focus gain and its single fetch callback goes to the most recent caller; a read issued before that focus read
        /// would be answered into Unity IAP's own cache and never reach the app.
        /// </summary>
        public const long ResumePurchasesReadDelayMs = 2000;

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
        int _consentFailures;
        long _nextConsentRetryMs = -1;
        long _purchasesReadDueMs = -1;

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

            _adPolicy.ApplyConfig(RemoteConfigAdapters.ToAdPolicyConfig(_remoteConfig.Current));
            _remoteConfig.Updated += values =>
            {
                _adPolicy.ApplyConfig(RemoteConfigAdapters.ToAdPolicyConfig(values));
                RefreshAdPreload();
            };
            _purchases.EntitlementsChanged += entitlements =>
            {
                RefreshAdPreload();
                PublishEntitlementProperties(entitlements);
            };
            _ads.BeforeShow = _consent.EnsureTrackingDecision;
            _ads.BeforeShowAbandoned += _consent.AbandonTrackingDecision;
            _consent.ConsentChanged += ApplyConsentMode;
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
            if (_nextConsentRetryMs >= 0 && now >= _nextConsentRetryMs) RetryConsent("backoff");
            if (_purchasesReadDueMs >= 0 && now >= _purchasesReadDueMs)
            {
                _purchasesReadDueMs = -1;
                _purchases.RefreshPurchases();
            }
            if (!AdsStarted && _consent.CanRequestAds && now - _firstTickMs >= AdsStartDelayMs)
            {
                AdsStarted = true;
                _log.Info(Tag, "Starting the ads SDK.");
                _ads.Initialize();
                RefreshAdPreload();
            }
        }

        /// <summary>A purchases re-read is scheduled (<see cref="ResumePurchasesReadDelayMs"/>).</summary>
        public bool PurchasesReadScheduled => _purchasesReadDueMs >= 0;

        /// <summary>
        /// The app returned to the foreground: re-read purchases shortly (<see cref="ResumePurchasesReadDelayMs"/>) and retry a
        /// consent gather that has not produced a decision yet.
        /// </summary>
        public void OnApplicationResumed()
        {
            if (!_started) return;
            SchedulePurchasesRead();
            if (NeedsConsentRetry()) RetryConsent("resume");
        }

        /// <summary>The app gained (true) or lost focus: a gain re-schedules the purchases re-read after it.</summary>
        public void OnApplicationFocusChanged(bool hasFocus)
        {
            if (_started && hasFocus) SchedulePurchasesRead();
        }

        void SchedulePurchasesRead()
        {
            _purchasesReadDueMs = _clock.MonotonicMs + ResumePurchasesReadDelayMs;
        }

        void OnConsentGathered(ConsentGatherResult result)
        {
            ConsentSettled = true;
            if (result.Error != null) _crash.Log("consent: " + result.Error);
            ApplyConsentMode();
            if (NeedsConsentRetry())
            {
                long delay = ConsentRetryDelayMs(++_consentFailures);
                _nextConsentRetryMs = _clock.MonotonicMs + delay;
                _log.Info(Tag, "No consent decision allowing ads yet; gathering again in " + delay + " ms.");
            }
            else
            {
                _consentFailures = 0;
                _nextConsentRetryMs = -1;
            }
        }

        /// <summary>30 s, 60 s, 120 s, 240 s, then every 300 s.</summary>
        public static long ConsentRetryDelayMs(int failures)
        {
            long delay = ConsentRetryFirstDelayMs;
            for (int i = 1; i < failures && delay < ConsentRetryMaxDelayMs; i++) delay *= 2;
            return Math.Min(delay, ConsentRetryMaxDelayMs);
        }

        bool NeedsConsentRetry()
        {
            if (_consent.CanRequestAds || _consent.GatherState == ConsentGatherState.Gathering) return false;
            // Only while there is no decision: the update failed (offline, timeout) or the required form was not answered.
            return _consent.GatherState == ConsentGatherState.Failed
                   || _consent.Requirement == ConsentRequirement.Required
                   || _consent.Requirement == ConsentRequirement.Unknown;
        }

        void RetryConsent(string reason)
        {
            _nextConsentRetryMs = -1;
            if (!NeedsConsentRetry()) return;
            _log.Info(Tag, "Gathering consent again (" + reason + ").");
            _consent.Gather(OnConsentGathered);
        }

        /// <summary>
        /// Consent Mode and analytics collection from the current consent state (spec §9.1). Runs after every gather and
        /// whenever the decision changes (privacy options form). Without a decision nothing is enabled: events stay buffered.
        /// </summary>
        void ApplyConsentMode()
        {
            var decision = ConsentModeRules.Decide(_consent.Requirement, _consent.Tcf);
            if (!decision.HasDecision) return;
            _analytics.SetConsent(decision.Consent);
            if (_analytics.CollectionEnabled != true) _analytics.SetCollectionEnabled(true);
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
            _analytics.SetUserProperty(AnalyticsUserProperties.HasNoWatermark, entitlements.Contains(Entitlement.NoWatermark) ? "true" : "false");
            _analytics.SetUserProperty(AnalyticsUserProperties.OwnedModesCount,
                entitlements.ModeCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
