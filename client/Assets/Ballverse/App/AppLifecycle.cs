using System;
using Ballverse.Platform;

namespace Ballverse.App
{
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
