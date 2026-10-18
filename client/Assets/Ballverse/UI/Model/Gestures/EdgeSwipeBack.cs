using System;

namespace Ballverse.UI.Gestures
{
    /// <summary>
    /// iOS swipe from the left edge = back (spec §4.6 "свайп от левого края iOS"). A press that starts within
    /// <see cref="EdgeWidth"/> of the left edge and travels right by at least <see cref="MinDistance"/>, mostly
    /// horizontally, is a back swipe; it fires once, on release. Positions are panel units (≈ pt). Pure.
    /// </summary>
    public sealed class EdgeSwipeBack
    {
        /// <summary>Width of the edge zone where the swipe must start.</summary>
        public const float EdgeWidth = 20f;
        /// <summary>Horizontal travel needed (also at least <see cref="MinWidthFraction"/> of the screen width).</summary>
        public const float MinDistance = 72f;
        public const float MinWidthFraction = 0.2f;
        /// <summary>The horizontal travel must be at least this many times the vertical travel.</summary>
        public const float MinHorizontalRatio = 2f;

        int _pointer = -1;
        float _x0;
        float _y0;
        float _width;

        public bool Tracking => _pointer >= 0;

        /// <summary>Starts tracking when the press is in the edge zone. Returns true when it is tracked.</summary>
        public bool Down(int pointerId, float x, float y, float screenWidth)
        {
            if (_pointer >= 0 || !(screenWidth > 0f) || x < 0f || x > EdgeWidth) return false;
            _pointer = pointerId;
            _x0 = x;
            _y0 = y;
            _width = screenWidth;
            return true;
        }

        /// <summary>True when the release completes a back swipe.</summary>
        public bool Up(int pointerId, float x, float y)
        {
            if (pointerId != _pointer) return false;
            _pointer = -1;
            float dx = x - _x0;
            float dy = Math.Abs(y - _y0);
            float need = Math.Max(MinDistance, _width * MinWidthFraction);
            return dx >= need && dx >= dy * MinHorizontalRatio;
        }

        public void Cancel(int pointerId)
        {
            if (pointerId == _pointer) _pointer = -1;
        }
    }
}
