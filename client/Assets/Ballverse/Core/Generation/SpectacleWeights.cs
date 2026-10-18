using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Weights w1…w5 of the spectacle score (spec §2.4 step 4), per mode, overridable via Remote Config
    /// <c>generation_weights</c> (spec §9.3). Immutable; every weight is within 0..<see cref="MaxWeight"/>
    /// (w5 is subtracted by the formula, so it is stored as a positive penalty).
    /// </summary>
    public sealed class SpectacleWeights : IEquatable<SpectacleWeights>
    {
        /// <summary>
        /// Largest accepted weight. Guards against a broken Remote Config value (e.g. 1e308) overflowing the total to infinity,
        /// which would make every candidate pass and tie.
        /// </summary>
        public const double MaxWeight = 100.0;

        /// <summary>Generic defaults used when a mode has no specific entry: fit 1.0, density 0.8, tension 0.8, coverage 0.5, stagnation 1.5.</summary>
        public static readonly SpectacleWeights Default = new SpectacleWeights(1.0, 0.8, 0.8, 0.5, 1.5);

        /// <summary>w1, fitDuration.</summary>
        public double W1 { get; }
        /// <summary>w2, eventDensity.</summary>
        public double W2 { get; }
        /// <summary>w3, finishTension.</summary>
        public double W3 { get; }
        /// <summary>w4, coverage.</summary>
        public double W4 { get; }
        /// <summary>w5, stagnation penalty.</summary>
        public double W5 { get; }

        /// <exception cref="ArgumentOutOfRangeException">A weight is negative, NaN or above <see cref="MaxWeight"/>.</exception>
        public SpectacleWeights(double w1, double w2, double w3, double w4, double w5)
        {
            if (!IsValidWeight(w1)) throw new ArgumentOutOfRangeException(nameof(w1));
            if (!IsValidWeight(w2)) throw new ArgumentOutOfRangeException(nameof(w2));
            if (!IsValidWeight(w3)) throw new ArgumentOutOfRangeException(nameof(w3));
            if (!IsValidWeight(w4)) throw new ArgumentOutOfRangeException(nameof(w4));
            if (!IsValidWeight(w5)) throw new ArgumentOutOfRangeException(nameof(w5));
            W1 = w1;
            W2 = w2;
            W3 = w3;
            W4 = w4;
            W5 = w5;
        }

        /// <summary>Highest reachable total: w1 + w2 + w3 + w4 (all positive metrics 1, stagnation 0).</summary>
        public double MaxTotal => W1 + W2 + W3 + W4;

        /// <summary>True when the value is within 0..<see cref="MaxWeight"/> (NaN is rejected).</summary>
        public static bool IsValidWeight(double w)
        {
            return w >= 0.0 && w <= MaxWeight;
        }

        public bool Equals(SpectacleWeights other)
        {
            if (other == null) return false;
            return W1.Equals(other.W1) && W2.Equals(other.W2) && W3.Equals(other.W3) && W4.Equals(other.W4) && W5.Equals(other.W5);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SpectacleWeights);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = W1.GetHashCode();
                h = h * 397 ^ W2.GetHashCode();
                h = h * 397 ^ W3.GetHashCode();
                h = h * 397 ^ W4.GetHashCode();
                return h * 397 ^ W5.GetHashCode();
            }
        }
    }
}
