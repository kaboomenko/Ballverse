using System;
using Ballverse.Core.Ads;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// Interstitial after every N completed games (spec §5.3.2, Р7) — the Core <see cref="InterstitialPolicy"/> wired to the
    /// ads service, persistence and analytics.
    /// <para>
    /// Usage: report finished runs with <see cref="OnGameCompleted"/>. At «Ещё раз», «🎲», «Назад» or gallery exit call
    /// <see cref="EvaluateNaturalBreak"/>; on <see cref="InterstitialDecisionKind.Show"/> show the 0.5 s «Реклама» label
    /// (<see cref="InterstitialPolicy.AdLabelDurationMs"/>, IN8) and then <see cref="ShowInterstitial"/>; otherwise continue at once.
    /// </para>
    /// </summary>
    public sealed class InterstitialCoordinator
    {
        const string Tag = "Interstitial";

        readonly IAdsService _ads;
        readonly AdPolicyStore _store;
        readonly IClock _clock;
        readonly IAnalyticsService _analytics;
        readonly Func<EntitlementSet> _entitlements;
        readonly IServiceLog _log;
        NaturalBreakKind? _approvedBreak;

        public InterstitialCoordinator(IAdsService ads, AdPolicyStore store, IClock clock, IAnalyticsService analytics,
            Func<EntitlementSet> entitlements, IServiceLog log)
        {
            _ads = ads ?? throw new ArgumentNullException(nameof(ads));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
            _log = log ?? NullServiceLog.Instance;
        }

        public InterstitialPolicy Policy => _store.Interstitial;

        bool HasNoInterstitials => _entitlements().Contains(Entitlement.NoInterstitials);

        /// <summary>Counts a finished run (spec §2.1 <c>GameCompleted</c>, IN1). Never shows anything (IN2).</summary>
        public bool OnGameCompleted(GameRunSummary run)
        {
            bool counted = _store.Interstitial.OnGameCompleted(run);
            if (counted)
            {
                _store.SaveInterstitial();
                UpdatePreload();
            }
            return counted;
        }

        /// <summary>
        /// Decides at a natural break (IN2–IN7). <see cref="InterstitialDecisionKind.Show"/> authorizes exactly one following
        /// <see cref="ShowInterstitial"/> call.
        /// </summary>
        public InterstitialDecision EvaluateNaturalBreak(NaturalBreakKind kind, bool inStudio, bool exporting, bool inPurchaseFlow)
        {
            var context = new NaturalBreakContext(kind, _clock.UnixTimeMs, _ads.IsReady(AdFormat.Interstitial))
            {
                InStudio = inStudio,
                Exporting = exporting,
                InPurchaseFlow = inPurchaseFlow,
                HasNoInterstitialsEntitlement = HasNoInterstitials,
            };
            var decision = _store.Interstitial.OnNaturalBreak(context);
            _store.SaveInterstitial();

            if (decision.IsShow)
            {
                _approvedBreak = kind;
            }
            else
            {
                _approvedBreak = null;
                if (decision.Kind == InterstitialDecisionKind.Suppressed && decision.Reason != InterstitialSuppressReason.Entitlement)
                {
                    _analytics.LogEvent(AnalyticsEvents.AdInterstitialSkip,
                        AnalyticsParam.Str(AnalyticsParams.Trigger, AdAnalyticsNames.Trigger(kind)),
                        AnalyticsParam.Str(AnalyticsParams.Reason, AdAnalyticsNames.Reason(decision.Reason)));
                }
            }
            UpdatePreload();
            return decision;
        }

        /// <summary>
        /// Presents the interstitial approved by the last <see cref="EvaluateNaturalBreak"/>; <paramref name="done"/> gets
        /// whether it was shown. Without an approval, or if the ad vanished meanwhile, it completes at once with false and the
        /// user is never delayed (IN4).
        /// </summary>
        public void ShowInterstitial(Action<bool> done)
        {
            if (_approvedBreak == null)
            {
                _log.Warning(Tag, "ShowInterstitial without a Show decision; ignored.");
                done?.Invoke(false);
                return;
            }
            var kind = _approvedBreak.Value;
            _approvedBreak = null;
            _ads.ShowInterstitial(result =>
            {
                bool shown = result == InterstitialShowResult.Shown;
                if (shown)
                {
                    _store.Interstitial.OnInterstitialShown(_clock.UnixTimeMs);
                    _analytics.LogEvent(AnalyticsEvents.AdInterstitialShow,
                        AnalyticsParam.Str(AnalyticsParams.Trigger, AdAnalyticsNames.Trigger(kind)));
                }
                else
                {
                    _store.Interstitial.OnInterstitialNotShown();
                    _analytics.LogEvent(AnalyticsEvents.AdInterstitialSkip,
                        AnalyticsParam.Str(AnalyticsParams.Trigger, AdAnalyticsNames.Trigger(kind)),
                        AnalyticsParam.Str(AnalyticsParams.Reason, result == InterstitialShowResult.NotReady ? "ad_not_ready" : "show_failed"));
                }
                _store.SaveInterstitial();
                UpdatePreload();
                done?.Invoke(shown);
            });
        }

        /// <summary>A rewarded ad closed at <paramref name="unixMs"/>: starts the IN3 quiet period.</summary>
        public void OnRewardedShown(long unixMs)
        {
            _store.Interstitial.OnRewardedShown(unixMs);
            _store.SaveInterstitial();
        }

        /// <summary>Keeps an interstitial loaded only when the policy will need one soon and the user has no NO_INTERSTITIALS (IN7).</summary>
        public void UpdatePreload()
        {
            _ads.SetKeepLoaded(AdFormat.Interstitial, AdPreloadAdvisor.ShouldPreloadInterstitial(_store.Interstitial, HasNoInterstitials));
        }
    }
}
