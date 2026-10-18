using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// One note of a melody for the Melody sound pack (spec §2.7: pitch, duration weight, volume).
    /// JSON form: <c>{"pitch":64,"durWeight":1,"velocity":0.8}</c>.
    /// </summary>
    public sealed class MelodyNote
    {
        /// <summary>MIDI note number 0–127.</summary>
        public int Pitch { get; }
        /// <summary>Relative duration weight, finite and &gt; 0.</summary>
        public float DurWeight { get; }
        /// <summary>Volume 0–1.</summary>
        public float Velocity { get; }

        /// <exception cref="ArgumentOutOfRangeException">A value is outside its range.</exception>
        public MelodyNote(int pitch, float durWeight, float velocity)
        {
            if (pitch < RemoteConfigLimits.PitchMin || pitch > RemoteConfigLimits.PitchMax)
                throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "pitch must be 0–127");
            if (!IsValidDurWeight(durWeight))
                throw new ArgumentOutOfRangeException(nameof(durWeight), durWeight, "durWeight must be finite and > 0");
            if (!IsValidVelocity(velocity))
                throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "velocity must be 0–1");
            Pitch = pitch;
            DurWeight = durWeight;
            Velocity = velocity;
        }

        public static bool IsValidDurWeight(double w)
        {
            return !double.IsNaN(w) && !double.IsInfinity(w) && w > 0.0 && w <= float.MaxValue && (float)w > 0f;
        }

        public static bool IsValidVelocity(double v)
        {
            return !double.IsNaN(v) && v >= 0.0 && v <= 1.0;
        }
    }
}
