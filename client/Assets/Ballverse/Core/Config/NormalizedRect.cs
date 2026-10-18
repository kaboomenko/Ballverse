using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Rectangle in normalized frame coordinates (spec §3.3 platform masks): origin at the top-left corner of the 9:16 frame,
    /// x/w as a fraction of the width, y/h as a fraction of the height. Always inside the frame: x,y ≥ 0, w,h &gt; 0, x+w ≤ 1, y+h ≤ 1.
    /// JSON form: <c>{"x":0.84,"y":0.38,"w":0.16,"h":0.44}</c>.
    /// </summary>
    public sealed class NormalizedRect
    {
        public float X { get; }
        public float Y { get; }
        public float W { get; }
        public float H { get; }

        /// <exception cref="ArgumentOutOfRangeException">The rectangle is empty, not finite or leaves the frame.</exception>
        public NormalizedRect(float x, float y, float w, float h)
        {
            if (!IsValid(x, y, w, h)) throw new ArgumentOutOfRangeException(nameof(x), "rect must lie inside 0..1 with positive size");
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        /// <summary>True when the values form a non-empty rectangle inside the unit frame.</summary>
        public static bool IsValid(float x, float y, float w, float h)
        {
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(w) || !IsFinite(h)) return false;
            return x >= 0f && y >= 0f && w > 0f && h > 0f && x + w <= 1f && y + h <= 1f;
        }

        static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }
    }
}
