namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>What <see cref="LoudnessNormalizer"/> measured and did (spec §3.6). All values finite.</summary>
    public struct NormalizationReport
    {
        /// <summary>Measurement before processing.</summary>
        public LoudnessResult Input;
        /// <summary>Measurement after gain and limiting.</summary>
        public LoudnessResult Output;
        /// <summary>Static gain applied before the limiter, dB (0 for silent input).</summary>
        public double GainDb;
        /// <summary>Deepest limiter gain reduction, dB (≤ 0).</summary>
        public double LimiterReductionDb;
    }
}
