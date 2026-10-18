using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Immutable integer pixel rectangle. Coordinate system of the whole Layout module: origin at the top-left
    /// corner of the canvas, X to the right, Y down. Rectangles are half-open: [X, Right) x [Y, Bottom).
    /// Two rectangles that only touch along an edge do not intersect.
    /// </summary>
    public readonly struct IntRect : IEquatable<IntRect>
    {
        public static readonly IntRect Empty = new IntRect(0, 0, 0, 0);

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public IntRect(int x, int y, int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), "width must be >= 0");
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), "height must be >= 0");
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>Rectangle from its edges; right/bottom are exclusive. Inverted edges give an empty rectangle.</summary>
        public static IntRect FromEdges(int left, int top, int right, int bottom)
        {
            return new IntRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        public int Right => X + Width;
        public int Bottom => Y + Height;
        public long Area => (long)Width * Height;
        public bool IsEmpty => Width == 0 || Height == 0;
        public IntVec2 Position => new IntVec2(X, Y);
        public IntVec2 Size => new IntVec2(Width, Height);

        /// <summary>Exact geometric center (may be a half pixel).</summary>
        public FloatVec2 Center => new FloatVec2(X + Width * 0.5f, Y + Height * 0.5f);

        /// <summary>True if the pixel at (px, py) lies inside (half-open).</summary>
        public bool Contains(int px, int py)
        {
            return px >= X && px < Right && py >= Y && py < Bottom;
        }

        /// <summary>True if <paramref name="other"/> lies fully inside this rectangle. An empty rect is inside any rect that contains its position.</summary>
        public bool Contains(IntRect other)
        {
            return other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
        }

        /// <summary>True if the two rectangles share a positive area.</summary>
        public bool Intersects(IntRect other)
        {
            return !IsEmpty && !other.IsEmpty
                && other.X < Right && X < other.Right
                && other.Y < Bottom && Y < other.Bottom;
        }

        /// <summary>Overlap of two rectangles, or <see cref="Empty"/> if they do not intersect.</summary>
        public IntRect Intersection(IntRect other)
        {
            if (!Intersects(other)) return Empty;
            int l = Math.Max(X, other.X);
            int t = Math.Max(Y, other.Y);
            int r = Math.Min(Right, other.Right);
            int b = Math.Min(Bottom, other.Bottom);
            return new IntRect(l, t, r - l, b - t);
        }

        public long IntersectionArea(IntRect other)
        {
            return Intersection(other).Area;
        }

        public IntRect Offset(int dx, int dy)
        {
            return new IntRect(X + dx, Y + dy, Width, Height);
        }

        public IntRect WithPosition(int x, int y)
        {
            return new IntRect(x, y, Width, Height);
        }

        /// <summary>
        /// Shifts the rectangle (size unchanged) so it lies inside a W x H canvas. A rectangle larger than the canvas
        /// is aligned to the top/left edge on that axis.
        /// </summary>
        public IntRect ClampInside(int canvasWidth, int canvasHeight)
        {
            int x = Math.Max(0, Math.Min(X, canvasWidth - Width));
            int y = Math.Max(0, Math.Min(Y, canvasHeight - Height));
            return new IntRect(x, y, Width, Height);
        }

        /// <summary>Same rectangle reflected across the vertical center line of a canvas of the given width.</summary>
        public IntRect MirrorX(int canvasWidth)
        {
            return new IntRect(canvasWidth - Right, Y, Width, Height);
        }

        /// <summary>Same rectangle reflected across the horizontal center line of a canvas of the given height.</summary>
        public IntRect MirrorY(int canvasHeight)
        {
            return new IntRect(X, canvasHeight - Bottom, Width, Height);
        }

        public bool Equals(IntRect other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object obj)
        {
            return obj is IntRect other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X;
                h = h * 397 ^ Y;
                h = h * 397 ^ Width;
                h = h * 397 ^ Height;
                return h;
            }
        }

        public static bool operator ==(IntRect a, IntRect b) => a.Equals(b);
        public static bool operator !=(IntRect a, IntRect b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0},{1} {2}x{3}]", X, Y, Width, Height);
        }
    }
}
