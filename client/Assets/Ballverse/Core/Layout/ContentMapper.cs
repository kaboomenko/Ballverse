using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Maps a source picture into a slot (spec §3.3: Cover/Fit, pan, zoom, 90° rotation, horizontal mirror; spec §4.6 gestures).
    /// <para>
    /// Model: the source is rotated clockwise by <see cref="SlotContent.Rotation"/>, then mirrored horizontally if
    /// <see cref="SlotContent.Mirror"/>, giving the displayed picture (OrientedWidth x OrientedHeight). It is scaled by
    /// s = (Cover ? max : min)(slotW/oW, slotH/oH) * zoom. Per axis: if the scaled picture is at least the slot length (always
    /// in Cover) the slot is fully covered and the visible window slides inside the picture by pan; otherwise (Fit) the
    /// picture is centered with symmetric margins and pan is ignored on that axis. Pan is clamped to [-1, 1], so Cover
    /// never shows an empty edge. In letterboxed axes the destination edges are snapped to whole pixels
    /// (left = round_half_up(margin), right = slotLength - left), a sub-pixel stretch of at most 1 px.
    /// </para>
    /// </summary>
    public static class ContentMapper
    {
        /// <summary>Maps a source of <paramref name="sourceWidth"/> x <paramref name="sourceHeight"/> into a slot.</summary>
        public static ContentMapping Map(int sourceWidth, int sourceHeight, int slotWidth, int slotHeight, SlotContent content)
        {
            Validate(sourceWidth, sourceHeight, slotWidth, slotHeight, content);
            bool swapAxes = content.Rotation == 90 || content.Rotation == 270;
            int ow = swapAxes ? sourceHeight : sourceWidth;
            int oh = swapAxes ? sourceWidth : sourceHeight;
            double s = BaseScale(ow, oh, slotWidth, slotHeight, content.Fill) * content.Zoom;
            bool cover = content.Fill == SlotFill.Cover;

            Axis(ow, slotWidth, s, content.Pan.X, cover, out double cx0, out double cx1, out int dx0, out int dx1);
            Axis(oh, slotHeight, s, content.Pan.Y, cover, out double cy0, out double cy1, out int dy0, out int dy1);

            var displayCrop = new FloatRect((float)cx0, (float)cy0, (float)(cx1 - cx0), (float)(cy1 - cy0));
            var destination = IntRect.FromEdges(dx0, dy0, dx1, dy1);

            // Source crop: map the two crop corners back into source pixels (90° rotations keep rects axis-aligned).
            DisplayToSourcePx(cx0, cy0, ow, oh, sourceWidth, sourceHeight, content, out double sx0, out double sy0);
            DisplayToSourcePx(cx1, cy1, ow, oh, sourceWidth, sourceHeight, content, out double sx1, out double sy1);
            var sourceCrop = FloatRect.FromEdges(
                (float)Math.Min(sx0, sx1), (float)Math.Min(sy0, sy1), (float)Math.Max(sx0, sx1), (float)Math.Max(sy0, sy1));

            // UV: dest-local (x, y) -> display normalized -> source normalized; affine, so sample three points.
            double a0 = cx0 / ow, aw = (cx1 - cx0) / ow, b0 = cy0 / oh, bh = (cy1 - cy0) / oh;
            DisplayToSourceUv(a0, b0, content, out double tu, out double tv);
            DisplayToSourceUv(a0 + aw, b0, content, out double xu, out double xv);
            DisplayToSourceUv(a0, b0 + bh, content, out double yu, out double yv);
            var uv = new UvTransform((float)(xu - tu), (float)(yu - tu), (float)(xv - tv), (float)(yv - tv), (float)tu, (float)tv);

            return new ContentMapping(sourceWidth, sourceHeight, ow, oh, slotWidth, slotHeight, sourceCrop, displayCrop,
                destination, uv, (float)s);
        }

        /// <summary>
        /// Drag gesture (spec §4.6): moves the content by (dx, dy) slot pixels and returns the new content settings with the
        /// pan updated and clamped. Axes without slack (letterboxed or exactly fitting) keep their pan.
        /// </summary>
        public static SlotContent PanByPixels(SlotContent content, float dx, float dy, int sourceWidth, int sourceHeight,
            int slotWidth, int slotHeight)
        {
            Validate(sourceWidth, sourceHeight, slotWidth, slotHeight, content);
            PixelMath.RequireFinite(dx, nameof(dx));
            PixelMath.RequireFinite(dy, nameof(dy));
            bool swapAxes = content.Rotation == 90 || content.Rotation == 270;
            int ow = swapAxes ? sourceHeight : sourceWidth;
            int oh = swapAxes ? sourceWidth : sourceHeight;
            double s = BaseScale(ow, oh, slotWidth, slotHeight, content.Fill) * content.Zoom;
            double halfX = (ow * s - slotWidth) / 2.0;
            double halfY = (oh * s - slotHeight) / 2.0;
            float px = content.Pan.X, py = content.Pan.Y;
            if (halfX > 1e-6) px = (float)(px + dx / halfX);
            if (halfY > 1e-6) py = (float)(py + dy / halfY);
            return content.WithPan(new FloatVec2(px, py));
        }

        /// <summary>Pinch gesture (spec §4.6): multiplies zoom by <paramref name="factor"/> (&gt; 0), clamped to 1..MaxZoom.</summary>
        public static SlotContent ZoomBy(SlotContent content, float factor)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            PixelMath.RequireFinite(factor, nameof(factor));
            if (factor <= 0f) throw new ArgumentOutOfRangeException(nameof(factor), "factor must be > 0");
            return content.WithZoom(content.Zoom * factor);
        }

        static void Validate(int sourceWidth, int sourceHeight, int slotWidth, int slotHeight, SlotContent content)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth), "source size must be > 0");
            if (slotWidth <= 0 || slotHeight <= 0) throw new ArgumentOutOfRangeException(nameof(slotWidth), "slot size must be > 0");
            if (content == null) throw new ArgumentNullException(nameof(content));
        }

        static double BaseScale(int ow, int oh, int slotW, int slotH, SlotFill fill)
        {
            double sx = (double)slotW / ow;
            double sy = (double)slotH / oh;
            return fill == SlotFill.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy);
        }

        static void Axis(int o, int len, double s, double pan, bool cover, out double crop0, out double crop1, out int d0, out int d1)
        {
            double displayed = o * s;
            if (cover || displayed >= len)
            {
                double visible = Math.Min(o, len / s);
                double slack = o - visible;
                double center = o / 2.0 - pan * slack / 2.0;
                crop0 = center - visible / 2.0;
                crop1 = center + visible / 2.0;
                if (crop0 < 0) { crop1 -= crop0; crop0 = 0; }
                if (crop1 > o) { crop0 -= crop1 - o; crop1 = o; }
                if (crop0 < 0) crop0 = 0;
                d0 = 0;
                d1 = len;
                return;
            }
            crop0 = 0;
            crop1 = o;
            int margin = PixelMath.RoundHalfUp((len - displayed) / 2.0);
            margin = Math.Min(margin, (len - 1) / 2);
            d0 = margin;
            d1 = len - margin;
        }

        static void DisplayToSourcePx(double x, double y, int ow, int oh, int sw, int sh, SlotContent c, out double sx, out double sy)
        {
            double xu = c.Mirror ? ow - x : x;
            switch (c.Rotation)
            {
                case 90: sx = y; sy = ow - xu; break;
                case 180: sx = sw - xu; sy = sh - y; break;
                case 270: sx = oh - y; sy = xu; break;
                default: sx = xu; sy = y; break;
            }
        }

        static void DisplayToSourceUv(double a, double b, SlotContent c, out double u, out double v)
        {
            double au = c.Mirror ? 1.0 - a : a;
            switch (c.Rotation)
            {
                case 90: u = b; v = 1.0 - au; break;
                case 180: u = 1.0 - au; v = 1.0 - b; break;
                case 270: u = 1.0 - b; v = au; break;
                default: u = au; v = b; break;
            }
        }
    }
}
