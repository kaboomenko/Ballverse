namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Safe area of the frame (spec §3.10 "внутри безопасной зоны"): the canvas inset by a fixed margin of
    /// <see cref="MarginRefPx"/> px at the 1080 reference width. Platform UI masks (<see cref="PlatformUiMasks"/>) are
    /// handled separately by the placement rules that need them (watermark, PiP magnets).
    /// </summary>
    public static class SafeArea
    {
        /// <summary>Edge margin at the reference width: 32 px (21 px at 720 width).</summary>
        public const int MarginRefPx = 32;

        /// <summary>Margin in canvas pixels (reference-scaled).</summary>
        public static int Margin(int canvasWidth)
        {
            return PixelMath.ScaleRef(MarginRefPx, canvasWidth);
        }

        /// <summary>Safe rectangle of the canvas.</summary>
        public static IntRect Rect(int canvasWidth, int canvasHeight)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            int m = Margin(canvasWidth);
            m = System.Math.Min(m, (System.Math.Min(canvasWidth, canvasHeight) - 1) / 2);
            return IntRect.FromEdges(m, m, canvasWidth - m, canvasHeight - m);
        }
    }
}
