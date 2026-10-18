using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// PiP drag magnets (spec §3.3 L3/L5, §4.6 "Перетаскивание окна PiP — положение с магнитами").
    /// <para>
    /// On release the window snaps to one of four corner anchors. Each anchor starts in its corner of the safe area
    /// (<see cref="SafeArea.MarginRefPx"/>, reduced if the window would not fit) and is slid vertically away from its corner
    /// (top corners down, bottom corners up) past platform UI masks, keeping a safe-margin gap, as long as it stays inside the
    /// safe area; if no mask-free position exists it stays in the corner. Choice: among anchors with zero mask overlap the
    /// one whose center is nearest to the dragged window center (squared pixel distance); if every anchor overlaps, the
    /// anchors with the least overlap area compete by distance. Remaining ties go to <see cref="PipCorner"/> order
    /// (TopLeft, TopRight, BottomLeft, BottomRight). Overlap is measured on the window's bounding rect (also for circles).
    /// </para>
    /// </summary>
    public static class PipMagnets
    {
        /// <summary>Four corner anchors for a window of the spec's size (normalized center of the spec is ignored).</summary>
        public static PipAnchor[] ComputeAnchors(int canvasWidth, int canvasHeight, PipSpec pip, RectUnion masks)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (pip == null) throw new ArgumentNullException(nameof(pip));
            if (masks == null) throw new ArgumentNullException(nameof(masks));
            IntVec2 size = LayoutCalculator.PipWindowSize(canvasWidth, canvasHeight, pip);
            int margin = SafeArea.Margin(canvasWidth);
            int mx = Math.Min(margin, (canvasWidth - size.X) / 2);
            int my = Math.Min(margin, (canvasHeight - size.Y) / 2);
            int left = mx, right = canvasWidth - mx - size.X;
            int top = my, bottom = canvasHeight - my - size.Y;

            var anchors = new PipAnchor[4];
            anchors[0] = Anchor(PipCorner.TopLeft, new IntRect(left, top, size.X, size.Y), true, margin, my, canvasHeight - my, masks);
            anchors[1] = Anchor(PipCorner.TopRight, new IntRect(right, top, size.X, size.Y), true, margin, my, canvasHeight - my, masks);
            anchors[2] = Anchor(PipCorner.BottomLeft, new IntRect(left, bottom, size.X, size.Y), false, margin, my, canvasHeight - my, masks);
            anchors[3] = Anchor(PipCorner.BottomRight, new IntRect(right, bottom, size.X, size.Y), false, margin, my, canvasHeight - my, masks);
            return anchors;
        }

        /// <summary>
        /// Snaps a dragged window (spec size, center at <paramref name="draggedCenter"/>, normalized, any finite value) to the
        /// best corner anchor (see class remarks).
        /// </summary>
        public static PipSnapResult Snap(int canvasWidth, int canvasHeight, PipSpec pip, FloatVec2 draggedCenter, RectUnion masks)
        {
            if (!draggedCenter.IsFinite) throw new ArgumentException("dragged center must be finite", nameof(draggedCenter));
            PipAnchor[] anchors = ComputeAnchors(canvasWidth, canvasHeight, pip, masks);
            IntRect dragged = LayoutCalculator.PipWindowRect(canvasWidth, canvasHeight, pip.WithCenter(draggedCenter));
            long dcx = 2L * dragged.X + dragged.Width;
            long dcy = 2L * dragged.Y + dragged.Height;

            long minOverlap = long.MaxValue;
            for (int i = 0; i < anchors.Length; i++) minOverlap = Math.Min(minOverlap, anchors[i].MaskOverlap);

            int best = -1;
            long bestDist = long.MaxValue;
            for (int i = 0; i < anchors.Length; i++)
            {
                if (anchors[i].MaskOverlap != minOverlap) continue;
                IntRect r = anchors[i].Rect;
                long ex = 2L * r.X + r.Width - dcx;
                long ey = 2L * r.Y + r.Height - dcy;
                long dist = ex * ex + ey * ey;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            PipAnchor chosen = anchors[best];
            FloatVec2 center = LayoutCalculator.PipCenterOf(chosen.Rect, canvasWidth, canvasHeight);
            return new PipSnapResult(chosen, pip.WithCenter(center), anchors);
        }

        static PipAnchor Anchor(PipCorner corner, IntRect start, bool down, int gap, int minY, int maxBottom, RectUnion masks)
        {
            IntRect r = MaskAvoidance.PushVertical(start, masks, down, gap, minY, maxBottom, out bool _);
            return new PipAnchor(corner, r, masks.IntersectionArea(r));
        }
    }
}
