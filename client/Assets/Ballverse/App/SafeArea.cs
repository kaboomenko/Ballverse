using System;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Safe-area insets in pixels from each screen edge (notch, rounded corners, home indicator, punch-hole camera).
    /// Edges are named as seen by the user; Unity's bottom-left origin is converted here once.
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
        /// Builds insets from Unity's <see cref="Screen.safeArea"/> (bottom-left origin, pixels). The rect is clipped to
        /// the screen; a degenerate screen size yields <see cref="IsEmpty"/> insets.
        /// </summary>
        public static SafeAreaInsets FromUnityRect(Rect safeArea, int screenWidth, int screenHeight)
        {
            if (screenWidth <= 0 || screenHeight <= 0) return new SafeAreaInsets(0, 0, 0, 0, 0, 0);
            int xMin = Clamp(Mathf.RoundToInt(safeArea.xMin), 0, screenWidth);
            int xMax = Clamp(Mathf.RoundToInt(safeArea.xMax), xMin, screenWidth);
            int yMin = Clamp(Mathf.RoundToInt(safeArea.yMin), 0, screenHeight);
            int yMax = Clamp(Mathf.RoundToInt(safeArea.yMax), yMin, screenHeight);
            return new SafeAreaInsets(screenWidth, screenHeight,
                left: xMin, top: screenHeight - yMax, right: screenWidth - xMax, bottom: yMin);
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

    /// <summary><see cref="ISafeAreaProvider"/> polled once per frame by <see cref="Bootstrap"/> (Unity has no safe-area event).</summary>
    public sealed class SafeAreaProvider : ISafeAreaProvider
    {
        readonly Action<string, Exception> _reportError;

        public SafeAreaProvider(Action<string, Exception> reportError)
        {
            _reportError = reportError ?? throw new ArgumentNullException(nameof(reportError));
        }

        public SafeAreaInsets Current { get; private set; }

        public event Action<SafeAreaInsets> Changed;

        /// <summary>Stores <paramref name="insets"/>; raises <see cref="Changed"/> and returns true when they differ from the current value.</summary>
        public bool Update(SafeAreaInsets insets)
        {
            if (insets == Current) return false;
            Current = insets;
            AppEvents.Raise(Changed, insets, _reportError, "SafeAreaProvider.Changed");
            return true;
        }

        public bool UpdateFromScreen() =>
            Update(SafeAreaInsets.FromUnityRect(Screen.safeArea, Screen.width, Screen.height));
    }
}
