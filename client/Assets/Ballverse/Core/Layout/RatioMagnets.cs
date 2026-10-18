using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Ratio slider behaviour for L1/L2 (spec §3.3): range 30–70%, step 1%, magnets at 1/3, 1/2, 2/3.
    /// A raw value within <see cref="SnapThreshold"/> of a magnet snaps exactly onto it (nearest magnet wins; magnets are
    /// 1/6 apart so at most one can match); otherwise the value is clamped and quantized to the 1% step.
    /// </summary>
    public static class RatioMagnets
    {
        /// <summary>Snap distance around each magnet: 0.02 (2 percentage points, i.e. two slider steps).</summary>
        public const float SnapThreshold = 0.02f;

        /// <summary>Slider step (1%).</summary>
        public const float Step = 0.01f;

        static readonly float[] s_magnets = { 1f / 3f, 0.5f, 2f / 3f };

        /// <summary>Magnet values in ascending order: 1/3, 1/2, 2/3.</summary>
        public static float[] Magnets => (float[])s_magnets.Clone();

        /// <summary>Snaps a raw slider value (spec §3.3). Uses <see cref="SnapThreshold"/>.</summary>
        public static float Snap(float raw)
        {
            return Snap(raw, SnapThreshold);
        }

        /// <summary>Snaps a raw slider value with a custom threshold (&gt;= 0).</summary>
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
            if (!float.IsNaN(best)) return best;
            int percent = PixelMath.RoundHalfUp(clamped * 100.0);
            return PixelMath.ClampF(percent / 100f, LayoutSpec.MinRatio, LayoutSpec.MaxRatio);
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
