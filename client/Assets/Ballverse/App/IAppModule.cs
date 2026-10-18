namespace Ballverse.App
{
    /// <summary>
    /// When a module starts (spec §7.9). <see cref="Immediate"/> modules start during the boot scene's Awake,
    /// before the first frame (crash reporting, local settings and entitlement cache, UI, simulation).
    /// <see cref="AfterFirstFrame"/> modules start once the first frame has been presented
    /// (Remote Config, IAP, consent, ads), so SDK initialisation never delays the first picture.
    /// </summary>
    public enum AppStartPhase
    {
        Immediate = 0,
        AfterFirstFrame = 1,
    }

    /// <summary>
    /// A unit of app composition. Every feature assembly (UI, Services, Media, Render, Studio, Sim) is plugged into the
    /// app through one module class that lives in Ballverse.App (App is the only assembly allowed to depend on all
    /// others, spec §7.3) and is listed in <see cref="AppModuleCatalog"/>.
    /// <para>
    /// Boot order: every module's <see cref="Register"/> runs first, in catalog order, while the service registry is
    /// open; then the registry is sealed and <see cref="Start"/> runs per phase, in catalog order. Shutdown runs in
    /// reverse start order. A module that throws is reported and skipped; the rest of the app keeps booting.
    /// </para>
    /// </summary>
    public interface IAppModule
    {
        /// <summary>Short stable name for logs and diagnostics, e.g. "Services".</summary>
        string Name { get; }

        AppStartPhase Phase { get; }

        /// <summary>Publish the services this module provides. Must not resolve services of other modules.</summary>
        void Register(AppServices services);

        /// <summary>Resolve dependencies from <see cref="AppModuleContext.Services"/> and start working.</summary>
        void Start(AppModuleContext context);

        /// <summary>Release resources. Called once, only for modules whose <see cref="Start"/> completed.</summary>
        void Shutdown();
    }
}
