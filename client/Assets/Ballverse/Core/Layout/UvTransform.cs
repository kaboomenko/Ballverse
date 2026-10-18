using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// 2x3 affine map from destination-local normalized coordinates (x, y in [0,1] over the destination rect) to source
    /// texture coordinates (u, v in [0,1] over the decoded frame), both with a top-left origin:
    /// <c>u = M00*x + M01*y + Tx; v = M10*x + M11*y + Ty</c>. Encodes crop, rotation and mirror, so a shader samples
    /// the source with one matrix (spec §7.5 step c). Use <see cref="ToBottomLeftOrigin"/> for Unity UV conventions.
    /// </summary>
    public readonly struct UvTransform : IEquatable<UvTransform>
    {
        public static readonly UvTransform Identity = new UvTransform(1f, 0f, 0f, 1f, 0f, 0f);

        public float M00 { get; }
        public float M01 { get; }
        public float M10 { get; }
        public float M11 { get; }
        public float Tx { get; }
        public float Ty { get; }

        public UvTransform(float m00, float m01, float m10, float m11, float tx, float ty)
        {
            M00 = m00;
            M01 = m01;
            M10 = m10;
            M11 = m11;
            Tx = tx;
            Ty = ty;
        }

        public FloatVec2 Apply(FloatVec2 p)
        {
            return new FloatVec2(M00 * p.X + M01 * p.Y + Tx, M10 * p.X + M11 * p.Y + Ty);
        }

        /// <summary>
        /// Same mapping expressed with bottom-left origins on both sides (v' = 1 - v, y' = 1 - y), as Unity texture UVs use.
        /// </summary>
        public UvTransform ToBottomLeftOrigin()
        {
            // u = M00 x + M01 (1 - y') + Tx ; v' = 1 - (M10 x + M11 (1 - y') + Ty)
            return new UvTransform(M00, -M01, -M10, M11, M01 + Tx, 1f - M11 - Ty);
        }

        public bool ApproximatelyEquals(UvTransform o, float eps)
        {
            return Math.Abs(M00 - o.M00) <= eps && Math.Abs(M01 - o.M01) <= eps && Math.Abs(M10 - o.M10) <= eps
                && Math.Abs(M11 - o.M11) <= eps && Math.Abs(Tx - o.Tx) <= eps && Math.Abs(Ty - o.Ty) <= eps;
        }

        public bool Equals(UvTransform o)
        {
            return M00.Equals(o.M00) && M01.Equals(o.M01) && M10.Equals(o.M10) && M11.Equals(o.M11)
                && Tx.Equals(o.Tx) && Ty.Equals(o.Ty);
        }

        public override bool Equals(object obj)
        {
            return obj is UvTransform o && Equals(o);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = M00.GetHashCode();
                h = h * 397 ^ M01.GetHashCode();
                h = h * 397 ^ M10.GetHashCode();
                h = h * 397 ^ M11.GetHashCode();
                h = h * 397 ^ Tx.GetHashCode();
                h = h * 397 ^ Ty.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(UvTransform a, UvTransform b) => a.Equals(b);
        public static bool operator !=(UvTransform a, UvTransform b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "u={0}x+{1}y+{2}; v={3}x+{4}y+{5}", M00, M01, Tx, M10, M11, Ty);
        }
    }
}
