using System;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace Ballverse.Sim
{
    /// <summary>
    /// Short impact vibration (spec §2.7): iOS <c>UIImpactFeedbackGenerator</c> through <c>Plugins/iOS/BallverseHaptics.mm</c>,
    /// Android <c>Vibrator.vibrate(VibrationEffect.createOneShot(ms, amplitude))</c> (API 26+, the release-1 minimum) through
    /// JNI. Elsewhere (editor, desktop) it does nothing. Main thread.
    /// </summary>
    public sealed class UnityHapticsDevice : IHapticsDevice
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void BallverseHaptics_Impact(float intensity);

        public bool IsSupported => true;

        public void Impact(float intensity)
        {
            BallverseHaptics_Impact(Mathf.Clamp01(intensity));
        }
#elif UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _vibrator;
        AndroidJavaClass _effect;
        bool _amplitudeControl;
        bool _initialized;
        bool _supported;

        public bool IsSupported
        {
            get
            {
                EnsureInitialized();
                return _supported;
            }
        }

        public void Impact(float intensity)
        {
            EnsureInitialized();
            if (!_supported) return;
            int amplitude = _amplitudeControl ? Mathf.Clamp(Mathf.RoundToInt(40 + 215 * Mathf.Clamp01(intensity)), 1, 255) : -1;
            using (AndroidJavaObject effect = _effect.CallStatic<AndroidJavaObject>("createOneShot", 18L, amplitude))
            {
                _vibrator.Call("vibrate", effect);
            }
        }

        /// <summary>
        /// Never called: referencing <see cref="Handheld.Vibrate"/> makes Unity add <c>android.permission.VIBRATE</c> to the
        /// player manifest, which the JNI calls above need.
        /// </summary>
        internal static void VibratePermissionMarker()
        {
            Handheld.Vibrate();
        }

        void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                _supported = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
                if (_supported)
                {
                    _effect = new AndroidJavaClass("android.os.VibrationEffect");
                    _amplitudeControl = _vibrator.Call<bool>("hasAmplitudeControl");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ballverse.Sim] Vibrator unavailable: " + e.Message);
                _supported = false;
            }
        }
#else
        public bool IsSupported => false;

        public void Impact(float intensity)
        {
        }
#endif
    }
}
