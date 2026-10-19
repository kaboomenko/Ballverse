using System;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Modes;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>
    /// Shared state of the composed app that the view models work with: the services, media bridge, studio runtime, mode
    /// registry, Remote Config view, persisted settings, the live simulation runtime and the platform ports. Built once by
    /// <see cref="AppComposition"/> on the main thread.
    /// </summary>
    public sealed class AppContext
    {
        DeviceTier _tier = DeviceTier.B;

        public AppContext(BallverseServices services, IMediaBridge media, IStudioBackend studio, ModeRegistry modes,
            RemoteSettings remote, AppSettingsStore settings, UserPresetStore presets, AppNotices notices, AppWorkQueue main,
            IAppClock clock, IDeviceInfo device, IPlatformActions platform, ITextureLoader textures, IAppLifecycle lifecycle,
            IScreenAwake screenAwake, IFrameRatePolicy frameRate, IAppPaths paths)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Media = media ?? throw new ArgumentNullException(nameof(media));
            Studio = studio ?? throw new ArgumentNullException(nameof(studio));
            Modes = modes ?? throw new ArgumentNullException(nameof(modes));
            Remote = remote ?? throw new ArgumentNullException(nameof(remote));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Presets = presets ?? throw new ArgumentNullException(nameof(presets));
            Notices = notices ?? throw new ArgumentNullException(nameof(notices));
            Main = main ?? throw new ArgumentNullException(nameof(main));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Platform = platform ?? throw new ArgumentNullException(nameof(platform));
            Textures = textures ?? throw new ArgumentNullException(nameof(textures));
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            ScreenAwake = screenAwake ?? throw new ArgumentNullException(nameof(screenAwake));
            FrameRate = frameRate ?? throw new ArgumentNullException(nameof(frameRate));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public BallverseServices Services { get; }
        public IMediaBridge Media { get; }
        public IStudioBackend Studio { get; }
        public ModeRegistry Modes { get; }
        public RemoteSettings Remote { get; }
        public AppSettingsStore Settings { get; }
        public UserPresetStore Presets { get; }
        public AppNotices Notices { get; }
        public AppWorkQueue Main { get; }
        public IAppClock Clock { get; }
        public IDeviceInfo Device { get; }
        public IPlatformActions Platform { get; }
        public ITextureLoader Textures { get; }
        public IAppLifecycle Lifecycle { get; }
        public IScreenAwake ScreenAwake { get; }
        public IFrameRatePolicy FrameRate { get; }
        public IAppPaths Paths { get; }

        public LiveSimController Live { get; set; }
        public LiveAudioEngine Audio { get; set; }
        public LiveHaptics Haptics { get; set; }
        public ISimSurface Surface { get; set; }
        public IGalleryPreviews Previews { get; set; }
        public AdBreaks Ads { get; set; }

        /// <summary>The UI shell (null until the UI module started, and in headless tests that do not need it).</summary>
        public IAppUi Ui { get; set; }

        /// <summary>Tier detected from the device (spec §8.1).</summary>
        public DeviceTier DetectedTier { get; set; } = DeviceTier.B;

        /// <summary>Tier in force: detected, adjusted by the quality setting and power saving.</summary>
        public DeviceTier Tier
        {
            get => _tier;
            set
            {
                if (_tier == value) return;
                _tier = value;
                TierChanged?.Invoke(value);
            }
        }

        public event Action<DeviceTier> TierChanged;

        /// <summary>The app is in the background.</summary>
        public bool AppPaused => Lifecycle.IsPaused;

        /// <summary>The system power saver (Low Power Mode / Battery Saver) as of the last <see cref="RefreshTier"/>.</summary>
        public bool LowPowerMode { get; private set; }

        bool _signalsErrorReported;

        /// <summary>
        /// Recomputes <see cref="Tier"/> from the detected tier, the quality setting and the power saver, and switches haptics
        /// off while the power saver is on (spec §2.7 "Тактильный отклик … отключается … при включённом системном режиме
        /// экономии энергии").
        /// </summary>
        public void RefreshTier()
        {
            bool lowPower = false;
            try
            {
                lowPower = Media.Signals != null && Media.Signals.LowPowerMode;
            }
            catch (Exception e) when (e is InvalidOperationException || e is MediaException)
            {
                if (!_signalsErrorReported) Notices.Report("device.signals", e);
                _signalsErrorReported = true;
            }
            LowPowerMode = lowPower;
            if (Haptics != null) Haptics.PowerSaving = lowPower;
            Tier = DeviceTierDetector.Effective(DetectedTier, Settings.Data.Quality, lowPower);
        }
    }

    /// <summary>
    /// Interstitials at natural breaks (spec §5.3.2 IN1–IN8): Retry, Generate, Back from the result, to the gallery, "В видео".
    /// Asks the services' coordinator; on Show the 0.5 s «Реклама» plaque comes first, then the ad, then the action continues.
    /// Every other outcome continues at once (an ad never delays the user, IN4). Finished runs are reported through
    /// <see cref="OnRunEnded"/> (the coordinator counts <c>GameCompleted</c>).
    /// </summary>
    public sealed class AdBreaks
    {
        readonly BallverseServices _services;
        readonly AppNotices _notices;
        readonly Func<IAppUi> _ui;

        int _pendingBreaks;
        bool _fullScreen;

        public AdBreaks(BallverseServices services, AppNotices notices, Func<IAppUi> ui)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _notices = notices ?? throw new ArgumentNullException(nameof(notices));
            _ui = ui ?? (() => null);
            try
            {
                _services.Ads.FullScreenChanged += OnFullScreenChanged;
            }
            catch (Exception e)
            {
                _notices.Report("ads.subscribe_full_screen", e);
            }
        }

        /// <summary>
        /// An interstitial break is under way (the «Реклама» plaque or the ad, until its continuation runs) or a full-screen ad
        /// (interstitial or rewarded) is on screen. Unity pauses the app while a full-screen ad shows (Android always; iOS through
        /// <c>SetiOSAppPauseOnBackground(true)</c>), so the app pause that follows is the ad covering the screen, not the user
        /// leaving it: a pending 🎲 must survive it (spec §2.4 cancels only when the user leaves the screen).
        /// </summary>
        public bool AdCoversScreen => _pendingBreaks > 0 || _fullScreen;

        void OnFullScreenChanged(bool showing) => _fullScreen = showing;

        /// <summary>Unsubscribes from the services (composition shutdown).</summary>
        public void Detach()
        {
            try
            {
                _services.Ads.FullScreenChanged -= OnFullScreenChanged;
            }
            catch (Exception e)
            {
                _notices.Report("ads.unsubscribe_full_screen", e);
            }
        }

        /// <summary>The user is in the studio (IN3).</summary>
        public bool InStudio { get; set; }

        /// <summary>An export runs or its result is still on screen (IN3).</summary>
        public bool Exporting { get; set; }

        /// <summary>A purchase sheet is open (IN3).</summary>
        public bool InPurchaseFlow { get; set; }

        /// <summary>Every evaluated break and its decision (diagnostics, tests).</summary>
        public event Action<NaturalBreakKind, InterstitialDecision> Evaluated;

        /// <summary>An interstitial finished (true = it was shown).</summary>
        public event Action<bool> InterstitialDone;

        public void OnRunEnded(GameRunSummary run)
        {
            try
            {
                _services.Interstitials.OnGameCompleted(run);
            }
            catch (Exception e)
            {
                _notices.Report("ads.game_completed", e);
            }
        }

        /// <summary>Evaluates the break and runs <paramref name="then"/> after the interstitial (if any) or at once.</summary>
        public void At(NaturalBreakKind kind, Action then)
        {
            InterstitialDecision decision;
            try
            {
                decision = _services.Interstitials.EvaluateNaturalBreak(kind, InStudio, Exporting, InPurchaseFlow);
            }
            catch (Exception e)
            {
                _notices.Report("ads.natural_break", e);
                then?.Invoke();
                return;
            }
            Evaluated?.Invoke(kind, decision);
            if (!decision.IsShow)
            {
                then?.Invoke();
                return;
            }
            IAppUi ui = _ui();
            _pendingBreaks++;
            bool finished = false;
            Action done = () =>
            {
                if (finished) return;
                finished = true;
                _pendingBreaks = Math.Max(0, _pendingBreaks - 1);
            };
            Action show = () =>
            {
                try
                {
                    _services.Interstitials.ShowInterstitial(shown =>
                    {
                        done();
                        InterstitialDone?.Invoke(shown);
                        then?.Invoke();
                    });
                }
                catch (Exception e)
                {
                    done();
                    _notices.Report("ads.show_interstitial", e);
                    then?.Invoke();
                }
            };
            if (ui != null) ui.ShowAdNotice(show);
            else show();
        }
    }
}
