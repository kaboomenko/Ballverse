#if BALLVERSE_ADMOB
using System;
using System.Collections.Generic;
using GoogleMobileAds.Api;

namespace Ballverse.Services
{
    /// <summary>
    /// <see cref="IAdNetwork"/> over the Google Mobile Ads Unity plugin (GoogleMobileAds.Api, 9.x–11.x): <c>MobileAds</c>,
    /// <c>RewardedAd</c>, <c>InterstitialAd</c>, <c>AdRequest</c>. Every plugin callback is posted to the
    /// <see cref="MainThreadQueue"/> (the plugin may call back on a platform thread; the obsolete
    /// <c>RaiseAdEventsOnUnityMainThread</c> switch is not used).
    /// Request configuration follows spec §5.6: not child-directed, not under the age of consent, max content rating T.
    /// On iOS the Unity loop pauses while a full-screen ad is up (<c>SetiOSAppPauseOnBackground</c>, RW6).
    /// </summary>
    public sealed class GoogleMobileAdsNetwork : IAdNetwork
    {
        const string Tag = "AdMob";

        readonly MainThreadQueue _queue;
        readonly AdUnitIds _units;
        readonly IReadOnlyList<string> _testDeviceIds;
        readonly IServiceLog _log;
        Action<AdRevenue> _paid;

        // Loaded ads waiting to be shown, and the ads currently on screen (kept apart so a reload during a show never
        // replaces the presenting ad).
        RewardedAd _rewarded;
        InterstitialAd _interstitial;
        RewardedAd _rewardedOnScreen;
        InterstitialAd _interstitialOnScreen;
        AdShowCallbacks _rewardedShow;
        AdShowCallbacks _interstitialShow;
        int _rewardedGeneration;
        int _interstitialGeneration;

        public GoogleMobileAdsNetwork(MainThreadQueue queue, AdUnitIds units, IReadOnlyList<string> testDeviceIds, IServiceLog log)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _testDeviceIds = testDeviceIds ?? Array.Empty<string>();
            _log = log ?? NullServiceLog.Instance;
        }

        public bool IsSdkAvailable => true;

        public Action<AdRevenue> PaidHandler { set => _paid = value; }

        public void Initialize(Action<bool> done)
        {
            MobileAds.SetiOSAppPauseOnBackground(true);

            var configuration = new RequestConfiguration { MaxAdContentRating = MaxAdContentRating.T };
#pragma warning disable 618 // Deprecated in 11.3 in favour of AgeRestrictedTreatment, still honoured; kept for 9.x–11.x.
            configuration.TagForChildDirectedTreatment = TagForChildDirectedTreatment.False;
            configuration.TagForUnderAgeOfConsent = TagForUnderAgeOfConsent.False;
#pragma warning restore 618
            foreach (string id in _testDeviceIds)
            {
                if (!string.IsNullOrEmpty(id)) configuration.TestDeviceIds.Add(id);
            }
            MobileAds.SetRequestConfiguration(configuration);

            MobileAds.Initialize(status => _queue.Post(() => done?.Invoke(true)));
        }

        public void Load(AdFormat format, Action<AdNetworkLoadResult> done)
        {
            if (format == AdFormat.Rewarded)
            {
                int generation = ++_rewardedGeneration;
                RewardedAd.Load(_units.Rewarded, new AdRequest(), (ad, error) => _queue.Post(() => OnRewardedLoaded(generation, ad, error, done)));
            }
            else
            {
                int generation = ++_interstitialGeneration;
                InterstitialAd.Load(_units.Interstitial, new AdRequest(), (ad, error) => _queue.Post(() => OnInterstitialLoaded(generation, ad, error, done)));
            }
        }

        public bool IsLoaded(AdFormat format)
        {
            if (format == AdFormat.Rewarded) return _rewarded != null && _rewarded.CanShowAd();
            return _interstitial != null && _interstitial.CanShowAd();
        }

        public void Show(AdFormat format, AdShowCallbacks callbacks)
        {
            if (format == AdFormat.Rewarded)
            {
                var ad = _rewarded;
                if (ad == null || !ad.CanShowAd() || _rewardedOnScreen != null)
                {
                    callbacks?.FailedToShow?.Invoke(new AdErrorInfo(-1, "rewarded ad not loaded"));
                    return;
                }
                _rewarded = null;
                _rewardedOnScreen = ad;
                _rewardedShow = callbacks;
                ad.Show(reward => _queue.Post(() =>
                {
                    if (callbacks != null && callbacks.RewardEarned != null) callbacks.RewardEarned();
                }));
            }
            else
            {
                var ad = _interstitial;
                if (ad == null || !ad.CanShowAd() || _interstitialOnScreen != null)
                {
                    callbacks?.FailedToShow?.Invoke(new AdErrorInfo(-1, "interstitial not loaded"));
                    return;
                }
                _interstitial = null;
                _interstitialOnScreen = ad;
                _interstitialShow = callbacks;
                ad.Show();
            }
        }

        public void Discard(AdFormat format)
        {
            if (format == AdFormat.Rewarded)
            {
                _rewardedGeneration++; // a load still in flight is destroyed on arrival
                if (_rewarded != null)
                {
                    _rewarded.Destroy();
                    _rewarded = null;
                }
            }
            else
            {
                _interstitialGeneration++;
                if (_interstitial != null)
                {
                    _interstitial.Destroy();
                    _interstitial = null;
                }
            }
        }

        void OnRewardedLoaded(int generation, RewardedAd ad, LoadAdError error, Action<AdNetworkLoadResult> done)
        {
            if (error != null || ad == null)
            {
                done?.Invoke(Failure(error));
                return;
            }
            if (generation != _rewardedGeneration)
            {
                ad.Destroy();
                return;
            }
            if (_rewarded != null) _rewarded.Destroy();
            _rewarded = ad;
            ad.OnAdFullScreenContentOpened += () => _queue.Post(() =>
            {
                if (ReferenceEquals(ad, _rewardedOnScreen) && _rewardedShow != null && _rewardedShow.Opened != null) _rewardedShow.Opened();
            });
            ad.OnAdFullScreenContentClosed += () => _queue.Post(() => EndRewarded(ad, null));
            ad.OnAdFullScreenContentFailed += adError => _queue.Post(() => EndRewarded(ad, adError));
            ad.OnAdPaid += value => _queue.Post(() => ReportPaid(AdFormat.Rewarded, value));
            done?.Invoke(AdNetworkLoadResult.Ok);
        }

        void OnInterstitialLoaded(int generation, InterstitialAd ad, LoadAdError error, Action<AdNetworkLoadResult> done)
        {
            if (error != null || ad == null)
            {
                done?.Invoke(Failure(error));
                return;
            }
            if (generation != _interstitialGeneration)
            {
                ad.Destroy();
                return;
            }
            if (_interstitial != null) _interstitial.Destroy();
            _interstitial = ad;
            ad.OnAdFullScreenContentOpened += () => _queue.Post(() =>
            {
                if (ReferenceEquals(ad, _interstitialOnScreen) && _interstitialShow != null && _interstitialShow.Opened != null) _interstitialShow.Opened();
            });
            ad.OnAdFullScreenContentClosed += () => _queue.Post(() => EndInterstitial(ad, null));
            ad.OnAdFullScreenContentFailed += adError => _queue.Post(() => EndInterstitial(ad, adError));
            ad.OnAdPaid += value => _queue.Post(() => ReportPaid(AdFormat.Interstitial, value));
            done?.Invoke(AdNetworkLoadResult.Ok);
        }

        void EndRewarded(RewardedAd ad, AdError error)
        {
            if (!ReferenceEquals(ad, _rewardedOnScreen)) return;
            var callbacks = _rewardedShow;
            _rewardedShow = null;
            _rewardedOnScreen = null;
            ad.Destroy();
            if (callbacks == null) return;
            if (error == null) callbacks.Closed?.Invoke();
            else callbacks.FailedToShow?.Invoke(new AdErrorInfo(error.GetCode(), error.GetMessage()));
        }

        void EndInterstitial(InterstitialAd ad, AdError error)
        {
            if (!ReferenceEquals(ad, _interstitialOnScreen)) return;
            var callbacks = _interstitialShow;
            _interstitialShow = null;
            _interstitialOnScreen = null;
            ad.Destroy();
            if (callbacks == null) return;
            if (error == null) callbacks.Closed?.Invoke();
            else callbacks.FailedToShow?.Invoke(new AdErrorInfo(error.GetCode(), error.GetMessage()));
        }

        void ReportPaid(AdFormat format, AdValue value)
        {
            if (value == null || _paid == null) return;
            _paid(new AdRevenue(format, value.Value, value.CurrencyCode, (int)value.Precision));
        }

        AdNetworkLoadResult Failure(LoadAdError error)
        {
            if (error == null) return AdNetworkLoadResult.Fail(-1, "no ad and no error");
            _log.Info(Tag, "Load failed: " + error.GetCode() + " " + error.GetMessage());
            return AdNetworkLoadResult.Fail(error.GetCode(), error.GetMessage());
        }
    }
}
#endif
