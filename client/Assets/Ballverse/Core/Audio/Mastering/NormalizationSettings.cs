namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>Settings of <see cref="LoudnessNormalizer"/> (spec §3.6: −14 LUFS, limiter −1 dBTP).</summary>
    public sealed class NormalizationSettings
    {
        /// <summary>Target integrated loudness, LUFS (spec §3.6: −14).</summary>
        public double TargetLufs { get; set; } = -14.0;

        /// <summary>Limiter ceiling, dBTP (spec §3.6: −1).</summary>
        public double CeilingDbtp { get; set; } = -1.0;

        /// <summary>
        /// Largest boost before the limiter, dB (default +24). Keeps near-silent mixes (e.g. only a few soft clicks) from having
        /// their noise raised by 40+ dB; such mixes, and extremely sparse percussive ones whose limited loudness would need
        /// more boost, end up quieter than the target. Attenuation is unlimited.
        /// </summary>
        public double MaxGainDb { get; set; } = 24.0;

        /// <summary>Limiter look-ahead, ms.</summary>
        public double LookAheadMs { get; set; } = 5.0;

        /// <summary>Limiter release, ms.</summary>
        public double ReleaseMs { get; set; } = 150.0;
    }
}
