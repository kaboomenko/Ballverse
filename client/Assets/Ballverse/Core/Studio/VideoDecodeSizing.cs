using System;
using Ballverse.Core.Layout;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Bounding box to open the user-video reader with (Media <c>VideoReaderOptions(maxWidth, maxHeight)</c>) for a video slot
    /// (spec §7.5 step b "декодер отдаёт RGBA в размере слота", §3.2 "масштабирование при компоновке"): the decoded frame is as
    /// large as the slot shows it (one source texel per output pixel after Cover/Fit, zoom and rotation) but never larger than
    /// the source, so 4K input costs no more than the slot needs and 1080p input is never upscaled by the decoder. Pure.
    /// </summary>
    public static class VideoDecodeSizing
    {
        /// <summary>Smallest box side (the reader rounds to even and needs at least 2×2).</summary>
        public const int MinSide = 16;

        /// <param name="displayWidth">Source width after the rotation metadata (Media <c>MediaInfo.DisplayWidth</c>).</param>
        /// <param name="displayHeight">Source height after the rotation metadata.</param>
        /// <param name="slotWidth">Video slot width in canvas pixels.</param>
        /// <param name="slotHeight">Video slot height in canvas pixels.</param>
        /// <param name="content">Rotation, mirror, fill, pan and zoom of the slot (<see cref="ProjectComposition.BuildVideoContent"/>).</param>
        /// <returns>Box (width, height), even, inside the display size.</returns>
        public static IntVec2 Compute(int displayWidth, int displayHeight, int slotWidth, int slotHeight, SlotContent content)
        {
            if (displayWidth <= 0 || displayHeight <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth), "display size must be > 0");
            if (slotWidth <= 0 || slotHeight <= 0) throw new ArgumentOutOfRangeException(nameof(slotWidth), "slot size must be > 0");
            if (content == null) throw new ArgumentNullException(nameof(content));
            ContentMapping m = ContentMapper.Map(displayWidth, displayHeight, slotWidth, slotHeight, content);
            double scale = Math.Min(1.0, m.Scale);
            if (!(scale > 0)) scale = 1.0;
            // The tolerance keeps float noise (1080 / 1920 · 1920 = 1080.0001) from adding two pixels.
            int w = EvenUp(Math.Ceiling(displayWidth * scale - 1e-3));
            int h = EvenUp(Math.Ceiling(displayHeight * scale - 1e-3));
            w = Math.Min(Math.Max(MinSide, w), Math.Max(MinSide, displayWidth & ~1));
            h = Math.Min(Math.Max(MinSide, h), Math.Max(MinSide, displayHeight & ~1));
            return new IntVec2(w, h);
        }

        /// <summary>
        /// Decode box of the studio preview's realtime reader: what a full 1080×1920 frame needs, halved (a phone preview area is
        /// ≤ ~540 px wide), with the zoom rounded up to a power of two (1, 2, 4, 8) so a pinch gesture reopens the decoder at most a
        /// few times, and only when it zooms in (see <see cref="Covers"/>). Slot, layout and canvas changes do not change it.
        /// </summary>
        public static IntVec2 PreviewBox(int displayWidth, int displayHeight, SlotContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            float zoom = 1f;
            while (zoom < content.Zoom && zoom < SlotContent.MaxZoom) zoom *= 2f;
            IntVec2 box = Compute(displayWidth, displayHeight, 1080, 1920, content.WithZoom(Math.Min(SlotContent.MaxZoom, zoom)).WithPan(FloatVec2.Zero));
            return new IntVec2(Math.Max(MinSide, (box.X / 2) & ~1), Math.Max(MinSide, (box.Y / 2) & ~1));
        }

        /// <summary>True when a reader opened with box <paramref name="open"/> serves a request for <paramref name="needed"/> (no reopen).</summary>
        public static bool Covers(IntVec2 open, IntVec2 needed)
        {
            return open.X >= needed.X && open.Y >= needed.Y;
        }

        static int EvenUp(double v)
        {
            long n = (long)v;
            if ((n & 1) != 0) n++;
            return (int)Math.Min(int.MaxValue - 1, n);
        }
    }
}
