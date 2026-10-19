using System;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>What the studio factory of an <see cref="AppEnvironment"/> receives.</summary>
    public sealed class StudioEnvironment
    {
        public IAppPaths Paths { get; set; }
        public IMediaBridge Media { get; set; }
        public IAppLifecycle Lifecycle { get; set; }
        public IScreenAwake ScreenAwake { get; set; }
        public IExportGate Gate { get; set; }
        public ModeRegistry Modes { get; set; }
        public DeviceTier Tier { get; set; }
        public Action<string, Exception> ReportError { get; set; }
    }

    /// <summary>
    /// The engine- and SDK-facing pieces the app modules are built from. <see cref="DeviceEnvironment.Create"/> wires the real
    /// ones (Services installer, media bridge, Studio runtime over Ballverse.Render, UI Toolkit shell, Unity runner); EditMode
    /// tests pass fakes (fake services mode, fake media, a GPU-free studio backend, a recording UI).
    /// </summary>
    public sealed class AppEnvironment
    {
        /// <summary>Builds the services (frame-0 part: crash reporter, cached rights, ad policy state).</summary>
        public Func<BallverseServices> CreateServices { get; set; }

        /// <summary>Starts ticking the services every frame; dispose to stop. Null = the caller ticks them (tests).</summary>
        public Func<BallverseServices, IDisposable> RunServices { get; set; }

        public Func<IMediaBridge> CreateMedia { get; set; }

        /// <summary>The modes of this build (spec §7.6: explicit registration).</summary>
        public Func<ModeRegistry> CreateModes { get; set; } = ReleaseModes.CreateRegistry;

        public Func<StudioEnvironment, IStudioBackend> CreateStudio { get; set; }

        public Func<ISimSurface> CreateSurface { get; set; }

        public Func<IGalleryPreviews> CreatePreviews { get; set; }

        public Func<LiveAudioEngine, IDisposable> CreateAudioOutput { get; set; }

        public IHapticsDevice Haptics { get; set; }
        public IDeviceInfo Device { get; set; }
        public IPlatformActions Platform { get; set; }
        public ITextureLoader Textures { get; set; }
        public IAppClock Clock { get; set; }

        /// <summary>Creates the UI shell over the view models. Null = headless (no UI).</summary>
        public Func<IBallverseViewModels, IAppUi> CreateUi { get; set; }

        /// <summary>Calls the composition's Tick / LateTick every frame; dispose to stop. Null = the caller ticks (tests).</summary>
        public Func<Action<float>, Action<float>, IDisposable> RunLoop { get; set; }

        /// <summary>Releases environment-owned resources (shared renderer resources) at shutdown.</summary>
        public Action Shutdown { get; set; }
    }

    /// <summary>Holder of the studio runtime registered by <see cref="StudioModule"/> and filled when it starts.</summary>
    public sealed class StudioRuntime
    {
        public StudioRuntime(RewardedGateBridge gate)
        {
            Gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public RewardedGateBridge Gate { get; }
        public IStudioBackend Backend { get; internal set; }
    }

    /// <summary>Holder of the composed experience (view models, UI) registered by <see cref="ExperienceModule"/>.</summary>
    public sealed class AppExperience
    {
        public AppComposition Composition { get; internal set; }
        public IAppUi Ui { get; internal set; }
    }

    /// <summary>An <see cref="IDisposable"/> that runs an action once.</summary>
    public sealed class ActionDisposable : IDisposable
    {
        Action _action;

        public ActionDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            Action a = _action;
            _action = null;
            a?.Invoke();
        }
    }
}
