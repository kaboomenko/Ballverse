using System;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;

namespace Ballverse.App
{
    /// <summary>
    /// Services (spec §7.9): <see cref="Register"/> builds them through the environment (Services installer: crash reporter,
    /// cached rights from the signed cache and ad policy state on frame 0) and publishes <see cref="BallverseServices"/>;
    /// <see cref="Start"/> attaches the per-frame runner, which phases Remote Config → IAP → UMP consent → ads (≥ 3 s after
    /// the first frame, after consent) itself; ATT waits for the first ad.
    /// </summary>
    public sealed class ServicesModule : IAppModule
    {
        readonly AppEnvironment _env;
        BallverseServices _services;
        IDisposable _runner;

        public ServicesModule(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "Services";
        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            _services = (_env.CreateServices ?? throw new InvalidOperationException("No services factory"))();
            services.Register(_services);
        }

        public void Start(AppModuleContext context)
        {
            _runner = _env.RunServices?.Invoke(_services);
        }

        public void Shutdown()
        {
            _runner?.Dispose();
            _runner = null;
        }
    }

    /// <summary>Media bridge (spec §7.5): device codecs, gallery picker / save, share sheet, device signals.</summary>
    public sealed class MediaModule : IAppModule
    {
        readonly AppEnvironment _env;
        IMediaBridge _media;

        public MediaModule(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "Media";
        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            _media = (_env.CreateMedia ?? throw new InvalidOperationException("No media factory"))();
            services.Register(_media);
        }

        public void Start(AppModuleContext context)
        {
        }

        public void Shutdown()
        {
            _media?.Dispose();
            _media = null;
        }
    }

    /// <summary>The release's mode definitions (Core <c>ReleaseModes</c>; M02 / M05 join by registration only, spec §7.6).</summary>
    public sealed class ModesModule : IAppModule
    {
        readonly AppEnvironment _env;

        public ModesModule(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "Modes";
        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            services.Register((_env.CreateModes ?? ReleaseModes.CreateRegistry)());
        }

        public void Start(AppModuleContext context)
        {
        }

        public void Shutdown()
        {
        }
    }

    /// <summary>
    /// Studio runtime (spec §3, §7.5): storage, recent runs and clips, projects with autosave, preview, MP4 export with resume.
    /// The rewarded gate bridge is published at registration; the runtime is created at start from the media bridge and the
    /// platform services. When it cannot start (storage error, full disk) the failure is reported and an
    /// <see cref="UnavailableStudioBackend"/> takes its place, so the rest of the app still comes up (spec §7.12).
    /// </summary>
    public sealed class StudioModule : IAppModule
    {
        readonly AppEnvironment _env;
        StudioRuntime _runtime;

        public StudioModule(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "Studio";
        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            _runtime = new StudioRuntime(new RewardedGateBridge());
            services.Register(_runtime);
        }

        public void Start(AppModuleContext context)
        {
            AppServices s = context.Services;
            try
            {
                if (_env.CreateStudio == null) throw new InvalidOperationException("No studio factory");
                _runtime.Backend = _env.CreateStudio(new StudioEnvironment
                {
                    Paths = s.Get<IAppPaths>(),
                    Media = s.Get<IMediaBridge>(),
                    Lifecycle = s.Get<IAppLifecycle>(),
                    ScreenAwake = s.Get<IScreenAwake>(),
                    Gate = _runtime.Gate,
                    Modes = s.Get<ModeRegistry>(),
                    Tier = DeviceTierDetector.Detect(_env.Device, s.Get<BallverseServices>().RemoteConfig.Current),
                    ReportError = context.ReportError,
                }) ?? throw new InvalidOperationException("The studio factory returned no backend");
            }
            catch (Exception e)
            {
                // Degraded: the simulation, gallery, shop and settings keep working without the studio (spec §7.12).
                context.ReportError("Studio: start", e);
                _runtime.Backend = new UnavailableStudioBackend(e);
            }
        }

        public void Shutdown()
        {
            _runtime?.Backend?.Dispose();
            if (_runtime != null) _runtime.Backend = null;
        }
    }

    /// <summary>
    /// The experience (spec §4): the composition (view models over Services / Studio / Sim), the UI shell bound to the real view
    /// models, the first-run flow and the per-frame loop. Starts before the first frame so the first picture is the app.
    /// </summary>
    public sealed class ExperienceModule : IAppModule
    {
        readonly AppEnvironment _env;
        readonly AppExperience _experience = new AppExperience();
        IDisposable _loop;

        public ExperienceModule(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
        }

        public string Name => "Experience";
        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            services.Register(_experience);
        }

        public void Start(AppModuleContext context)
        {
            AppServices s = context.Services;
            StudioRuntime studio = s.Get<StudioRuntime>();
            if (studio.Backend == null) studio.Backend = new UnavailableStudioBackend(new InvalidOperationException("The studio runtime did not start"));
            var composition = new AppComposition(new AppCompositionOptions
            {
                Services = s.Get<BallverseServices>(),
                Media = s.Get<IMediaBridge>(),
                Studio = studio.Backend,
                Gate = studio.Gate,
                Modes = s.Get<ModeRegistry>(),
                Lifecycle = s.Get<IAppLifecycle>(),
                ScreenAwake = s.Get<IScreenAwake>(),
                FrameRate = s.Get<IFrameRatePolicy>(),
                Paths = s.Get<IAppPaths>(),
                Device = _env.Device,
                Platform = _env.Platform,
                Textures = _env.Textures,
                Clock = _env.Clock,
                Surface = _env.CreateSurface?.Invoke(),
                Previews = _env.CreatePreviews?.Invoke(),
                Haptics = _env.Haptics,
                CreateAudioOutput = _env.CreateAudioOutput,
                ReportError = context.ReportError,
            });
            _experience.Composition = composition;
            IAppUi ui = null;
            try
            {
                ui = _env.CreateUi?.Invoke(composition.ViewModels);
            }
            catch (Exception e)
            {
                context.ReportError("Experience: UI shell", e);
            }
            _experience.Ui = ui;
            composition.AttachUi(ui);
            _loop = _env.RunLoop?.Invoke(composition.Tick, composition.LateTick);
        }

        public void Shutdown()
        {
            _loop?.Dispose();
            _loop = null;
            _experience.Ui?.Dispose();
            _experience.Ui = null;
            _experience.Composition?.Dispose();
            _experience.Composition = null;
            _env.Shutdown?.Invoke();
        }
    }

    /// <summary>After the first frame: the "Продолжить экспорт" offer for an export the system interrupted (spec §3.8 RW4).</summary>
    public sealed class DeferredStartModule : IAppModule
    {
        public string Name => "DeferredStart";
        public AppStartPhase Phase => AppStartPhase.AfterFirstFrame;

        public void Register(AppServices services)
        {
        }

        public void Start(AppModuleContext context)
        {
            AppExperience experience = context.Services.Get<AppExperience>();
            experience.Composition?.StartDeferred();
        }

        public void Shutdown()
        {
        }
    }
}
