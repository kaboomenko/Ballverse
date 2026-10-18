using System;

namespace Ballverse.App
{
    /// <summary>
    /// App lifecycle as events. Subscribers: autosave of the project and render stream (on pause), audio and
    /// simulation pause, export pipeline (pause/resume encoders), ads/IAP SDK hooks.
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

    /// <summary>
    /// <see cref="IAppLifecycle"/> fed by <see cref="Bootstrap"/> from Unity callbacks. Duplicate notifications
    /// (Unity may repeat pause/focus callbacks) do not raise events twice.
    /// </summary>
    public sealed class AppLifecycle : IAppLifecycle
    {
        readonly Action<string, Exception> _reportError;

        public AppLifecycle(Action<string, Exception> reportError)
        {
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public bool IsPaused { get; private set; }

        public bool HasFocus { get; private set; } = true;

        public bool IsQuitting { get; private set; }

        public event Action Paused;
        public event Action Resumed;
        public event Action<bool> FocusChanged;
        public event Action LowMemory;
        public event Action Quitting;

        public void NotifyPause(bool paused)
        {
            if (IsQuitting || paused == IsPaused) return;
            IsPaused = paused;
            if (paused) AppEvents.Raise(Paused, _reportError, "AppLifecycle.Paused");
            else AppEvents.Raise(Resumed, _reportError, "AppLifecycle.Resumed");
        }

        public void NotifyFocus(bool hasFocus)
        {
            if (IsQuitting || hasFocus == HasFocus) return;
            HasFocus = hasFocus;
            AppEvents.Raise(FocusChanged, hasFocus, _reportError, "AppLifecycle.FocusChanged");
        }

        public void NotifyLowMemory()
        {
            if (IsQuitting) return;
            AppEvents.Raise(LowMemory, _reportError, "AppLifecycle.LowMemory");
        }

        public void NotifyQuitting()
        {
            if (IsQuitting) return;
            IsQuitting = true;
            AppEvents.Raise(Quitting, _reportError, "AppLifecycle.Quitting");
        }
    }
}
