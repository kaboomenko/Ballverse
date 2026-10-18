using System;

namespace Ballverse.Core.Sim
{
    /// <summary>
    /// Scalar helpers shared by the solver. All pure float functions with a fixed evaluation order.
    /// <para>
    /// Trigonometry is implemented here (range reduction + minimax polynomials, float arithmetic and IEEE-exact
    /// <c>Floor</c>/<c>Sqrt</c> only) instead of <c>System.MathF.Sin/Cos/Atan2/Asin</c>: those map to the platform libm under
    /// IL2CPP, which an OS update may change, and a replay recorded before such an update must still reproduce its hashes
    /// (spec §6.5 "своя тригонометрия", §7.4). Accuracy is within a few float ulps (|error| ≤ 5e-7 on the solver's ranges).
    /// The solver keeps angles wrapped to (−π, π], so the reduction never meets large arguments; beyond ±1e5 rad the
    /// argument is wrapped first (with the precision such an angle has in float anyway).
    /// </para>
    /// </summary>
    public static class SimMath
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float HalfPi = 1.57079632679490f;
        public const float QuarterPi = 0.785398163397448f;
        public const float DegToRad = Pi / 180f;

        const float TwoOverPi = 0.636619772367581f;
        // π/2 split into three floats (Cody–Waite): k·A and k·B are exact for the |k| this reduction meets.
        const float HalfPiA = 1.5703125f;
        const float HalfPiB = 4.837512969970703125e-4f;
        const float HalfPiC = 7.54978995489188216e-8f;
        const float Tan3PiOver8 = 2.414213562373095f;
        const float TanPiOver8 = 0.4142135623730950f;

        /// <summary>Sine and cosine of <paramref name="x"/> (radians) in one reduction.</summary>
        public static void SinCos(float x, out float sin, out float cos)
        {
            if (!IsFinite(x))
            {
                sin = float.NaN;
                cos = float.NaN;
                return;
            }
            if (x > 1e5f || x < -1e5f) x = WrapPi(x);
            float kf = MathF.Floor(x * TwoOverPi + 0.5f);
            int k = (int)kf;
            float r = ((x - kf * HalfPiA) - kf * HalfPiB) - kf * HalfPiC;
            float z = r * r;
            // Minimax polynomials on [−π/4, π/4] (Cephes sinf / cosf coefficients).
            float s = ((-1.9515295891e-4f * z + 8.3321608736e-3f) * z - 1.6666654611e-1f) * z * r + r;
            float c = ((2.443315711809948e-5f * z - 1.388731625493765e-3f) * z + 4.166664568298827e-2f) * z * z - 0.5f * z + 1f;
            switch (k & 3)
            {
                case 0:
                    sin = s;
                    cos = c;
                    break;
                case 1:
                    sin = c;
                    cos = -s;
                    break;
                case 2:
                    sin = -s;
                    cos = -c;
                    break;
                default:
                    sin = -c;
                    cos = s;
                    break;
            }
        }

        public static float Sin(float x)
        {
            SinCos(x, out float s, out _);
            return s;
        }

        public static float Cos(float x)
        {
            SinCos(x, out _, out float c);
            return c;
        }

        /// <summary>Arc tangent in (−π/2, π/2).</summary>
        public static float Atan(float x)
        {
            if (float.IsNaN(x)) return float.NaN;
            bool negative = x < 0f;
            if (negative) x = -x;
            float y;
            if (x > Tan3PiOver8)
            {
                y = HalfPi;
                x = -1f / x;
            }
            else if (x > TanPiOver8)
            {
                y = QuarterPi;
                x = (x - 1f) / (x + 1f);
            }
            else
            {
                y = 0f;
            }
            float z = x * x;
            // Cephes atanf polynomial on [−tan(π/8), tan(π/8)].
            y += (((8.05374449538e-2f * z - 1.38776856032e-1f) * z + 1.99777106478e-1f) * z - 3.33329491539e-1f) * z * x + x;
            return negative ? -y : y;
        }

        /// <summary>Angle of the vector (x, y) in [−π, π]; (0, 0) gives 0.</summary>
        public static float Atan2(float y, float x)
        {
            if (float.IsNaN(x) || float.IsNaN(y)) return float.NaN;
            if (x == 0f)
            {
                if (y > 0f) return HalfPi;
                if (y < 0f) return -HalfPi;
                return 0f;
            }
            float ax = x < 0f ? -x : x;
            float ay = y < 0f ? -y : y;
            float a;
            // Keep the ratio ≤ 1 so the reduction inside Atan stays accurate.
            if (ay <= ax)
            {
                a = Atan(ay / ax);
            }
            else
            {
                a = HalfPi - Atan(ax / ay);
            }
            if (x < 0f) a = Pi - a;
            return y < 0f ? -a : a;
        }

        /// <summary>Arc sine in [−π/2, π/2]; NaN outside [−1, 1].</summary>
        public static float Asin(float x)
        {
            if (float.IsNaN(x)) return float.NaN;
            bool negative = x < 0f;
            float a = negative ? -x : x;
            if (a > 1f) return float.NaN;
            bool big = a > 0.5f;
            float z, r;
            if (big)
            {
                z = 0.5f * (1f - a);
                r = MathF.Sqrt(z);
            }
            else
            {
                z = a * a;
                r = a;
            }
            // Cephes asinf polynomial.
            float p = ((((4.2163199048e-2f * z + 2.4181311049e-2f) * z + 4.5470025998e-2f) * z + 7.4953002686e-2f) * z + 1.6666752422e-1f) * z * r + r;
            if (big) p = HalfPi - (p + p);
            return negative ? -p : p;
        }

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
