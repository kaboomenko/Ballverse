using System;

namespace Ballverse.Services
{
    /// <summary>Callbacks of one full-screen ad presentation. All are invoked on the main thread.</summary>
    public sealed class AdShowCallbacks
    {
        public Action Opened;
        public Action RewardEarned;
        public Action Closed;
        public Action<AdErrorInfo> FailedToShow;
    }

    public readonly struct AdNetworkLoadResult
    {
        public AdNetworkLoadResult(bool success, AdErrorInfo error)
        {
            Success = success;
            Error = error;
        }

        public bool Success { get; }
        public AdErrorInfo Error { get; }

        public static AdNetworkLoadResult Ok => new AdNetworkLoadResult(true, default);

        public static AdNetworkLoadResult Fail(int code, string message)
        {
            return new AdNetworkLoadResult(false, new AdErrorInfo(code, message));
        }
    }

    /// <summary>
    /// Thin SDK adapter under <see cref="AdsService"/>: one slot per format, no policy. Implementations deliver every callback
    /// on the main thread (through <see cref="MainThreadQueue"/>), possibly synchronously inside the call.
    /// </summary>
    public interface IAdNetwork
    {
        /// <summary>False when the build has no ads SDK.</summary>
        bool IsSdkAvailable { get; }

        void Initialize(Action<bool> done);

        void Load(AdFormat format, Action<AdNetworkLoadResult> done);

        bool IsLoaded(AdFormat format);

        /// <summary>Presents the loaded ad. If none is loaded, <see cref="AdShowCallbacks.FailedToShow"/> is invoked.</summary>
        void Show(AdFormat format, AdShowCallbacks callbacks);

        /// <summary>Destroys the loaded ad of <paramref name="format"/>, if any.</summary>
        void Discard(AdFormat format);

        /// <summary>Receives impression-level revenue (may arrive after the ad closed).</summary>
        Action<AdRevenue> PaidHandler { set; }
    }

    /// <summary>Builds without an ads SDK: nothing ever loads.</summary>
    public sealed class UnavailableAdNetwork : IAdNetwork
    {
        public bool IsSdkAvailable => false;

        public void Initialize(Action<bool> done)
        {
            done?.Invoke(false);
        }

        public void Load(AdFormat format, Action<AdNetworkLoadResult> done)
        {
            done?.Invoke(AdNetworkLoadResult.Fail(-1, "no ads SDK"));
        }

        public bool IsLoaded(AdFormat format)
        {
            return false;
        }

        public void Show(AdFormat format, AdShowCallbacks callbacks)
        {
            callbacks?.FailedToShow?.Invoke(new AdErrorInfo(-1, "no ads SDK"));
        }

        public void Discard(AdFormat format)
        {
        }

        public Action<AdRevenue> PaidHandler { set { } }
    }
}
