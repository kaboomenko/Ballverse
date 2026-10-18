using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Builds a HASH record value: XXH64 over quantized positions and velocities (spec §7.4). Quantization removes
    /// −0/+0 and sub-resolution noise; values are fed in a stable order chosen by the mode (spec §7.4 rule 3).
    /// Reusable: call <see cref="Reset"/> between ticks.
    /// </summary>
    public sealed class StateHashBuilder
    {
        /// <summary>Default fixed-point scale for hashed floats: 2^16 units per unit (≈ 0.015 mm for meters).</summary>
        public const int DefaultUnitsPerUnit = 65536;

        /// <summary>Hashed in place of NaN so a NaN state still hashes deterministically.</summary>
        const long NaNSentinel = long.MinValue;

        readonly XxHash64 _hash = new XxHash64();

        public void Reset()
        {
            _hash.Reset();
        }

        public void AddInt(int value)
        {
            _hash.AppendInt(value);
        }

        public void AddLong(long value)
        {
            _hash.AppendULong((ulong)value);
        }

        /// <summary>Adds <c>round(value · unitsPerUnit)</c> as a 64-bit integer (saturating; NaN → sentinel).</summary>
        public void AddQuantized(float value, int unitsPerUnit = DefaultUnitsPerUnit)
        {
            if (unitsPerUnit <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerUnit), "unitsPerUnit must be > 0");
            long q;
            if (float.IsNaN(value))
            {
                q = NaNSentinel;
            }
            else
            {
                double scaled = Math.Round((double)value * unitsPerUnit, MidpointRounding.AwayFromZero);
                if (scaled >= 9.2e18) q = long.MaxValue;
                else if (scaled <= -9.2e18) q = long.MinValue + 1;
                else q = (long)scaled;
            }
            _hash.AppendULong((ulong)q);
        }

        /// <summary>Adds a 2D vector (position or velocity), x then y.</summary>
        public void AddVector(float x, float y, int unitsPerUnit = DefaultUnitsPerUnit)
        {
            AddQuantized(x, unitsPerUnit);
            AddQuantized(y, unitsPerUnit);
        }

        /// <summary>Current hash; does not reset the builder.</summary>
        public ulong Digest()
        {
            return _hash.Digest();
        }
    }
}
