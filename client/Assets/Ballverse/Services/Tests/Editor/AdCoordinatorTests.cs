using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Monetization;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class AdCoordinatorTests
    {
        const string Project = "project-1";

        ManualClock _clock;
        MemoryKeyValueStore _store;
        FakeAdNetwork _network;
        AdsService _ads;
        FakeConnectivity _connectivity;
        RecordingAnalyticsService _analytics;
        EntitlementSet _rights;
        AdPolicyStore _policy;
        InterstitialCoordinator _interstitials;
        RewardedExportCoordinator _rewarded;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _store = new MemoryKeyValueStore();
            _network = new FakeAdNetwork(_clock);
            _ads = new AdsService(_network, _clock, null);
            _connectivity = new FakeConnectivity();
            _analytics = new RecordingAnalyticsService();
            _analytics.SetCollectionEnabled(true);
            _rights = EntitlementSet.Empty;
            Build();
        }

        void Build()
        {
            _policy = new AdPolicyStore(_store, AdPolicyConfig.CreateDefault(), null);
            _interstitials = new InterstitialCoordinator(_ads, _policy, _clock, _analytics, () => _rights, null);
            _rewarded = new RewardedExportCoordinator(_ads, _policy, _connectivity, _clock, _analytics, _interstitials, () => _rights, null);
        }

        void Tick()
        {
            _network.Tick();
            _ads.Tick();
            _rewarded.Tick();
        }

        static GameRunSummary Finished => GameRunSummary.FinishedRegular(10 * SimConstants.TickRate);

        // ---- rewarded gate ----

        [Test]
        public void EntitlementSkipsTheOffer()
        {
            _rights = EntitlementSet.Of(Entitlement.NoExportAds);
            var d = _rewarded.RequestExport(Project);
            Assert.AreEqual(ExportGateDecisionKind.ExportWithoutAd, d.Kind);
            Assert.AreEqual(ExportWithoutAdReason.Entitlement, d.Reason);
        }

        [Test]
        public void StudioPreloadsTheRewardedAd()
        {
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            Assert.IsTrue(_ads.IsReady(AdFormat.Rewarded));
            _rewarded.SetInStudio(false);
            Assert.IsFalse(_ads.IsKeepLoaded(AdFormat.Rewarded));
        }

        [Test]
        public void WatchingTheAdStartsTheExportAfterClose()
        {
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            Assert.IsTrue(_rewarded.RequestExport(Project).IsShowOffer);

            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            Assert.AreEqual(RewardedGateStepKind.StartExport, step.Value.Kind);
            Assert.AreEqual(ExportFunding.RewardEarned, step.Value.Funding);
            Assert.AreEqual(_clock.UnixTimeMs, _interstitials.Policy.LastRewardedShownMs, "IN3 quiet period starts");
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.AdRewardedOffer).Count);
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.AdRewardedShow).Count);
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.AdRewardedReward).Count);

            // RW4: the export failed → free retry within the window, persisted across restarts.
            var finish = _rewarded.OnExportFinished(Project, ExportResult.Failed);
            Assert.AreEqual(ExportFinishKind.RetryCreditGranted, finish.Kind);
            Build();
            var retry = _rewarded.RequestExport(Project);
            Assert.AreEqual(ExportWithoutAdReason.RetryCredit, retry.Reason);
        }

        [Test]
        public void OfflineExportsWithoutAdImmediately()
        {
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            _connectivity.IsOffline = true;
            _rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            Assert.AreEqual(RewardedGateStepKind.StartExport, step.Value.Kind);
            Assert.AreEqual(ExportFunding.AdUnavailable, step.Value.Funding);
            var fail = _analytics.EventsNamed(AnalyticsEvents.AdRewardedFail);
            Assert.AreEqual(1, fail.Count);
            Assert.IsTrue(fail[0].TryGet(AnalyticsParams.ErrorCode, out var code));
            Assert.AreEqual("offline", code.StringValue);
        }

        [Test]
        public void NoFillExportsWithoutAdWithoutWaitingTheFullTimeout()
        {
            _network.Fill = false;
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            _rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            Assert.AreEqual(ExportFunding.AdUnavailable, step.Value.Funding);
        }

        [Test]
        public void SlowAdTimesOutAfterFiveSeconds()
        {
            _network.LoadDelayMs = 60_000;
            _ads.Initialize();
            _rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            Assert.IsTrue(_rewarded.IsWaitingForAd);
            _clock.Advance(4999);
            Tick();
            Assert.IsNull(step);
            _clock.Advance(1);
            Tick();
            Assert.AreEqual(RewardedGateStepKind.StartExport, step.Value.Kind);
            Assert.AreEqual(ExportFunding.AdUnavailable, step.Value.Funding);
            Assert.IsFalse(_rewarded.IsWaitingForAd);
        }

        [Test]
        public void AdArrivingDuringTheWaitIsShown()
        {
            _network.LoadDelayMs = 2000;
            _ads.Initialize();
            _rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            _clock.Advance(2000);
            Tick();
            Assert.AreEqual(ExportFunding.RewardEarned, step.Value.Funding);
        }

        [Test]
        public void AdsSdkUnavailableNeverBlocksTheExport()
        {
            var ads = new AdsService(new UnavailableAdNetwork(), _clock, null);
            ads.Initialize();
            var rewarded = new RewardedExportCoordinator(ads, _policy, _connectivity, _clock, _analytics, _interstitials, () => _rights, null);
            rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            rewarded.WatchAd(Project, s => step = s);
            Assert.AreEqual(ExportFunding.AdUnavailable, step.Value.Funding);
        }

        [Test]
        public void ClosingEarlyShowsTheOfferAgain()
        {
            _network.GrantReward = false;
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            _rewarded.RequestExport(Project);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s);
            _clock.Advance(AdsService.RewardGraceMs);
            Tick();
            Assert.AreEqual(RewardedGateStepKind.ShowOfferAgain, step.Value.Kind);
            Assert.AreEqual(Project, _rewarded.Gate.OpenOfferProjectId);
        }

        [Test]
        public void DeclineAndPurchaseChoice()
        {
            _rewarded.RequestExport(Project);
            Assert.AreEqual(RewardedGateStepKind.StartPurchase, _rewarded.Decline(Project, purchaseChosen: true).Kind);
            _rewarded.RequestExport(Project);
            Assert.AreEqual(RewardedGateStepKind.Abort, _rewarded.Decline(Project, purchaseChosen: false).Kind);
            Assert.AreEqual(2, _analytics.EventsNamed(AnalyticsEvents.AdRewardedDecline).Count);
        }

        [Test]
        public void OutcomeWithoutAnOpenOfferStillExports()
        {
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            RewardedGateStep? step = null;
            _rewarded.WatchAd(Project, s => step = s); // RequestExport was never called
            Assert.AreEqual(RewardedGateStepKind.StartExport, step.Value.Kind);
        }

        [Test]
        public void KillSwitchFromRemoteConfig()
        {
            var config = AdPolicyConfig.CreateDefault();
            config.RewardedExportEnabled = false;
            _policy.ApplyConfig(config);
            var d = _rewarded.RequestExport(Project);
            Assert.AreEqual(ExportWithoutAdReason.GateDisabled, d.Reason);
        }

        // ---- interstitials ----

        [Test]
        public void InterstitialAfterThreeCompletedGamesAtTheNextBreak()
        {
            _ads.Initialize();
            _interstitials.UpdatePreload();
            Assert.IsTrue(_ads.IsReady(AdFormat.Interstitial), "preloaded a full cycle ahead");

            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            Assert.AreEqual(InterstitialDecisionKind.None, _interstitials.EvaluateNaturalBreak(NaturalBreakKind.Retry, false, false, false).Kind);
            _interstitials.OnGameCompleted(Finished);

            var d = _interstitials.EvaluateNaturalBreak(NaturalBreakKind.Generate, false, false, false);
            Assert.AreEqual(InterstitialDecisionKind.Show, d.Kind);
            bool? shown = null;
            _interstitials.ShowInterstitial(s => shown = s);
            Assert.AreEqual(true, shown);
            Assert.AreEqual(0, _interstitials.Policy.CompletedGames);
            Assert.AreEqual(_clock.UnixTimeMs, _interstitials.Policy.LastInterstitialShownMs);
            var show = _analytics.EventsNamed(AnalyticsEvents.AdInterstitialShow);
            Assert.AreEqual(1, show.Count);
            Assert.IsTrue(show[0].TryGet(AnalyticsParams.Trigger, out var trigger));
            Assert.AreEqual("generate", trigger.StringValue);

            // Persisted (IN1 "хранится между сессиями").
            Build();
            Assert.AreEqual(_clock.UnixTimeMs, _interstitials.Policy.LastInterstitialShownMs);
        }

        [Test]
        public void ShortRunsDoNotCount()
        {
            Assert.IsFalse(_interstitials.OnGameCompleted(GameRunSummary.FinishedRegular(4 * SimConstants.TickRate)));
            Assert.IsFalse(_interstitials.OnGameCompleted(new GameRunSummary(GameRunKind.StudioReplayPlayback, GameRunEnd.Finished, 600)));
            Assert.AreEqual(0, _interstitials.Policy.CompletedGames);
        }

        [Test]
        public void StudioKeepsItPendingAndMissingAdResetsTheCounter()
        {
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);

            var inStudio = _interstitials.EvaluateNaturalBreak(NaturalBreakKind.BackFromResult, true, false, false);
            Assert.AreEqual(InterstitialSuppressReason.InStudio, inStudio.Reason);
            Assert.IsTrue(_interstitials.Policy.IsPending);

            // Ads SDK not started: no ad ready → the user is not delayed and the cycle restarts (IN4).
            var notReady = _interstitials.EvaluateNaturalBreak(NaturalBreakKind.ToGallery, false, false, false);
            Assert.AreEqual(InterstitialSuppressReason.AdNotReady, notReady.Reason);
            Assert.AreEqual(0, _interstitials.Policy.CompletedGames);
            Assert.AreEqual(2, _analytics.EventsNamed(AnalyticsEvents.AdInterstitialSkip).Count);
        }

        [Test]
        public void NoInterstitialsEntitlementStopsLoading()
        {
            _ads.Initialize();
            _interstitials.UpdatePreload();
            Assert.IsTrue(_ads.IsKeepLoaded(AdFormat.Interstitial));
            _rights = EntitlementSet.Of(Entitlement.NoInterstitials);
            _interstitials.UpdatePreload();
            Assert.IsFalse(_ads.IsKeepLoaded(AdFormat.Interstitial));
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            var d = _interstitials.EvaluateNaturalBreak(NaturalBreakKind.Retry, false, false, false);
            Assert.AreEqual(InterstitialSuppressReason.Entitlement, d.Reason);
        }

        [Test]
        public void QuietPeriodAfterRewarded()
        {
            _ads.Initialize();
            _rewarded.SetInStudio(true);
            _rewarded.RequestExport(Project);
            _rewarded.WatchAd(Project, _ => { });
            _interstitials.UpdatePreload();
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            _interstitials.OnGameCompleted(Finished);
            _clock.Advance(29_999);
            Assert.AreEqual(InterstitialSuppressReason.RewardedQuietPeriod,
                _interstitials.EvaluateNaturalBreak(NaturalBreakKind.Retry, false, false, false).Reason);
            _clock.Advance(1);
            Assert.AreEqual(InterstitialDecisionKind.Show, _interstitials.EvaluateNaturalBreak(NaturalBreakKind.Retry, false, false, false).Kind);
        }

        [Test]
        public void ShowWithoutApprovalIsRefused()
        {
            bool? shown = null;
            _interstitials.ShowInterstitial(s => shown = s);
            Assert.AreEqual(false, shown);
        }

        [Test]
        public void CorruptStateStartsFresh()
        {
            _store.SetString(AdPolicyStore.InterstitialKey, "{not json");
            _store.SetString(AdPolicyStore.RewardedGateKey, "[1,2]");
            var log = new MemoryServiceLog();
            var policy = new AdPolicyStore(_store, AdPolicyConfig.CreateDefault(), log);
            Assert.AreEqual(0, policy.Interstitial.CompletedGames);
            Assert.AreEqual(0, policy.Gate.CreditCount);
            Assert.AreEqual(2, log.WarningCount);
        }
    }
}
