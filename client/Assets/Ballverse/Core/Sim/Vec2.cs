using System;

namespace Ballverse.Core.Sim
{
    /// <summary>
    /// 2D vector in simulation metres. Own type (not System.Numerics) so every operation is a plain float expression in a
    /// fixed order: no SIMD lowering differences between runtimes and no hidden reassociation (spec §7.4 determinism).
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);

        public float LengthSquared => X * X + Y * Y;

        public float Length => MathF.Sqrt(X * X + Y * Y);

        /// <summary>Unit vector or zero for a (near) zero vector.</summary>
        public Vec2 Normalized
        {
            get
            {
                float l = MathF.Sqrt(X * X + Y * Y);
                return l > 1e-12f ? new Vec2(X / l, Y / l) : Zero;
            }
        }

        /// <summary>Counter-clockwise perpendicular (−y, x).</summary>
        public Vec2 Perp => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);

        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);

        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);

        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>z component of the 3D cross product (a.x·b.y − a.y·b.x).</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>ω × r for a scalar angular velocity ω (rad/s): (−ω·r.y, ω·r.x).</summary>
        public static Vec2 Cross(float w, Vec2 r) => new Vec2(-w * r.Y, w * r.X);

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>Rotates by the angle whose cosine and sine are given.</summary>
        public static Vec2 Rotate(Vec2 v, float cos, float sin) => new Vec2(cos * v.X - sin * v.Y, sin * v.X + cos * v.Y);

        /// <summary>Rotates by minus the angle whose cosine and sine are given.</summary>
        public static Vec2 InvRotate(Vec2 v, float cos, float sin) => new Vec2(cos * v.X + sin * v.Y, -sin * v.X + cos * v.Y);

        public static Vec2 FromAngle(float radians) => new Vec2(MathF.Cos(radians), MathF.Sin(radians));

        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();

        public override string ToString()
        {
            return "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
