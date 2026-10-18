using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Output frame sizes (spec §3.8): exports are 720x1280 or 1080x1920 (9:16); previews may use any size, usually the
    /// largest 9:16 rectangle that fits the preview area (spec §3.7). Layout math accepts any canvas from
    /// <see cref="MinSide"/> to <see cref="MaxSide"/> px per side.
    /// </summary>
    public static class OutputCanvas
    {
        /// <summary>Smallest canvas side the layout math supports (keeps every slot at least 1 px).</summary>
        public const int MinSide = 8;

        /// <summary>Largest canvas side (keeps all fixed-point products well inside 64-bit range).</summary>
        public const int MaxSide = 16384;

        /// <summary>Export canvas for a resolution: 720 -&gt; 720x1280, 1080 -&gt; 1080x1920 (spec §3.8).</summary>
        public static IntVec2 ForExport(ExportResolution resolution)
        {
            switch (resolution)
            {
                case ExportResolution.P720: return new IntVec2(720, 1280);
                case ExportResolution.P1080: return new IntVec2(1080, 1920);
                default: throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "unknown export resolution");
            }
        }

        /// <summary>
        /// Largest 9:16 preview canvas fitting into <paramref name="availableWidth"/> x <paramref name="availableHeight"/>:
        /// width = min(availW, floor(availH * 9 / 16)), height = min(availH, round_half_up(width * 16 / 9)).
        /// Throws if the result would be smaller than <see cref="MinSide"/>.
        /// </summary>
        public static IntVec2 FitPreview(int availableWidth, int availableHeight)
        {
            long w = Math.Min(availableWidth, (long)availableHeight * 9 / 16);
            w = Math.Min(w, MaxSide * 9L / 16);
            if (w < MinSide) throw new ArgumentOutOfRangeException(nameof(availableWidth), "preview area too small");
            long h = Math.Min(availableHeight, PixelMath.RoundDiv(w * 16, 9));
            return new IntVec2((int)w, (int)h);
        }
    }
}
