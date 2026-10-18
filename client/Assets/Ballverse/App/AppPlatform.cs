using System;
using Ballverse.Platform;

namespace Ballverse.App
{
    /// <summary>
    /// Engine-facing services owned by <see cref="Bootstrap"/> (it feeds them from Unity callbacks) and published to
    /// modules by <see cref="PlatformModule"/>. Tests build one from fakes with the public constructor.
    /// </summary>
    public sealed class AppPlatform
    {
        public AppPlatform(AppLifecycle lifecycle, FrameRatePolicy frameRate, ScreenAwake screenAwake,
            SafeAreaProvider safeArea, AppPaths paths)
        {
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            FrameRate = frameRate ?? throw new ArgumentNullException(nameof(frameRate));
            ScreenAwake = screenAwake ?? throw new ArgumentNullException(nameof(screenAwake));
            SafeArea = safeArea ?? throw new ArgumentNullException(nameof(safeArea));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public AppLifecycle Lifecycle { get; }
        public FrameRatePolicy FrameRate { get; }
        public ScreenAwake ScreenAwake { get; }
        public SafeAreaProvider SafeArea { get; }
        public AppPaths Paths { get; }

        /// <summary>Platform services wired to the Unity engine (device and Play Mode).</summary>
        public static AppPlatform CreateForDevice(Action<string, Exception> reportError) =>
            new AppPlatform(
                new AppLifecycle(reportError),
                new FrameRatePolicy(FrameRatePolicy.UnityApply),
                new ScreenAwake(ScreenAwake.UnityApply),
                new SafeAreaProvider(reportError),
                AppPaths.ForDevice());
    }

    /// <summary>
    /// First module in the catalog: publishes the platform services, applies the frame-rate policy and creates the
    /// standard storage folders before any feature module starts.
    /// </summary>
    public sealed class PlatformModule : IAppModule
    {
        readonly AppPlatform _platform;

        public PlatformModule(AppPlatform platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public string Name => "Platform";

        public AppStartPhase Phase => AppStartPhase.Immediate;

        public void Register(AppServices services)
        {
            services.Register<IAppLifecycle>(_platform.Lifecycle);
            services.Register<IFrameRatePolicy>(_platform.FrameRate);
            services.Register<IScreenAwake>(_platform.ScreenAwake);
            services.Register<ISafeAreaProvider>(_platform.SafeArea);
            services.Register<IAppPaths>(_platform.Paths);
        }

        public void Start(AppModuleContext context)
        {
            _platform.FrameRate.ResetToDefault();
            _platform.Paths.EnsureStandardDirs();
        }

        public void Shutdown()
        {
        }
    }
}
