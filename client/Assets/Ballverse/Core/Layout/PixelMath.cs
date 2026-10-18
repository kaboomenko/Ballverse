using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Deterministic pixel arithmetic shared by the compositor geometry (spec §7.5: one compositor for preview and export).
    /// <para>
    /// Reference-scale rule: every pixel value stored in the project model (divider, padding, corner radius, text size,
    /// stroke, margins) is defined at the 1080-wide reference canvas (1080x1920, spec §3.8) and is scaled to any canvas by
    /// <c>canvasWidth / 1080</c>. Integer snapping always rounds half up (towards +infinity) using integer arithmetic where
    /// possible, so preview and export of the same project compute bit-identical rectangles for the same canvas size.
    /// </para>
    /// </summary>
    public static class PixelMath
    {
        /// <summary>Width of the reference canvas at which project pixel values are defined (spec §3.8, 1080x1920).</summary>
        public const int ReferenceWidth = 1080;

        /// <summary>Height of the reference canvas.</summary>
        public const int ReferenceHeight = 1920;

        /// <summary>floor(v + 0.5). NaN and infinities are rejected.</summary>
        public static int RoundHalfUp(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("value must be finite", nameof(v));
            return (int)Math.Floor(v + 0.5);
        }

        /// <summary>floor(a / b) for b &gt; 0 (C# integer division truncates towards zero).</summary>
        public static long FloorDiv(long a, long b)
        {
            if (b <= 0) throw new ArgumentOutOfRangeException(nameof(b), "divisor must be > 0");
            long q = a / b;
            if (a % b != 0 && a < 0) q--;
            return q;
        }

        /// <summary>Exact round-half-up of num / den for den &gt; 0: floor((2*num + den) / (2*den)).</summary>
        public static int RoundDiv(long num, long den)
        {
            return (int)FloorDiv(2 * num + den, 2 * den);
        }

        /// <summary>
        /// Scales a reference pixel value (defined at 1080 width) to a canvas: round_half_up(refPx * canvasWidth / 1080).
        /// E.g. 6 px -&gt; 4 px at 720 width, 48 px -&gt; 32 px.
        /// </summary>
        public static int ScaleRef(int refPx, int canvasWidth)
        {
            return RoundDiv((long)refPx * canvasWidth, ReferenceWidth);
        }

        /// <summary>Unrounded reference scaling for values that stay float (text sizes, strokes).</summary>
        public static float ScaleRefF(float refPx, int canvasWidth)
        {
            return (float)((double)refPx * canvasWidth / ReferenceWidth);
        }

        internal static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        internal static float ClampF(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        internal static void RequireCanvas(int canvasWidth, int canvasHeight)
        {
            if (canvasWidth < OutputCanvas.MinSide || canvasHeight < OutputCanvas.MinSide)
            {
                throw new ArgumentOutOfRangeException(nameof(canvasWidth),
                    "canvas sides must be >= " + OutputCanvas.MinSide + " px");
            }
            if (canvasWidth > OutputCanvas.MaxSide || canvasHeight > OutputCanvas.MaxSide)
            {
                throw new ArgumentOutOfRangeException(nameof(canvasWidth),
                    "canvas sides must be <= " + OutputCanvas.MaxSide + " px");
            }
        }

        internal static void RequireFinite(float v, string name)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new ArgumentException(name + " must be finite", name);
        }
    }
}
