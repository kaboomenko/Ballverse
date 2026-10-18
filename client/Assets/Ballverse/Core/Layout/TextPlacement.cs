namespace Ballverse.Core.Layout
{
    /// <summary>Anchor and scaled sizes of one text layer on a canvas (spec §3.5). Produced by <see cref="TextLayerGeometry.Place"/>.</summary>
    public readonly struct TextPlacement
    {
        /// <summary>Exact anchor point in canvas pixels, inside [0, W] x [0, H].</summary>
        public FloatVec2 Anchor { get; }

        /// <summary>Pixel containing the anchor (round half up), clamped to [0, W-1] x [0, H-1].</summary>
        public IntVec2 AnchorPixel { get; }

        /// <summary>Font size in canvas pixels (reference size scaled by canvasWidth / 1080; not rounded).</summary>
        public float FontSizePx { get; }

        /// <summary>Stroke width in canvas pixels (reference stroke scaled by canvasWidth / 1080; not rounded).</summary>
        public float StrokePx { get; }

        /// <summary>canvasWidth / 1080; the Unity side scales measured text and plate paddings by it.</summary>
        public float Scale { get; }

        public TextPlacement(FloatVec2 anchor, IntVec2 anchorPixel, float fontSizePx, float strokePx, float scale)
        {
            Anchor = anchor;
            AnchorPixel = anchorPixel;
            FontSizePx = fontSizePx;
            StrokePx = strokePx;
            Scale = scale;
        }
    }
}
