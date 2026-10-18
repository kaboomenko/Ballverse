using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Everything the generator needs to score one mode: weights, pleasant event density and quality threshold
    /// (spec §2.4 step 4–5; one entry of Remote Config <c>generation_weights</c>, spec §9.3).
    /// </summary>
    public sealed class SpectacleProfile
    {
        public SpectacleWeights Weights { get; }
        public DensityRange Density { get; }

        /// <summary>
        /// Minimum <see cref="SpectacleBreakdown.Total"/> for a candidate to "pass" (spec §2.4 step 5).
        /// Expressed in the same units as the total, so it must be retuned together with the weights.
        /// </summary>
        public double QualityThreshold { get; }

        /// <summary>Largest accepted |threshold| (guards against a broken Remote Config value).</summary>
        public const double MaxAbsThreshold = 1000.0;

        /// <exception cref="ArgumentNullException">weights is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">threshold is not valid (<see cref="IsValidThreshold"/>).</exception>
        /// <exception cref="ArgumentException">density is invalid (e.g. <c>default(DensityRange)</c>).</exception>
        public SpectacleProfile(SpectacleWeights weights, DensityRange density, double qualityThreshold)
        {
            if (!IsValidThreshold(qualityThreshold))
                throw new ArgumentOutOfRangeException(nameof(qualityThreshold));
            if (!density.IsValidRange) throw new ArgumentException("Density range is invalid", nameof(density));
            Weights = weights ?? throw new ArgumentNullException(nameof(weights));
            Density = density;
            QualityThreshold = qualityThreshold;
        }

        /// <summary>True for a finite threshold with |threshold| ≤ <see cref="MaxAbsThreshold"/>.</summary>
        public static bool IsValidThreshold(double threshold)
        {
            return !double.IsNaN(threshold) && Math.Abs(threshold) <= MaxAbsThreshold;
        }
    }
}
