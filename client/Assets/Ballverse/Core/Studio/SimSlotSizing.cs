using System;
using Ballverse.Core.Layout;

namespace Ballverse.Core.Studio
{
    /// <summary>Render target size of the simulation picture and how it maps into its slot (see <see cref="SimSlotSizing"/>).</summary>
    public readonly struct SimSlotRender
    {
        /// <summary>Size of the render texture the simulation renderer draws the whole arena into (9:16, even sides).</summary>
        public IntVec2 TargetSize { get; }
        /// <summary>Mapping of that texture into the slot (crop, letterbox, UV) for the compositor.</summary>
        public ContentMapping Mapping { get; }

        public SimSlotRender(IntVec2 targetSize, ContentMapping mapping)
        {
            TargetSize = targetSize;
            Mapping = mapping;
        }
    }

    /// <summary>
    /// Size of the simulation render target for a slot (spec §7.5 step a "рендер в RT слота S в разрешении слота", §3.3 slot fill /
    /// pan / zoom of the simulation). The renderer always draws the full 9:16 arena; the compositor then places that picture
    /// into the slot exactly like the user video (<see cref="ContentMapper"/>: Cover/Fit, pan, zoom). The target is sized so the
    /// visible part lands at about one texel per output pixel, capped at <c>maxSide</c> (large zooms are then slightly soft
    /// instead of allocating huge textures). Pure; the same inputs give the same size for preview and export of the same canvas.
    /// </summary>
    public static class SimSlotSizing
    {
        /// <summary>Arena aspect: 9 × 16 (spec §2.1, <see cref="SimConstants.ArenaWidth"/> × <see cref="SimConstants.ArenaHeight"/>).</summary>
        public const int AspectWidth = 9;
        public const int AspectHeight = 16;
        /// <summary>Smallest target side (keeps the renderer's buffers valid for tiny preview slots).</summary>
        public const int MinSide = 16;
        /// <summary>Default cap of the longer side (4096 is supported by every GLES 3.1 / Metal / Vulkan device of tier C).</summary>
        public const int DefaultMaxSide = 4096;

        /// <param name="slotWidth">Slot width in canvas pixels.</param>
        /// <param name="slotHeight">Slot height in canvas pixels.</param>
        /// <param name="content">Simulation slot content (<see cref="ProjectComposition.BuildSimContent"/>).</param>
        /// <param name="maxSide">Cap of the target's longer side.</param>
        public static SimSlotRender Compute(int slotWidth, int slotHeight, SlotContent content, int maxSide = DefaultMaxSide)
        {
            if (slotWidth <= 0 || slotHeight <= 0) throw new ArgumentOutOfRangeException(nameof(slotWidth), "slot size must be > 0");
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (maxSide < MinSide) throw new ArgumentOutOfRangeException(nameof(maxSide));
            // Displayed pixels per arena unit for a nominal 9 x 16 source; the target needs that many texels per unit.
            ContentMapping nominal = ContentMapper.Map(AspectWidth * 1000, AspectHeight * 1000, slotWidth, slotHeight, content);
            double unitsPerSide = nominal.Scale * 1000.0;                      // texels per 1/9 of the arena width
            double height = Math.Ceiling(unitsPerSide * AspectHeight);
            double limit = maxSide;
            if (height > limit) height = limit;
            int h = Math.Max(MinSide, (int)height & ~1);
            int w = Math.Max(MinSide, (int)Math.Round(h * (double)AspectWidth / AspectHeight, MidpointRounding.AwayFromZero) & ~1);
            ContentMapping mapping = ContentMapper.Map(w, h, slotWidth, slotHeight, content);
            return new SimSlotRender(new IntVec2(w, h), mapping);
        }
    }
}
