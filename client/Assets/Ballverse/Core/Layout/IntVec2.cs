using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>Immutable integer 2D vector / size (pixels).</summary>
    public readonly struct IntVec2 : IEquatable<IntVec2>
    {
        public int X { get; }
        public int Y { get; }

        public IntVec2(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(IntVec2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is IntVec2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return X * 397 ^ Y;
            }
        }

        public static bool operator ==(IntVec2 a, IntVec2 b) => a.Equals(b);
        public static bool operator !=(IntVec2 a, IntVec2 b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0},{1})", X, Y);
        }
    }
}
