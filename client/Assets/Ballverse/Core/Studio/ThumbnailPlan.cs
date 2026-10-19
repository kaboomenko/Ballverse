using System;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Where the timeline track thumbnails are taken (spec §3.4: frame thumbnails under the trim handles of tracks S and V). The
    /// tracks show the whole clip / source video with the trimmed parts dimmed, so the thumbnails cover the full length and do not
    /// change with the trim: sample k is at the centre of the k-th of <c>count</c> equal parts. Pure.
    /// </summary>
    public static class ThumbnailPlan
    {
        /// <summary>Default number of thumbnails per track (a phone timeline is ~8 thumbnails wide; zoom shows more).</summary>
        public const int DefaultCount = 16;
        /// <summary>Thumbnail width, px (height follows the picture's aspect).</summary>
        public const int DefaultWidth = 96;

        /// <summary>Render-stream ticks of <paramref name="count"/> simulation thumbnails over a clip of <paramref name="tickCount"/> ticks.</summary>
        public static int[] Ticks(int tickCount, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (tickCount <= 0 || count == 0) return Array.Empty<int>();
            var ticks = new int[count];
            for (int k = 0; k < count; k++) ticks[k] = (int)Math.Min(tickCount - 1, ((2L * k + 1) * tickCount) / (2L * count));
            return ticks;
        }

        /// <summary>Source times (µs) of <paramref name="count"/> video thumbnails over a video of <paramref name="durationUs"/>.</summary>
        public static long[] Times(long durationUs, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (durationUs <= 0 || count == 0) return Array.Empty<long>();
            var times = new long[count];
            for (int k = 0; k < count; k++)
            {
                double t = (2.0 * k + 1) * durationUs / (2.0 * count);
                times[k] = Math.Min(durationUs - 1, (long)t);
            }
            return times;
        }

        /// <summary>Even thumbnail height for <paramref name="width"/> and a picture of <paramref name="pictureWidth"/> × <paramref name="pictureHeight"/>.</summary>
        public static int HeightFor(int width, int pictureWidth, int pictureHeight)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (pictureWidth <= 0 || pictureHeight <= 0) return width;
            int h = 2 * (int)Math.Round((double)width * pictureHeight / (2.0 * pictureWidth), MidpointRounding.AwayFromZero);
            return Math.Max(2, h);
        }
    }
}
