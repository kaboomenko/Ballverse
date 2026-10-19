using System;
using System.Globalization;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Rectangle in normalized frame coordinates (spec §3.3 platform masks): origin at the top-left corner of the 9:16 frame,
    /// x/w as a fraction of the width, y/h as a fraction of the height. Always inside the frame: x,y ≥ 0, w,h &gt; 0, x+w ≤ 1, y+h ≤ 1
    /// (the sums rounded to float, as stored; see <see cref="EdgeTolerance"/> for what the constructor accepts).
    /// JSON form: <c>{"x":0.84,"y":0.38,"w":0.16,"h":0.44}</c>.
    /// </summary>
    public sealed class NormalizedRect
    {
        /// <summary>
        /// How far past the frame edge <c>x + w</c> / <c>y + h</c> may reach (computed exactly, in double) and still be
        /// accepted: 1e-5 of the frame, about 0.02 px of a 1920 px side. The constructor then shrinks <see cref="W"/> /
        /// <see cref="H"/> so the stored rectangle ends at the edge, so the contract above holds for every instance.
        /// </summary>
        /// <remarks>
        /// Why a tolerance, and why in double: float sums such as <c>0.85f + 0.15f</c> or <c>0.8f + 0.2f</c> are exactly
        /// 1.0 in IEEE single precision but 1.0000000298 / 1.0000000149 when evaluated at higher precision, which the
        /// C# spec allows and Unity's Mono editor does (CoreCLR rounds every float operation to single). The former check
        /// <c>x + w &lt;= 1f</c> therefore rejected the built-in safe-zone defaults under Mono only; RemoteConfigValues'
        /// type initializer threw and took every consumer of the config down (CI, Unity tests: Services EditMode). Every
        /// comparison here widens to double explicitly (<c>(double)x + w</c>), which both runtimes evaluate identically,
        /// and the tolerance absorbs rounding by producers of the values (decimal JSON, pixel → normalized conversions).
        /// </remarks>
        public const double EdgeTolerance = 1e-5;

        public float X { get; }
        public float Y { get; }
        public float W { get; }
        public float H { get; }

        /// <exception cref="ArgumentOutOfRangeException">
        /// The rectangle is empty, not finite or leaves the frame (by more than <see cref="EdgeTolerance"/>); the message
        /// names the offending value.
        /// </exception>
        public NormalizedRect(float x, float y, float w, float h)
        {
            string problem = Problem(x, y, w, h, out string param);
            if (problem != null) throw new ArgumentOutOfRangeException(param, problem);
            X = x;
            Y = y;
            W = FitsWithin(x, w) ? w : FitSize(x);
            H = FitsWithin(y, h) ? h : FitSize(y);
        }

        /// <summary>
        /// True when the values form a non-empty rectangle inside the unit frame, within <see cref="EdgeTolerance"/>
        /// (i.e. when the constructor accepts them). Gives the same answer on every runtime (Mono, IL2CPP, CoreCLR).
        /// </summary>
        public static bool IsValid(float x, float y, float w, float h)
        {
            return Problem(x, y, w, h, out _) == null;
        }

        /// <summary>
        /// True when the rectangle already lies inside the frame exactly — <c>x + w ≤ 1</c> and <c>y + h ≤ 1</c> with the
        /// sums rounded to float — so the constructor keeps it bit for bit. Same answer on every runtime.
        /// </summary>
        internal static bool IsInsideExactly(float x, float y, float w, float h)
        {
            return IsValid(x, y, w, h) && FitsWithin(x, w) && FitsWithin(y, h);
        }

        /// <summary>
        /// Core of <see cref="IsValid"/> on the widened values: null when valid, otherwise what is wrong, with the name of the
        /// offending parameter in <paramref name="param"/>. All arithmetic is double, so no runtime can evaluate it at a
        /// different precision.
        /// </summary>
        internal static string Problem(double x, double y, double w, double h, out string param)
        {
            if (!IsFinite(x)) return Fail("x", "x must be finite, got " + F(x), out param);
            if (!IsFinite(y)) return Fail("y", "y must be finite, got " + F(y), out param);
            if (!IsFinite(w)) return Fail("w", "w must be finite, got " + F(w), out param);
            if (!IsFinite(h)) return Fail("h", "h must be finite, got " + F(h), out param);
            if (x < 0.0) return Fail("x", "x must be >= 0, got " + F(x), out param);
            if (y < 0.0) return Fail("y", "y must be >= 0, got " + F(y), out param);
            if (!(w > 0.0)) return Fail("w", "w must be > 0, got " + F(w), out param);
            if (!(h > 0.0)) return Fail("h", "h must be > 0, got " + F(h), out param);
            if (!(x < 1.0)) return Fail("x", "x must be < 1, got " + F(x), out param);
            if (!(y < 1.0)) return Fail("y", "y must be < 1, got " + F(y), out param);
            if (x + w > 1.0 + EdgeTolerance) return Fail("w", "x + w must be <= 1, got " + F(x) + " + " + F(w) + " = " + Sum(x + w), out param);
            if (y + h > 1.0 + EdgeTolerance) return Fail("h", "y + h must be <= 1, got " + F(y) + " + " + F(h) + " = " + Sum(y + h), out param);
            param = null;
            return null;
        }

        static string Problem(float x, float y, float w, float h, out string param)
        {
            return Problem((double)x, (double)y, (double)w, (double)h, out param);
        }

        /// <summary>origin + size ≤ 1 with the sum rounded to float: the double sum of two floats rounded once to float is
        /// the IEEE single sum on every runtime (53 ≥ 2·24 + 2 bits, so the double rounding is innocuous).</summary>
        static bool FitsWithin(float origin, float size)
        {
            return (float)((double)origin + size) <= 1f;
        }

        /// <summary>The largest size with origin + size ≤ 1 (float); origin is in [0, 1), so the result is &gt; 0.</summary>
        static float FitSize(float origin)
        {
            float size = (float)(1.0 - origin);
            for (int i = 0; i < 4 && !FitsWithin(origin, size); i++)
            {
                size = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(size) - 1);
            }
            return size;
        }

        static string Fail(string name, string message, out string param)
        {
            param = name;
            return "rect " + message;
        }

        /// <summary>A component (a float widened to double) as the float it is: "0.85", not "0.85000002384185791".</summary>
        static string F(double v)
        {
            return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>An exact double sum, e.g. "1.0000000298023224".</summary>
        static string Sum(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
