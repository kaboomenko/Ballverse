using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// A body-local point of a polygon or polyline in the quantized position domain (spec §7.4 "16-битные координаты"):
    /// metres → int16 with <see cref="RenderQuantization.QuantizePosition"/>, see <see cref="RenderQuantization.QuantizePoint"/>.
    /// </summary>
    public struct ShapePoint : IEquatable<ShapePoint>
    {
        public short X;
        public short Y;

        public ShapePoint(short x, short y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(ShapePoint other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is ShapePoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (X << 16) ^ (ushort)Y;
        }

        public static bool operator ==(ShapePoint a, ShapePoint b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ShapePoint a, ShapePoint b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }
    }
}
