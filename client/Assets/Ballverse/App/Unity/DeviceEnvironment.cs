using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Monetization;
using Ballverse.Media;
using Ballverse.Render;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI;
using UnityEngine;

namespace Ballverse.App
{
    public static partial class AppModuleCatalog
    {
        /// <summary>The device catalog used by <see cref="Bootstrap"/>.</summary>
        public static IReadOnlyList<IAppModule> Create(AppPlatform platform)
        {
            return Create(platform, DeviceEnvironment.Create(platform));
        }
    }

    /// <summary>
    /// The real engine and SDK pieces of the app (device and Play Mode): Services installer with the release-1 catalog preset,
    /// the platform media bridge, Ballverse.Studio's runtime with the App's renderer adapter over Ballverse.Render, the live
    /// simulation view / gallery previews / audio output / haptics of Ballverse.Sim, the app's one AudioListener
    /// (<see cref="DeviceAudio"/>), the UI Toolkit shell and the Unity runner.
    /// One <see cref="RenderResources"/> set is shared by the live view, the previews and the studio renderer.
    /// </summary>
    public static class DeviceEnvironment
    {
        public static AppEnvironment Create(AppPlatform platform)
        {
            RenderResources resources = null;
            RenderResources Resources() => resources ?? (resources = new RenderResources());
            Action<string, Exception> report = (c, e) =>
            {
                Debug.LogError("[Ballverse] " + c + ": " + e.Message);
                Debug.LogException(e);
            };

            var env = new AppEnvironment
            {
                CreateServices = () => ServicesInstaller.Create(new ServicesOptions { Monetization = MonetizationConfig.Release1 }),
                RunServices = services =>
                {
                    ServicesRunner runner = ServicesRunner.Attach(services);
                    return new ActionDisposable(() =>
                    {
                        if (runner != null) runner.Detach();
                    });
                },
                CreateMedia = () => MediaBridgeFactory.Create(),
                CreateStudio = studio =>
                {
                    // StudioPreviewPlayer's PreviewAudioOutput is mixed only while an enabled AudioListener exists.
                    DeviceAudio.EnsureListener();
                    DeviceTier tier = studio.Tier;
                    StudioServices services = StudioServices.Create(new StudioServicesOptions
                    {
                        Paths = studio.Paths,
                        Media = studio.Media,
                        // One renderer per preview / export / cover / thumbnails; disposed by its user (StudioSimRenderer is IDisposable).
                        SimRendererFactory = () => new StudioSimRenderer(studio.Modes, Resources()) { Tier = tier },
                        StreamRebuilder = new ModeStreamRebuilder(studio.Modes),
                        Lifecycle = studio.Lifecycle,
                        ScreenAwake = studio.ScreenAwake,
                        Sounds = new StudioSoundProvider(),
                        Gate = studio.Gate,
                        Tier = studio.Tier,
                    });
                    return new StudioBackend(services, t => tier = t);
                },
                CreateSurface = () => new UnitySimSurface(new LiveSimView(Resources())),
                CreatePreviews = () => new UnityGalleryPreviews(new GalleryPreviewRenderer(Resources(), report)),
                // The live output and the studio preview output never add listeners: DeviceAudio owns the app's only one.
                CreateAudioOutput = DeviceAudio.CreateLiveOutput,
                Haptics = new UnityHapticsDevice(),
                Device = new UnityDeviceInfo(),
                Platform = new UnityPlatformActions(),
                Textures = new UnityTextureLoader(),
                Clock = new StopwatchClock(),
                CreateUi = viewModels => new UnityAppUi(BallverseUIRoot.Create(viewModels, platform.SafeArea)),
                // Every frame: a reused listener that went away (destroyed with its scene) is replaced by the app's own.
                RunLoop = (tick, late) => AppRunner.Create(DeviceAudio.KeepingListener(tick), late),
                Shutdown = () =>
                {
                    resources?.Dispose();
                    resources = null;
                    DeviceAudio.ReleaseListener();
                },
            };
            return env;
        }
    }
}
