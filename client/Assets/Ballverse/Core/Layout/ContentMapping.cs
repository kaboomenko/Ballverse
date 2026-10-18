namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Result of <see cref="ContentMapper.Map"/>: which part of the source is shown and where it lands inside the slot.
    /// Immutable.
    /// </summary>
    public sealed class ContentMapping
    {
        public int SourceWidth { get; }
        public int SourceHeight { get; }

        /// <summary>Source size after rotation (90/270 swap width and height).</summary>
        public int OrientedWidth { get; }
        public int OrientedHeight { get; }

        public int SlotWidth { get; }
        public int SlotHeight { get; }

        /// <summary>Visible region in source (decoded, unrotated) pixels. Always inside [0, SourceWidth] x [0, SourceHeight].</summary>
        public FloatRect SourceCrop { get; }

        /// <summary>Visible region in displayed pixels (after rotation and mirror), inside [0, OrientedWidth] x [0, OrientedHeight].</summary>
        public FloatRect DisplayCrop { get; }

        /// <summary>
        /// Where the visible region is drawn, in slot-local pixels. Equals the full slot in Cover mode; in Fit mode it is
        /// centered with symmetric letterbox margins (filled with the margin background, spec §3.3).
        /// </summary>
        public IntRect Destination { get; }

        /// <summary>Destination-local normalized coordinates -&gt; source UV (top-left origin).</summary>
        public UvTransform Uv { get; }

        /// <summary>Displayed pixels per source pixel (Cover/Fit scale times zoom).</summary>
        public float Scale { get; }

        /// <summary>True when the destination does not cover the whole slot (Fit letterboxing).</summary>
        public bool HasLetterbox => Destination.X != 0 || Destination.Y != 0
            || Destination.Width != SlotWidth || Destination.Height != SlotHeight;

        internal ContentMapping(int sourceWidth, int sourceHeight, int orientedWidth, int orientedHeight, int slotWidth,
            int slotHeight, FloatRect sourceCrop, FloatRect displayCrop, IntRect destination, UvTransform uv, float scale)
        {
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            OrientedWidth = orientedWidth;
            OrientedHeight = orientedHeight;
            SlotWidth = slotWidth;
            SlotHeight = slotHeight;
            SourceCrop = sourceCrop;
            DisplayCrop = displayCrop;
            Destination = destination;
            Uv = uv;
            Scale = scale;
        }
    }
}
