using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Input of <see cref="FrameComposer.Compose"/>: the project's layout state plus per-canvas facts. Immutable (list
    /// inputs are copied). Preview and export build the same request from the same project and differ only in canvas size
    /// (spec §7.5); masks and text bounds are therefore given in normalized, canvas-independent coordinates.
    /// </summary>
    public sealed class FrameRequest
    {
        static readonly FloatRect[] s_noRects = new FloatRect[0];
        static readonly IReadOnlyList<FloatRect> s_defaultMaskRects = PlatformUiMasks.Default.Union();
        static readonly RectUnion s_defaultReferenceMasks =
            RectUnion.FromNormalized(s_defaultMaskRects, PixelMath.ReferenceWidth, PixelMath.ReferenceHeight);
        static volatile CanvasMasks s_lastDefaultMasks;

        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public LayoutSpec Layout { get; }

        /// <summary>Decoded user video frame size; 0 x 0 when no video is loaded (the V slot shows its placeholder).</summary>
        public IntVec2 VideoSourceSize { get; }

        public SlotContent VideoContent { get; }

        /// <summary>
        /// Normalized platform UI mask rects (e.g. <see cref="PlatformUiMasks.Union"/> of the platforms built from Config's
        /// <c>platform_safe_zones</c>). Default: all <see cref="PlatformUiMasks.Default"/> platforms. Read-only view.
        /// </summary>
        public IReadOnlyList<FloatRect> MaskRects { get; }

        /// <summary><see cref="MaskRects"/> in canvas pixels.</summary>
        public RectUnion Masks { get; }

        /// <summary><see cref="MaskRects"/> in 1080x1920 reference pixels (watermark corner decision, spec §3.10).</summary>
        public RectUnion ReferenceMasks { get; }

        public bool ShowWatermark { get; }
        public float WatermarkLogoAspect { get; }

        /// <summary>
        /// Text layer boxes for watermark avoidance (spec §3.10), normalized to the canvas (measured by the Unity text
        /// renderer; <see cref="FloatRect.FromPixels"/> converts a pixel box). Pass the boxes of every text layer of the
        /// clip, whatever its show time, not only the layers visible at the current frame, so the watermark never jumps
        /// corners mid-video. Read-only copy of the input.
        /// </summary>
        public IReadOnlyList<FloatRect> TextBounds { get; }

        public FrameRequest(int canvasWidth, int canvasHeight, LayoutSpec layout, IntVec2 videoSourceSize = default(IntVec2),
            SlotContent videoContent = null, bool showWatermark = true, float watermarkLogoAspect = WatermarkLayout.DefaultLogoAspect,
            IReadOnlyList<FloatRect> masks = null, IReadOnlyList<FloatRect> textBounds = null)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (videoSourceSize.X < 0 || videoSourceSize.Y < 0)
                throw new ArgumentOutOfRangeException(nameof(videoSourceSize), "video size must be >= 0");
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            Layout = layout;
            VideoSourceSize = videoSourceSize;
            VideoContent = videoContent ?? SlotContent.Default;
            ShowWatermark = showWatermark;
            WatermarkLogoAspect = watermarkLogoAspect;
            if (masks == null)
            {
                MaskRects = s_defaultMaskRects;
                ReferenceMasks = s_defaultReferenceMasks;
                Masks = DefaultMasks(canvasWidth, canvasHeight);
            }
            else
            {
                MaskRects = Copy(masks);
                ReferenceMasks = RectUnion.FromNormalized(MaskRects, PixelMath.ReferenceWidth, PixelMath.ReferenceHeight);
                Masks = IsReference(canvasWidth, canvasHeight)
                    ? ReferenceMasks
                    : RectUnion.FromNormalized(MaskRects, canvasWidth, canvasHeight);
            }
            TextBounds = textBounds == null || textBounds.Count == 0 ? Array.AsReadOnly(s_noRects) : Copy(textBounds);
        }

        public bool HasVideoSource => VideoSourceSize.X > 0 && VideoSourceSize.Y > 0;

        internal static bool IsReference(int canvasWidth, int canvasHeight)
        {
            return canvasWidth == PixelMath.ReferenceWidth && canvasHeight == PixelMath.ReferenceHeight;
        }

        static IReadOnlyList<FloatRect> Copy(IReadOnlyList<FloatRect> rects)
        {
            var copy = new FloatRect[rects.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = rects[i];
            return Array.AsReadOnly(copy);
        }

        /// <summary>Default masks in canvas pixels, cached for the last canvas size (preview and export reuse one size per frame).</summary>
        static RectUnion DefaultMasks(int w, int h)
        {
            if (IsReference(w, h)) return s_defaultReferenceMasks;
            CanvasMasks last = s_lastDefaultMasks;
            if (last != null && last.Width == w && last.Height == h) return last.Masks;
            var fresh = new CanvasMasks(w, h, RectUnion.FromNormalized(s_defaultMaskRects, w, h));
            s_lastDefaultMasks = fresh;
            return fresh.Masks;
        }

        sealed class CanvasMasks
        {
            public readonly int Width;
            public readonly int Height;
            public readonly RectUnion Masks;

            public CanvasMasks(int width, int height, RectUnion masks)
            {
                Width = width;
                Height = height;
                Masks = masks;
            }
        }
    }
}
