using System;

namespace Ballverse.UI.Gestures
{
    /// <summary>
    /// iOS swipe from the left edge = back (spec §4.6 "свайп от левого края iOS"). A press that starts within
    /// <see cref="EdgeWidth"/> of the left edge and travels right by at least <see cref="MinDistance"/>, mostly
    /// horizontally, is a back swipe; it fires once, on release. Positions are panel units (≈ pt). Pure.
    /// <para>
    /// The shell does not always see the release: an element that captures the pointer (a button's Clickable, a drag)
    /// receives the pointer's events alone. Two rules keep the recognizer from going stale: a new press with the same
    /// pointer id restarts it (that press means the old one ended), and the shell calls <see cref="Cancel"/> when a
    /// dragging element takes the pointer. A press captured by a plain tap target is followed through that target, and
    /// <see cref="Move"/> says when the motion is clearly an edge swipe so the shell can take the pointer over (the tap
    /// is then cancelled instead of firing).
    /// </para>
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
        /// <summary>Rightward travel after which a tracked press is an edge swipe rather than a tap on what lies under it.</summary>
        public const float ClaimDistance = 16f;

        int _pointer = -1;
        float _x0;
        float _y0;
        float _width;

        public bool Tracking => _pointer >= 0;

        /// <summary>True while <paramref name="pointerId"/> is the tracked press.</summary>
        public bool IsTracking(int pointerId)
        {
            return _pointer >= 0 && pointerId == _pointer;
        }

        /// <summary>
        /// A press. Starts tracking when it is in the edge zone and returns true. A press with the tracked pointer id
        /// first drops the old press (its release was not seen); a press of another pointer while one is tracked is a
        /// second finger and is ignored.
        /// </summary>
        public bool Down(int pointerId, float x, float y, float screenWidth)
        {
            if (_pointer >= 0)
            {
                if (pointerId != _pointer) return false;
                _pointer = -1;
            }
            if (pointerId < 0 || !(screenWidth > 0f) || x < 0f || x > EdgeWidth) return false;
            _pointer = pointerId;
            _x0 = x;
            _y0 = y;
            _width = screenWidth;
            return true;
        }

        /// <summary>
        /// Motion of the tracked press. True when it has become an edge swipe (at least <see cref="ClaimDistance"/> to
        /// the right and mostly horizontal): the shell then takes the pointer from a tap target under the finger.
        /// </summary>
        public bool Move(int pointerId, float x, float y)
        {
            if (pointerId != _pointer || _pointer < 0) return false;
            float dx = x - _x0;
            float dy = Math.Abs(y - _y0);
            return dx >= ClaimDistance && dx >= dy * MinHorizontalRatio;
        }

        /// <summary>True when the release completes a back swipe.</summary>
        public bool Up(int pointerId, float x, float y)
        {
            if (pointerId != _pointer || _pointer < 0) return false;
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
