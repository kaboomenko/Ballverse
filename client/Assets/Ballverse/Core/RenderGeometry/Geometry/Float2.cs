using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>A 2D point or vector in metres (render geometry, spec §7.7). Plain value type with no engine dependency.</summary>
    public struct Float2 : IEquatable<Float2>
    {
        public float X;
        public float Y;

        public Float2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length => MathF.Sqrt(X * X + Y * Y);

        public static Float2 operator +(Float2 a, Float2 b) => new Float2(a.X + b.X, a.Y + b.Y);
        public static Float2 operator -(Float2 a, Float2 b) => new Float2(a.X - b.X, a.Y - b.Y);
        public static Float2 operator *(Float2 a, float s) => new Float2(a.X * s, a.Y * s);

        public static float Dot(Float2 a, Float2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>z of the 3D cross product: positive when <paramref name="b"/> turns counter-clockwise from <paramref name="a"/>.</summary>
        public static float Cross(Float2 a, Float2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>Unit vector, or zero for a (near) zero vector.</summary>
        public Float2 Normalized()
        {
            float len = Length;
            return len > 1e-12f ? new Float2(X / len, Y / len) : default;
        }

        /// <summary>Left-hand perpendicular (rotated +90°).</summary>
        public Float2 PerpLeft() => new Float2(-Y, X);

        public bool Equals(Float2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Float2 other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Y.GetHashCode();

        public override string ToString() => "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
            + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
