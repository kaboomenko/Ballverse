using System;
using Ballverse.Core.Layout;

namespace Ballverse.Core.Studio
{
    /// <summary>Render target size of the simulation picture and how it maps into its slot (see <see cref="SimSlotSizing"/>).</summary>
    public readonly struct SimSlotRender
    {
        /// <summary>Size of the render texture the simulation renderer draws into (even sides).</summary>
        public IntVec2 TargetSize { get; }
        /// <summary>Mapping of that texture into the slot (crop, letterbox, UV) for the compositor.</summary>
        public ContentMapping Mapping { get; }
        /// <summary>
        /// Part of the 9:16 arena picture the target shows, normalized with a top-left origin (y down): (0, 0, 1, 1) for the whole
        /// arena; the visible window when the renderer draws a viewport (<see cref="SimSlotSizing.Compute"/> with
        /// <c>viewport: true</c>).
        /// </summary>
        public FloatRect Viewport { get; }

        public SimSlotRender(IntVec2 targetSize, ContentMapping mapping)
            : this(targetSize, mapping, new FloatRect(0f, 0f, 1f, 1f))
        {
        }

        public SimSlotRender(IntVec2 targetSize, ContentMapping mapping, FloatRect viewport)
        {
            TargetSize = targetSize;
            Mapping = mapping;
            Viewport = viewport;
        }

        /// <summary>True when the target shows only a window of the arena.</summary>
        public bool IsViewport => Viewport.X != 0f || Viewport.Y != 0f || Viewport.Width != 1f || Viewport.Height != 1f;
    }

    /// <summary>
    /// Size of the simulation render target for a slot (spec §7.5 step a "рендер в RT слота S в разрешении слота", §3.3 slot fill /
    /// pan / zoom of the simulation, §7.11 memory budgets). Two modes:
    /// <list type="bullet">
    /// <item>viewport (the renderer can draw a window of the arena): the target is the visible window at one texel per output
    /// pixel — the slot's resolution — and maps 1:1 into the slot's destination rect;</item>
    /// <item>whole arena (default): the renderer draws the full 9:16 arena and the compositor places it like the user video
    /// (<see cref="ContentMapper"/>). The target is sized for zoom 1 (one texel per output pixel of the unzoomed slot): zooming
    /// in magnifies that picture instead of growing the target, so a full-canvas slot never exceeds the canvas (≤ 8.3 MB at
    /// 1080×1920, plus the renderer's own buffers at that size).</item>
    /// </list>
    /// Pure; the same inputs give the same result for preview and export of the same canvas.
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

        const int NominalScale = 1000;

        /// <param name="slotWidth">Slot width in canvas pixels.</param>
        /// <param name="slotHeight">Slot height in canvas pixels.</param>
        /// <param name="content">Simulation slot content (<see cref="ProjectComposition.BuildSimContent"/>).</param>
        /// <param name="maxSide">Cap of the target's longer side.</param>
        /// <param name="viewport">The renderer draws <see cref="SimSlotRender.Viewport"/> windows (see <c>ISimFrameRenderer.SupportsViewport</c>).</param>
        public static SimSlotRender Compute(int slotWidth, int slotHeight, SlotContent content, int maxSide = DefaultMaxSide, bool viewport = false)
        {
            if (slotWidth <= 0 || slotHeight <= 0) throw new ArgumentOutOfRangeException(nameof(slotWidth), "slot size must be > 0");
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (maxSide < MinSide) throw new ArgumentOutOfRangeException(nameof(maxSide));
            int srcW = AspectWidth * NominalScale, srcH = AspectHeight * NominalScale;
            if (viewport)
            {
                ContentMapping visible = ContentMapper.Map(srcW, srcH, slotWidth, slotHeight, content);
                FloatRect crop = visible.SourceCrop;
                var window = new FloatRect(crop.X / srcW, crop.Y / srcH, crop.Width / srcW, crop.Height / srcH);
                IntVec2 size = Fit(visible.Destination.Width, visible.Destination.Height, maxSide);
                // The window has the destination's aspect: Fit at zoom 1 puts it exactly on the destination rect (Cover slots:
                // the whole slot; Fit slots: the same letterbox). Mirror is applied by the mapping, the window is in source space.
                var place = new SlotContent(0, content.Mirror, SlotFill.Fit, FloatVec2.Zero, 1f);
                if (!visible.HasLetterbox) place = place.WithFill(SlotFill.Cover); // even-pixel rounding then crops, never bars
                return new SimSlotRender(size, ContentMapper.Map(size.X, size.Y, slotWidth, slotHeight, place), window);
            }
            // Displayed pixels per arena unit at zoom 1 for a nominal 9 x 16 source; the target needs that many texels per unit.
            ContentMapping nominal = ContentMapper.Map(srcW, srcH, slotWidth, slotHeight, content.WithZoom(SlotContent.MinZoom));
            double unitsPerSide = nominal.Scale * NominalScale;                // texels per 1/9 of the arena width
            double height = Math.Ceiling(unitsPerSide * AspectHeight);
            if (height > maxSide) height = maxSide;
            int h = Math.Max(MinSide, (int)height & ~1);
            int w = Math.Max(MinSide, (int)Math.Round(h * (double)AspectWidth / AspectHeight, MidpointRounding.AwayFromZero) & ~1);
            ContentMapping mapping = ContentMapper.Map(w, h, slotWidth, slotHeight, content);
            return new SimSlotRender(new IntVec2(w, h), mapping);
        }

        /// <summary>Even size of a <paramref name="width"/> × <paramref name="height"/> rect, longer side capped, aspect kept.</summary>
        static IntVec2 Fit(int width, int height, int maxSide)
        {
            double scale = Math.Min(1.0, (double)maxSide / Math.Max(1, Math.Max(width, height)));
            int w = Math.Max(MinSide, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero) & ~1);
            int h = Math.Max(MinSide, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero) & ~1);
            return new IntVec2(w, h);
        }
    }
}
