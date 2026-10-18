using System;

namespace Ballverse.Media
{
    /// <summary>Options of <see cref="IMediaBridge.OpenVideoReader"/>. Immutable.</summary>
    public sealed class VideoReaderOptions
    {
        /// <summary>
        /// Bounding box of the output frame in pixels, applied after rotation. The frame keeps its aspect ratio, is never
        /// upscaled, and has even dimensions. 0 = no limit on that axis. Use the slot size of the layout (or 1080×1920).
        /// </summary>
        public int MaxWidth { get; }
        public int MaxHeight { get; }

        /// <summary>Tone-map HDR (PQ/HLG) to SDR BT.709. Always true for the studio (spec §3.2); false keeps raw 8-bit conversion.</summary>
        public bool ToneMapHdrToSdr { get; }

        public VideoReaderOptions(int maxWidth, int maxHeight, bool toneMapHdrToSdr = true)
        {
            if (maxWidth < 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
            if (maxHeight < 0) throw new ArgumentOutOfRangeException(nameof(maxHeight));
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            ToneMapHdrToSdr = toneMapHdrToSdr;
        }

        /// <summary>Full-frame portrait export bound (1080×1920).</summary>
        public static readonly VideoReaderOptions Export1080 = new VideoReaderOptions(1080, 1920);

        /// <summary>
        /// Output size rule shared by all backends: fit (displayWidth × displayHeight) into the box, never upscale,
        /// round to even, minimum 2×2.
        /// </summary>
        public static void ComputeOutputSize(int displayWidth, int displayHeight, int maxWidth, int maxHeight, out int width, out int height)
        {
            if (displayWidth <= 0 || displayHeight <= 0) throw new ArgumentOutOfRangeException(nameof(displayWidth), "display size must be positive");
            double scale = 1.0;
            if (maxWidth > 0) scale = Math.Min(scale, (double)maxWidth / displayWidth);
            if (maxHeight > 0) scale = Math.Min(scale, (double)maxHeight / displayHeight);
            // floor(x + 0.5) matches Java Math.round and C lround for positive values, so all backends agree.
            width = Math.Max(2, (int)Math.Floor(displayWidth * scale + 0.5) & ~1);
            height = Math.Max(2, (int)Math.Floor(displayHeight * scale + 0.5) & ~1);
        }
    }
}
