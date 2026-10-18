using System;

namespace Ballverse.Services
{
    /// <summary>
    /// Rewarded and interstitial ads (spec §5.3). Starts only after consent (spec §7.9, <see cref="IConsentService"/>); keeps
    /// the formats loaded that the policies ask for; never delays the user when an ad is missing (Р14, IN4).
    /// Which ad to show and when is decided by <see cref="RewardedExportCoordinator"/> and <see cref="InterstitialCoordinator"/>
    /// over the Core policies. Main thread only.
    /// </summary>
    public interface IAdsService
    {
        AdsServiceState State { get; }

        /// <summary>Starts the SDK. Call only once consent allows ads (the start-up sequence does this).</summary>
        void Initialize();

        /// <summary>
        /// Keep an ad of <paramref name="format"/> loaded (reloading after shows and failures with backoff), or release it.
        /// Before the SDK is ready the wish is remembered.
        /// </summary>
        void SetKeepLoaded(AdFormat format, bool keep);

        bool IsKeepLoaded(AdFormat format);

        /// <summary>Loads now, skipping a pending backoff (the user is waiting, RW3).</summary>
        void LoadNow(AdFormat format);

        bool IsReady(AdFormat format);

        AdLoadState GetLoadState(AdFormat format);

        /// <summary>Error of the most recent failed load of <paramref name="format"/>.</summary>
        AdErrorInfo? LastLoadError(AdFormat format);

        /// <summary>True while a full-screen ad is on screen.</summary>
        bool IsShowing { get; }

        /// <summary>Shows the rewarded ad; the result arrives after the ad has closed (RW6).</summary>
        void ShowRewarded(Action<RewardedShowResult> done);

        void ShowInterstitial(Action<InterstitialShowResult> done);

        /// <summary>Runs before every presentation (the ATT decision, spec §5.6); call the argument to continue.</summary>
        Action<Action> BeforeShow { get; set; }

        /// <summary>
        /// <see cref="BeforeShow"/> did not continue in time and the presentation was given up (Р14): whoever runs the hook
        /// should end it, so its screens close and a late answer does nothing.
        /// </summary>
        event Action BeforeShowAbandoned;

        /// <summary>A full-screen ad opened (true) or closed (false): mute app audio (RW6).</summary>
        event Action<bool> FullScreenChanged;

        /// <summary>Impression-level revenue (spec §9.1).</summary>
        event Action<AdRevenue> Paid;

        /// <summary>A load attempt finished (true = success) — lets waiting flows react without polling.</summary>
        event Action<AdFormat, bool> LoadFinished;
    }
}
