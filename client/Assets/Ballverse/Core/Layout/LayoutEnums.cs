namespace Ballverse.Core.Layout
{
    /// <summary>Shape of the floating window in L3/L5 (spec §3.3).</summary>
    public enum PipShape
    {
        RoundedRect = 0,
        Circle = 1,
    }

    /// <summary>Content shown by a slot: S (simulation) or V (user video), spec §3.3.</summary>
    public enum SlotKind
    {
        Simulation = 0,
        Video = 1,
    }

    /// <summary>How the compositor clips a slot.</summary>
    public enum SlotShape
    {
        /// <summary>Plain rectangle (full-frame slots).</summary>
        Rect = 0,
        /// <summary>Rectangle with a uniform corner radius (may be 0).</summary>
        RoundedRect = 1,
        /// <summary>Circle inscribed in a square slot rect.</summary>
        Circle = 2,
    }

    /// <summary>Corner anchors of the PiP magnets (spec §3.3, §4.6). The declaration order is the deterministic tie-break order.</summary>
    public enum PipCorner
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
    }

    /// <summary>Watermark corner (spec §3.10). The declaration order is the tie-break order (top-left preferred).</summary>
    public enum WatermarkCorner
    {
        TopLeft = 0,
        TopRight = 1,
    }
}
