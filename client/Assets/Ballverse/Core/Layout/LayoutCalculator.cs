using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Frame geometry of the five layouts (spec §3.3), shared by preview and export (spec §7.5: one compositor; only the
    /// canvas size differs). Pure, deterministic, integer-exact.
    /// </summary>
    public static class LayoutCalculator
    {
        /// <summary>Fixed-point denominator of the ratio used in pixel math (ratio is quantized to 1e-4).</summary>
        public const int RatioDenominator = 10000;

        /// <summary>
        /// Computes the slots for a canvas (spec §3.3).
        /// <list type="bullet">
        /// <item>L1/L2: padding frame + slot S + divider + slot V tile the canvas exactly (no gaps, no overlaps, integer pixels)
        /// for any canvas. Every boundary is the half-up rounding of its exact position at this canvas size, so the same
        /// project at 720 and 1080 width agrees within 1 px after scaling. With swap the result is mirrored along the
        /// split axis (S keeps its size). Padding is limited so the content area keeps at least 2 px, the divider so that
        /// each slot keeps at least 1 px (only matters for tiny previews).</item>
        /// <item>L3/L5: the background slot covers the canvas (z 0), the window (z 1) keeps the canvas aspect (RoundedRect) or is a
        /// square (Circle), is <see cref="PipSpec.SizeFraction"/> of the canvas width wide and is clamped inside the canvas.</item>
        /// <item>L4: simulation only, full canvas.</item>
        /// </list>
        /// Reference pixel values are scaled by canvasWidth / 1080 (see <see cref="PixelMath"/>).
        /// </summary>
        public static FrameLayout ComputeSlots(int canvasWidth, int canvasHeight, LayoutSpec spec)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            var full = new IntRect(0, 0, canvasWidth, canvasHeight);
            LayoutType type = spec.EffectiveType;
            switch (type)
            {
                case LayoutType.L1:
                    return ComputeSplit(canvasWidth, canvasHeight, spec, true);
                case LayoutType.L2:
                    return ComputeSplit(canvasWidth, canvasHeight, spec, false);
                case LayoutType.L4:
                    return new FrameLayout(canvasWidth, canvasHeight, type,
                        new SlotGeometry(SlotKind.Simulation, full, SlotShape.Rect, 0, 0),
                        false, default(SlotGeometry), IntRect.Empty, full, false);
                case LayoutType.L3:
                case LayoutType.L5:
                {
                    SlotKind windowKind = type == LayoutType.L3 ? SlotKind.Video : SlotKind.Simulation;
                    SlotKind backKind = type == LayoutType.L3 ? SlotKind.Simulation : SlotKind.Video;
                    var back = new SlotGeometry(backKind, full, SlotShape.Rect, 0, 0);
                    SlotGeometry window = WindowSlot(canvasWidth, canvasHeight, spec, windowKind);
                    SlotGeometry sim = backKind == SlotKind.Simulation ? back : window;
                    SlotGeometry video = backKind == SlotKind.Video ? back : window;
                    return new FrameLayout(canvasWidth, canvasHeight, type, sim, true, video, IntRect.Empty, full, true);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(spec), type, "unknown layout");
            }
        }

        /// <summary>Ratio in 1/10000 units used by the pixel math: round_half_up(ratio * 10000), clamped to 3000–7000.</summary>
        public static int QuantizeRatio(float ratio)
        {
            PixelMath.RequireFinite(ratio, nameof(ratio));
            int q = PixelMath.RoundHalfUp((double)ratio * RatioDenominator);
            return PixelMath.Clamp(q, 3000, 7000);
        }

        /// <summary>
        /// Size of the L3/L5 window: width = round_half_up(fraction * canvasW); RoundedRect height = round_half_up(fraction * canvasH)
        /// (canvas aspect); Circle is a square of side min(width, canvasH).
        /// </summary>
        public static IntVec2 PipWindowSize(int canvasWidth, int canvasHeight, PipSpec pip)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (pip == null) throw new ArgumentNullException(nameof(pip));
            int w = PixelMath.Clamp(PixelMath.RoundHalfUp((double)pip.SizeFraction * canvasWidth), 1, canvasWidth);
            if (pip.Shape == SlotShape.Circle)
            {
                int d = Math.Min(w, canvasHeight);
                return new IntVec2(d, d);
            }
            int h = PixelMath.Clamp(PixelMath.RoundHalfUp((double)pip.SizeFraction * canvasHeight), 1, canvasHeight);
            return new IntVec2(w, h);
        }

        /// <summary>
        /// Window rect for a PiP spec: centered on <see cref="PipSpec.Center"/> (top-left = round_half_up(center*canvas - size/2))
        /// and clamped so the window stays fully inside the canvas.
        /// </summary>
        public static IntRect PipWindowRect(int canvasWidth, int canvasHeight, PipSpec pip)
        {
            IntVec2 size = PipWindowSize(canvasWidth, canvasHeight, pip);
            int x = PixelMath.RoundHalfUp((double)pip.Center.X * canvasWidth - size.X / 2.0);
            int y = PixelMath.RoundHalfUp((double)pip.Center.Y * canvasHeight - size.Y / 2.0);
            x = PixelMath.Clamp(x, 0, canvasWidth - size.X);
            y = PixelMath.Clamp(y, 0, canvasHeight - size.Y);
            return new IntRect(x, y, size.X, size.Y);
        }

        /// <summary>Normalized center that reproduces <paramref name="windowRect"/> through <see cref="PipWindowRect"/>.</summary>
        public static FloatVec2 PipCenterOf(IntRect windowRect, int canvasWidth, int canvasHeight)
        {
            return new FloatVec2(
                (float)((windowRect.X + windowRect.Width / 2.0) / canvasWidth),
                (float)((windowRect.Y + windowRect.Height / 2.0) / canvasHeight));
        }

        static SlotGeometry WindowSlot(int w, int h, LayoutSpec spec, SlotKind kind)
        {
            IntRect rect = PipWindowRect(w, h, spec.Pip);
            if (spec.Pip.Shape == SlotShape.Circle)
            {
                return new SlotGeometry(kind, rect, SlotShape.Circle, rect.Width / 2, 1);
            }
            int radius = ClampRadius(PixelMath.ScaleRef(spec.CornerRadiusPx, w), rect);
            return new SlotGeometry(kind, rect, SlotShape.RoundedRect, radius, 1);
        }

        static FrameLayout ComputeSplit(int w, int h, LayoutSpec spec, bool stacked)
        {
            int axis = stacked ? h : w;
            int cross = stacked ? w : h;

            int padScaled = PixelMath.ScaleRef(spec.PaddingPx, w);
            int padMax = (Math.Min(w, h) - 2) / 2;
            bool padClamped = padScaled > padMax;
            int pad = Math.Min(padScaled, padMax);

            // Exact positions in units of 1 / (1080 * 10000) px. pad1080 = exact padding * 1080, div1080 = exact divider * 1080.
            long ratio = QuantizeRatio(spec.Ratio);
            long pad1080 = padClamped ? (long)pad * PixelMath.ReferenceWidth : (long)spec.PaddingPx * w;
            long div1080 = (long)spec.DividerPx * w;
            long den = (long)PixelMath.ReferenceWidth * RatioDenominator;
            long firstEndNum = pad1080 * RatioDenominator + ((long)axis * PixelMath.ReferenceWidth - 2 * pad1080 - div1080) * ratio;
            long dividerEndNum = firstEndNum + div1080 * RatioDenominator;

            int c0 = pad;
            int c1 = axis - pad;
            int firstEnd = PixelMath.Clamp(PixelMath.RoundDiv(firstEndNum, den), c0 + 1, c1 - 1);
            int dividerEnd = PixelMath.Clamp(PixelMath.RoundDiv(dividerEndNum, den), firstEnd, c1 - 1);

            // Unswapped: S = [c0, firstEnd), divider = [firstEnd, dividerEnd), V = [dividerEnd, c1). Swap mirrors the axis.
            int s0 = c0, s1 = firstEnd, d0 = firstEnd, d1 = dividerEnd, v0 = dividerEnd, v1 = c1;
            if (spec.Swap)
            {
                int ns0 = axis - s1, ns1 = axis - s0;
                int nd0 = axis - d1, nd1 = axis - d0;
                int nv0 = axis - v1, nv1 = axis - v0;
                s0 = ns0; s1 = ns1; d0 = nd0; d1 = nd1; v0 = nv0; v1 = nv1;
            }

            IntRect simRect = AxisRect(stacked, s0, s1, pad, cross - pad);
            IntRect videoRect = AxisRect(stacked, v0, v1, pad, cross - pad);
            IntRect divider = d1 > d0 ? AxisRect(stacked, d0, d1, pad, cross - pad) : IntRect.Empty;
            IntRect content = AxisRect(stacked, c0, c1, pad, cross - pad);

            int radius = PixelMath.ScaleRef(spec.CornerRadiusPx, w);
            var sim = new SlotGeometry(SlotKind.Simulation, simRect, SlotShape.RoundedRect, ClampRadius(radius, simRect), 0);
            var video = new SlotGeometry(SlotKind.Video, videoRect, SlotShape.RoundedRect, ClampRadius(radius, videoRect), 0);
            return new FrameLayout(w, h, stacked ? LayoutType.L1 : LayoutType.L2, sim, true, video, divider, content, false);
        }

        static IntRect AxisRect(bool stacked, int a0, int a1, int c0, int c1)
        {
            return stacked ? IntRect.FromEdges(c0, a0, c1, a1) : IntRect.FromEdges(a0, c0, a1, c1);
        }

        static int ClampRadius(int radius, IntRect rect)
        {
            return Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
        }
    }
}
