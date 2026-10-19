using System;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Config;
using Ballverse.Core.Modes;
using Ballverse.Core.Studio;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>The real view models handed to <c>BallverseUIRoot.Create</c>.</summary>
    public sealed class BallverseViewModels : IBallverseViewModels
    {
        internal BallverseViewModels(GalleryViewModel gallery, SimulationViewModel simulation, StudioViewModel studio, ExportViewModel export,
            ProjectsViewModel projects, ShopViewModel shop, AppSettingsViewModel settings, TrackingConsentViewModel tracking,
            SystemAppearanceViewModel appearance, QuestCelebrationViewModel quest)
        {
            GalleryModel = gallery;
            SimulationModel = simulation;
            StudioModel = studio;
            ExportModel = export;
            ProjectsModel = projects;
            ShopModel = shop;
            SettingsModel = settings;
            TrackingModel = tracking;
            AppearanceModel = appearance;
            QuestModel = quest;
        }

        public GalleryViewModel GalleryModel { get; }
        public SimulationViewModel SimulationModel { get; }
        public StudioViewModel StudioModel { get; }
        public ExportViewModel ExportModel { get; }
        public ProjectsViewModel ProjectsModel { get; }
        public ShopViewModel ShopModel { get; }
        public AppSettingsViewModel SettingsModel { get; }
        public TrackingConsentViewModel TrackingModel { get; }
        public SystemAppearanceViewModel AppearanceModel { get; }
        public QuestCelebrationViewModel QuestModel { get; }

        public IGalleryViewModel Gallery => GalleryModel;
        public ISimulationViewModel Simulation => SimulationModel;
        public IStudioViewModel Studio => StudioModel;
        public IExportViewModel Export => ExportModel;
        public IProjectsViewModel Projects => ProjectsModel;
        public IShopViewModel Shop => ShopModel;
        public IAppSettingsViewModel Settings => SettingsModel;
        public ITrackingConsentViewModel TrackingConsent => TrackingModel;
        public ISystemAppearance SystemAppearance => AppearanceModel;
        public IQuestCelebrationViewModel QuestCelebration => QuestModel;
    }

    /// <summary>Inputs of <see cref="AppComposition"/> (the modules resolve them from the service registry and the environment).</summary>
    public sealed class AppCompositionOptions
    {
        public BallverseServices Services;
        public IMediaBridge Media;
        public IStudioBackend Studio;
        public RewardedGateBridge Gate;
        public ModeRegistry Modes;
        public IAppLifecycle Lifecycle;
        public IScreenAwake ScreenAwake;
        public IFrameRatePolicy FrameRate;
        public IAppPaths Paths;
        public IDeviceInfo Device;
        public IPlatformActions Platform;
        public ITextureLoader Textures;
        public IAppClock Clock;
        public ISimSurface Surface;
        public IGalleryPreviews Previews;
        public IHapticsDevice Haptics;
        /// <summary>Plays the live audio engine (device: <c>LiveAudioOutput</c>); null = silent (tests).</summary>
        public Func<LiveAudioEngine, IDisposable> CreateAudioOutput;
        public Action<string, Exception> ReportError;
    }

    /// <summary>
    /// Builds the release-1 experience on top of the registered services (spec §7.1 Ballverse.App): Remote Config view, device
    /// tier, settings and presets, the live simulation runtime (controller, audio, haptics, view), every view model, the
    /// interstitial breaks, the ATT pre-prompt bridge and the rewarded export gate; then drives them every frame
    /// (<see cref="Tick"/> / <see cref="LateTick"/>) and reacts to pause, resume and low memory. Engine-free: EditMode tests
    /// build it with fakes. Main thread.
    /// </summary>
    public sealed class AppComposition : IDisposable
    {
        const float SettingsSaveInterval = 2f;
        /// <summary>How often the power saver is re-read while the app is in the foreground (tier and haptics follow it).</summary>
        const float PowerPollInterval = 10f;

        readonly AppCompositionOptions _o;
        readonly IDisposable _audioOutput;
        float _sinceSave;
        float _sincePowerPoll;
        bool _disposed;

        public AppComposition(AppCompositionOptions options)
        {
            _o = options ?? throw new ArgumentNullException(nameof(options));
            Action<string, Exception> report = options.ReportError ?? ((c, e) => { });
            var notices = new AppNotices(report);
            var main = new AppWorkQueue(report);
            var remote = new RemoteSettings(options.Modes, SafeRemote(options.Services), report);
            var settings = new AppSettingsStore(options.Paths.Settings, report);
            var presets = new UserPresetStore(options.Paths.Presets, report);
            Context = new AppContext(options.Services, options.Media, options.Studio, options.Modes, remote, settings, presets, notices, main,
                options.Clock ?? new StopwatchClock(), options.Device, options.Platform, options.Textures, options.Lifecycle, options.ScreenAwake,
                options.FrameRate, options.Paths);
            AppContext ctx = Context;
            notices.Text = new AppText(() => LanguageResolver.Resolve(settings.Data.Language, SafeLanguageTag(options.Device)));

            ctx.DetectedTier = DeviceTierDetector.Detect(options.Device, remote.Values);
            ctx.RefreshTier();

            // Live simulation runtime.
            ctx.Audio = new LiveAudioEngine();
            ctx.Haptics = new LiveHaptics(options.Haptics, report) { SettingEnabled = settings.Data.HapticsEnabled, PowerSaving = ctx.LowPowerMode };
            ctx.Live = new LiveSimController(new LiveSimOptions
            {
                Tier = ctx.Tier,
                AppVersion = SafeVersion(options.Device),
                FrameHistory = ctx.Tier == DeviceTier.C ? 120 : 240,
            }, ctx.Audio, ctx.Haptics);
            UpdateArenaMetrics();
            ctx.Surface = options.Surface;
            ctx.Surface?.Bind(ctx.Live);
            ctx.Surface?.SetTier(ctx.Tier, settings.Data.Quality);
            ctx.Previews = options.Previews;
            ctx.Ads = new AdBreaks(options.Services, notices, () => ctx.Ui);
            try
            {
                _audioOutput = options.CreateAudioOutput?.Invoke(ctx.Audio);
            }
            catch (Exception e)
            {
                notices.Report("audio.output", e);
            }

            // View models.
            var simulation = new SimulationViewModel(ctx);
            var studio = new StudioViewModel(ctx);
            var export = new ExportViewModel(ctx, studio);
            var gallery = new GalleryViewModel(ctx, simulation);
            var projects = new ProjectsViewModel(ctx, studio, simulation);
            var shop = new ShopViewModel(ctx);
            var appSettings = new AppSettingsViewModel(ctx, shop);
            var tracking = new TrackingConsentViewModel(() => ctx.Ui);
            var appearance = new SystemAppearanceViewModel(ctx);
            var quest = new QuestCelebrationViewModel(ctx, simulation);
            simulation.Studio = studio;
            studio.Simulation = simulation;
            ViewModels = new BallverseViewModels(gallery, simulation, studio, export, projects, shop, appSettings, tracking, appearance, quest);
            options.Gate?.Attach(export);

            simulation.LibraryChanged += projects.Invalidate;
            studio.LibraryChanged += projects.Invalidate;
            export.Exported += _ =>
            {
                projects.Invalidate();
                quest.Refresh();
                simulation.NotifyChanged();
            };
            appSettings.QualityChanged += OnQualityChanged;
            appSettings.LanguageChanged += simulation.PresetsModel.Rebuild;
            appSettings.ApplyMotion();

            try
            {
                options.Services.Consent.AttPrePrompt = tracking;
            }
            catch (Exception e)
            {
                notices.Report("consent.att_preprompt", e);
            }
            try
            {
                options.Services.RemoteConfig.Updated += OnRemoteConfig;
            }
            catch (Exception e)
            {
                notices.Report("remote.subscribe", e);
            }
            ctx.TierChanged += OnTierChanged;
            options.Lifecycle.Paused += OnPaused;
            options.Lifecycle.Resumed += OnResumed;
            options.Lifecycle.LowMemory += OnLowMemory;
            options.Lifecycle.Quitting += SaveAll;
        }

        public AppContext Context { get; }

        public BallverseViewModels ViewModels { get; }

        /// <summary>
        /// Connects the UI shell: toasts, device tier, Android back on the Modes root, and the first-run flow (spec §4.3: the
        /// first cold start goes straight to Ring Escape with its classic preset and the hints; later starts open the gallery).
        /// </summary>
        public void AttachUi(IAppUi ui)
        {
            AppContext ctx = Context;
            ctx.Ui = ui;
            if (ui == null) return;
            ctx.Notices.AttachUi(ui.ShowToast);
            ui.SetDeviceTier(ctx.Tier);
            if (ctx.Studio is UnavailableStudioBackend) ctx.Notices.Toast(AppStrings.StudioUnavailable);
            ui.ExitRequested += () =>
            {
                try
                {
                    ctx.Platform.MoveTaskToBack();
                }
                catch (Exception e)
                {
                    ctx.Notices.Report("platform.move_task_to_back", e);
                }
            };
            if (!ctx.Settings.Data.FirstRunDone)
            {
                string first = FirstRunMode();
                ctx.Settings.Update(d => d.FirstRunDone = true);
                ctx.Settings.Save();
                if (first != null) ui.OpenMode(first);
            }
        }

        /// <summary>Ring Escape when it is available, else the first gallery mode.</summary>
        public string FirstRunMode()
        {
            const string preferred = "ring_escape";
            if (Context.Remote.IsAvailable(preferred, out _)) return preferred;
            var modes = Context.Remote.GalleryModes();
            return modes.Count > 0 ? modes[0].Info.Id : null;
        }

        /// <summary>After the first frame: offer to continue an interrupted export (spec §3.8 RW4).</summary>
        public void StartDeferred()
        {
            ViewModels.ExportModel.CheckResumable();
        }

        /// <summary>Per frame, before the UI update: posted work, live simulation, previews, studio polling, autosave.</summary>
        public void Tick(float deltaSeconds)
        {
            if (_disposed) return;
            Context.Main.Drain();
            ViewModels.SimulationModel.Tick(deltaSeconds);
            ViewModels.GalleryModel.Tick(deltaSeconds);
            ViewModels.StudioModel.Tick(deltaSeconds);
            ViewModels.ProjectsModel.Tick(deltaSeconds);
            _sincePowerPoll += deltaSeconds;
            if (_sincePowerPoll >= PowerPollInterval)
            {
                _sincePowerPoll = 0f;
                Context.RefreshTier();
            }
            _sinceSave += deltaSeconds;
            if (_sinceSave >= SettingsSaveInterval)
            {
                _sinceSave = 0f;
                if (Context.Settings.IsDirty) Context.Settings.Save();
            }
        }

        /// <summary>Per frame, after the UI update: draws the live simulation.</summary>
        public void LateTick(float deltaSeconds)
        {
            if (_disposed) return;
            ViewModels.SimulationModel.LateTick(deltaSeconds);
        }

        void OnRemoteConfig(RemoteConfigValues values)
        {
            Context.Remote.Apply(values);
            Context.DetectedTier = DeviceTierDetector.Detect(Context.Device, values);
            Context.RefreshTier();
            ViewModels.SimulationModel.PresetsModel.Rebuild();
        }

        void OnQualityChanged()
        {
            Context.Surface?.SetTier(Context.Tier, Context.Settings.Data.Quality);
        }

        void OnTierChanged(DeviceTier tier)
        {
            AppContext ctx = Context;
            ctx.Live.Options.Tier = tier;
            ctx.Surface?.SetTier(tier, ctx.Settings.Data.Quality);
            try
            {
                ctx.Studio.Tier = tier;
            }
            catch (Exception e)
            {
                ctx.Notices.Report("studio.tier", e);
            }
            ctx.Ui?.SetDeviceTier(tier);
        }

        void OnPaused()
        {
            SaveAll();
        }

        /// <summary>The arena spans the screen width on the simulation screen: finger speed in points for the mode's input mapper.</summary>
        void UpdateArenaMetrics()
        {
            float points = 0f;
            try
            {
                points = _o.Device != null ? _o.Device.ScreenWidthPoints : 0f;
            }
            catch (Exception e)
            {
                Context.Notices.Report("device.screen_points", e);
            }
            Context.Live.PointsPerArenaWidth = points > 100f && points < 2000f ? points : 390f;
        }

        void OnResumed()
        {
            UpdateArenaMetrics();
            Context.RefreshTier();
            ViewModels.SettingsModel.ApplyMotion();
        }

        void OnLowMemory()
        {
            AppContext ctx = Context;
            try
            {
                ctx.Previews?.ReleaseTextures();
                ViewModels.SimulationModel.ReleaseThumbnails();
                ViewModels.ProjectsModel.ReleaseCovers();
                LiveSoundSetup.TrimBanks(ctx.Audio.PackId);
                if (!ctx.Studio.IsExporting) ctx.Studio.ClearCaches();
                GC.Collect();
            }
            catch (Exception e)
            {
                ctx.Notices.Report("low_memory", e);
            }
        }

        void SaveAll()
        {
            Context.Settings.Save();
            Context.Presets.Save();
        }

        static RemoteConfigValues SafeRemote(BallverseServices services)
        {
            try
            {
                return services.RemoteConfig.Current ?? RemoteConfigValues.Defaults;
            }
            catch (Exception)
            {
                return RemoteConfigValues.Defaults;
            }
        }

        static string SafeLanguageTag(IDeviceInfo device)
        {
            try
            {
                return device?.LanguageTag;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string SafeVersion(IDeviceInfo device)
        {
            string v = device?.AppVersion ?? "";
            var chars = new System.Text.StringBuilder(v.Length);
            foreach (char c in v)
            {
                if (c >= 0x20 && c <= 0x7E) chars.Append(c);
                if (chars.Length == 64) break;
            }
            return chars.ToString();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SaveAll();
            AppContext ctx = Context;
            ctx.Lifecycle.Paused -= OnPaused;
            ctx.Lifecycle.Resumed -= OnResumed;
            ctx.Lifecycle.LowMemory -= OnLowMemory;
            ctx.Lifecycle.Quitting -= SaveAll;
            try
            {
                ctx.Services.RemoteConfig.Updated -= OnRemoteConfig;
            }
            catch (Exception)
            {
                // services already torn down
            }
            ViewModels.SimulationModel.CancelGeneration();
            ViewModels.SimulationModel.CancelSetupHint();
            ctx.Ads?.Detach();
            ViewModels.StudioModel.Close();
            ctx.Live.Dispose();
            _audioOutput?.Dispose();
            ctx.Surface?.Dispose();
            ctx.Previews?.Dispose();
        }
    }
}
