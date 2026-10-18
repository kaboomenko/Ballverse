using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Export mastering (spec §3.6, §7.8): measure integrated loudness (BS.1770-4), apply the static gain that brings it
    /// to the target (−14 LUFS, boost capped by <see cref="NormalizationSettings.MaxGainDb"/>), then the −1 dBTP
    /// <see cref="PeakLimiter"/>. Silent input (no block above the −70 LUFS gate) gets 0 dB gain but is still limited,
    /// so even a lone loud click shorter than 400 ms respects the ceiling. Because the limiter only removes peaks the
    /// final loudness can end slightly below the target; the report carries the exact output measurement.
    /// Non-finite samples are replaced by 0. In place; deterministic.
    /// </summary>
    public static class LoudnessNormalizer
    {
        public static NormalizationReport Process(Span<float> interleaved, NormalizationSettings settings = null, int channels = 2)
        {
            settings = settings ?? new NormalizationSettings();
            if (double.IsNaN(settings.TargetLufs) || double.IsInfinity(settings.TargetLufs))
                throw new ArgumentException("TargetLufs must be finite", nameof(settings));
            var limiter = new PeakLimiter(settings.CeilingDbtp, settings.LookAheadMs, settings.ReleaseMs);
            for (int i = 0; i < interleaved.Length; i++) interleaved[i] = (float)TruePeak.Clean(interleaved[i]);

            var report = new NormalizationReport { Input = LoudnessMeter.Measure(interleaved, channels) };
            double gainDb = 0;
            if (!report.Input.IsSilent)
            {
                gainDb = settings.TargetLufs - report.Input.IntegratedLufs;
                double cap = double.IsNaN(settings.MaxGainDb) ? 0 : settings.MaxGainDb;
                if (gainDb > cap) gainDb = cap;
            }
            report.GainDb = gainDb;
            if (gainDb != 0)
            {
                double g = AudioMath.DbToGain(gainDb);
                for (int i = 0; i < interleaved.Length; i++) interleaved[i] = (float)(interleaved[i] * g);
            }
            report.LimiterReductionDb = limiter.Process(interleaved, channels);
            report.Output = LoudnessMeter.Measure(interleaved, channels);
            return report;
        }
    }
}
