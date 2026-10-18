namespace Ballverse.Core.Layout
{
    /// <summary>Result of <see cref="WatermarkLayout.Place"/>, <see cref="WatermarkLayout.PlaceOnReference"/> and <see cref="WatermarkLayout.PlaceAt"/> (spec §3.10).</summary>
    public readonly struct WatermarkPlacement
    {
        public WatermarkCorner Corner { get; }

        /// <summary>Watermark rect in canvas pixels.</summary>
        public IntRect Rect { get; }

        /// <summary>Pixels of the watermark covered by occupied rects (PiP window, text layers).</summary>
        public long OccupiedOverlap { get; }

        /// <summary>Pixels of the watermark covered by platform UI masks.</summary>
        public long MaskOverlap { get; }

        /// <summary>No conflict with occupied rects nor masks.</summary>
        public bool IsClear => OccupiedOverlap == 0 && MaskOverlap == 0;

        public WatermarkPlacement(WatermarkCorner corner, IntRect rect, long occupiedOverlap, long maskOverlap)
        {
            Corner = corner;
            Rect = rect;
            OccupiedOverlap = occupiedOverlap;
            MaskOverlap = maskOverlap;
        }
    }
}
