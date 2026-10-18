using System;

namespace Ballverse.UI.Layout
{
    /// <summary>"Magnets" of sliders (spec §3.3: proportion 30–70% with magnets at 33/50/67%).</summary>
    public static class MagnetSnap
    {
        public static readonly int[] ProportionMagnets = { 33, 50, 67 };
        public const int ProportionMin = 30;
        public const int ProportionMax = 70;
        /// <summary>Distance in percent at which a magnet captures the value.</summary>
        public const int ProportionRadius = 2;

        public static int Snap(int value, int[] magnets, int radius)
        {
            if (magnets == null) return value;
            int best = value;
            int bestD = int.MaxValue;
            foreach (int m in magnets)
            {
                int d = Math.Abs(m - value);
                if (d <= radius && d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        /// <summary>Split proportion of layouts L1/L2: clamped to 30–70 and captured by the magnets.</summary>
        public static int SnapProportion(int percent)
        {
            int clamped = Math.Max(ProportionMin, Math.Min(ProportionMax, percent));
            return Snap(clamped, ProportionMagnets, ProportionRadius);
        }
    }
}
