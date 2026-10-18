using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Quantization of interactive input payloads and event impulses for the replay (spec §2.6: inputs are quantized to the
    /// tick and written to the replay; §7.4 EVNT "impulse (quantized)"). All conversions round half away from zero in double
    /// precision and are deterministic. NaN throws <see cref="ArgumentException"/>; out-of-range values saturate.
    /// </summary>
    public static class ReplayQuantization
    {
        /// <summary>Coordinate resolution: 1 mm (arena is 9 × 16 m, spec §2.1). Round-trip error ≤ 0.5 mm within range.</summary>
        public const int CoordinateUnitsPerMeter = 1000;
        /// <summary>Coordinates saturate at ±1 km (±1 000 000 units), far outside any arena.</summary>
        public const int MaxCoordinateUnits = 1000000;
        /// <summary>Angles use the full u16 circle: 65536 steps per turn, error ≤ π/65536 rad.</summary>
        public const int AngleSteps = 65536;
        /// <summary>Normalized 0..1 values map to 0..65535; error ≤ 0.5/65535.</summary>
        public const int UnitSteps = 65535;
        /// <summary>Signed normalized −1..1 values map to −32767..32767 (symmetric); error ≤ 0.5/32767.</summary>
        public const int SignedUnitSteps = 32767;
        /// <summary>Impulse resolution: 1/256 N·s; u16 range 0..255.996 N·s (saturating). Negative impulses clamp to 0.</summary>
        public const int ImpulseUnitsPerNewtonSecond = 256;

        const double TwoPi = 2.0 * Math.PI;

        /// <summary>Arena coordinate in meters → integer millimetres (spec §2.1, §2.6 "x-coordinate", "coordinates").</summary>
        public static int QuantizeCoordinate(float meters)
        {
            return (int)RoundClamp(meters, CoordinateUnitsPerMeter, -MaxCoordinateUnits, MaxCoordinateUnits, nameof(meters));
        }

        public static float DequantizeCoordinate(int units)
        {
            return (float)((double)units / CoordinateUnitsPerMeter);
        }

        /// <summary>Angle in radians (any value; wrapped to [0, 2π)) → u16 (spec §2.6 "угол").</summary>
        public static ushort QuantizeAngle(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians)) throw new ArgumentException("Angle must be finite", nameof(radians));
            double a = radians % TwoPi;
            if (a < 0) a += TwoPi;
            long q = (long)Math.Round(a / TwoPi * AngleSteps, MidpointRounding.AwayFromZero);
            return (ushort)(q & 0xFFFF);
        }

        /// <summary>u16 → angle in [0, 2π).</summary>
        public static float DequantizeAngle(ushort q)
        {
            return (float)(q * TwoPi / AngleSteps);
        }

        /// <summary>Normalized value 0..1 (force, power) → u16 (spec §2.6 "сила"). Clamped to [0, 1].</summary>
        public static ushort QuantizeUnit(float value01)
        {
            return (ushort)RoundClamp(value01, UnitSteps, 0, UnitSteps, nameof(value01));
        }

        public static float DequantizeUnit(ushort q)
        {
            return (float)((double)q / UnitSteps);
        }

        /// <summary>Signed normalized value −1..1 (gravity vector component, spec §2.6 M10) → short. Clamped to [−1, 1].</summary>
        public static short QuantizeSignedUnit(float value)
        {
            return (short)RoundClamp(value, SignedUnitSteps, -SignedUnitSteps, SignedUnitSteps, nameof(value));
        }

        public static float DequantizeSignedUnit(short q)
        {
            return (float)((double)q / SignedUnitSteps);
        }

        /// <summary>Contact impulse in N·s → u16 for EVNT records (spec §2.7 volume by impulse).</summary>
        public static ushort QuantizeImpulse(float impulse)
        {
            return (ushort)RoundClamp(impulse, ImpulseUnitsPerNewtonSecond, 0, ushort.MaxValue, nameof(impulse));
        }

        public static float DequantizeImpulse(ushort q)
        {
            return (float)((double)q / ImpulseUnitsPerNewtonSecond);
        }

        /// <summary>
        /// Generic fixed-point quantization: <c>round(value · unitsPerOne)</c> saturated to the int range
        /// (e.g. angular velocity in mrad/s for M05, spec §2.6). Error ≤ 0.5/unitsPerOne within range.
        /// </summary>
        public static int QuantizeFixed(float value, int unitsPerOne)
        {
            if (unitsPerOne <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerOne), "unitsPerOne must be > 0");
            return (int)RoundClamp(value, unitsPerOne, int.MinValue, int.MaxValue, nameof(value));
        }

        public static float DequantizeFixed(int q, int unitsPerOne)
        {
            if (unitsPerOne <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerOne), "unitsPerOne must be > 0");
            return (float)((double)q / unitsPerOne);
        }

        static long RoundClamp(float value, double scale, long min, long max, string paramName)
        {
            if (float.IsNaN(value)) throw new ArgumentException("Value must not be NaN", paramName);
            double scaled = Math.Round(value * scale, MidpointRounding.AwayFromZero);
            if (scaled <= min) return min;
            if (scaled >= max) return max;
            return (long)scaled;
        }
    }
}
