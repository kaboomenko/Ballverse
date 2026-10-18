using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Ratio slider behaviour for L1/L2 (spec §3.3): range 30–70%, step 1%, magnets at 1/3, 1/2, 2/3.
    /// <para>
    /// <see cref="Snap(float)"/>: the raw value is clamped to [0.30, 0.70] and quantized to the 1% step (round half up);
    /// the magnet steps 33%, 50% and 67% return exactly 1/3, 1/2 and 2/3. The magnet window is therefore the half step
    /// around each magnet's step, and every integer percent from 30 to 70 stays reachable (33 and 67 as 1/3 and 2/3).
    /// </para>
    /// <para>
    /// <see cref="Snap(float, float)"/> adds an explicit distance window (e.g. a stickier magnet while dragging); a window
    /// wider than half a step makes the neighbouring steps unreachable, so the UI should commit values with
    /// <see cref="Snap(float)"/>.
    /// </para>
    /// </summary>
    public static class RatioMagnets
    {
        /// <summary>Slider step (1%).</summary>
        public const float Step = 0.01f;

        static readonly float[] s_magnets = { 1f / 3f, 0.5f, 2f / 3f };
        static readonly int[] s_magnetPercents = { 33, 50, 67 };

        /// <summary>Magnet values in ascending order: 1/3, 1/2, 2/3.</summary>
        public static float[] Magnets => (float[])s_magnets.Clone();

        /// <summary>Snaps a raw slider value to a 1% step or a magnet (spec §3.3, see class remarks).</summary>
        public static float Snap(float raw)
        {
            PixelMath.RequireFinite(raw, nameof(raw));
            float clamped = PixelMath.ClampF(raw, LayoutSpec.MinRatio, LayoutSpec.MaxRatio);
            int percent = PixelMath.RoundHalfUp(clamped * 100.0);
            for (int i = 0; i < s_magnetPercents.Length; i++)
            {
                if (percent == s_magnetPercents[i]) return s_magnets[i];
            }
            return PixelMath.ClampF(percent / 100f, LayoutSpec.MinRatio, LayoutSpec.MaxRatio);
        }

        /// <summary>
        /// Snaps onto the nearest magnet when the clamped raw value is within <paramref name="threshold"/> (&gt;= 0) of it
        /// (magnets are 1/6 apart, so at most one matches for thresholds below 1/12); otherwise <see cref="Snap(float)"/>.
        /// </summary>
        public static float Snap(float raw, float threshold)
        {
            PixelMath.RequireFinite(raw, nameof(raw));
            PixelMath.RequireFinite(threshold, nameof(threshold));
            if (threshold < 0f) throw new ArgumentOutOfRangeException(nameof(threshold), "threshold must be >= 0");
            float clamped = PixelMath.ClampF(raw, LayoutSpec.MinRatio, LayoutSpec.MaxRatio);
            float best = float.NaN;
            float bestDist = float.MaxValue;
            for (int i = 0; i < s_magnets.Length; i++)
            {
                float d = Math.Abs(clamped - s_magnets[i]);
                if (d <= threshold + 1e-6f && d < bestDist)
                {
                    best = s_magnets[i];
                    bestDist = d;
                }
            }
            return float.IsNaN(best) ? Snap(raw) : best;
        }

        /// <summary>True if the value equals one of the magnets.</summary>
        public static bool IsMagnet(float ratio)
        {
            for (int i = 0; i < s_magnets.Length; i++)
            {
                if (ratio == s_magnets[i]) return true;
            }
            return false;
        }
    }
}
