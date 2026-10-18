using System;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Per-<see cref="DeviceTier"/> upper bound of a numeric parameter (spec §7.6 <c>tierCap</c>, §2.9 body budgets).
    /// A null entry means "no cap on that tier" (the parameter's own max applies).
    /// </summary>
    public sealed class TierCap : IEquatable<TierCap>
    {
        public double? A { get; }
        public double? B { get; }
        public double? C { get; }

        public TierCap(double? a, double? b, double? c)
        {
            A = a;
            B = b;
            C = c;
        }

        public double? For(DeviceTier tier)
        {
            switch (tier)
            {
                case DeviceTier.A: return A;
                case DeviceTier.B: return B;
                case DeviceTier.C: return C;
                default: throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        public bool Equals(TierCap other)
        {
            return other != null && A == other.A && B == other.B && C == other.C;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TierCap);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (A.GetHashCode() * 397 ^ B.GetHashCode()) * 397 ^ C.GetHashCode();
            }
        }

        public override string ToString()
        {
            return "A:" + (A?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")
                + " B:" + (B?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-")
                + " C:" + (C?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-");
        }
    }
}
