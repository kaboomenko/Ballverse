using System;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Rewarded gate before export (spec §5.3.1 RW1–RW7, §3.8 interruptions, owner decisions Р7, Р14) as pure decisions
    /// over a serializable per-project credit store. All times are unix milliseconds supplied by the caller.
    /// <para>
    /// Flow: after the user confirms the export sheet, call <see cref="Decide"/>. On
    /// <see cref="ExportGateDecisionKind.ShowOffer"/> show the dialog and report what happened with
    /// <see cref="OnOfferOutcome"/>; follow the returned <see cref="RewardedGateStep"/>. When an export run ends, report
    /// <see cref="OnExportFinished"/>.
    /// </para>
    /// <para>
    /// Credits: a reward gives the project one held credit (RW2, RW5). A successful export of the project consumes its
    /// credit. A failed or cancelled export that was funded by a held credit turns it into a retry credit valid for
    /// <c>rewarded_retry_window_min</c> minutes, inclusive (RW4, §3.8, question В5). A credit is bound to its project id
    /// and never transfers (ordinal comparison). Exports that ran without an ad because the network could not deliver
    /// (RW3) earn no credit: the next export shows the offer again.
    /// </para>
    /// </summary>
    public sealed class RewardedExportGate
    {
        AdPolicyConfig _config;
        readonly RewardedGateState _state;

        /// <param name="config">Ad config; a clamped copy is kept (<see cref="AdPolicyConfig.Validated"/>).</param>
        /// <param name="state">Persisted state (copied, then normalized) or null for a new install.</param>
        public RewardedExportGate(AdPolicyConfig config, RewardedGateState state = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config.Validated();
            _state = state != null ? state.Clone() : new RewardedGateState();
            _state.Normalize();
        }

        /// <summary>Restores a gate from JSON produced by <see cref="SerializeState"/>.</summary>
        public static RewardedExportGate FromJson(AdPolicyConfig config, string stateJson)
        {
            return new RewardedExportGate(config, RewardedGateState.FromJson(stateJson));
        }

        /// <summary>Current config (a clamped copy). Setting it applies a fresh Remote Config without losing state.</summary>
        public AdPolicyConfig Config
        {
            get { return _config.Clone(); }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                _config = value.Validated();
            }
        }

        /// <summary>Project whose offer dialog is open, or null.</summary>
        public string OpenOfferProjectId => _state.OpenOfferProjectId;

        /// <summary>Number of stored credits (including timed ones not yet pruned).</summary>
        public int CreditCount => _state.Credits.Count;

        /// <summary>Copy of the persistent state.</summary>
        public RewardedGateState SnapshotState()
        {
            return _state.Clone();
        }

        /// <summary>State as compact JSON (<see cref="CoreJson"/>).</summary>
        public string SerializeState()
        {
            return _state.ToJson();
        }

        /// <summary>
        /// Decides whether the export of <paramref name="projectId"/> needs the rewarded offer (spec §5.3.1). In order:
        /// entitlement <c>NO_EXPORT_ADS</c>; RW7 kill switch; a valid credit of this project (RW4 retry or unused RW2 reward);
        /// otherwise <see cref="ExportGateDecisionKind.ShowOffer"/>, which opens the offer for this project.
        /// Using a credit claims it for the export that starts now: it stays valid until <see cref="OnExportFinished"/>.
        /// </summary>
        public ExportGateDecision Decide(string projectId, long nowMs, bool hasNoExportAdsEntitlement)
        {
            RequireProject(projectId);
            InterstitialPolicy.RequireTime(nowMs, nameof(nowMs));
            PruneExpired(nowMs);

            if (hasNoExportAdsEntitlement)
            {
                _state.OpenOfferProjectId = null;
                return ExportGateDecision.ExportWithoutAd(ExportWithoutAdReason.Entitlement);
            }

            if (!_config.RewardedExportEnabled)
            {
                _state.OpenOfferProjectId = null;
                return ExportGateDecision.ExportWithoutAd(ExportWithoutAdReason.GateDisabled);
            }

            int i = RewardedGateState.IndexOf(_state.Credits, projectId);
            if (i >= 0 && _state.Credits[i].IsValidAt(nowMs))
            {
                var credit = _state.Credits[i];
                credit.ExpiresAtMs = null;
                _state.OpenOfferProjectId = null;
                return ExportGateDecision.ExportWithoutAd(credit.Source == RewardedCreditSource.Retry
                    ? ExportWithoutAdReason.RetryCredit
                    : ExportWithoutAdReason.EarnedCredit);
            }

            _state.OpenOfferProjectId = projectId;
            return ExportGateDecision.ShowOffer;
        }

        /// <summary>
        /// Reports the outcome of the open offer of <paramref name="projectId"/> (spec §5.3.1):
        /// <list type="bullet">
        /// <item><see cref="RewardedOfferOutcome.UserEarnedReward"/>: one held credit for this project, start export (RW2, RW5).</item>
        /// <item><see cref="RewardedOfferOutcome.AdClosedEarly"/>: show the offer again, no credit (RW2).</item>
        /// <item>Timeout, failure, offline: start export without ad and without credit (RW3, Р14, §5.4).</item>
        /// <item>Purchase chosen: the caller runs the purchase, then calls <see cref="Decide"/> again.</item>
        /// <item>Cancelled: abort.</item>
        /// </list>
        /// </summary>
        /// <exception cref="InvalidOperationException">No offer is open for <paramref name="projectId"/>.</exception>
        public RewardedGateStep OnOfferOutcome(string projectId, RewardedOfferOutcome outcome, long nowMs)
        {
            RequireProject(projectId);
            InterstitialPolicy.RequireTime(nowMs, nameof(nowMs));
            if (!string.Equals(_state.OpenOfferProjectId, projectId, StringComparison.Ordinal))
                throw new InvalidOperationException("No rewarded offer is open for project '" + projectId + "'.");
            PruneExpired(nowMs);

            switch (outcome)
            {
                case RewardedOfferOutcome.UserEarnedReward:
                    UpsertHeldReward(projectId);
                    _state.OpenOfferProjectId = null;
                    return new RewardedGateStep(RewardedGateStepKind.StartExport, ExportFunding.RewardEarned, true);
                case RewardedOfferOutcome.AdClosedEarly:
                    return new RewardedGateStep(RewardedGateStepKind.ShowOfferAgain, ExportFunding.None, true);
                case RewardedOfferOutcome.AdLoadTimedOut:
                case RewardedOfferOutcome.AdFailed:
                case RewardedOfferOutcome.Offline:
                    _state.OpenOfferProjectId = null;
                    return new RewardedGateStep(RewardedGateStepKind.StartExport, ExportFunding.AdUnavailable, false);
                case RewardedOfferOutcome.PurchaseRemoveExportAdsChosen:
                    _state.OpenOfferProjectId = null;
                    return new RewardedGateStep(RewardedGateStepKind.StartPurchase, ExportFunding.None, false);
                case RewardedOfferOutcome.Cancelled:
                    _state.OpenOfferProjectId = null;
                    return new RewardedGateStep(RewardedGateStepKind.Abort, ExportFunding.None, false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unknown outcome");
            }
        }

        /// <summary>
        /// Reports the end of an export run of <paramref name="projectId"/> (spec §3.8). Success consumes the project's
        /// credit, whatever funded the export (RW5). Failure or cancellation of an export funded by a held credit converts
        /// it into a retry credit expiring at <c>nowMs + window</c> (inclusive, RW4); with a window of 0 the credit is
        /// burned. A timed retry credit is not extended by an export it did not fund.
        /// </summary>
        public ExportFinishOutcome OnExportFinished(string projectId, ExportResult result, long nowMs)
        {
            RequireProject(projectId);
            InterstitialPolicy.RequireTime(nowMs, nameof(nowMs));
            if (!Enum.IsDefined(typeof(ExportResult), result))
                throw new ArgumentOutOfRangeException(nameof(result), result, "unknown export result");
            PruneExpired(nowMs);

            int i = RewardedGateState.IndexOf(_state.Credits, projectId);
            if (i < 0) return new ExportFinishOutcome(ExportFinishKind.None, null);

            if (result == ExportResult.Succeeded)
            {
                _state.Credits.RemoveAt(i);
                return new ExportFinishOutcome(ExportFinishKind.CreditConsumed, null);
            }

            var credit = _state.Credits[i];
            if (credit.ExpiresAtMs.HasValue) return new ExportFinishOutcome(ExportFinishKind.None, null);

            long window = _config.RewardedRetryWindowMs;
            if (window <= 0)
            {
                _state.Credits.RemoveAt(i);
                return new ExportFinishOutcome(ExportFinishKind.CreditBurned, null);
            }

            long expires = nowMs > long.MaxValue - window ? long.MaxValue : nowMs + window;
            credit.Source = RewardedCreditSource.Retry;
            credit.ExpiresAtMs = expires;
            return new ExportFinishOutcome(ExportFinishKind.RetryCreditGranted, expires);
        }

        /// <summary>True if <paramref name="projectId"/> has a credit usable at <paramref name="nowMs"/>; <paramref name="credit"/> is a copy.</summary>
        public bool TryGetCredit(string projectId, long nowMs, out RewardedExportCredit credit)
        {
            RequireProject(projectId);
            InterstitialPolicy.RequireTime(nowMs, nameof(nowMs));
            int i = RewardedGateState.IndexOf(_state.Credits, projectId);
            if (i >= 0 && _state.Credits[i].IsValidAt(nowMs))
            {
                credit = _state.Credits[i].Clone();
                return true;
            }
            credit = null;
            return false;
        }

        /// <summary>
        /// RW3: true once the rewarded ad has been loading for <c>rewarded_wait_timeout_sec</c> or longer (exactly 5 s
        /// times out). The caller then reports <see cref="RewardedOfferOutcome.AdLoadTimedOut"/>.
        /// </summary>
        public bool HasWaitTimedOut(long waitStartedMs, long nowMs)
        {
            InterstitialPolicy.RequireTime(waitStartedMs, nameof(waitStartedMs));
            InterstitialPolicy.RequireTime(nowMs, nameof(nowMs));
            return nowMs - waitStartedMs >= _config.RewardedWaitTimeoutMs;
        }

        void UpsertHeldReward(string projectId)
        {
            int i = RewardedGateState.IndexOf(_state.Credits, projectId);
            if (i >= 0)
            {
                // RW5: credits never stack; a second reward for the same project just holds the one credit.
                _state.Credits[i].Source = RewardedCreditSource.Reward;
                _state.Credits[i].ExpiresAtMs = null;
                return;
            }
            var credit = new RewardedExportCredit(projectId, RewardedCreditSource.Reward, null);
            int at = 0;
            while (at < _state.Credits.Count && string.CompareOrdinal(_state.Credits[at].ProjectId, projectId) < 0) at++;
            _state.Credits.Insert(at, credit);
        }

        void PruneExpired(long nowMs)
        {
            _state.Credits.RemoveAll(c => !c.IsValidAt(nowMs));
        }

        static void RequireProject(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) throw new ArgumentException("projectId must be non-empty", nameof(projectId));
        }
    }
}
