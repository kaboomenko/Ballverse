using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// The single compositor geometry entry point (spec §7.5: "Предпросмотр в студии использует тот же компоновщик"):
    /// preview and export call it with the same project state and only a different canvas size, so both draw the same
    /// frame (spec §3.3 acceptance: every layout x swap x resolution matches). Pure and deterministic.
    /// <para>
    /// The plan depends only on the request (layout, video size and content settings, masks, text bounds, canvas), not on
    /// the frame time, so callers compute it when one of those inputs changes and reuse it for every frame drawn at that
    /// canvas size; calling it per frame works but allocates (a few hundred bytes) each time.
    /// </para>
    /// </summary>
    public static class FrameComposer
    {
        /// <summary>
        /// Computes slots (<see cref="LayoutCalculator.ComputeSlots"/>), the video mapping (<see cref="ContentMapper.Map"/>)
        /// and the watermark (spec §3.10): the corner is decided on the 1080x1920 reference canvas from the PiP window and
        /// the text bounds (<see cref="WatermarkLayout.ChooseCorner"/>), so every canvas size picks the same corner, and the
        /// rect is then placed on this canvas (<see cref="WatermarkLayout.PlaceAt"/>).
        /// </summary>
        public static FramePlan Compose(FrameRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            int w = request.CanvasWidth, h = request.CanvasHeight;
            FrameLayout layout = LayoutCalculator.ComputeSlots(w, h, request.Layout);

            ContentMapping mapping = null;
            IntRect videoDest = IntRect.Empty;
            if (layout.HasVideo && request.HasVideoSource)
            {
                IntRect slot = layout.Video.Rect;
                mapping = ContentMapper.Map(request.VideoSourceSize.X, request.VideoSourceSize.Y, slot.Width, slot.Height,
                    request.VideoContent);
                videoDest = mapping.Destination.Offset(slot.X, slot.Y);
            }

            WatermarkPlacement watermark = default(WatermarkPlacement);
            if (request.ShowWatermark)
            {
                const int refW = PixelMath.ReferenceWidth, refH = PixelMath.ReferenceHeight;
                bool isReference = FrameRequest.IsReference(w, h);
                IReadOnlyList<FloatRect> text = request.TextBounds;
                var occupied = new List<IntRect>(text.Count + 1);
                var refOccupied = isReference ? occupied : new List<IntRect>(text.Count + 1);
                if (layout.HasWindow)
                {
                    occupied.Add(layout.Window.Rect);
                    if (!isReference) refOccupied.Add(LayoutCalculator.PipWindowRect(refW, refH, request.Layout.Pip));
                }
                for (int i = 0; i < text.Count; i++)
                {
                    occupied.Add(text[i].ToPixels(w, h));
                    if (!isReference) refOccupied.Add(text[i].ToPixels(refW, refH));
                }
                WatermarkCorner corner = WatermarkLayout.ChooseCorner(request.WatermarkLogoAspect, request.ReferenceMasks, refOccupied);
                watermark = WatermarkLayout.PlaceAt(w, h, request.WatermarkLogoAspect, request.Masks, occupied, corner);
            }
            return new FramePlan(layout, mapping, videoDest, request.ShowWatermark, watermark);
        }
    }
}
