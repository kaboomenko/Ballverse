using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// The single compositor geometry entry point (spec §7.5: "Предпросмотр в студии использует тот же компоновщик"):
    /// preview and export call it with the same project state and only a different canvas size, so both draw the same
    /// frame (spec §3.3 acceptance: every layout x swap x resolution matches). Pure and deterministic.
    /// </summary>
    public static class FrameComposer
    {
        /// <summary>
        /// Computes slots (<see cref="LayoutCalculator.ComputeSlots"/>), the video mapping (<see cref="ContentMapper.Map"/>)
        /// and the watermark (<see cref="WatermarkLayout.Place"/>, avoiding the PiP window and the given text bounds).
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
                var occupied = new List<IntRect>();
                if (layout.HasWindow) occupied.Add(layout.Window.Rect);
                occupied.AddRange(request.TextBounds);
                watermark = WatermarkLayout.Place(w, h, request.WatermarkLogoAspect, request.Masks, occupied);
            }
            return new FramePlan(layout, mapping, videoDest, request.ShowWatermark, watermark);
        }
    }
}
