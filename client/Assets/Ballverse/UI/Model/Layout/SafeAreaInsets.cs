using System;

namespace Ballverse.UI.Layout
{
    /// <summary>Safe-area insets in panel units (top, right, bottom, left).</summary>
    public readonly struct SafeAreaInsets : IEquatable<SafeAreaInsets>
    {
        public float Top { get; }
        public float Right { get; }
        public float Bottom { get; }
        public float Left { get; }

        public SafeAreaInsets(float top, float right, float bottom, float left)
        {
            Top = top;
            Right = right;
            Bottom = bottom;
            Left = left;
        }

        /// <summary>
        /// Insets from the screen size and the safe rectangle in screen pixels (origin bottom-left, as reported by the
        /// engine), scaled to panel units by <paramref name="panelUnitsPerPixel"/>. Negative values are clamped to 0.
        /// </summary>
        public static SafeAreaInsets FromScreen(float screenWidth, float screenHeight, float safeX, float safeY,
            float safeWidth, float safeHeight, float panelUnitsPerPixel)
        {
            float left = Math.Max(0f, safeX);
            float bottom = Math.Max(0f, safeY);
            float right = Math.Max(0f, screenWidth - (safeX + safeWidth));
            float top = Math.Max(0f, screenHeight - (safeY + safeHeight));
            float s = panelUnitsPerPixel > 0 ? panelUnitsPerPixel : 1f;
            return new SafeAreaInsets(top * s, right * s, bottom * s, left * s);
        }

        public bool Equals(SafeAreaInsets o)
        {
            return Math.Abs(Top - o.Top) < 0.01f && Math.Abs(Right - o.Right) < 0.01f
                && Math.Abs(Bottom - o.Bottom) < 0.01f && Math.Abs(Left - o.Left) < 0.01f;
        }

        public override bool Equals(object obj)
        {
            return obj is SafeAreaInsets s && Equals(s);
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
