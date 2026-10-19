using System;
using Ballverse.Core.Audio;

namespace Ballverse.Sim
{
    /// <summary>The platform's short impact vibration (Core Haptics / UIImpactFeedbackGenerator on iOS, VibrationEffect on Android).</summary>
    public interface IHapticsDevice
    {
        bool IsSupported { get; }

        /// <summary>One short impact, intensity 0–1. Must not block.</summary>
        void Impact(float intensity);
    }

    /// <summary>
    /// Haptic feedback of a live run (spec §2.7 "Тактильный отклик"): at most once per 80 ms of real time, intensity from the
    /// impulse through the sound pack's i0 / i1 (Core <see cref="HapticsLimiter"/>), off by the setting, the run's
    /// <c>haptics</c> parameter or the system power saver. Main thread.
    /// </summary>
    public sealed class LiveHaptics : ILiveSoundSink
    {
        readonly IHapticsDevice _device;
        HapticsLimiter _limiter = new HapticsLimiter(0.5f, 6f);
        Action<string, Exception> _onError;

        public LiveHaptics(IHapticsDevice device, Action<string, Exception> onError = null)
        {
            _device = device;
            _onError = onError;
        }

        /// <summary>Setting "Вибрация".</summary>
        public bool SettingEnabled { get; set; } = true;

        /// <summary>The run's <c>haptics</c> parameter.</summary>
        public bool RunEnabled { get; set; } = true;

        /// <summary>Low Power Mode / Battery Saver.</summary>
        public bool PowerSaving { get; set; }

        public bool Active => SettingEnabled && RunEnabled && !PowerSaving && _device != null && _device.IsSupported;

        /// <summary>Thresholds of the run's sound pack (null keeps the defaults).</summary>
        public void SetPack(SoundPack pack)
        {
            if (pack == null) return;
            _limiter = new HapticsLimiter(pack);
        }

        public void BeginRun()
        {
            _limiter.Reset();
        }

        public void OnSound(in SoundEvent e, long runFrame)
        {
            if (!Active) return;
            if (!_limiter.TryTrigger(runFrame, e.Impulse, out float intensity)) return;
            try
            {
                _device.Impact(intensity);
            }
            catch (Exception ex)
            {
                // A vibrator that fails (missing permission, OEM quirk) must never stop the simulation.
                SettingEnabled = false;
                _onError?.Invoke("haptics", ex);
                _onError = null;
            }
        }
    }
}
