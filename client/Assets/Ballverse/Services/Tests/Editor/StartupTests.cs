using System.Collections.Generic;
using Ballverse.Core;
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
                InitialRemoteConfig = remote,
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
        public void LateAttPrePromptAnswerAfterTheWatchdogIsIgnoredAndTheScreenIsDismissed()
        {
            var tracking = new FakeTrackingAuthorization();
            _consent = new FakeConsentService(tracking, _clock);
            System.Action<bool> answer = null;
            var prePrompt = new CallbackPrePrompt(next => answer = next);
            _consent.AttPrePrompt = prePrompt;
            var s = Create();
            s.Tick();
            _clock.Advance(3000);
            s.Tick();
            s.RewardedExport.SetInStudio(true);
            Assert.IsTrue(s.RewardedExport.RequestExport("p").IsShowOffer);
            Ballverse.Core.Ads.RewardedGateStep? step = null;
            s.RewardedExport.WatchAd("p", st => step = st);
            s.Tick();
            Assert.IsNotNull(answer);

            _clock.Advance(AdsService.BeforeShowTimeoutMs + 1000);
            s.Tick();
            Assert.AreEqual(Ballverse.Core.Ads.ExportFunding.AdUnavailable, step.Value.Funding);
            Assert.AreEqual(1, prePrompt.Dismissals, "the UI is told to close the stale screen");

            answer(true); // the user taps "continue" on the stale screen during the export
            Assert.AreEqual(0, tracking.RequestCount, "no system ATT dialog during the export");
            Assert.AreEqual(TrackingAuthorizationStatus.NotDetermined, s.Consent.TrackingStatus);

            // Later ad shows are not held up again this session.
            s.RewardedExport.OnExportFinished("p", Ballverse.Core.Ads.ExportResult.Succeeded);
            Assert.IsTrue(s.RewardedExport.RequestExport("q").IsShowOffer);
            Ballverse.Core.Ads.RewardedGateStep? second = null;
            s.RewardedExport.WatchAd("q", st => second = st);
            s.Tick();
            Assert.IsTrue(second.HasValue);
            Assert.AreEqual(1, s.FakeAds.ShowCalls);
            Assert.AreEqual(0, tracking.RequestCount);
        }

        /// <summary>
        /// Unity IAP 5.4 on Google Play: one fetch callback, the last caller's; on every focus gain it reads purchases itself
        /// and that answer only fills its own cache (StoreController.OnPurchasesFetched is not raised).
        /// </summary>
        sealed class IapFocusModelBackend : IStoreBackend
        {
            IStoreBackendListener _listener;
            bool _appCallback;
            int _outstanding;

            public readonly List<StorePurchase> Orders = new List<StorePurchase>();
            public int AppReads;
            public int AppAnswers;
            public int ConfirmCalls;

            public bool IsAvailable => true;

            public IStoreBackendListener Listener { set => _listener = value; }

            public void Connect(IReadOnlyList<string> productIds)
            {
                _listener.OnConnected();
                var products = new List<StoreProduct>();
                foreach (string id in productIds) products.Add(new StoreProduct(id, "$1", id, id, "USD", 1m, true));
                _listener.OnProductsFetched(products);
            }

            public void FetchPurchases()
            {
                AppReads++;
                _appCallback = true;
                _outstanding++;
            }

            public void FocusGained()
            {
                _appCallback = false;
                _outstanding++;
            }

            public void AnswerAll()
            {
                int n = _outstanding;
                _outstanding = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!_appCallback) continue;
                    AppAnswers++;
                    _listener.OnPurchasesFetched(Orders.ToArray());
                }
            }

            public void Purchase(string productId)
            {
            }

            public void Confirm(StorePurchase purchase)
            {
                ConfirmCalls++;
                Orders.Remove(purchase);
                Orders.Add(new StorePurchase(purchase.ProductId, purchase.TransactionId, StorePurchaseState.Confirmed, purchase.Receipt, null));
            }

            public void Restore(System.Action<bool, string> done)
            {
                done?.Invoke(true, null);
            }
        }

        BallverseServices CreateWithBackend(IStoreBackend backend)
        {
            return ServicesInstaller.Create(new ServicesOptions
            {
                Mode = ServicesMode.Fake,
                Clock = _clock,
                Store = _store,
                Log = new MemoryServiceLog(),
                Connectivity = new FakeConnectivity(),
                Consent = _consent,
                StoreBackend = backend,
            });
        }

        [Test]
        public void ResumeReadComesAfterTheIapFocusReadSoAnApprovedPurchaseIsGranted()
        {
            var backend = new IapFocusModelBackend();
            var s = CreateWithBackend(backend);
            s.Tick();
            backend.AnswerAll();
            Assert.AreEqual(1, backend.AppAnswers);
            Assert.IsTrue(s.Entitlements.IsEmpty);

            for (int round = 0; round < 3; round++)
            {
                // A cash / parental-approval purchase completes while the app is in the background.
                if (round == 0) backend.Orders.Add(new StorePurchase(ProductIds.NoAds, "tx-1", StorePurchaseState.AwaitingConfirmation, "{}", null));
                s.OnApplicationResumed();                 // Android: onResume first …
                s.Tick();
                backend.FocusGained();                    // … then window focus: Unity IAP reads into its cache
                s.OnApplicationFocusChanged(true);
                s.Tick();
                backend.AnswerAll();
                Assert.IsTrue(s.Startup.PurchasesReadScheduled);

                _clock.Advance(ServicesStartup.ResumePurchasesReadDelayMs);
                s.Tick();
                backend.AnswerAll();
                Assert.IsFalse(s.Startup.PurchasesReadScheduled);
                Assert.IsTrue(s.Entitlements.Contains(Entitlement.NoExportAds), "round " + round);
            }
            Assert.AreEqual(4, backend.AppReads);
            Assert.AreEqual(4, backend.AppAnswers);
            Assert.AreEqual(1, backend.ConfirmCalls, "acknowledged once");
        }

        [Test]
        public void ResumeReadSwallowedByALateFocusReadIsRecoveredByTheTimeout()
        {
            var backend = new IapFocusModelBackend();
            var s = CreateWithBackend(backend);
            s.Tick();
            backend.AnswerAll();
            backend.Orders.Add(new StorePurchase(ProductIds.RemoveInterstitialAds, "tx-2", StorePurchaseState.AwaitingConfirmation, "{}", null));

            s.OnApplicationResumed();
            _clock.Advance(ServicesStartup.ResumePurchasesReadDelayMs);
            s.Tick();
            Assert.AreEqual(2, backend.AppReads);
            backend.FocusGained(); // focus arrives late (shade, dialog): the app read's answer goes to Unity IAP's cache
            s.OnApplicationFocusChanged(true);
            backend.AnswerAll();
            Assert.IsTrue(s.Entitlements.IsEmpty);

            _clock.Advance(PurchaseService.FetchTimeoutMs);
            s.Tick();
            backend.AnswerAll();
            Assert.IsTrue(s.Entitlements.Contains(Entitlement.NoInterstitials));
            Assert.AreEqual(1, backend.ConfirmCalls);
        }

        [Test]
        public void FakeStorePurchasesSurviveARestartOfTheServices()
        {
            // OwnerTest / Dev device builds without the store apps: the fake account lives in the app's key-value store.
            var first = ServicesInstaller.Create(new ServicesOptions
            {
                Mode = ServicesMode.Fake, Clock = _clock, Store = _store, Log = new MemoryServiceLog(),
                Connectivity = new FakeConnectivity(), Consent = _consent,
            });
            Assert.IsTrue(first.FakeStore.IsPersistent);
            first.Tick();
            first.Purchases.Purchase(ProductIds.NoAds, "shop", _ => { });
            Assert.IsTrue(first.Entitlements.Contains(Entitlement.NoWatermark));

            var keepOnlyTheAccount = new MemoryKeyValueStore();
            keepOnlyTheAccount.SetString(FakeStoreBackend.AccountKey, _store.GetString(FakeStoreBackend.AccountKey));
            var second = ServicesInstaller.Create(new ServicesOptions
            {
                Mode = ServicesMode.Fake, Clock = _clock, Store = keepOnlyTheAccount, Log = new MemoryServiceLog(),
                Connectivity = new FakeConnectivity(), Consent = new FakeConsentService(new FakeTrackingAuthorization()),
            });
            Assert.IsTrue(second.Entitlements.IsEmpty, "no entitlement cache in this store");
            second.Tick();
            Assert.IsTrue(second.Entitlements.Contains(Entitlement.NoWatermark), "re-delivered by the fake store account");

            // An explicit test setup starts from that setup, not from a persisted account.
            var scripted = Create(new FakeStoreConfig());
            Assert.IsFalse(scripted.FakeStore.IsPersistent);
        }

        [Test]
        public void RemoteConfigModesReachThePurchasesCatalog()
        {
            var s = Create(remote: new Dictionary<string, string> { { RemoteConfigKeys.ModesDisabled, "[\"M05\"]" } });
            Assert.IsTrue(MonetizationConfigMapper.SameCatalog(MonetizationConfig.Release1, s.Purchases.Config), "build defaults at frame 0");
            int changes = 0;
            s.Purchases.ConfigChanged += _ => changes++;
            s.Tick(); // Remote Config applies the delivered values
            Assert.AreEqual(1, changes);
            Assert.IsFalse(s.Purchases.Config.IsReleased(ModeCode.M05));
            Assert.IsTrue(s.Purchases.Config.IsFree(ModeCode.M05), "free set unchanged, availability is decided by release");
            CollectionAssert.AreEqual(MonetizationConfig.Release1.SellableProductIds, s.Purchases.Config.SellableProductIds);
        }

        [Test]
        public void RemoteConfigIssuesAreReportedNotThrown()
        {
            var remote = new BuildDefaultsRemoteConfigService(new MemoryServiceLog());
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

            public int Dismissals { get; private set; }

            public void Show(System.Action<bool> onContinue)
            {
                _show(onContinue);
            }

            public void Dismiss()
            {
                Dismissals++;
            }
        }
    }
}
