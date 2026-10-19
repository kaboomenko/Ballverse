using System;
using System.Reflection;
using Ballverse.Sim;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// The app's audio wiring on the device: the one <see cref="AudioListener"/> and the live sound output. Unity mixes
    /// nothing without an enabled listener, and the boot scene has no camera (so no camera listener). The App owns it, so
    /// Ballverse.Sim (<see cref="LiveAudioOutput"/>) and Ballverse.Studio (<c>PreviewAudioOutput</c>) never add listeners of
    /// their own.
    /// <para>The listener lives on its own persistent object, <see cref="ListenerObjectName"/>, and never on an object that has an
    /// <see cref="AudioSource"/> or an <c>OnAudioFilterRead</c> script. Unity inserts a script's <c>OnAudioFilterRead</c> as a DSP
    /// filter into the chain of the AudioSource or AudioListener on the same object. A filter can sit in only one of them; the
    /// second one to attach it logs "GameObject has multiple AudioSources and/or AudioListeners attached". On a listener the
    /// filter also processes the final mix, so the output scripts' <c>OnAudioFilterRead</c>, which overwrites its buffer, would
    /// wipe every other sound. Sources: Unity 6 Scripting API <c>MonoBehaviour.OnAudioFilterRead</c> and Manual
    /// "Audio Listener" (only one listener per scene; effects on the listener apply to all audible sounds).</para>
    /// </summary>
    public static class DeviceAudio
    {
        /// <summary>Name of the dedicated listener object the app creates when no enabled listener exists.</summary>
        public const string ListenerObjectName = "BallverseAudioListener";

        static AudioListener s_owned;
        static AudioListener s_current;

        /// <summary>The listener created by <see cref="EnsureListener"/>, or null (none created, or an existing one was reused).</summary>
        public static AudioListener OwnedListener => s_owned;

        /// <summary>The listener the last <see cref="EnsureListener"/> returned (the app's own or a reused one), or null.</summary>
        public static AudioListener CurrentListener => s_current;

        /// <summary>
        /// Makes sure exactly one enabled listener exists, and returns it. The dedicated listener is created at most once (and
        /// re-enabled if something disabled it). Otherwise an enabled listener that is already in the scene, such as a
        /// camera's, is reused. Main thread. Call it before creating an audio output.
        /// </summary>
        public static AudioListener EnsureListener()
        {
            if (s_owned != null)
            {
                if (!s_owned.gameObject.activeSelf) s_owned.gameObject.SetActive(true);
                if (!s_owned.enabled) s_owned.enabled = true;
                return s_current = s_owned;
            }
            AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i].isActiveAndEnabled) return s_current = listeners[i];
            }
            var go = new GameObject(ListenerObjectName);
            // A root object (DontDestroyOnLoad works only on root objects, and only in Play Mode).
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
            s_owned = go.AddComponent<AudioListener>();
            return s_current = s_owned;
        }

        /// <summary>
        /// Re-runs <see cref="EnsureListener"/> when the listener it last returned is gone or disabled, for example a reused
        /// camera listener destroyed with its scene. Without this the live output and the studio preview would stay silent for
        /// the rest of the session. Cheap when nothing changed (a destroyed-object check and <c>isActiveAndEnabled</c>, no
        /// search), so the app runs it every frame through <see cref="KeepingListener"/>. Main thread.
        /// </summary>
        public static void KeepListener()
        {
            if (s_current != null && s_current.isActiveAndEnabled) return;
            EnsureListener();
        }

        /// <summary>The app's per-frame tick with <see cref="KeepListener"/> in front of it (DeviceEnvironment.RunLoop).</summary>
        public static Action<float> KeepingListener(Action<float> tick)
        {
            return dt =>
            {
                KeepListener();
                tick?.Invoke(dt);
            };
        }

        /// <summary>Destroys the dedicated listener if <see cref="EnsureListener"/> created it (app shutdown, tests).</summary>
        public static void ReleaseListener()
        {
            AudioListener owned = s_owned;
            s_owned = null;
            s_current = null;
            if (owned == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(owned.gameObject);
            else UnityEngine.Object.DestroyImmediate(owned.gameObject);
        }

        /// <summary>
        /// Null when the loaded scenes follow the audio rules above, else what breaks them: exactly one AudioListener (on an
        /// active object) and it is enabled, and no listener shares its object with an <see cref="AudioSource"/> or a script that
        /// implements <c>OnAudioFilterRead</c> (found by name, as Unity finds it). For tests and diagnostics; main thread.
        /// </summary>
        public static string FindPlacementProblem()
        {
            AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            if (listeners.Length != 1) return "expected exactly one AudioListener, found " + listeners.Length + Names(listeners);
            AudioListener listener = listeners[0];
            if (!listener.isActiveAndEnabled) return "the AudioListener on " + listener.gameObject.name + " is disabled";
            GameObject go = listener.gameObject;
            if (go.GetComponent<AudioSource>() != null) return "the AudioListener shares " + go.name + " with an AudioSource";
            MonoBehaviour[] scripts = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < scripts.Length; i++)
            {
                if (scripts[i] == null) continue;
                Type type = scripts[i].GetType();
                if (type.GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
                    return "the AudioListener shares " + go.name + " with the OnAudioFilterRead script " + type.Name;
            }
            return null;
        }

        static string Names(AudioListener[] listeners)
        {
            if (listeners.Length == 0) return string.Empty;
            var names = new string[listeners.Length];
            for (int i = 0; i < listeners.Length; i++) names[i] = listeners[i].gameObject.name;
            return " (" + string.Join(", ", names) + ")";
        }

        /// <summary>
        /// The live run's sound output (<see cref="LiveAudioOutput"/> on its own persistent object), after
        /// <see cref="EnsureListener"/>. Disposing it destroys the output object; the listener stays for the studio preview.
        /// </summary>
        public static IDisposable CreateLiveOutput(LiveAudioEngine engine)
        {
            EnsureListener();
            LiveAudioOutput output = LiveAudioOutput.Create(engine);
            return new ActionDisposable(() =>
            {
                if (output == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(output.gameObject);
                else UnityEngine.Object.DestroyImmediate(output.gameObject);
            });
        }
    }
}
