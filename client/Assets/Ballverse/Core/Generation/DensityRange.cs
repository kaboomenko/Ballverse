using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// The mode's "pleasant" significant-event density, events per second: not too few, not a cacophony (spec §2.4 <c>eventDensity</c>).
    /// </summary>
    public readonly struct DensityRange : IEquatable<DensityRange>
    {
        public double MinPerSecond { get; }
        public double MaxPerSecond { get; }

        /// <exception cref="ArgumentOutOfRangeException">min &lt; 0, max &lt;= 0, min &gt; max or a value is not finite.</exception>
        public DensityRange(double minPerSecond, double maxPerSecond)
        {
            if (!IsValid(minPerSecond, maxPerSecond))
                throw new ArgumentOutOfRangeException(nameof(maxPerSecond), "Density range needs finite 0 <= min <= max, max > 0");
            MinPerSecond = minPerSecond;
            MaxPerSecond = maxPerSecond;
        }

        /// <summary>Validation shared with the remote-config parser.</summary>
        public static bool IsValid(double minPerSecond, double maxPerSecond)
        {
            return IsFinite(minPerSecond) && IsFinite(maxPerSecond)
                && minPerSecond >= 0.0 && maxPerSecond > 0.0 && minPerSecond <= maxPerSecond;
        }

        static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        public bool Equals(DensityRange other)
        {
            return MinPerSecond.Equals(other.MinPerSecond) && MaxPerSecond.Equals(other.MaxPerSecond);
        }

        public override bool Equals(object obj)
        {
            return obj is DensityRange other && Equals(other);
        }

        public override int GetHashCode()
        {
            return MinPerSecond.GetHashCode() * 397 ^ MaxPerSecond.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0}..{1}]/s", MinPerSecond, MaxPerSecond);
        }
    }
}
