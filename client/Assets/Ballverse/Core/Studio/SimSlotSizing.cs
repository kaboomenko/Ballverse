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
    /// (<see cref="ContentMapper"/>). The target grows with the zoom up to <c>maxZoomDensity</c> × the unzoomed slot (one texel
    /// per output pixel up to that zoom); beyond it the picture is magnified. Preview passes 1 (a full-canvas slot never exceeds
    /// the canvas: ≤ 8.3 MB at 1080×1920); export passes <see cref="ExportMaxZoomDensity"/> so a pinch-zoomed simulation stays
    /// sharp in the file up to 2× (≤ 33 MB at 2160×3840 for a full 1080p slot) while no release-1 renderer draws viewports.</item>
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
        /// <summary>
        /// Export: the whole-arena target follows the zoom up to 2× the unzoomed slot (QA round 2: with the release-1 renderer, which
        /// has no viewport, a 2× zoom otherwise left 2×2 output pixels per texel). Bounded so a full-canvas 1080p slot stays at most
        /// 2160×3840 (§7.11).
        /// </summary>
        public const float ExportMaxZoomDensity = 2f;

        const int NominalScale = 1000;

        /// <param name="slotWidth">Slot width in canvas pixels.</param>
        /// <param name="slotHeight">Slot height in canvas pixels.</param>
        /// <param name="content">Simulation slot content (<see cref="ProjectComposition.BuildSimContent"/>).</param>
        /// <param name="maxSide">Cap of the target's longer side.</param>
        /// <param name="viewport">The renderer draws <see cref="SimSlotRender.Viewport"/> windows (see <c>ISimFrameRenderer.SupportsViewport</c>).</param>
        /// <param name="maxZoomDensity">Whole-arena mode: the zoom up to which the target grows to keep one texel per output pixel
        /// (≥ 1; 1 = sized for the unzoomed slot). Ignored with a viewport (always the slot's resolution).</param>
        public static SimSlotRender Compute(int slotWidth, int slotHeight, SlotContent content, int maxSide = DefaultMaxSide, bool viewport = false,
            float maxZoomDensity = 1f)
        {
            if (slotWidth <= 0 || slotHeight <= 0) throw new ArgumentOutOfRangeException(nameof(slotWidth), "slot size must be > 0");
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (maxSide < MinSide) throw new ArgumentOutOfRangeException(nameof(maxSide));
            if (float.IsNaN(maxZoomDensity) || maxZoomDensity < 1f) maxZoomDensity = 1f;
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
            // Displayed pixels per arena unit at the sizing zoom (the zoom, at most maxZoomDensity) for a nominal 9 x 16 source; the
            // target needs that many texels per unit.
            float sizingZoom = Math.Max(SlotContent.MinZoom, Math.Min(content.Zoom, maxZoomDensity));
            ContentMapping nominal = ContentMapper.Map(srcW, srcH, slotWidth, slotHeight, content.WithZoom(sizingZoom));
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
