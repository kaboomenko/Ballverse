using System;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Haptic feedback rate helper (spec §2.7): at most once per 80 ms (3840 frames at 48 kHz; exactly 80 ms apart is
    /// allowed), intensity from the impulse with the same curve as the volume, clamp((impulse − i0) / (i1 − i0), 0.15, 1).
    /// Impulses below i0 produce no haptic and do not consume the 80 ms window. Realtime only — haptics never reach the
    /// video (spec §2.2). The platform layer switches it off via <see cref="Enabled"/> (settings, low-power mode).
    /// </summary>
    public sealed class HapticsLimiter
    {
        readonly float _i0;
        readonly float _i1;
        long _last;
        bool _hasLast;

        public HapticsLimiter(float i0, float i1)
        {
            if (float.IsNaN(i0) || float.IsInfinity(i0) || float.IsNaN(i1) || float.IsInfinity(i1) || !(i1 > i0))
                throw new ArgumentException("requires finite i0 < i1");
            _i0 = i0;
            _i1 = i1;
        }

        /// <summary>Uses the pack's impulse thresholds; throws <see cref="ArgumentNullException"/> for a null pack.</summary>
        public HapticsLimiter(SoundPack pack)
            : this(NotNull(pack).I0, pack.I1)
        {
        }

        static SoundPack NotNull(SoundPack pack)
        {
            return pack ?? throw new ArgumentNullException(nameof(pack));
        }

        public bool Enabled { get; set; } = true;

        /// <summary>Haptic for an event at its tick time (tick × 800 frames).</summary>
        public bool TryTrigger(in SoundEvent e, out float intensity)
        {
            return TryTrigger(AudioConstants.TickToFrame(e.Tick), e.Impulse, out intensity);
        }

        /// <summary>Haptic at <paramref name="frame"/> (48 kHz frames). Returns false when disabled, too soon or too weak.</summary>
        public bool TryTrigger(long frame, float impulse, out float intensity)
        {
            intensity = 0f;
            if (!Enabled) return false;
            float v = AudioMath.ImpulseVolume(impulse, _i0, _i1);
            if (v <= 0f) return false;
            if (_hasLast && frame - _last < AudioConstants.HapticsMinIntervalFrames) return false;
            _last = frame;
            _hasLast = true;
            intensity = v;
            return true;
        }

        public void Reset()
        {
            _hasLast = false;
            _last = 0;
        }
    }
}
