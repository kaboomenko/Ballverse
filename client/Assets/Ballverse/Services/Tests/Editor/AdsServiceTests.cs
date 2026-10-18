using System.Collections.Generic;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class AdsServiceTests
    {
        ManualClock _clock;
        FakeAdNetwork _network;
        AdsService _ads;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _network = new FakeAdNetwork(_clock);
            _ads = new AdsService(_network, _clock, new MemoryServiceLog());
        }

        [Test]
        public void NothingLoadsBeforeInitializeAndWishesAreKept()
        {
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            Assert.AreEqual(0, _network.LoadCalls);
            _ads.Initialize();
            Assert.AreEqual(AdsServiceState.Ready, _ads.State);
            Assert.IsTrue(_ads.IsReady(AdFormat.Interstitial));
            Assert.IsFalse(_ads.IsReady(AdFormat.Rewarded));
        }

        [Test]
        public void UnavailableSdkNeverLoads()
        {
            var ads = new AdsService(new UnavailableAdNetwork(), _clock, null);
            ads.Initialize();
            ads.SetKeepLoaded(AdFormat.Rewarded, true);
            Assert.AreEqual(AdsServiceState.Unavailable, ads.State);
            RewardedShowResult? result = null;
            ads.ShowRewarded(r => result = r);
            Assert.AreEqual(RewardedShowResult.NotReady, result);
        }

        [Test]
        public void FailedLoadsRetryWithBackoff()
        {
            _network.Fill = false;
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            Assert.AreEqual(1, _network.LoadCalls);
            Assert.AreEqual(AdLoadState.Failed, _ads.GetLoadState(AdFormat.Rewarded));
            Assert.AreEqual(3, _ads.LastLoadError(AdFormat.Rewarded).Value.Code);

            _clock.Advance(1999);
            _ads.Tick();
            Assert.AreEqual(1, _network.LoadCalls);
            _clock.Advance(1);
            _ads.Tick();
            Assert.AreEqual(2, _network.LoadCalls);
            _clock.Advance(3999);
            _ads.Tick();
            Assert.AreEqual(2, _network.LoadCalls);

            _network.Fill = true;
            _clock.Advance(1);
            _ads.Tick();
            Assert.AreEqual(3, _network.LoadCalls);
            Assert.IsTrue(_ads.IsReady(AdFormat.Rewarded));

            Assert.AreEqual(2000, AdsService.RetryDelayMs(1));
            Assert.AreEqual(4000, AdsService.RetryDelayMs(2));
            Assert.AreEqual(60000, AdsService.RetryDelayMs(10));
        }

        [Test]
        public void RewardedResultArrivesAfterCloseAndNextAdLoads()
        {
            var events = new List<bool>();
            _ads.FullScreenChanged += showing => events.Add(showing);
            var paid = new List<AdRevenue>();
            _ads.Paid += r => paid.Add(r);
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);

            _network.ManualClose = true;
            RewardedShowResult? result = null;
            _ads.ShowRewarded(r => result = r);
            Assert.IsNull(result, "no result while the ad is on screen (RW6)");
            Assert.IsTrue(_ads.IsShowing);
            CollectionAssert.AreEqual(new[] { true }, events);

            _network.CloseShownAd();
            Assert.AreEqual(RewardedShowResult.Earned, result);
            CollectionAssert.AreEqual(new[] { true, false }, events);
            Assert.AreEqual(1, paid.Count);
            Assert.IsTrue(_ads.IsReady(AdFormat.Rewarded), "reloaded after close");
        }

        [Test]
        public void ClosingEarlyGivesNoReward()
        {
            _network.GrantReward = false;
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            RewardedShowResult? result = null;
            _ads.ShowRewarded(r => result = r);
            Assert.IsNull(result, "waits for a late reward");
            _clock.Advance(AdsService.RewardGraceMs);
            _ads.Tick();
            Assert.AreEqual(RewardedShowResult.ClosedEarly, result);
        }

        [Test]
        public void RewardDeliveredJustAfterCloseStillCounts()
        {
            _network.RewardAfterClose = true;
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            RewardedShowResult? result = null;
            _ads.ShowRewarded(r => result = r);
            Assert.AreEqual(RewardedShowResult.Earned, result);
        }

        [Test]
        public void ShowFailureIsReported()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            _network.FailToShow = true;
            InterstitialShowResult? result = null;
            _ads.ShowInterstitial(r => result = r);
            Assert.AreEqual(InterstitialShowResult.FailedToShow, result);
            Assert.IsFalse(_ads.IsShowing);
        }

        [Test]
        public void BeforeShowHookRunsFirst()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            System.Action pending = null;
            _ads.BeforeShow = next => pending = next;
            InterstitialShowResult? result = null;
            _ads.ShowInterstitial(r => result = r);
            Assert.AreEqual(0, _network.ShowCalls);
            pending();
            Assert.AreEqual(1, _network.ShowCalls);
            Assert.AreEqual(InterstitialShowResult.Shown, result);
        }

        [Test]
        public void BeforeShowHookThatThrowsStillShowsTheAd()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            _ads.BeforeShow = next => throw new System.InvalidOperationException("pre-prompt broke");
            InterstitialShowResult? result = null;
            _ads.ShowInterstitial(r => result = r);
            Assert.AreEqual(InterstitialShowResult.Shown, result);
            Assert.AreEqual(0, _ads.PendingBeforeShowCount);
        }

        [Test]
        public void BeforeShowHookThatNeverContinuesIsAbandoned()
        {
            // Р14 / IN4: an ATT pre-prompt that never answers must not hold the export or the game up.
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            var continuations = new List<System.Action>();
            _ads.BeforeShow = next => continuations.Add(next);
            RewardedShowResult? rewarded = null;
            InterstitialShowResult? interstitial = null;
            _ads.ShowRewarded(r => rewarded = r);
            _ads.ShowInterstitial(r => interstitial = r);
            Assert.AreEqual(2, _ads.PendingBeforeShowCount);

            _clock.Advance(AdsService.BeforeShowTimeoutMs - 1);
            _ads.Tick();
            Assert.IsNull(rewarded);
            Assert.IsNull(interstitial);

            _clock.Advance(1);
            _ads.Tick();
            Assert.AreEqual(RewardedShowResult.NotReady, rewarded);
            Assert.AreEqual(InterstitialShowResult.NotReady, interstitial);
            Assert.AreEqual(0, _ads.PendingBeforeShowCount);

            // A late answer is ignored: no surprise ad a minute later.
            foreach (var c in continuations) c();
            Assert.AreEqual(0, _network.ShowCalls);
            Assert.IsFalse(_ads.IsShowing);
            Assert.IsTrue(_ads.IsReady(AdFormat.Rewarded), "the unused ad stays loaded for the next export");
        }

        [Test]
        public void StaleAdsAreReplaced()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            Assert.AreEqual(1, _network.LoadCalls);
            _clock.Advance(AdsService.MaxAdAgeMs);
            Assert.IsFalse(_ads.IsReady(AdFormat.Interstitial));
            _ads.Tick();
            Assert.AreEqual(2, _network.LoadCalls);
            Assert.IsTrue(_ads.IsReady(AdFormat.Interstitial));
        }

        [Test]
        public void ReleasingAFormatDiscardsItsAd()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            _ads.SetKeepLoaded(AdFormat.Interstitial, false);
            Assert.IsFalse(_ads.IsReady(AdFormat.Interstitial));
            Assert.AreEqual(1, _network.DiscardCalls);
            _clock.Advance(100_000);
            _ads.Tick();
            Assert.AreEqual(1, _network.LoadCalls);
        }

        [Test]
        public void NoReloadOfAFormatWhileItIsOnScreen()
        {
            _ads.Initialize();
            _ads.SetKeepLoaded(AdFormat.Interstitial, true);
            _network.ManualClose = true;
            InterstitialShowResult? result = null;
            _ads.ShowInterstitial(r => result = r);
            _clock.Advance(10_000);
            _ads.Tick();
            Assert.AreEqual(1, _network.LoadCalls);
            Assert.IsFalse(_ads.IsReady(AdFormat.Interstitial));
            _network.CloseShownAd();
            Assert.AreEqual(InterstitialShowResult.Shown, result);
            Assert.AreEqual(2, _network.LoadCalls);
            Assert.IsTrue(_ads.IsReady(AdFormat.Interstitial));
        }

        [Test]
        public void SlowLoadCompletesOnTick()
        {
            _network.LoadDelayMs = 1500;
            _ads.Initialize();
            var finished = new List<bool>();
            _ads.LoadFinished += (f, ok) => finished.Add(ok);
            _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            Assert.AreEqual(AdLoadState.Loading, _ads.GetLoadState(AdFormat.Rewarded));
            _clock.Advance(1500);
            _network.Tick();
            Assert.IsTrue(_ads.IsReady(AdFormat.Rewarded));
            CollectionAssert.AreEqual(new[] { true }, finished);
        }
    }
}
