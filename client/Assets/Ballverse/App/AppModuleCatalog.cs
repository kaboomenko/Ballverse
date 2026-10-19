using System.Collections.Generic;

namespace Ballverse.App
{
    /// <summary>
    /// The registration list of app modules, in boot order (spec §7.9). This is the single place where feature
    /// assemblies join the app:
    /// <list type="number">
    /// <item>add the assembly name to the "references" of Ballverse.App.asmdef (the feature assembly itself references
    /// Ballverse.Platform for the platform contracts, never Ballverse.App);</item>
    /// <item>add an <see cref="IAppModule"/> class for it under Assets/Ballverse/App/Modules/ (it registers the
    /// assembly's services and starts it);</item>
    /// <item>append one line below, at the position that matches its init order.</item>
    /// </list>
    /// Order: Platform → Services (frame 0: crash reporter, cached rights, ad policy; the services phase Remote Config → IAP →
    /// consent → ads after the first frame themselves) → Media → Modes → Studio → Experience (view models, UI shell, live
    /// simulation, first-run flow; all <see cref="AppStartPhase.Immediate"/>), then the export resume offer
    /// (<see cref="AppStartPhase.AfterFirstFrame"/>). Engine and SDK pieces come from the <see cref="AppEnvironment"/>:
    /// <see cref="DeviceEnvironment.Create"/> on device, fakes in EditMode tests.
    /// </summary>
    public static partial class AppModuleCatalog
    {
        /// <summary>The modules over <paramref name="environment"/> (device: <c>Create(AppPlatform)</c> in Unity/DeviceEnvironment.cs).</summary>
        public static IReadOnlyList<IAppModule> Create(AppPlatform platform, AppEnvironment environment)
        {
            return new List<IAppModule>
            {
                new PlatformModule(platform),
                new ServicesModule(environment),
                new MediaModule(environment),
                new ModesModule(environment),
                new StudioModule(environment),
                new ExperienceModule(environment),
                new DeferredStartModule(),
            };
        }
    }
}
