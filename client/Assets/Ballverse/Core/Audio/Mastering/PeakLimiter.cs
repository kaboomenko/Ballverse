using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Deterministic look-ahead true-peak limiter for the export mix (spec §3.6 "limiter −1 dBTP"). Offline, zero latency,
    /// stereo-linked (one gain for all channels), streaming internally: scratch memory is a few kilobytes whatever the
    /// buffer length (spec §7.11 export memory budget).
    /// <para>Algorithm per pass: per-frame true peak p[n] (4×, <see cref="TruePeak"/>); required gain r[n] = min(1, c / p[n]);
    /// look-ahead minimum m[n] = min r[n … n+L]; release h[n] = min(m[n], h[n−1] + α·(1 − h[n−1])); gain g[n] = mean of
    /// h[n−L … n]. Since every h in that window is ≤ r[n], g[n] ≤ r[n], so no sample exceeds the ceiling; the L-frame
    /// moving average gives a smooth (click-free) attack. Up to <see cref="MaxPasses"/> passes run while the true peak of
    /// the result still exceeds the ceiling (gain modulation can move inter-sample peaks), then samples are hard-clamped
    /// to the largest float not above the ceiling as a last guard against rounding.</para>
    /// A buffer whose true peak is already within the ceiling is left bit-for-bit unchanged.
    /// </summary>
    public sealed class PeakLimiter
    {
        public const int MaxPasses = 4;

        readonly double _ceiling;
        readonly double _target;
        readonly float _ceilingF;
        readonly int _lookAhead;
        readonly double _releaseCoef;

        /// <param name="ceilingDbtp">Ceiling in dBTP (default −1, spec §3.6); must be ≤ 0.</param>
        /// <param name="lookAheadMs">Look-ahead / attack length, 0.5…50 ms (default 5).</param>
        /// <param name="releaseMs">Release time constant, 1…5000 ms (default 150).</param>
        public PeakLimiter(double ceilingDbtp = -1.0, double lookAheadMs = 5.0, double releaseMs = 150.0)
        {
            if (double.IsNaN(ceilingDbtp) || ceilingDbtp > 0 || ceilingDbtp < -60) throw new ArgumentOutOfRangeException(nameof(ceilingDbtp));
            if (double.IsNaN(lookAheadMs) || lookAheadMs < 0.5 || lookAheadMs > 50) throw new ArgumentOutOfRangeException(nameof(lookAheadMs));
            if (double.IsNaN(releaseMs) || releaseMs < 1 || releaseMs > 5000) throw new ArgumentOutOfRangeException(nameof(releaseMs));
            _ceiling = AudioMath.DbToGain(ceilingDbtp);
            _target = _ceiling * (1.0 - 1e-6); // absorbs float rounding of the gain multiply (≈ −0.00001 dB)
            float cf = (float)_ceiling;
            if (cf > _ceiling) cf = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(cf) - 1);
            _ceilingF = cf;
            _lookAhead = (int)Math.Round(lookAheadMs * AudioConstants.SampleRate / 1000.0);
            _releaseCoef = 1.0 - Math.Exp(-1.0 / (releaseMs * AudioConstants.SampleRate / 1000.0));
        }

        /// <summary>Linear ceiling.</summary>
        public double Ceiling => _ceiling;

        /// <summary>Look-ahead in frames.</summary>
        public int LookAheadFrames => _lookAhead;

        /// <summary>Limits <paramref name="interleaved"/> in place; returns the deepest gain reduction in dB (≤ 0).</summary>
        public double Process(Span<float> interleaved, int channels = 2)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            return CreatePipeline(channels).Run(interleaved, 1.0, interleaved, null);
        }

        internal LimiterPipeline CreatePipeline(int channels)
        {
            return new LimiterPipeline(channels, _lookAhead, _ceiling, _target, _releaseCoef, _ceilingF);
        }
    }
}
