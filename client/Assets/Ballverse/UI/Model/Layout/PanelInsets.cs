using System;
using Ballverse.Platform;

namespace Ballverse.UI.Layout
{
    /// <summary>
    /// Safe-area insets in panel units (top, right, bottom, left). The device insets come from the platform layer
    /// (<see cref="SafeAreaInsets"/>, screen pixels from each edge, provided by <see cref="ISafeAreaProvider"/>); the UI
    /// only scales them to panel units, so there is one conversion of Unity's safe-area rectangle in the app.
    /// </summary>
    public readonly struct PanelInsets : IEquatable<PanelInsets>
    {
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
        public float Left { get; }

        public PanelInsets(float top, float right, float bottom, float left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        /// <summary>True when a panel width is usable for <see cref="FromDevice"/> (laid out, positive).</summary>
        public static bool IsUsablePanelWidth(float panelWidth)
        {
            return !float.IsNaN(panelWidth) && !float.IsInfinity(panelWidth) && panelWidth > 0f;
        }

        /// <summary>
        /// Device insets (pixels) scaled to panel units: the panel spans the screen width, so one pixel is
        /// <paramref name="panelWidth"/> / screen width units. Unknown device insets (<see cref="SafeAreaInsets.IsEmpty"/>)
        /// or an unusable panel width give zero insets.
        /// </summary>
        public static PanelInsets FromDevice(SafeAreaInsets device, float panelWidth)
        {
            if (device.IsEmpty || !IsUsablePanelWidth(panelWidth)) return default;
            float s = panelWidth / device.ScreenWidth;
            return new PanelInsets(Math.Max(0, device.Top) * s, Math.Max(0, device.Right) * s,
                Math.Max(0, device.Bottom) * s, Math.Max(0, device.Left) * s);
        }

        public bool Equals(PanelInsets o)
        {
            return Math.Abs(Top - o.Top) < 0.01f && Math.Abs(Right - o.Right) < 0.01f
                && Math.Abs(Bottom - o.Bottom) < 0.01f && Math.Abs(Left - o.Left) < 0.01f;
        }

        public override bool Equals(object obj)
        {
            return obj is PanelInsets s && Equals(s);
        }

        public override int GetHashCode()
        {
            return Top.GetHashCode() ^ Bottom.GetHashCode() * 31;
        }
    }

    /// <summary>Box fitting for the 9:16 studio preview and similar fixed-aspect slots.</summary>
    public static class AspectFit
    {
        /// <summary>Largest width×height with aspect <paramref name="aspect"/> (w/h) inside the given box.</summary>
        public static void Fit(float boxWidth, float boxHeight, float aspect, out float width, out float height)
        {
            if (boxWidth <= 0 || boxHeight <= 0 || aspect <= 0)
            {
                width = 0;
                height = 0;
                return;
            }
            if (boxWidth / boxHeight > aspect)
            {
                height = boxHeight;
                width = boxHeight * aspect;
            }
            else
            {
                width = boxWidth;
                height = boxWidth / aspect;
            }
        }

        /// <summary>
        /// UV scale/offset that maps a destination rectangle of aspect <paramref name="dstAspect"/> onto a source of
        /// aspect <paramref name="srcAspect"/> with "cover" (crop) fitting: <c>uvSrc = uvDst * scale + offset</c>.
        /// </summary>
        public static void CoverUv(float srcAspect, float dstAspect, out float scaleX, out float scaleY, out float offsetX, out float offsetY)
        {
            scaleX = 1f;
            scaleY = 1f;
            if (srcAspect > 0 && dstAspect > 0)
            {
                if (srcAspect > dstAspect) scaleX = dstAspect / srcAspect;
                else scaleY = srcAspect / dstAspect;
            }
            offsetX = (1f - scaleX) * 0.5f;
            offsetY = (1f - scaleY) * 0.5f;
        }
    }
}
