using System.Collections.Generic;

namespace Ballverse.App
{
    /// <summary>
    /// The registration list of app modules, in boot order (spec §7.9). This is the single place where feature
    /// assemblies join the app:
    /// <list type="number">
    /// <item>add the assembly name to the "references" of Ballverse.App.asmdef;</item>
    /// <item>add an <see cref="IAppModule"/> class for it under Assets/Ballverse/App/Modules/ (it registers the
    /// assembly's services and starts it);</item>
    /// <item>append one line below, at the position that matches its init order.</item>
    /// </list>
    /// Intended order once the assemblies exist: Platform → Crash → LocalSettings/Entitlements cache → Render →
    /// Audio → Media → Sim → Studio → UI (all <see cref="AppStartPhase.Immediate"/>), then RemoteConfig → IAP →
    /// Consent → Ads (<see cref="AppStartPhase.AfterFirstFrame"/>).
    /// </summary>
    public static class AppModuleCatalog
    {
        public static IReadOnlyList<IAppModule> Create(AppPlatform platform)
        {
            return new List<IAppModule>
            {
                new PlatformModule(platform),
            };
        }
    }
}
