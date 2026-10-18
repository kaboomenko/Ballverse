using System;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Interstitial rules IN1–IN8 (spec §5.3.2, owner decision Р7) as a deterministic state machine.
    /// The caller reports events with explicit unix-millisecond times; the policy answers whether to show an ad.
    /// <para>
    /// Lifecycle: <see cref="OnGameCompleted"/> counts games (IN1, IN6) and never shows anything — the user must see the
    /// result (IN2). <see cref="OnNaturalBreak"/> decides at «Ещё раз», «🎲», «Назад» or gallery exit. After a
    /// <see cref="InterstitialDecisionKind.Show"/> the caller presents the ad and reports <see cref="OnInterstitialShown"/>
    /// (or <see cref="OnInterstitialNotShown"/> if presenting failed). Rewarded ads are reported with
    /// <see cref="OnRewardedShown"/> for the IN3 quiet period.
    /// </para>
    /// <para>
    /// Grace period (IN6), precisely: no interstitial until <c>totalCompletedGames &gt; graceGames</c>, and games completed
    /// while <c>totalCompletedGames ≤ graceGames</c> do not accumulate in the counter. With grace 3 and N 3 the first
    /// interstitial becomes pending after the 6th completed game.
    /// </para>
    /// </summary>
    public sealed class InterstitialPolicy
    {
        /// <summary>
        /// IN8: duration of the short «Реклама» label shown instead of a countdown before the ad, milliseconds.
        /// The label itself is UI; Core only exposes the constant.
        /// </summary>
        public const int AdLabelDurationMs = 500;

        AdPolicyConfig _config;
        readonly InterstitialState _state;

        /// <param name="config">Ad config; a clamped copy is kept (<see cref="AdPolicyConfig.Validated"/>).</param>
        /// <param name="state">Persisted state (copied, then normalized) or null for a new user.</param>
        public InterstitialPolicy(AdPolicyConfig config, InterstitialState state = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config.Validated();
            _state = state != null ? state.Clone() : new InterstitialState();
            _state.Normalize();
        }

        /// <summary>Restores a policy from JSON produced by <see cref="SerializeState"/>.</summary>
        public static InterstitialPolicy FromJson(AdPolicyConfig config, string stateJson)
        {
            return new InterstitialPolicy(config, InterstitialState.FromJson(stateJson));
        }

        /// <summary>
        /// Current config (a clamped copy). Setting it applies a fresh Remote Config without losing state; the new values
        /// are clamped too (<see cref="AdPolicyConfig.Validated"/>), so a remote N = 0 behaves as N = 1.
        /// </summary>
        public AdPolicyConfig Config
        {
            get { return _config.Clone(); }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                _config = value.Validated();
            }
        }

        /// <summary>IN1 counter: completed games since the last show or reset. Never negative.</summary>
        public int CompletedGames => _state.CompletedGames;

        /// <summary>Lifetime completed games (IN6).</summary>
        public long TotalCompletedGames => _state.TotalCompletedGames;

        public long? LastInterstitialShownMs => _state.LastInterstitialShownMs;
        public long? LastRewardedShownMs => _state.LastRewardedShownMs;

        /// <summary>
        /// True while the grace period of IN6 lasts: <c>graceGames &gt; 0</c> and <c>totalCompletedGames ≤ graceGames</c>.
        /// Always false with the default grace 0, so a new user is not reported as being in a grace period.
        /// </summary>
        public bool InGracePeriod => _config.InterstitialGraceGames > 0 && _state.TotalCompletedGames <= _config.InterstitialGraceGames;

        /// <summary>IN1: an interstitial is waiting for the next natural break (counter ≥ N and the grace period is over).</summary>
        public bool IsPending => !InGracePeriod && _state.CompletedGames >= _config.InterstitialEveryNGames;

        /// <summary>Completed games still needed before an interstitial becomes pending (0 when pending). Includes remaining grace games.</summary>
        public long GamesUntilPending
        {
            get
            {
                if (IsPending) return 0;
                long counterLeft = Math.Max(0L, (long)_config.InterstitialEveryNGames - _state.CompletedGames);
                if (!InGracePeriod) return counterLeft;
                // Games up to totalCompletedGames == graceGames do not count; the next one both ends the grace and counts.
                long graceLeft = _config.InterstitialGraceGames - _state.TotalCompletedGames;
                return graceLeft + Math.Max(1L, counterLeft);
            }
        }

        /// <summary>Copy of the persistent state.</summary>
        public InterstitialState SnapshotState()
        {
            return _state.Clone();
        }

        /// <summary>State as compact JSON (<see cref="CoreJson"/>), to persist between sessions (IN1).</summary>
        public string SerializeState()
        {
            return _state.ToJson();
        }

        /// <summary>
        /// Reports the end of a run. Applies <see cref="GameCompletedRule"/> (spec §2.1); a counted game increments the
        /// lifetime total and, after the grace period (IN6), the IN1 counter. Never shows an ad (IN2: not at the finale).
        /// </summary>
        /// <returns>True if the run counted as <c>GameCompleted</c>.</returns>
        public bool OnGameCompleted(GameRunSummary run)
        {
            if (!GameCompletedRule.IsGameCompleted(run)) return false;
            if (_state.TotalCompletedGames < long.MaxValue) _state.TotalCompletedGames++;
            if (!InGracePeriod && _state.CompletedGames < int.MaxValue) _state.CompletedGames++;
            return true;
        }

        /// <summary>
        /// Decides at a natural break (IN2). Rules, in order:
        /// <list type="number">
        /// <item>IN7 entitlement: <see cref="InterstitialSuppressReason.Entitlement"/>; the counter is cleared, nothing ever accumulates.</item>
        /// <item>Not pending (IN1, IN6): <see cref="InterstitialDecisionKind.None"/>.</item>
        /// <item>IN3 «В видео», studio, export, purchase, quiet period after rewarded; IN5 minimum interval: suppressed, stays pending.</item>
        /// <item>IN4 ad not ready: suppressed and the counter resets to 0.</item>
        /// <item>Otherwise <see cref="InterstitialDecisionKind.Show"/>.</item>
        /// </list>
        /// Boundaries are inclusive for showing: exactly 60 s after the last interstitial (IN5) or exactly 30 s after a
        /// rewarded ad (IN3) is allowed. If the device clock moved backwards past a stored timestamp, that timestamp is
        /// pulled back to <c>nowMs</c>: a backward clock change delays ads by at most one interval (measured from the
        /// moment it is noticed) instead of blocking them until the clock catches up.
        /// </summary>
        public InterstitialDecision OnNaturalBreak(NaturalBreakContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            long now = context.NowMs;
            RequireTime(now, nameof(context));
            ClampFutureTimestamps(now);

            if (context.HasNoInterstitialsEntitlement)
            {
                bool hadCount = _state.CompletedGames > 0;
                _state.CompletedGames = 0;
                return InterstitialDecision.Suppressed(InterstitialSuppressReason.Entitlement, hadCount);
            }

            if (!IsPending) return InterstitialDecision.None;

            if (context.Kind == NaturalBreakKind.ToVideo) return Keep(InterstitialSuppressReason.ToVideo);
            if (context.InStudio) return Keep(InterstitialSuppressReason.InStudio);
            if (context.Exporting) return Keep(InterstitialSuppressReason.Exporting);
            if (context.InPurchaseFlow) return Keep(InterstitialSuppressReason.PurchaseFlow);
            if (_state.LastRewardedShownMs.HasValue && now - _state.LastRewardedShownMs.Value < _config.InterstitialQuietAfterRewardedMs)
                return Keep(InterstitialSuppressReason.RewardedQuietPeriod);
            if (_state.LastInterstitialShownMs.HasValue && now - _state.LastInterstitialShownMs.Value < _config.InterstitialMinIntervalMs)
                return Keep(InterstitialSuppressReason.MinInterval);

            if (!context.AdReady)
            {
                _state.CompletedGames = 0;
                return InterstitialDecision.Suppressed(InterstitialSuppressReason.AdNotReady, true);
            }

            return InterstitialDecision.Show;
        }

        /// <summary>The interstitial was presented: the counter resets to 0 (IN4) and the IN5 interval starts at <paramref name="nowMs"/>.</summary>
        public void OnInterstitialShown(long nowMs)
        {
            RequireTime(nowMs, nameof(nowMs));
            _state.CompletedGames = 0;
            _state.LastInterstitialShownMs = nowMs;
        }

        /// <summary>
        /// A <see cref="InterstitialDecisionKind.Show"/> could not be presented after all (the ad expired or failed to open).
        /// Treated like an ad that was not ready (IN4): the counter resets to 0 and the IN5 interval is not restarted.
        /// </summary>
        public void OnInterstitialNotShown()
        {
            _state.CompletedGames = 0;
        }

        /// <summary>A rewarded ad was on screen; report when it closes. Starts the IN3 quiet period at <paramref name="nowMs"/>.</summary>
        public void OnRewardedShown(long nowMs)
        {
            RequireTime(nowMs, nameof(nowMs));
            _state.LastRewardedShownMs = nowMs;
        }

        static InterstitialDecision Keep(InterstitialSuppressReason reason)
        {
            return InterstitialDecision.Suppressed(reason, false);
        }

        void ClampFutureTimestamps(long now)
        {
            if (_state.LastInterstitialShownMs.HasValue && _state.LastInterstitialShownMs.Value > now) _state.LastInterstitialShownMs = now;
            if (_state.LastRewardedShownMs.HasValue && _state.LastRewardedShownMs.Value > now) _state.LastRewardedShownMs = now;
        }

        internal static void RequireTime(long nowMs, string paramName)
        {
            if (nowMs < 0) throw new ArgumentOutOfRangeException(paramName, "time must be unix milliseconds >= 0");
        }
    }
}
