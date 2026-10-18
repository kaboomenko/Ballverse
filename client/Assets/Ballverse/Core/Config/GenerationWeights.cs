using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Spectacle score weights of one mode (spec §2.4):
    /// <c>score = w1·fitDuration + w2·eventDensity + w3·finishTension + w4·coverage − w5·stagnation</c>.
    /// The minus sign of w5 lives in the formula, so every weight is finite and ≥ 0.
    /// JSON form: <c>{"w1":1,"w2":0.5,"w3":1,"w4":0.25,"w5":0.75}</c> or <c>[1,0.5,1,0.25,0.75]</c>.
    /// </summary>
    public sealed class GenerationWeights
    {
        public float W1 { get; }
        public float W2 { get; }
        public float W3 { get; }
        public float W4 { get; }
        public float W5 { get; }

        /// <exception cref="ArgumentOutOfRangeException">A weight is negative, NaN or infinite.</exception>
        public GenerationWeights(float w1, float w2, float w3, float w4, float w5)
        {
            W1 = Check(w1, nameof(w1));
            W2 = Check(w2, nameof(w2));
            W3 = Check(w3, nameof(w3));
            W4 = Check(w4, nameof(w4));
            W5 = Check(w5, nameof(w5));
        }

        /// <summary>True when <paramref name="w"/> is an acceptable weight (finite, ≥ 0).</summary>
        public static bool IsValidWeight(double w)
        {
            return !double.IsNaN(w) && !double.IsInfinity(w) && w >= 0.0 && w <= float.MaxValue;
        }

        static float Check(float w, string name)
        {
            if (!IsValidWeight(w)) throw new ArgumentOutOfRangeException(name, w, "weight must be finite and >= 0");
            return w;
        }
    }
}
