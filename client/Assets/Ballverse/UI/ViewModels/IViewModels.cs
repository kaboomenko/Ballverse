using System;
using Ballverse.UI.Theming;

namespace Ballverse.UI.ViewModels
{
    /// <summary>
    /// Base of every screen view model: the screen re-reads the properties it shows when <see cref="Changed"/> fires.
    /// Implementations raise it on the main thread. Actions are plain methods; navigation that follows an action is
    /// done by the screen controller.
    /// </summary>
    public interface IObservableViewModel
    {
        event Action Changed;
    }

    /// <summary>
    /// Everything the UI needs from the app (composition root, spec §7.1 Ballverse.App). The app binds real
    /// implementations backed by Core / Services / Studio; <c>Ballverse.UI.Fakes.FakeViewModels</c> is the editor stand-in.
    /// </summary>
    public interface IBallverseViewModels
    {
        IGalleryViewModel Gallery { get; }
        ISimulationViewModel Simulation { get; }
        IStudioViewModel Studio { get; }
        IExportViewModel Export { get; }
        IProjectsViewModel Projects { get; }
        IShopViewModel Shop { get; }
        IAppSettingsViewModel Settings { get; }
        ITrackingConsentViewModel TrackingConsent { get; }
        ISystemAppearance SystemAppearance { get; }
    }

    /// <summary>Device appearance and accessibility flags (native plugins; spec §4.5, §4.8).</summary>
    public interface ISystemAppearance
    {
        SystemAppearanceState Current { get; }
        event Action Changed;
    }

    /// <summary>The own ATT explanation screen before the system dialog (spec §5.6).</summary>
    public interface ITrackingConsentViewModel
    {
        /// <summary>The user read the explanation: show the system ATT dialog now; the screen closes itself.</summary>
        void ContinueToSystemPrompt();
    }
}
