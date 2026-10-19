#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
using System;

namespace Ballverse.Services
{
    /// <summary>
    /// Ads without an SDK for the editor and tests (spec §7.9: in the editor an ad is "shown" instantly). Behaviour is
    /// scriptable: fill, load delay, whether the reward is granted, whether presenting fails.
    /// </summary>
    public sealed class FakeAdNetwork : IAdNetwork, ITickable
    {
        sealed class Slot
        {
            public bool Loaded;
            public Action<AdNetworkLoadResult> PendingDone;
            public long CompleteAtMs;
        }

        readonly IClock _clock;
        readonly Slot _rewarded = new Slot();
        readonly Slot _interstitial = new Slot();
        Action<AdRevenue> _paid;

        public FakeAdNetwork(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool IsSdkAvailable => true;

        /// <summary>False simulates "no fill" for every load.</summary>
        public bool Fill { get; set; } = true;

        /// <summary>0 completes loads synchronously; otherwise in <see cref="Tick"/> after this many milliseconds.</summary>
        public long LoadDelayMs { get; set; }

        public bool InitializeSucceeds { get; set; } = true;

        /// <summary>Rewarded ads grant the reward before closing (false = the user "closed early").</summary>
        public bool GrantReward { get; set; } = true;

        /// <summary>Presenting fails (e.g. the ad expired).</summary>
        public bool FailToShow { get; set; }

        /// <summary>Holds the close of a shown ad until <see cref="CloseShownAd"/> (to test what happens while an ad is on screen).</summary>
        public bool ManualClose { get; set; }

        /// <summary>Delivers the reward callback after the close callback (an ordering some SDK versions produce).</summary>
        public bool RewardAfterClose { get; set; }

        public int LoadCalls { get; private set; }
        public int ShowCalls { get; private set; }
        public int DiscardCalls { get; private set; }

        AdShowCallbacks _openCallbacks;
        AdFormat _openFormat;

        public void Initialize(Action<bool> done)
        {
            done?.Invoke(InitializeSucceeds);
        }

        public void Load(AdFormat format, Action<AdNetworkLoadResult> done)
        {
            LoadCalls++;
            var slot = SlotOf(format);
            slot.Loaded = false;
            if (LoadDelayMs <= 0)
            {
                Complete(slot, done);
                return;
            }
            slot.PendingDone = done;
            slot.CompleteAtMs = _clock.MonotonicMs + LoadDelayMs;
        }

        public void Tick()
        {
            TickSlot(_rewarded);
            TickSlot(_interstitial);
        }

        public bool IsLoaded(AdFormat format)
        {
            return SlotOf(format).Loaded;
        }

        public void Show(AdFormat format, AdShowCallbacks callbacks)
        {
            ShowCalls++;
            var slot = SlotOf(format);
            if (!slot.Loaded || FailToShow)
            {
                slot.Loaded = false;
                callbacks?.FailedToShow?.Invoke(new AdErrorInfo(1, "fake: failed to show"));
                return;
            }
            slot.Loaded = false;
            callbacks?.Opened?.Invoke();
            _paid?.Invoke(new AdRevenue(format, 1000, "USD", 0));
            _openCallbacks = callbacks;
            _openFormat = format;
            if (!ManualClose) CloseShownAd();
        }

        /// <summary>Closes the ad currently "on screen".</summary>
        public void CloseShownAd()
        {
            var cb = _openCallbacks;
            _openCallbacks = null;
            if (cb == null) return;
            bool reward = _openFormat == AdFormat.Rewarded && GrantReward;
            if (reward && !RewardAfterClose) cb.RewardEarned?.Invoke();
            cb.Closed?.Invoke();
            if (reward && RewardAfterClose) cb.RewardEarned?.Invoke();
        }

        public void Discard(AdFormat format)
        {
            DiscardCalls++;
            var slot = SlotOf(format);
            slot.Loaded = false;
            slot.PendingDone = null;
        }

        public Action<AdRevenue> PaidHandler { set => _paid = value; }

        void TickSlot(Slot slot)
        {
            if (slot.PendingDone == null || _clock.MonotonicMs < slot.CompleteAtMs) return;
            var done = slot.PendingDone;
            slot.PendingDone = null;
            Complete(slot, done);
        }

        void Complete(Slot slot, Action<AdNetworkLoadResult> done)
        {
            if (Fill)
            {
                slot.Loaded = true;
                done?.Invoke(AdNetworkLoadResult.Ok);
            }
            else
            {
                done?.Invoke(AdNetworkLoadResult.Fail(3, "fake: no fill"));
            }
        }

        Slot SlotOf(AdFormat format)
        {
            return format == AdFormat.Rewarded ? _rewarded : _interstitial;
        }
    }
}
#endif
