namespace Ballverse.Core.Layout
{
    /// <summary>One candidate corner position of the PiP window.</summary>
    public readonly struct PipAnchor
    {
        public PipCorner Corner { get; }
        public IntRect Rect { get; }

        /// <summary>Pixels of the window covered by the platform UI mask union.</summary>
        public long MaskOverlap { get; }

        public PipAnchor(PipCorner corner, IntRect rect, long maskOverlap)
        {
            Corner = corner;
            Rect = rect;
            MaskOverlap = maskOverlap;
        }
    }
}
