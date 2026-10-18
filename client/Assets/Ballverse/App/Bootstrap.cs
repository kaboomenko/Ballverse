using System;
using System.Collections;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// The only component of the boot scene (Assets/Scenes/Boot.unity). Creates the platform services and the
    /// <see cref="AppHost"/>, boots the modules from <see cref="AppModuleCatalog"/>, starts deferred modules after the
    /// first frame and forwards Unity lifecycle callbacks. Survives scene loads; a second instance destroys itself.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class Bootstrap : MonoBehaviour
    {
        static Bootstrap s_instance;

        AppPlatform _platform;
        AppHost _host;

        /// <summary>The running app host, or null before boot / after shutdown.</summary>
        public static AppHost Host => s_instance != null ? s_instance._host : null;

        /// <summary>Platform services of the running app, or null before boot.</summary>
        public static AppPlatform Platform => s_instance != null ? s_instance._platform : null;

        // Statics survive Play Mode entry when domain reload is disabled (Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_instance = null;

        void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_instance = this;
            DontDestroyOnLoad(gameObject);

            _platform = AppPlatform.CreateForDevice(ReportError);
            _platform.SafeArea.UpdateFromScreen();
            Application.lowMemory += OnLowMemory;

            _host = new AppHost(AppModuleCatalog.Create(_platform), ReportError);
            _host.Boot();
        }

        IEnumerator Start()
        {
            if (s_instance != this) yield break;
            // Resumes on the next frame, i.e. after the first frame has been rendered and presented.
            yield return null;
            if (_host != null && _host.State == AppHostState.Booted) _host.StartDeferred();
        }

        void Update()
        {
            if (_platform != null) _platform.SafeArea.UpdateFromScreen();
        }

        void OnApplicationPause(bool paused)
        {
            if (_platform != null) _platform.Lifecycle.NotifyPause(paused);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (_platform != null) _platform.Lifecycle.NotifyFocus(hasFocus);
        }

        void OnApplicationQuit()
        {
            if (_platform == null) return;
            _platform.Lifecycle.NotifyQuitting();
            _host?.Shutdown();
        }

        void OnDestroy()
        {
            if (s_instance != this) return;
            Application.lowMemory -= OnLowMemory;
            _host?.Shutdown();
            s_instance = null;
        }

        void OnLowMemory()
        {
            if (_platform != null) _platform.Lifecycle.NotifyLowMemory();
        }

        static void ReportError(string context, Exception exception)
        {
            Debug.LogError("[Ballverse] " + context + ": " + exception.Message);
            Debug.LogException(exception);
        }
    }
}
