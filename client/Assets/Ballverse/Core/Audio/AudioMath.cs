using System;

namespace Ballverse.Core.Audio
{
    /// <summary>Small deterministic audio formulas shared by the mixer, haptics and mastering (spec §2.7, §3.6).</summary>
    public static class AudioMath
    {
        // 2^(k/12), k = 0..11, as literals so pitch ratios do not depend on the platform's Math.Pow.
        static readonly double[] s_semitone =
        {
            1.0,
            1.0594630943592953,
            1.122462048309373,
            1.189207115002721,
            1.2599210498948732,
            1.3348398541700344,
            1.4142135623730951,
            1.4983070768766815,
            1.5874010519681994,
            1.681792830507429,
            1.7817974362806785,
            1.8877486253633868,
        };

        /// <summary>Pitch shift range of the mixer: ±48 semitones (ratio 1/16 … 16).</summary>
        public const int MaxSemitoneShift = 48;

        /// <summary>
        /// Impulse volume (spec §2.7): v = clamp((impulse − i0) / (i1 − i0), 0.15, 1). Returns 0 (silent) when
        /// impulse &lt; i0 or impulse is NaN. Exactly i0 is audible at 0.15. Requires i1 &gt; i0.
        /// </summary>
        public static float ImpulseVolume(float impulse, float i0, float i1)
        {
            if (float.IsNaN(impulse) || impulse < i0) return 0f;
            float v = (impulse - i0) / (i1 - i0);
            if (float.IsNaN(v)) return 1f;
            if (v < AudioConstants.MinAudibleVolume) return AudioConstants.MinAudibleVolume;
            if (v > 1f) return 1f;
            return v;
        }

        /// <summary>Exact 2^(semitones/12) built from a literal table; the shift is clamped to ±<see cref="MaxSemitoneShift"/>.</summary>
        public static double SemitoneRatio(int semitones)
        {
            if (semitones > MaxSemitoneShift) semitones = MaxSemitoneShift;
            if (semitones < -MaxSemitoneShift) semitones = -MaxSemitoneShift;
            int octave = semitones >= 0 ? semitones / 12 : -((-semitones + 11) / 12);
            int k = semitones - octave * 12;
            double r = s_semitone[k];
            if (octave >= 0) return r * (1L << octave);
            return r / (1L << -octave);
        }

        /// <summary>
        /// Constant-power pan law: left = cos(θ), right = sin(θ), θ = (pan + 1)·π/4. Centre gives −3 dB per channel and
        /// left² + right² = 1 everywhere. Pan is clamped to −1…1; NaN is centre.
        /// </summary>
        public static void ConstantPowerPan(float pan, out float left, out float right)
        {
            if (float.IsNaN(pan)) pan = 0f;
            if (pan < -1f) pan = -1f;
            if (pan > 1f) pan = 1f;
            double theta = (pan + 1.0) * (Math.PI / 4.0);
            left = (float)Math.Cos(theta);
            right = (float)Math.Sin(theta);
        }

        /// <summary>Linear gain for a level in dB.</summary>
        public static double DbToGain(double db)
        {
            return Math.Pow(10.0, db / 20.0);
        }

        /// <summary>Level in dB for a linear gain, floored at <paramref name="floorDb"/> (no −∞ / NaN, spec §3.6 mastering).</summary>
        public static double GainToDb(double gain, double floorDb)
        {
            if (!(gain > 0)) return floorDb;
            if (double.IsPositiveInfinity(gain)) gain = double.MaxValue;
            double db = 20.0 * Math.Log10(gain);
            return db < floorDb ? floorDb : db;
        }

        /// <summary>Clamps a gain setting to [min, max]; NaN becomes <paramref name="fallback"/>.</summary>
        public static float SanitizeGain(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value)) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
