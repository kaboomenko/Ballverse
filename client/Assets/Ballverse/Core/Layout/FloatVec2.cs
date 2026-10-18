using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>Immutable float 2D vector (normalized positions, pans, sub-pixel points).</summary>
    public readonly struct FloatVec2 : IEquatable<FloatVec2>
    {
        public static readonly FloatVec2 Zero = new FloatVec2(0f, 0f);

        public float X { get; }
        public float Y { get; }

        public FloatVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public bool IsFinite => !float.IsNaN(X) && !float.IsInfinity(X) && !float.IsNaN(Y) && !float.IsInfinity(Y);

        public bool ApproximatelyEquals(FloatVec2 other, float epsilon)
        {
            return Math.Abs(X - other.X) <= epsilon && Math.Abs(Y - other.Y) <= epsilon;
        }

        public bool Equals(FloatVec2 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is FloatVec2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return X.GetHashCode() * 397 ^ Y.GetHashCode();
            }
        }

        public static bool operator ==(FloatVec2 a, FloatVec2 b) => a.Equals(b);
        public static bool operator !=(FloatVec2 a, FloatVec2 b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0},{1})", X, Y);
        }
    }
}
