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
            Assert.AreEqual("false", analytics.SentUserProperties[AnalyticsUserProperties.HasNoWatermark]);
        }

        [Test]
        public void AcceptedFormGrantsAnalyticsStorageAndLeavesAdSignalsToTcf()
        {
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            _consent.ScriptedTcf = new TcfSignals(true, "11111111111");
            var s = Create();
            s.Tick();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            var consent = analytics.AppliedConsent.Value;
            Assert.AreEqual(true, consent.AnalyticsStorage);
            Assert.IsNull(consent.AdStorage);
            Assert.IsNull(consent.AdPersonalization);
            Assert.AreEqual(true, analytics.CollectionEnabled);
        }

        [Test]
        public void RefusedFormDeniesAnalyticsStorage()
        {
            // UMP reports OBTAINED for "Do not consent" too; the TCF purposes carry the refusal.
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            _consent.ScriptedTcf = new TcfSignals(true, "00000000000");
            var s = Create();
            s.Tick();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            Assert.AreEqual(false, analytics.AppliedConsent.Value.AnalyticsStorage);
            Assert.AreEqual(true, analytics.CollectionEnabled, "the user decided: cookieless measurement only");
        }

        [Test]
        public void PrivacyOptionsChangesReachConsentMode()
        {
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            _consent.ScriptedTcf = new TcfSignals(true, "11111111111");
            var s = Create();
            s.Tick();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            Assert.AreEqual(true, analytics.AppliedConsent.Value.AnalyticsStorage);

            _consent.ScriptedTcf = new TcfSignals(true, "00000000000");
            s.Consent.ShowPrivacyOptions(_ => { });
            Assert.AreEqual(false, analytics.AppliedConsent.Value.AnalyticsStorage);

            _consent.ScriptedTcf = new TcfSignals(true, "11111111111");
            s.Consent.ShowPrivacyOptions(_ => { });
            Assert.AreEqual(true, analytics.AppliedConsent.Value.AnalyticsStorage);
        }

        [Test]
        public void NoAnalyticsAreSentWithoutADecision()
        {
            // Spec §9.1: collection starts after the user's UMP decision where one is required.
            _consent.ScriptedRequirement = ConsentRequirement.Required;
            _consent.ScriptedCanRequestAds = false;
            var s = Create();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            s.Analytics.LogEvent(AnalyticsEvents.StudioOpen);
            s.Tick();
            Assert.IsTrue(s.Startup.ConsentSettled);
            Assert.IsNull(analytics.CollectionEnabled);
            Assert.AreEqual(0, analytics.Sent.Count);
            s.Analytics.LogEvent(AnalyticsEvents.StudioOpen);
            Assert.AreEqual(0, analytics.Sent.Count);

            // The form is answered on a later attempt: the buffered events go out with the decision.
            _consent.ScriptedRequirement = ConsentRequirement.Obtained;
            _consent.ScriptedCanRequestAds = true;
            _consent.ScriptedTcf = new TcfSignals(true, "11111111111");
            _clock.Advance(ServicesStartup.ConsentRetryFirstDelayMs);
            s.Tick();
            Assert.AreEqual(2, _consent.GatherCalls);
            Assert.AreEqual(true, analytics.CollectionEnabled);
            Assert.AreEqual(2, analytics.Sent.Count);
        }

        [Test]
        public void FailedConsentWithoutDecisionKeepsAnalyticsBuffered()
        {
            _consent.ScriptedError = "update_timeout";
            _consent.ScriptedRequirement = ConsentRequirement.Unknown;
            var s = Create();
            s.Tick();
            var analytics = (RecordingAnalyticsService)s.Analytics;
            Assert.IsNull(analytics.AppliedConsent);
            Assert.IsNull(analytics.CollectionEnabled);
            Assert.IsTrue(s.Startup.ConsentSettled);
        }

        [Test]
        public void FailedConsentIsGatheredAgainWithBackoffAndOnResume()
        {
            _consent.ScriptedError = "update_timeout";
            _consent.ScriptedRequirement = ConsentRequirement.Unknown;
            _consent.ScriptedCanRequestAds = false;
            var s = Create();
            s.Tick();
            Assert.AreEqual(1, _consent.GatherCalls);
            Assert.AreEqual(ConsentGatherState.Failed, _consent.GatherState);

            _clock.Advance(ServicesStartup.ConsentRetryFirstDelayMs - 1);
            s.Tick();
            Assert.AreEqual(1, _consent.GatherCalls);
            _clock.Advance(1);
            s.Tick();
            Assert.AreEqual(2, _consent.GatherCalls, "first retry after 30 s");

            // Still offline: the next retry waits twice as long, but returning to the app retries at once.
            s.Tick();
            Assert.AreEqual(2, _consent.GatherCalls);
            s.OnApplicationResumed();
            Assert.AreEqual(3, _consent.GatherCalls);

            // The network is back: a decision arrives, ads start, retries stop.
            _consent.ScriptedError = null;
            _consent.ScriptedRequirement = ConsentRequirement.NotRequired;
            _consent.ScriptedCanRequestAds = true;
            _clock.Advance(ServicesStartup.ConsentRetryDelayMs(3));
            s.Tick();
            Assert.AreEqual(4, _consent.GatherCalls);
            s.Tick();
            Assert.AreEqual(AdsServiceState.Ready, s.Ads.State);
            _clock.Advance(ServicesStartup.ConsentRetryMaxDelayMs);
            s.Tick();
            s.OnApplicationResumed();
            Assert.AreEqual(4, _consent.GatherCalls);
            Assert.AreEqual(60_000, ServicesStartup.ConsentRetryDelayMs(2));
            Assert.AreEqual(ServicesStartup.ConsentRetryMaxDelayMs, ServicesStartup.ConsentRetryDelayMs(20));
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
            var analytics = (RecordingAnalyticsService)s.Analytics;
            Assert.AreEqual("true", analytics.SentUserProperties[AnalyticsUserProperties.HasNoWatermark]);
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
        public void ThrowingAttPrePromptNeverBlocksLaterExportsOrInterstitials()
        {
            var tracking = new FakeTrackingAuthorization();
            _consent = new FakeConsentService(tracking, _clock);
            int prePrompts = 0;
            _consent.AttPrePrompt = new CallbackPrePrompt(next =>
            {
                prePrompts++;
                throw new System.InvalidOperationException("screen failed");
            });
            var s = Create();
            s.Tick();
            _clock.Advance(3000);
            s.Tick();
            s.RewardedExport.SetInStudio(true);

            for (int i = 0; i < 2; i++)
            {
                string project = "p" + i;
                Assert.IsTrue(s.RewardedExport.RequestExport(project).IsShowOffer);
                Ballverse.Core.Ads.RewardedGateStep? step = null;
                s.RewardedExport.WatchAd(project, st => step = st);
                s.Tick();
                Assert.IsTrue(step.HasValue, "export " + i + " continues");
                Assert.IsFalse(s.RewardedExport.IsBusy);
                s.RewardedExport.OnExportFinished(project, Ballverse.Core.Ads.ExportResult.Succeeded);
            }
            Assert.AreEqual(1, prePrompts);

            for (int round = 0; round < 2; round++)
            {
                for (int i = 0; i < 3; i++) s.Interstitials.OnGameCompleted(Ballverse.Core.Ads.GameRunSummary.FinishedRegular(600));
                _clock.Advance(200_000);
                s.Tick();
                var d = s.Interstitials.EvaluateNaturalBreak(Ballverse.Core.Ads.NaturalBreakKind.Retry, false, false, false);
                Assert.IsTrue(d.IsShow, "round " + round);
                bool? shown = null;
                s.Interstitials.ShowInterstitial(x => shown = x);
                Assert.IsTrue(shown.HasValue, "interstitial " + round + " calls back");
            }
        }

        [Test]
        public void UnansweredAttPrePromptLetsTheExportRunWithoutTheAd()
        {
            var tracking = new FakeTrackingAuthorization();
            _consent = new FakeConsentService(tracking, _clock);
            _consent.AttPrePrompt = new CallbackPrePrompt(next => { }); // closed by navigation, never answers
            var s = Create();
            s.Tick();
            _clock.Advance(3000);
            s.Tick();
            s.RewardedExport.SetInStudio(true);
            Assert.IsTrue(s.RewardedExport.RequestExport("p").IsShowOffer);
            Ballverse.Core.Ads.RewardedGateStep? step = null;
            s.RewardedExport.WatchAd("p", st => step = st);
            s.Tick();
            Assert.IsNull(step);
            Assert.IsTrue(s.RewardedExport.IsBusy);

            _clock.Advance(AdsService.BeforeShowTimeoutMs);
            s.Tick();
            Assert.IsTrue(step.HasValue);
            Assert.AreEqual(Ballverse.Core.Ads.ExportFunding.AdUnavailable, step.Value.Funding);
            Assert.IsFalse(s.RewardedExport.IsBusy);
            Assert.AreEqual(0, s.FakeAds.ShowCalls);
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
