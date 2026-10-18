using System.Collections.Generic;
using Ballverse.Core.Config;
using Ballverse.Core.Monetization;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class StartupTests
    {
        ManualClock _clock;
        MemoryKeyValueStore _store;
        FakeConsentService _consent;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _store = new MemoryKeyValueStore();
            _consent = new FakeConsentService(new FakeTrackingAuthorization());
        }

        BallverseServices Create(FakeStoreConfig store = null, IReadOnlyDictionary<string, string> remote = null)
        {
            return ServicesInstaller.Create(new ServicesOptions
            {
                Mode = ServicesMode.Fake,
                Clock = _clock,
                Store = _store,
                Log = new MemoryServiceLog(),
                Connectivity = new FakeConnectivity(),
                DeveloperBuild = true,
                Consent = _consent,
                FakeStore = store,
                FakeRemoteConfig = remote,
            });
        }

        [Test]
        public void Frame0DoesNotTouchTheNetworkServices()
        {
            var s = Create();
            Assert.AreEqual(ServicesMode.Fake, s.Mode);
            Assert.IsFalse(s.Startup.Started);
            Assert.AreEqual(PurchaseServiceState.NotInitialized, s.Purchases.State);
            Assert.AreEqual(AdsServiceState.NotInitialized, s.Ads.State);
            Assert.AreEqual(0, _consent.GatherCalls);
        }

        [Test]
        public void SequenceFollowsSpec79()
        {
            _consent.ManualCompletion = true;
            var s = Create();
            s.Tick();
            Assert.IsTrue(s.Startup.Started);
            Assert.AreEqual(PurchaseServiceState.Ready, s.Purchases.State);
            Assert.IsTrue(s.RemoteConfig.FetchSettled);
            Assert.AreEqual(1, _consent.GatherCalls);

            // Consent pending: no ads even after 3 s.
            _clock.Advance(5000);
            s.Tick();
            Assert.AreEqual(AdsServiceState.NotInitialized, s.Ads.State);

            _consent.CompleteGather();
            s.Tick();
            Assert.AreEqual(AdsServiceState.Ready, s.Ads.State);
            Assert.IsTrue(s.Ads.IsReady(AdFormat.Interstitial), "interstitial preloaded after start");
        }

        [Test]
        public void AdsWaitThreeSecondsAfterTheFirstFrame()
        {
            var s = Create();
            s.Tick();
            Assert.IsTrue(_consent.CanRequestAds);
            Assert.AreEqual(AdsServiceState.NotInitialized, s.Ads.State);
            _clock.Advance(ServicesStartup.AdsStartDelayMs - 1);
            s.Tick();
            Assert.AreEqual(AdsServiceState.NotInitialized, s.Ads.State);
            _clock.Advance(1);
            s.Tick();
            Assert.AreEqual(AdsServiceState.Ready, s.Ads.State);
        }

        [Test]
        public void RefusedConsentKeepsAdsOffButExportsStillWork()
        {
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            _consent.ScriptedCanRequestAds = false;
            var s = Create();
            s.Tick();
            _clock.Advance(10_000);
            s.Tick();
            Assert.AreEqual(AdsServiceState.NotInitialized, s.Ads.State);

            s.RewardedExport.SetInStudio(true);
            Assert.IsTrue(s.RewardedExport.RequestExport("p").IsShowOffer);
            Ballverse.Core.Ads.RewardedGateStep? step = null;
            s.RewardedExport.WatchAd("p", st => step = st);
            _clock.Advance(5000);
            s.Tick();
            Assert.AreEqual(Ballverse.Core.Ads.ExportFunding.AdUnavailable, step.Value.Funding);
        }

        [Test]
        public void AnalyticsWaitForTheConsentDecision()
        {
            _consent.ManualCompletion = true;
            var s = Create();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            s.Analytics.LogEvent(AnalyticsEvents.StudioOpen);
            s.Tick();
            Assert.AreEqual(0, analytics.Sent.Count);
            _consent.CompleteGather();
            Assert.AreEqual(1, analytics.Sent.Count);
            Assert.AreEqual(true, analytics.AppliedConsent.Value.AnalyticsStorage);
            Assert.AreEqual(true, analytics.AppliedConsent.Value.AdStorage);
            Assert.AreEqual("false", analytics.SentUserProperties[AnalyticsUserProperties.HasNoExportAds]);
        }

        [Test]
        public void AnsweredFormLeavesAdSignalsToTcf()
        {
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            var s = Create();
            s.Tick();
            var consent = ((RecordingAnalyticsService)s.Analytics).AppliedConsent.Value;
            Assert.AreEqual(true, consent.AnalyticsStorage);
            Assert.IsNull(consent.AdStorage);
            Assert.IsNull(consent.AdPersonalization);
        }

        [Test]
        public void FailedConsentWithoutDecisionDeniesAnalyticsStorage()
        {
            _consent.ScriptedError = "update_timeout";
            _consent.ScriptedRequirement = ConsentRequirement.Unknown;
            var s = Create();
            s.Tick();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            Assert.AreEqual(false, analytics.AppliedConsent.Value.AnalyticsStorage);
            Assert.AreEqual(true, analytics.CollectionEnabled);
            Assert.IsTrue(s.Startup.ConsentSettled);
        }

        [Test]
        public void RemoteConfigDrivesTheAdPolicy()
        {
            var remote = new Dictionary<string, string>
            {
                { RemoteConfigKeys.InterstitialEveryNGames, "5" },
                { RemoteConfigKeys.RewardedExportEnabled, "false" },
            };
            var s = Create(remote: remote);
            s.Tick();
            Assert.AreEqual(RemoteConfigSource.Fetched, s.RemoteConfig.Source);
            Assert.AreEqual(5, s.Interstitials.Policy.Config.InterstitialEveryNGames);
            Assert.AreEqual(Ballverse.Core.Ads.ExportWithoutAdReason.GateDisabled, s.RewardedExport.RequestExport("p").Reason);
        }

        [Test]
        public void PurchasedRightsSwitchOffAdLoading()
        {
            var s = Create();
            s.Tick();
            _clock.Advance(3000);
            s.Tick();
            Assert.IsTrue(s.Ads.IsKeepLoaded(AdFormat.Interstitial));

            PurchaseOutcome? outcome = null;
            s.Purchases.Purchase(ProductIds.NoAds, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.Purchased, outcome.Value.Kind);
            Assert.IsFalse(s.Ads.IsKeepLoaded(AdFormat.Interstitial));
            Assert.IsTrue(s.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.AreEqual(Ballverse.Core.Ads.ExportWithoutAdReason.Entitlement, s.RewardedExport.RequestExport("p").Reason);
        }

        [Test]
        public void AttPrePromptRunsBeforeTheFirstAd()
        {
            var tracking = new FakeTrackingAuthorization { Answer = TrackingAuthorizationStatus.Authorized };
            _consent = new FakeConsentService(tracking);
            int prePrompts = 0;
            _consent.AttPrePrompt = new CallbackPrePrompt(next =>
            {
                prePrompts++;
                next(true);
            });
            var s = Create();
            s.Tick();
            _clock.Advance(3000);
            s.Tick();
            for (int i = 0; i < 3; i++) s.Interstitials.OnGameCompleted(Ballverse.Core.Ads.GameRunSummary.FinishedRegular(600));
            var d = s.Interstitials.EvaluateNaturalBreak(Ballverse.Core.Ads.NaturalBreakKind.Retry, false, false, false);
            Assert.IsTrue(d.IsShow);
            bool? shown = null;
            s.Interstitials.ShowInterstitial(x => shown = x);
            Assert.AreEqual(true, shown);
            Assert.AreEqual(1, prePrompts);
            Assert.AreEqual(1, tracking.RequestCount);
            Assert.AreEqual(TrackingAuthorizationStatus.Authorized, s.Consent.TrackingStatus);
        }

        [Test]
        public void RemoteConfigIssuesAreReportedNotThrown()
        {
            var remote = new FakeRemoteConfigService(new MemoryServiceLog());
            RemoteConfigValues seen = null;
            remote.Updated += v => seen = v;
            remote.Initialize();
            remote.SetRaw(new Dictionary<string, string> { { RemoteConfigKeys.InterstitialEveryNGames, "not a number" } });
            Assert.IsNotNull(seen);
            Assert.AreEqual(3, seen.InterstitialEveryNGames);
            Assert.Greater(remote.Issues.Count, 0);
        }

        sealed class CallbackPrePrompt : IAttPrePrompt
        {
            readonly System.Action<System.Action<bool>> _show;

            public CallbackPrePrompt(System.Action<System.Action<bool>> show)
            {
                _show = show;
            }

            public void Show(System.Action<bool> onContinue)
            {
                _show(onContinue);
            }
        }
    }
}
