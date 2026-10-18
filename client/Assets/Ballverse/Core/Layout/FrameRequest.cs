using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Input of <see cref="FrameComposer.Compose"/>: the project's layout state plus per-canvas facts. Immutable.
    /// Preview and export build the same request from the same project and differ only in canvas size (spec §7.5).
    /// </summary>
    public sealed class FrameRequest
    {
        static readonly IntRect[] s_noRects = new IntRect[0];

        public int CanvasWidth { get; }
        public int CanvasHeight { get; }
        public LayoutSpec Layout { get; }

        /// <summary>Decoded user video frame size; 0 x 0 when no video is loaded (the V slot shows its placeholder).</summary>
        public IntVec2 VideoSourceSize { get; }

        public SlotContent VideoContent { get; }

        /// <summary>Platform UI mask union in canvas pixels. Default: all <see cref="PlatformUiMasks.Default"/> platforms.</summary>
        public RectUnion Masks { get; }

        public bool ShowWatermark { get; }
        public float WatermarkLogoAspect { get; }

        /// <summary>Measured text layer rects in canvas pixels (from the Unity text renderer), for watermark avoidance.</summary>
        public IReadOnlyList<IntRect> TextBounds { get; }

        public FrameRequest(int canvasWidth, int canvasHeight, LayoutSpec layout, IntVec2 videoSourceSize = default(IntVec2),
            SlotContent videoContent = null, bool showWatermark = true, float watermarkLogoAspect = WatermarkLayout.DefaultLogoAspect,
            RectUnion masks = null, IReadOnlyList<IntRect> textBounds = null)
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
            Masks = masks ?? PlatformUiMasks.Default.ToPixels(canvasWidth, canvasHeight);
            TextBounds = textBounds ?? s_noRects;
        }

        public bool HasVideoSource => VideoSourceSize.X > 0 && VideoSourceSize.Y > 0;
    }
}
