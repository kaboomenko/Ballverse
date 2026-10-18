using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// <see cref="IAdsService"/> over an <see cref="IAdNetwork"/> (Google Mobile Ads in builds, <see cref="FakeAdNetwork"/> in the
    /// editor and tests). Owns load state, reload with exponential backoff, expiry of stale ads, the presentation lifecycle and
    /// the "reward may arrive just after close" grace. Single-threaded: the network delivers callbacks on the main thread.
    /// </summary>
    public sealed class AdsService : IAdsService, ITickable
    {
        /// <summary>Loaded ads expire after one hour (Google); an older one is discarded and reloaded before use.</summary>
        public const long MaxAdAgeMs = 55L * 60L * 1000L;

        public const long FirstRetryDelayMs = 2000;
        public const long MaxRetryDelayMs = 60_000;

        /// <summary>After a rewarded ad closes without the reward, the reward callback is still awaited this long.</summary>
        public const long RewardGraceMs = 1000;

        /// <summary>
        /// The <see cref="BeforeShow"/> hook (ATT pre-prompt and system dialog) must continue within this time; otherwise the
        /// presentation is abandoned with <c>NotReady</c>, so the export runs without the ad and the interstitial is skipped
        /// (Р14, IN4). A late continuation is ignored.
        /// </summary>
        public const long BeforeShowTimeoutMs = 60_000;

        const string Tag = "Ads";

        sealed class Slot
        {
            public Slot(AdFormat format)
            {
                Format = format;
            }

            public readonly AdFormat Format;
            public bool Keep;
            public AdLoadState State;
            public int Token;
            public long LoadedAtMs;
            public long NextRetryAtMs;
            public int Failures;
            public AdErrorInfo? LastError;
        }

        sealed class RewardedSession
        {
            public Action<RewardedShowResult> Done;
            public bool Opened;
            public bool Earned;
            public bool Closed;
            public bool Finished;
            public long GraceDeadlineMs;
        }

        sealed class PendingHook
        {
            public long DeadlineMs;
            public bool Settled;
            public Action Abandon;
        }

        readonly IAdNetwork _network;
        readonly IClock _clock;
        readonly IServiceLog _log;
        readonly Slot _rewarded = new Slot(AdFormat.Rewarded);
        readonly Slot _interstitial = new Slot(AdFormat.Interstitial);
        readonly List<PendingHook> _pendingHooks = new List<PendingHook>(2);
        RewardedSession _rewardedSession;
        AdFormat _showingFormat;

        public AdsService(IAdNetwork network, IClock clock, IServiceLog log)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? NullServiceLog.Instance;
        }

        public AdsServiceState State { get; private set; }

        public bool IsShowing { get; private set; }

        public Action<Action> BeforeShow { get; set; }

        public event Action<bool> FullScreenChanged;

        public event Action<AdRevenue> Paid;

        public event Action<AdFormat, bool> LoadFinished;

        public void Initialize()
        {
            if (State != AdsServiceState.NotInitialized) return;
            if (!_network.IsSdkAvailable)
            {
                State = AdsServiceState.Unavailable;
                _log.Info(Tag, "No ads SDK in this build; exports run without ads.");
                return;
            }
            State = AdsServiceState.Initializing;
            _network.PaidHandler = revenue => Paid?.Invoke(revenue);
            try
            {
                _network.Initialize(OnInitialized);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Ads SDK initialization threw.", e);
                State = AdsServiceState.Unavailable;
            }
        }

        void OnInitialized(bool ok)
        {
            if (State != AdsServiceState.Initializing) return;
            State = ok ? AdsServiceState.Ready : AdsServiceState.Unavailable;
            _log.Info(Tag, ok ? "Ads SDK ready." : "Ads SDK failed to initialize.");
            if (!ok) return;
            TryLoad(_rewarded, false);
            TryLoad(_interstitial, false);
        }

        public void SetKeepLoaded(AdFormat format, bool keep)
        {
            var slot = SlotOf(format);
            if (slot.Keep == keep) return;
            slot.Keep = keep;
            if (keep)
            {
                TryLoad(slot, false);
                return;
            }
            if (slot.State == AdLoadState.Loaded || slot.State == AdLoadState.Loading) _network.Discard(format);
            slot.State = AdLoadState.Idle;
            slot.Token++;
            slot.Failures = 0;
            slot.NextRetryAtMs = 0;
        }

        public bool IsKeepLoaded(AdFormat format)
        {
            return SlotOf(format).Keep;
        }

        public void LoadNow(AdFormat format)
        {
            var slot = SlotOf(format);
            slot.Keep = true;
            slot.NextRetryAtMs = 0;
            TryLoad(slot, true);
        }

        public bool IsReady(AdFormat format)
        {
            var slot = SlotOf(format);
            return State == AdsServiceState.Ready && !IsShowing && slot.State == AdLoadState.Loaded && !IsExpired(slot)
                   && _network.IsLoaded(format);
        }

        public AdLoadState GetLoadState(AdFormat format)
        {
            return SlotOf(format).State;
        }

        public AdErrorInfo? LastLoadError(AdFormat format)
        {
            return SlotOf(format).LastError;
        }

        public void Tick()
        {
            TickSlot(_rewarded);
            TickSlot(_interstitial);
            TickPendingHooks();
            var session = _rewardedSession;
            if (session != null && session.Closed && !session.Finished && _clock.MonotonicMs >= session.GraceDeadlineMs)
                FinishRewarded(session, RewardedShowResult.ClosedEarly);
        }

        public void ShowRewarded(Action<RewardedShowResult> done)
        {
            if (!IsReady(AdFormat.Rewarded))
            {
                done?.Invoke(RewardedShowResult.NotReady);
                return;
            }
            RunBeforeShow(() => PresentRewarded(done), () => done?.Invoke(RewardedShowResult.NotReady));
        }

        public void ShowInterstitial(Action<InterstitialShowResult> done)
        {
            if (!IsReady(AdFormat.Interstitial))
            {
                done?.Invoke(InterstitialShowResult.NotReady);
                return;
            }
            RunBeforeShow(() => PresentInterstitial(done), () => done?.Invoke(InterstitialShowResult.NotReady));
        }

        /// <summary>Number of presentations waiting for the <see cref="BeforeShow"/> hook.</summary>
        public int PendingBeforeShowCount => _pendingHooks.Count;

        void RunBeforeShow(Action next, Action abandon)
        {
            var hook = BeforeShow;
            if (hook == null)
            {
                next();
                return;
            }
            var pending = new PendingHook { DeadlineMs = _clock.MonotonicMs + BeforeShowTimeoutMs, Abandon = abandon };
            _pendingHooks.Add(pending);
            Action proceed = () =>
            {
                if (pending.Settled) return;
                pending.Settled = true;
                _pendingHooks.Remove(pending);
                next();
            };
            try
            {
                hook(proceed);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "BeforeShow hook threw.", e);
                proceed();
            }
        }

        void TickPendingHooks()
        {
            if (_pendingHooks.Count == 0) return;
            long now = _clock.MonotonicMs;
            for (int i = _pendingHooks.Count - 1; i >= 0; i--)
            {
                var pending = _pendingHooks[i];
                if (pending.Settled || now < pending.DeadlineMs) continue;
                pending.Settled = true;
                _pendingHooks.RemoveAt(i);
                _log.Warning(Tag, "BeforeShow (ATT) did not continue in time; the ad is skipped.");
                pending.Abandon?.Invoke();
            }
        }

        void PresentRewarded(Action<RewardedShowResult> done)
        {
            // The ad may have expired or another ad started while the ATT dialogs were up.
            if (!IsReady(AdFormat.Rewarded))
            {
                done?.Invoke(RewardedShowResult.NotReady);
                return;
            }
            ConsumeLoaded(_rewarded);
            IsShowing = true;
            _showingFormat = AdFormat.Rewarded;
            var session = new RewardedSession { Done = done };
            _rewardedSession = session;
            var callbacks = new AdShowCallbacks
            {
                Opened = () =>
                {
                    if (session.Finished || session.Opened) return;
                    session.Opened = true;
                    FullScreenChanged?.Invoke(true);
                },
                RewardEarned = () =>
                {
                    if (session.Finished) return;
                    session.Earned = true;
                    if (session.Closed) FinishRewarded(session, RewardedShowResult.Earned);
                },
                Closed = () =>
                {
                    if (session.Finished || session.Closed) return;
                    session.Closed = true;
                    if (session.Earned) FinishRewarded(session, RewardedShowResult.Earned);
                    else session.GraceDeadlineMs = _clock.MonotonicMs + RewardGraceMs;
                },
                FailedToShow = error =>
                {
                    if (session.Finished) return;
                    _log.Warning(Tag, "Rewarded ad failed to show: " + error);
                    FinishRewarded(session, RewardedShowResult.FailedToShow);
                },
            };
            try
            {
                _network.Show(AdFormat.Rewarded, callbacks);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Showing the rewarded ad threw.", e);
                FinishRewarded(session, RewardedShowResult.FailedToShow);
            }
        }

        void FinishRewarded(RewardedSession session, RewardedShowResult result)
        {
            if (session.Finished) return;
            session.Finished = true;
            if (_rewardedSession == session) _rewardedSession = null;
            IsShowing = false;
            if (session.Opened) FullScreenChanged?.Invoke(false);
            TryLoad(_rewarded, false);
            session.Done?.Invoke(result);
        }

        void PresentInterstitial(Action<InterstitialShowResult> done)
        {
            if (!IsReady(AdFormat.Interstitial))
            {
                done?.Invoke(InterstitialShowResult.NotReady);
                return;
            }
            ConsumeLoaded(_interstitial);
            IsShowing = true;
            _showingFormat = AdFormat.Interstitial;
            bool opened = false;
            bool finished = false;
            Action<InterstitialShowResult> finish = result =>
            {
                if (finished) return;
                finished = true;
                IsShowing = false;
                if (opened) FullScreenChanged?.Invoke(false);
                TryLoad(_interstitial, false);
                done?.Invoke(result);
            };
            var callbacks = new AdShowCallbacks
            {
                Opened = () =>
                {
                    if (finished || opened) return;
                    opened = true;
                    FullScreenChanged?.Invoke(true);
                },
                Closed = () => finish(InterstitialShowResult.Shown),
                FailedToShow = error =>
                {
                    _log.Warning(Tag, "Interstitial failed to show: " + error);
                    finish(InterstitialShowResult.FailedToShow);
                },
            };
            try
            {
                _network.Show(AdFormat.Interstitial, callbacks);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Showing the interstitial threw.", e);
                finish(InterstitialShowResult.FailedToShow);
            }
        }

        void ConsumeLoaded(Slot slot)
        {
            // The loaded ad is used up by this presentation; the next one is loaded when it closes.
            slot.State = AdLoadState.Idle;
            slot.Token++;
        }

        void TickSlot(Slot slot)
        {
            if (State != AdsServiceState.Ready) return;
            if (slot.State == AdLoadState.Loaded && (IsExpired(slot) || !_network.IsLoaded(slot.Format)) && !IsShowing)
            {
                _network.Discard(slot.Format);
                slot.State = AdLoadState.Idle;
                slot.Token++;
                TryLoad(slot, false);
                return;
            }
            if (slot.State == AdLoadState.Failed || slot.State == AdLoadState.Idle) TryLoad(slot, false);
        }

        void TryLoad(Slot slot, bool force)
        {
            if (State != AdsServiceState.Ready || !slot.Keep) return;
            if (slot.State == AdLoadState.Loading || slot.State == AdLoadState.Loaded) return;
            if (slot.State == AdLoadState.Failed && !force && _clock.MonotonicMs < slot.NextRetryAtMs) return;
            if (IsShowing && slot.Format == _showingFormat) return; // the next ad of this format loads after the close
            slot.State = AdLoadState.Loading;
            int token = ++slot.Token;
            try
            {
                _network.Load(slot.Format, result => OnLoaded(slot, token, result));
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Loading " + slot.Format + " threw.", e);
                OnLoaded(slot, token, AdNetworkLoadResult.Fail(-2, e.Message));
            }
        }

        void OnLoaded(Slot slot, int token, AdNetworkLoadResult result)
        {
            if (token != slot.Token) return; // discarded or superseded meanwhile
            long now = _clock.MonotonicMs;
            if (result.Success)
            {
                slot.State = AdLoadState.Loaded;
                slot.LoadedAtMs = now;
                slot.Failures = 0;
                slot.LastError = null;
            }
            else
            {
                slot.State = AdLoadState.Failed;
                slot.Failures++;
                slot.LastError = result.Error;
                slot.NextRetryAtMs = now + RetryDelayMs(slot.Failures);
                _log.Info(Tag, slot.Format + " load failed (" + result.Error + "), retry in " + RetryDelayMs(slot.Failures) + " ms.");
            }
            LoadFinished?.Invoke(slot.Format, result.Success);
        }

        /// <summary>2 s, 4 s, 8 s … capped at 60 s.</summary>
        public static long RetryDelayMs(int failures)
        {
            if (failures <= 1) return FirstRetryDelayMs;
            long delay = FirstRetryDelayMs;
            for (int i = 1; i < failures && delay < MaxRetryDelayMs; i++) delay *= 2;
            return Math.Min(delay, MaxRetryDelayMs);
        }

        bool IsExpired(Slot slot)
        {
            return _clock.MonotonicMs - slot.LoadedAtMs >= MaxAdAgeMs;
        }

        Slot SlotOf(AdFormat format)
        {
            return format == AdFormat.Rewarded ? _rewarded : _interstitial;
        }
    }
}
