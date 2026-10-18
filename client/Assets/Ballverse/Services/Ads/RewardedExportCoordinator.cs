using System;
using Ballverse.Core.Ads;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// Rewarded ad before export (spec §5.3.1 RW1–RW7, Р7, Р14) — the Core <see cref="RewardedExportGate"/> wired to the ads
    /// service, persistence and analytics.
    /// <para>Flow for the studio/export UI:</para>
    /// <list type="number">
    /// <item>On entering/leaving the studio: <see cref="SetInStudio"/> (RW1 preload).</item>
    /// <item>After the export sheet is confirmed: <see cref="RequestExport"/>. <c>ExportWithoutAd</c> → start the export.
    /// <c>ShowOffer</c> → show the dialog «Смотреть рекламу и экспортировать / Экспорт без рекламы навсегда / Отмена».</item>
    /// <item>«Смотреть рекламу»: <see cref="WatchAd"/>. The step arrives after the ad has closed (RW6), or without an ad when it
    /// failed, timed out (<c>rewarded_wait_timeout_sec</c>) or the device is offline (RW3, Р14): follow
    /// <see cref="RewardedGateStep.Kind"/> — StartExport / ShowOfferAgain.</item>
    /// <item>«Экспорт без рекламы навсегда» or «Отмена»: <see cref="Decline"/> (StartPurchase / Abort). After a purchase call
    /// <see cref="RequestExport"/> again.</item>
    /// <item>When the export run ends: <see cref="OnExportFinished"/> (RW4 retry credit); when a project is deleted:
    /// <see cref="RemoveProject"/>.</item>
    /// </list>
    /// </summary>
    public sealed class RewardedExportCoordinator : ITickable
    {
        public const string Placement = "export";

        const string Tag = "RewardedExport";

        sealed class Wait
        {
            public string ProjectId;
            public Action<RewardedGateStep> OnStep;
            public long StartedMs;
        }

        readonly IAdsService _ads;
        readonly AdPolicyStore _store;
        readonly IConnectivity _connectivity;
        readonly IClock _clock;
        readonly IAnalyticsService _analytics;
        readonly InterstitialCoordinator _interstitials;
        readonly Func<EntitlementSet> _entitlements;
        readonly IServiceLog _log;
        Wait _wait;
        string _presentingFor;

        public RewardedExportCoordinator(IAdsService ads, AdPolicyStore store, IConnectivity connectivity, IClock clock,
            IAnalyticsService analytics, InterstitialCoordinator interstitials, Func<EntitlementSet> entitlements, IServiceLog log)
        {
            _ads = ads ?? throw new ArgumentNullException(nameof(ads));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _interstitials = interstitials ?? throw new ArgumentNullException(nameof(interstitials));
            _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
            _log = log ?? NullServiceLog.Instance;
        }

        public RewardedExportGate Gate => _store.Gate;

        public bool InStudio { get; private set; }

        /// <summary>True while <see cref="WatchAd"/> waits for an ad to load (show a spinner in the dialog).</summary>
        public bool IsWaitingForAd => _wait != null;

        /// <summary>True between <see cref="WatchAd"/> and its step.</summary>
        public bool IsBusy => _wait != null || _presentingFor != null;

        bool HasNoExportAds => _entitlements().Contains(Entitlement.NoExportAds);

        /// <summary>RW1: preload the rewarded ad while the user is in the studio.</summary>
        public void SetInStudio(bool inStudio)
        {
            InStudio = inStudio;
            UpdatePreload();
        }

        public void UpdatePreload()
        {
            if (IsBusy) return; // the running flow keeps its ad
            _ads.SetKeepLoaded(AdFormat.Rewarded, AdPreloadAdvisor.ShouldPreloadRewarded(InStudio, HasNoExportAds, _store.Gate.Config));
        }

        /// <summary>The export sheet was confirmed for <paramref name="projectId"/> (spec §5.3.1 "Когда").</summary>
        public ExportGateDecision RequestExport(string projectId)
        {
            var decision = _store.Gate.Decide(projectId, _clock.UnixTimeMs, HasNoExportAds);
            _store.SaveGate();
            if (decision.IsShowOffer)
            {
                _analytics.LogEvent(AnalyticsEvents.AdRewardedOffer, AnalyticsParam.Str(AnalyticsParams.Placement, Placement));
                if (_ads.State == AdsServiceState.Ready && !_ads.IsReady(AdFormat.Rewarded)) _ads.SetKeepLoaded(AdFormat.Rewarded, true);
            }
            return decision;
        }

        /// <summary>
        /// «Смотреть рекламу и экспортировать». <paramref name="onStep"/> is called exactly once, on the main thread, after the ad
        /// closed or when no ad could be shown. A second call while one is running is ignored.
        /// </summary>
        public void WatchAd(string projectId, Action<RewardedGateStep> onStep)
        {
            if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("projectId must be non-empty", nameof(projectId));
            if (IsBusy)
            {
                _log.Warning(Tag, "WatchAd while another rewarded flow runs; ignored.");
                return;
            }
            if (_connectivity.IsOffline)
            {
                Finish(projectId, RewardedOfferOutcome.Offline, onStep, "offline", 0);
                return;
            }
            if (_ads.State == AdsServiceState.Unavailable)
            {
                Finish(projectId, RewardedOfferOutcome.AdFailed, onStep, "ads_unavailable", 0);
                return;
            }
            if (_ads.IsReady(AdFormat.Rewarded))
            {
                Present(projectId, onStep, 0);
                return;
            }
            _wait = new Wait { ProjectId = projectId, OnStep = onStep, StartedMs = _clock.MonotonicMs };
            if (_ads.State == AdsServiceState.Ready) _ads.LoadNow(AdFormat.Rewarded);
            else _ads.SetKeepLoaded(AdFormat.Rewarded, true); // loads as soon as the SDK is ready
            CheckWait();
        }

        /// <summary>«Отмена» (also while waiting) or «Экспорт без рекламы навсегда» in the offer dialog.</summary>
        public RewardedGateStep Decline(string projectId, bool purchaseChosen)
        {
            if (_wait != null && _wait.ProjectId == projectId) _wait = null;
            _analytics.LogEvent(AnalyticsEvents.AdRewardedDecline,
                AnalyticsParam.Str(AnalyticsParams.Placement, Placement),
                AnalyticsParam.Str(AnalyticsParams.Reason, purchaseChosen ? "purchase" : "cancel"));
            var step = ReportOutcome(projectId, purchaseChosen ? RewardedOfferOutcome.PurchaseRemoveExportAdsChosen : RewardedOfferOutcome.Cancelled);
            UpdatePreload();
            return step;
        }

        /// <summary>The export run of <paramref name="projectId"/> ended (spec §3.8, RW4, RW5).</summary>
        public ExportFinishOutcome OnExportFinished(string projectId, ExportResult result)
        {
            var outcome = _store.Gate.OnExportFinished(projectId, result, _clock.UnixTimeMs);
            _store.SaveGate();
            return outcome;
        }

        /// <summary>The project was deleted (spec §3.9): drop its credit.</summary>
        public bool RemoveProject(string projectId)
        {
            bool removed = _store.Gate.RemoveProject(projectId);
            _store.SaveGate();
            return removed;
        }

        public void Tick()
        {
            CheckWait();
        }

        void CheckWait()
        {
            var w = _wait;
            if (w == null) return;
            long waited = Math.Max(0L, _clock.MonotonicMs - w.StartedMs);
            if (_ads.IsReady(AdFormat.Rewarded))
            {
                _wait = null;
                Present(w.ProjectId, w.OnStep, waited);
                return;
            }
            if (_connectivity.IsOffline)
            {
                _wait = null;
                Finish(w.ProjectId, RewardedOfferOutcome.Offline, w.OnStep, "offline", waited);
                return;
            }
            if (_ads.State == AdsServiceState.Unavailable)
            {
                _wait = null;
                Finish(w.ProjectId, RewardedOfferOutcome.AdFailed, w.OnStep, "ads_unavailable", waited);
                return;
            }
            if (_ads.State == AdsServiceState.Ready && _ads.GetLoadState(AdFormat.Rewarded) == AdLoadState.Failed)
            {
                // No fill or a network error: do not make the user sit out the rest of the timeout (RW3).
                _wait = null;
                var error = _ads.LastLoadError(AdFormat.Rewarded);
                Finish(w.ProjectId, RewardedOfferOutcome.AdFailed, w.OnStep, error.HasValue ? error.Value.AnalyticsCode : "load_failed", waited);
                return;
            }
            if (_store.Gate.HasWaitTimedOut(w.StartedMs, _clock.MonotonicMs))
            {
                _wait = null;
                Finish(w.ProjectId, RewardedOfferOutcome.AdLoadTimedOut, w.OnStep, "timeout", waited);
            }
        }

        void Present(string projectId, Action<RewardedGateStep> onStep, long waitedMs)
        {
            _presentingFor = projectId;
            _ads.ShowRewarded(result =>
            {
                _presentingFor = null;
                switch (result)
                {
                    case RewardedShowResult.Earned:
                        _analytics.LogEvent(AnalyticsEvents.AdRewardedShow, AnalyticsParam.Str(AnalyticsParams.Placement, Placement),
                            AnalyticsParam.Int(AnalyticsParams.WaitMs, waitedMs));
                        _analytics.LogEvent(AnalyticsEvents.AdRewardedReward, AnalyticsParam.Str(AnalyticsParams.Placement, Placement));
                        Finish(projectId, RewardedOfferOutcome.UserEarnedReward, onStep, null, waitedMs);
                        break;
                    case RewardedShowResult.ClosedEarly:
                        _analytics.LogEvent(AnalyticsEvents.AdRewardedShow, AnalyticsParam.Str(AnalyticsParams.Placement, Placement),
                            AnalyticsParam.Int(AnalyticsParams.WaitMs, waitedMs));
                        Finish(projectId, RewardedOfferOutcome.AdClosedEarly, onStep, null, waitedMs);
                        break;
                    default:
                        Finish(projectId, RewardedOfferOutcome.AdFailed, onStep,
                            result == RewardedShowResult.NotReady ? "not_ready" : "show_failed", waitedMs);
                        break;
                }
            });
        }

        void Finish(string projectId, RewardedOfferOutcome outcome, Action<RewardedGateStep> onStep, string errorCode, long waitedMs)
        {
            if (errorCode != null)
            {
                _analytics.LogEvent(AnalyticsEvents.AdRewardedFail,
                    AnalyticsParam.Str(AnalyticsParams.Placement, Placement),
                    AnalyticsParam.Str(AnalyticsParams.ErrorCode, errorCode),
                    AnalyticsParam.Int(AnalyticsParams.WaitMs, waitedMs));
            }
            var step = ReportOutcome(projectId, outcome);
            UpdatePreload();
            onStep?.Invoke(step);
        }

        RewardedGateStep ReportOutcome(string projectId, RewardedOfferOutcome outcome)
        {
            long now = _clock.UnixTimeMs;
            RewardedGateStep step;
            try
            {
                step = _store.Gate.OnOfferOutcome(projectId, outcome, now);
            }
            catch (InvalidOperationException e)
            {
                // No open offer (RequestExport was not called, or the state was lost). Never block the user's export (Р14).
                _log.Error(Tag, "Offer outcome without an open offer.", e);
                bool shown = outcome == RewardedOfferOutcome.UserEarnedReward || outcome == RewardedOfferOutcome.AdClosedEarly;
                switch (outcome)
                {
                    case RewardedOfferOutcome.Cancelled:
                        step = new RewardedGateStep(RewardedGateStepKind.Abort, ExportFunding.None, false);
                        break;
                    case RewardedOfferOutcome.PurchaseRemoveExportAdsChosen:
                        step = new RewardedGateStep(RewardedGateStepKind.StartPurchase, ExportFunding.None, false);
                        break;
                    default:
                        step = new RewardedGateStep(RewardedGateStepKind.StartExport, ExportFunding.AdUnavailable, shown);
                        break;
                }
            }
            _store.SaveGate();
            if (step.RewardedAdWasShown) _interstitials.OnRewardedShown(now);
            return step;
        }
    }
}
