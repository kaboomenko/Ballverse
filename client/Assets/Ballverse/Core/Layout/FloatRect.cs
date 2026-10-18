using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Immutable float rectangle (top-left origin, Y down). Used for normalized rectangles (0..1 of the canvas,
    /// e.g. platform UI masks) and for sub-pixel source crops. Equality is exact; use <see cref="ApproximatelyEquals"/> in tests.
    /// </summary>
    public readonly struct FloatRect : IEquatable<FloatRect>
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public FloatRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static FloatRect FromEdges(float left, float top, float right, float bottom)
        {
            return new FloatRect(left, top, right - left, bottom - top);
        }

        public float Right => X + Width;
        public float Bottom => Y + Height;
        public FloatVec2 Center => new FloatVec2(X + Width * 0.5f, Y + Height * 0.5f);
        public bool IsEmpty => !(Width > 0f) || !(Height > 0f);

        /// <summary>
        /// Converts a normalized rectangle to canvas pixels by rounding each edge half-up independently
        /// (spec §3.3 masks), clipped to the canvas. Adjacent normalized rects therefore stay adjacent in pixels.
        /// Each normalized edge is clamped to [0, 1] before scaling, so huge finite values cannot overflow. NaN throws.
        /// </summary>
        public IntRect ToPixels(int canvasWidth, int canvasHeight)
        {
            int l = Edge(X, canvasWidth);
            int t = Edge(Y, canvasHeight);
            int r = Edge((double)X + Width, canvasWidth);
            int b = Edge((double)Y + Height, canvasHeight);
            return IntRect.FromEdges(l, t, r, b);
        }

        /// <summary>
        /// Normalized rect (fractions of the canvas) of a pixel rect on a canvas of the given size.
        /// <para>
        /// Not for text bounds measured per canvas: a box measured at a preview size is quantized to that canvas's pixels
        /// (about 3 reference px at 360 width), which can flip the watermark corner decision between preview and export
        /// (spec §3.10). Text bounds for <see cref="FrameRequest"/> are measured once in the 1080x1920 reference layout and
        /// converted with <see cref="FromReferencePixels(FloatRect)"/>, and the same list goes into every request.
        /// </para>
        /// </summary>
        public static FloatRect FromPixels(IntRect rect, int canvasWidth, int canvasHeight)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(canvasWidth), "canvas must be > 0");
            return new FloatRect((float)((double)rect.X / canvasWidth), (float)((double)rect.Y / canvasHeight),
                (float)((double)rect.Width / canvasWidth), (float)((double)rect.Height / canvasHeight));
        }

        /// <summary>
        /// Normalized rect of a box given in 1080x1920 reference pixels, e.g. a text layer's bounds measured by the Unity
        /// text renderer in the reference layout (spec §3.5 sizes are defined at the 1080 reference), with sub-pixel float
        /// bounds kept. This is the supported way to build <see cref="FrameRequest"/> text bounds (spec §3.10, §7.5): one
        /// reference measurement per text layer, the same normalized list for preview and export. Non-finite values throw.
        /// </summary>
        public static FloatRect FromReferencePixels(FloatRect referencePixels)
        {
            PixelMath.RequireFinite(referencePixels.X, nameof(referencePixels));
            PixelMath.RequireFinite(referencePixels.Y, nameof(referencePixels));
            PixelMath.RequireFinite(referencePixels.Width, nameof(referencePixels));
            PixelMath.RequireFinite(referencePixels.Height, nameof(referencePixels));
            const double w = PixelMath.ReferenceWidth, h = PixelMath.ReferenceHeight;
            return new FloatRect((float)(referencePixels.X / w), (float)(referencePixels.Y / h),
                (float)(referencePixels.Width / w), (float)(referencePixels.Height / h));
        }

        /// <summary>Integer overload of <see cref="FromReferencePixels(FloatRect)"/>.</summary>
        public static FloatRect FromReferencePixels(IntRect referencePixels)
        {
            return FromPixels(referencePixels, PixelMath.ReferenceWidth, PixelMath.ReferenceHeight);
        }

        public bool ApproximatelyEquals(FloatRect other, float epsilon)
        {
            return Math.Abs(X - other.X) <= epsilon && Math.Abs(Y - other.Y) <= epsilon
                && Math.Abs(Width - other.Width) <= epsilon && Math.Abs(Height - other.Height) <= epsilon;
        }

        public bool Equals(FloatRect other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Width.Equals(other.Width) && Height.Equals(other.Height);
        }

        public override bool Equals(object obj)
        {
            return obj is FloatRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X.GetHashCode();
                h = h * 397 ^ Y.GetHashCode();
                h = h * 397 ^ Width.GetHashCode();
                h = h * 397 ^ Height.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(FloatRect a, FloatRect b) => a.Equals(b);
        public static bool operator !=(FloatRect a, FloatRect b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0},{1} {2}x{3}]", X, Y, Width, Height);
        }

        static int Edge(double normalized, int size)
        {
            if (double.IsNaN(normalized)) throw new ArgumentException("rect must not be NaN");
            double n = normalized < 0.0 ? 0.0 : (normalized > 1.0 ? 1.0 : normalized);
            return PixelMath.RoundHalfUp(n * size);
        }
    }
}
