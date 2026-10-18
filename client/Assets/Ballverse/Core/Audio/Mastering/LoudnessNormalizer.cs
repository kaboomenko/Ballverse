using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Export mastering (spec §3.6 "normalize to −14 LUFS with a −1 dBTP limiter", §7.8): measure integrated loudness
    /// (BS.1770-4), find the gain that puts the LIMITED result at the target, then apply gain + <see cref="PeakLimiter"/>.
    /// <para>Gain search: start from target − input. When the scaled true peak stays clearly under the ceiling the limiter
    /// cannot act and that gain is final. Otherwise trial renders (gain → first limiter pass → loudness, written nowhere;
    /// the later passes only shave inter-sample overshoots and do not move the loudness measurably) refine the
    /// gain — first step by the miss, then secant steps with the slope clamped to 0.1…1 — until the miss is within
    /// <see cref="ToleranceLu"/>, the boost cap <see cref="NormalizationSettings.MaxGainDb"/> is reached, or
    /// <see cref="MaxTrials"/> trials ran; the best trial's gain is applied. This matters for simulation tracks: short
    /// transients with a high peak-to-loudness ratio lose several dB in the limiter, so one static gain would land
    /// 2–3.5 LU under the target. Output may stay under the target only when the boost cap is reached.</para>
    /// Silent input (no block above the −70 LUFS gate) gets 0 dB gain but is still limited, so even a lone loud click
    /// shorter than 400 ms respects the ceiling. Non-finite samples are replaced by 0. In place; deterministic; scratch
    /// memory does not grow with the buffer length (spec §7.11).
    /// </summary>
    public static class LoudnessNormalizer
    {
        /// <summary>Largest number of trial renders of the gain search.</summary>
        public const int MaxTrials = 6;

        /// <summary>The gain search stops when the limited output is within this many LU of the target.</summary>
        public const double ToleranceLu = 0.05;

        /// <summary>
        /// Normalizes <paramref name="interleaved"/> (1 or 2 channels) in place and reports what was measured and done.
        /// </summary>
        public static NormalizationReport Process(Span<float> interleaved, NormalizationSettings settings = null, int channels = 2)
        {
            settings = settings ?? new NormalizationSettings();
            if (double.IsNaN(settings.TargetLufs) || double.IsInfinity(settings.TargetLufs))
                throw new ArgumentException("TargetLufs must be finite", nameof(settings));
            var limiter = new PeakLimiter(settings.CeilingDbtp, settings.LookAheadMs, settings.ReleaseMs);
            for (int i = 0; i < interleaved.Length; i++) interleaved[i] = (float)TruePeak.Clean(interleaved[i]);

            var report = new NormalizationReport { Input = LoudnessMeter.Measure(interleaved, channels) };
            LimiterPipeline pipeline = limiter.CreatePipeline(channels);
            double gainDb = 0;
            if (!report.Input.IsSilent)
            {
                double cap = double.IsNaN(settings.MaxGainDb) ? 0 : settings.MaxGainDb;
                gainDb = settings.TargetLufs - report.Input.IntegratedLufs;
                if (gainDb > cap) gainDb = cap;
                // 0.98: margin for float rounding of the gain multiply; below it the limiter is an identity.
                bool limiterActs = report.Input.TruePeak * AudioMath.DbToGain(gainDb) >= limiter.Ceiling * 0.98;
                if (limiterActs) gainDb = SearchGain(interleaved, channels, pipeline, gainDb, cap, settings.TargetLufs, ref report);
            }
            report.GainDb = gainDb;
            var meter = new LoudnessMeter(channels);
            report.LimiterReductionDb = pipeline.Run(interleaved, gainDb == 0 ? 1.0 : AudioMath.DbToGain(gainDb), interleaved, meter);
            report.Output = meter.GetResult();
            return report;
        }

        static double SearchGain(Span<float> x, int channels, LimiterPipeline pipeline, double gainDb, double cap, double target,
            ref NormalizationReport report)
        {
            double bestGain = gainDb;
            double bestMiss = double.PositiveInfinity;
            double prevGain = double.NaN;
            double prevOut = double.NaN;
            for (int trial = 0; trial < MaxTrials; trial++)
            {
                var meter = new LoudnessMeter(channels, false);
                pipeline.Run(x, AudioMath.DbToGain(gainDb), Span<float>.Empty, meter, 1);
                report.Trials++;
                LoudnessResult r = meter.GetResult();
                if (r.IsSilent) break;
                double miss = target - r.IntegratedLufs;
                if (Math.Abs(miss) < Math.Abs(bestMiss))
                {
                    bestMiss = miss;
                    bestGain = gainDb;
                }
                if (Math.Abs(miss) <= ToleranceLu) break;
                if (miss > 0 && gainDb >= cap) break;

                double slope = 1.0;
                if (trial > 0 && gainDb != prevGain)
                {
                    slope = (r.IntegratedLufs - prevOut) / (gainDb - prevGain);
                    if (double.IsNaN(slope) || slope > 1.0) slope = 1.0;
                    else if (slope < 0.1) slope = 0.1;
                }
                double next = gainDb + miss / slope;
                if (next > cap) next = cap;
                prevGain = gainDb;
                prevOut = r.IntegratedLufs;
                if (next == gainDb) break;
                gainDb = next;
            }
            return bestGain;
        }
    }
}
