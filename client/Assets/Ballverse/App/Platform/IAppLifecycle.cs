using System;

namespace Ballverse.Platform
{
    /// <summary>
    /// App lifecycle as events. Subscribers: autosave of the project and render stream (on pause), audio and
    /// simulation pause, export pipeline (pause/resume encoders), ads/IAP SDK hooks.
    /// Resolve it from the app's service registry (published by Ballverse.App's PlatformModule); do not add another
    /// OnApplicationPause runner.
    /// </summary>
    public interface IAppLifecycle
    {
        /// <summary>True while the app is in the background (Unity OnApplicationPause(true)).</summary>
        bool IsPaused { get; }

        bool HasFocus { get; }

        bool IsQuitting { get; }

        /// <summary>The app went to the background. Save state now: the OS may kill the process without further notice.</summary>
        event Action Paused;

        event Action Resumed;

        /// <summary>Focus changed (system dialogs, notification shade, purchase sheets). Argument: has focus.</summary>
        event Action<bool> FocusChanged;

        /// <summary>The OS reported memory pressure: drop caches (thumbnails, decoded frames, sample banks not in use).</summary>
        event Action LowMemory;

        /// <summary>The app is quitting normally. Not raised when the OS kills a background app; use <see cref="Paused"/> for saving.</summary>
        event Action Quitting;
    }
}
