using System;

namespace Ballverse.Core.Config
{
    /// <summary>One immutable value per device tier A/B/C (spec §8.1).</summary>
    public sealed class TierValues<T>
    {
        public T A { get; }
        public T B { get; }
        public T C { get; }

        public TierValues(T a, T b, T c)
        {
            A = a;
            B = b;
            C = c;
        }

        /// <exception cref="ArgumentOutOfRangeException">Unknown tier.</exception>
        public T Get(DeviceTier tier)
        {
            switch (tier)
            {
                case DeviceTier.A: return A;
                case DeviceTier.B: return B;
                case DeviceTier.C: return C;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        /// <summary>Copy with one tier replaced.</summary>
        public TierValues<T> With(DeviceTier tier, T value)
        {
            switch (tier)
            {
                case DeviceTier.A: return new TierValues<T>(value, B, C);
                case DeviceTier.B: return new TierValues<T>(A, value, C);
                case DeviceTier.C: return new TierValues<T>(A, B, value);
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }
    }
}
