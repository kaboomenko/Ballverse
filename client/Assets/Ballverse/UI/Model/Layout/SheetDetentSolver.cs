using System;

namespace Ballverse.UI.Layout
{
    /// <summary>Where a released bottom sheet goes.</summary>
    public readonly struct SheetTarget
    {
        /// <summary>The sheet closes (swipe down past the threshold or a fast fling from the lowest detent).</summary>
        public bool Dismiss { get; }
        /// <summary>Index into the detent heights when not dismissing.</summary>
        public int DetentIndex { get; }

        public SheetTarget(bool dismiss, int detentIndex)
        {
            Dismiss = dismiss;
            DetentIndex = detentIndex;
        }

        public static SheetTarget ToDismiss => new SheetTarget(true, -1);
        public static SheetTarget ToDetent(int index) => new SheetTarget(false, index);
    }

    /// <summary>
    /// Snap logic of the two-detent bottom sheet (spec §4.3: 50% / 90%; §4.6: swipe down closes / collapses). Heights
    /// are visible sheet heights in panel units, ascending. Pure.
    /// </summary>
    public static class SheetDetentSolver
    {
        /// <summary>Release speed (panel units per second) that counts as a fling.</summary>
        public const float FlingVelocity = 900f;
        /// <summary>Released below this fraction of the lowest detent: dismiss.</summary>
        public const float DismissFraction = 0.6f;

        /// <param name="detents">Visible heights of the detents, ascending, at least one.</param>
        /// <param name="visibleHeight">Visible height at release.</param>
        /// <param name="velocityDown">Release velocity, positive downwards (closing).</param>
        /// <param name="dismissible">False for sheets that cannot be swiped away.</param>
        public static SheetTarget Solve(float[] detents, float visibleHeight, float velocityDown, bool dismissible)
        {
            if (detents == null || detents.Length == 0) throw new ArgumentException("At least one detent is required", nameof(detents));
            int nearest = Nearest(detents, visibleHeight);
            if (velocityDown > FlingVelocity)
            {
                int below = HighestBelow(detents, visibleHeight);
                if (below >= 0) return SheetTarget.ToDetent(below);
                return dismissible ? SheetTarget.ToDismiss : SheetTarget.ToDetent(0);
            }
            if (velocityDown < -FlingVelocity)
            {
                int above = LowestAbove(detents, visibleHeight);
                return SheetTarget.ToDetent(above >= 0 ? above : detents.Length - 1);
            }
            if (dismissible && visibleHeight < detents[0] * DismissFraction) return SheetTarget.ToDismiss;
            return SheetTarget.ToDetent(nearest);
        }

        /// <summary>Visible height while dragging: follows the finger, with resistance above the highest detent.</summary>
        public static float DragHeight(float startHeight, float dragDown, float maxHeight)
        {
            float h = startHeight - dragDown;
            if (h > maxHeight) h = maxHeight + (h - maxHeight) * 0.25f;
            return Math.Max(0f, h);
        }

        static int Nearest(float[] detents, float h)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < detents.Length; i++)
            {
                float d = Math.Abs(detents[i] - h);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        static int HighestBelow(float[] detents, float h)
        {
            for (int i = detents.Length - 1; i >= 0; i--)
                if (detents[i] < h - 1f) return i;
            return -1;
        }

        static int LowestAbove(float[] detents, float h)
        {
            for (int i = 0; i < detents.Length; i++)
                if (detents[i] > h + 1f) return i;
            return -1;
        }
    }
}
