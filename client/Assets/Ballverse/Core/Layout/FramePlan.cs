using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Geometry of one composed frame (spec §7.5 step c): slots, the user video mapping inside its slot and the watermark.
    /// Draw order: slots back to front (<see cref="FrameLayout.SlotsBackToFront"/>), divider, text layers, watermark.
    /// </summary>
    public sealed class FramePlan
    {
        readonly ContentMapping _videoMapping;

        public FrameLayout Layout { get; }

        /// <summary>True when the layout has a V slot and a video source is loaded.</summary>
        public bool HasVideoMapping => _videoMapping != null;

        public ContentMapping VideoMapping => _videoMapping ?? throw new InvalidOperationException("no video mapping");

        /// <summary>Where the video picture lands in canvas pixels (slot rect offset by the mapping destination).</summary>
        public IntRect VideoDestination { get; }

        public bool HasWatermark { get; }
        public WatermarkPlacement Watermark { get; }

        internal FramePlan(FrameLayout layout, ContentMapping videoMapping, IntRect videoDestination, bool hasWatermark,
            WatermarkPlacement watermark)
        {
            Layout = layout;
            _videoMapping = videoMapping;
            VideoDestination = videoDestination;
            HasWatermark = hasWatermark;
            Watermark = watermark;
        }
    }
}
