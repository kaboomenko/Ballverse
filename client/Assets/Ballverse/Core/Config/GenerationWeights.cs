using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// One mode's entry of <c>generation_weights</c> (spec §2.4, §9.3): optionally the spectacle score weights w1…w5 of
    /// <c>score = w1·fitDuration + w2·eventDensity + w3·finishTension + w4·coverage − w5·stagnation</c>, and optionally the
    /// pleasant event density range and the quality threshold that are tuned together with them (the threshold is in the
    /// units of the weighted total).
    /// <para>JSON form (the only schema of this key, see <c>REMOTE_CONFIG_KEYS.md</c>):
    /// <c>{"w1":1,"w2":0.5,"w3":1,"w4":0.25,"w5":0.75,"densityMin":1,"densityMax":8,"qualityThreshold":1.6}</c> — the five weights
    /// all together or none of them, the other three fields each optional, at least one field present — or the short form
    /// <c>[1,0.5,1,0.25,0.75]</c> (weights only). Absent fields keep the mode's built-in values.</para>
    /// The minus sign of w5 lives in the formula, so every weight is within 0…<see cref="RemoteConfigLimits.WeightMax"/>.
    /// </summary>
    public sealed class GenerationWeights
    {
        /// <summary>True when the entry carries w1…w5; false for a density / threshold-only entry (the weights are then 0).</summary>
        public bool HasWeights { get; }
        public double W1 { get; }
        public double W2 { get; }
        public double W3 { get; }
        public double W4 { get; }
        public double W5 { get; }
        /// <summary>Lower end of the pleasant significant-event density, events/s (finite, ≥ 0), or null.</summary>
        public double? DensityMin { get; }
        /// <summary>Upper end of the pleasant significant-event density, events/s (finite, &gt; 0, ≥ <see cref="DensityMin"/>), or null.</summary>
        public double? DensityMax { get; }
        /// <summary>Minimum total for a candidate to pass (finite, |t| ≤ <see cref="RemoteConfigLimits.QualityThresholdAbsMax"/>), or null.</summary>
        public double? QualityThreshold { get; }

        /// <summary>Weights only.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A weight is negative, NaN, infinite or above <see cref="RemoteConfigLimits.WeightMax"/>.</exception>
        public GenerationWeights(double w1, double w2, double w3, double w4, double w5)
            : this(true, w1, w2, w3, w4, w5, null, null, null)
        {
        }

        /// <summary>Weights plus optional density range and threshold.</summary>
        /// <exception cref="ArgumentOutOfRangeException">A value is outside its range, or densityMin &gt; densityMax.</exception>
        public GenerationWeights(double w1, double w2, double w3, double w4, double w5, double? densityMin, double? densityMax, double? qualityThreshold)
            : this(true, w1, w2, w3, w4, w5, densityMin, densityMax, qualityThreshold)
        {
        }

        GenerationWeights(bool hasWeights, double w1, double w2, double w3, double w4, double w5,
            double? densityMin, double? densityMax, double? qualityThreshold)
        {
            HasWeights = hasWeights;
            W1 = Check(w1, nameof(w1));
            W2 = Check(w2, nameof(w2));
            W3 = Check(w3, nameof(w3));
            W4 = Check(w4, nameof(w4));
            W5 = Check(w5, nameof(w5));
            string error = CheckTuning(densityMin, densityMax, qualityThreshold);
            if (error != null) throw new ArgumentOutOfRangeException(nameof(densityMin), error);
            DensityMin = densityMin;
            DensityMax = densityMax;
            QualityThreshold = qualityThreshold;
        }

        /// <summary>An entry without weights: only the density range and / or the threshold (at least one of them).</summary>
        /// <exception cref="ArgumentException">All three are null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A value is outside its range, or densityMin &gt; densityMax.</exception>
        public static GenerationWeights TuningOnly(double? densityMin, double? densityMax, double? qualityThreshold)
        {
            if (!densityMin.HasValue && !densityMax.HasValue && !qualityThreshold.HasValue)
                throw new ArgumentException("an entry needs weights, a density bound or a threshold");
            return new GenerationWeights(false, 0, 0, 0, 0, 0, densityMin, densityMax, qualityThreshold);
        }

        /// <summary>True when <paramref name="w"/> is an acceptable weight: finite, 0 ≤ w ≤ <see cref="RemoteConfigLimits.WeightMax"/>.</summary>
        public static bool IsValidWeight(double w)
        {
            return w >= 0.0 && w <= RemoteConfigLimits.WeightMax;
        }

        /// <summary>True for a finite density bound ≥ 0.</summary>
        public static bool IsValidDensity(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d) && d >= 0.0;
        }

        /// <summary>True for a finite threshold with |t| ≤ <see cref="RemoteConfigLimits.QualityThresholdAbsMax"/>.</summary>
        public static bool IsValidThreshold(double t)
        {
            return !double.IsNaN(t) && Math.Abs(t) <= RemoteConfigLimits.QualityThresholdAbsMax;
        }

        /// <summary>Null when the optional fields are consistent, else the reason.</summary>
        internal static string CheckTuning(double? densityMin, double? densityMax, double? qualityThreshold)
        {
            if (densityMin.HasValue && !IsValidDensity(densityMin.Value)) return "densityMin must be finite and >= 0";
            if (densityMax.HasValue && (!IsValidDensity(densityMax.Value) || densityMax.Value <= 0.0)) return "densityMax must be finite and > 0";
            if (densityMin.HasValue && densityMax.HasValue && densityMin.Value > densityMax.Value) return "densityMin must not exceed densityMax";
            if (qualityThreshold.HasValue && !IsValidThreshold(qualityThreshold.Value))
                return "qualityThreshold must be finite with |qualityThreshold| <= " + RemoteConfigLimits.QualityThresholdAbsMax;
            return null;
        }

        static double Check(double w, string name)
        {
            if (!IsValidWeight(w)) throw new ArgumentOutOfRangeException(name, w, "weight must be within 0.." + RemoteConfigLimits.WeightMax);
            return w;
        }
    }
}
