using System;

namespace Ballverse.Core.Sim
{
    /// <summary>Scalar helpers shared by the solver. All pure float functions with a fixed evaluation order.</summary>
    public static class SimMath
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float HalfPi = 1.57079632679490f;
        public const float DegToRad = Pi / 180f;

        /// <summary>Wraps an angle to [0, 2π).</summary>
        public static float Wrap2Pi(float a)
        {
            if (a >= 0f && a < TwoPi) return a;
            float r = a - TwoPi * MathF.Floor(a / TwoPi);
            // Floor rounding can land exactly on 2π for tiny negative inputs.
            if (r >= TwoPi || r < 0f) r = 0f;
            return r;
        }

        /// <summary>Wraps an angle to (−π, π].</summary>
        public static float WrapPi(float a)
        {
            if (a > -Pi && a <= Pi) return a;
            float r = Wrap2Pi(a);
            return r > Pi ? r - TwoPi : r;
        }

        public static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        /// <summary>True when the angle lies on the counter-clockwise arc that starts at <paramref name="start"/> and spans <paramref name="span"/> radians.</summary>
        public static bool AngleInArc(float angle, float start, float span)
        {
            return Wrap2Pi(angle - start) <= span;
        }

        internal static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        internal static bool IsFinite(Vec2 v)
        {
            return IsFinite(v.X) && IsFinite(v.Y);
        }
    }
}
