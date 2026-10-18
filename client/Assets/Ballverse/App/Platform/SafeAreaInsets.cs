using System;

namespace Ballverse.Platform
{
    /// <summary>
    /// Safe-area insets in pixels from each screen edge (notch, rounded corners, home indicator, punch-hole camera).
    /// Edges are named as seen by the user; Unity's bottom-left origin is converted once, in
    /// <see cref="FromBottomLeftRect"/>.
    /// </summary>
    public readonly struct SafeAreaInsets : IEquatable<SafeAreaInsets>
    {
        public SafeAreaInsets(int screenWidth, int screenHeight, int left, int top, int right, int bottom)
        {
            ScreenWidth = screenWidth;
            ScreenHeight = screenHeight;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int ScreenWidth { get; }
        public int ScreenHeight { get; }
        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public bool IsEmpty => ScreenWidth <= 0 || ScreenHeight <= 0;

        /// <summary>Insets as fractions of the screen size (0..1), convenient for UI layout in reference units.</summary>
        public float LeftNormalized => ScreenWidth > 0 ? (float)Left / ScreenWidth : 0f;
        public float RightNormalized => ScreenWidth > 0 ? (float)Right / ScreenWidth : 0f;
        public float TopNormalized => ScreenHeight > 0 ? (float)Top / ScreenHeight : 0f;
        public float BottomNormalized => ScreenHeight > 0 ? (float)Bottom / ScreenHeight : 0f;

        /// <summary>
        /// Builds insets from a safe-area rectangle with a bottom-left origin in pixels (Unity's Screen.safeArea:
        /// xMin, yMin, xMax, yMax). The rect is clipped to the screen; a degenerate screen size yields
        /// <see cref="IsEmpty"/> insets. Coordinates are rounded like Unity's Mathf.RoundToInt (to even on .5).
        /// </summary>
        public static SafeAreaInsets FromBottomLeftRect(float xMin, float yMin, float xMax, float yMax, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0) return new SafeAreaInsets(0, 0, 0, 0, 0, 0);
            int left = Clamp(Round(xMin), 0, screenWidth);
            int right = Clamp(Round(xMax), left, screenWidth);
            int bottom = Clamp(Round(yMin), 0, screenHeight);
            int top = Clamp(Round(yMax), bottom, screenHeight);
            return new SafeAreaInsets(screenWidth, screenHeight,
                left: left, top: screenHeight - top, right: screenWidth - right, bottom: bottom);
        }

        static int Round(float v)
        {
            if (float.IsNaN(v)) return 0;
            double r = Math.Round((double)v);
            return r <= int.MinValue ? int.MinValue : r >= int.MaxValue ? int.MaxValue : (int)r;
        }

        static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        public bool Equals(SafeAreaInsets other) =>
            ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight &&
            Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

        public override bool Equals(object obj) => obj is SafeAreaInsets other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = ScreenWidth;
                h = h * 397 ^ ScreenHeight;
                h = h * 397 ^ Left;
                h = h * 397 ^ Top;
                h = h * 397 ^ Right;
                h = h * 397 ^ Bottom;
                return h;
            }
        }

        public static bool operator ==(SafeAreaInsets a, SafeAreaInsets b) => a.Equals(b);
        public static bool operator !=(SafeAreaInsets a, SafeAreaInsets b) => !a.Equals(b);

        public override string ToString() =>
            $"{ScreenWidth}x{ScreenHeight} L{Left} T{Top} R{Right} B{Bottom}";
    }

    /// <summary>Current safe area and its changes (rotation is locked to portrait, but split-view, resizes and foldables still change it).</summary>
    public interface ISafeAreaProvider
    {
        SafeAreaInsets Current { get; }

        event Action<SafeAreaInsets> Changed;
    }
}
