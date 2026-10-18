using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Deterministic look-ahead true-peak limiter for the export mix (spec §3.6 "limiter −1 dBTP"). Offline: processes a
    /// whole buffer in place with zero latency. Stereo-linked (one gain for all channels).
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
            int frames = TruePeak.CheckFrames(interleaved, channels);
            if (frames == 0) return 0;
            for (int i = 0; i < interleaved.Length; i++) interleaved[i] = (float)TruePeak.Clean(interleaved[i]);

            var work = new double[frames];
            var ring = new double[_lookAhead + 1];
            var deque = new int[_lookAhead + 2];
            double minGain = 1.0;

            for (int pass = 0; pass < MaxPasses; pass++)
            {
                TruePeak.FramePeaks(interleaved, channels, work);
                bool over = false;
                for (int n = 0; n < frames; n++)
                {
                    double p = work[n];
                    if (p > _ceiling)
                    {
                        over = true;
                        work[n] = _target / p;
                    }
                    else work[n] = 1.0;
                }
                if (!over) break;

                LookAheadMin(work, deque);
                ApplyReleaseAndAverage(work, ring);
                for (int n = 0; n < frames; n++)
                {
                    double g = work[n];
                    if (g < minGain) minGain = g;
                    if (g >= 1.0) continue;
                    int o = n * channels;
                    for (int c = 0; c < channels; c++) interleaved[o + c] = (float)(interleaved[o + c] * g);
                }
            }

            for (int i = 0; i < interleaved.Length; i++)
            {
                float s = interleaved[i];
                if (s > _ceilingF) interleaved[i] = _ceilingF;
                else if (s < -_ceilingF) interleaved[i] = -_ceilingF;
            }
            return AudioMath.GainToDb(minGain, LoudnessResult.SilenceFloor);
        }

        /// <summary>In place: r[n] ← min r[n … n+L] (monotonic deque; index n is popped before r[n] is overwritten).</summary>
        void LookAheadMin(double[] r, int[] deque)
        {
            int frames = r.Length;
            int head = 0, tail = 0, cap = deque.Length;
            int count = 0;
            int pushed = 0;
            for (int n = 0; n < frames; n++)
            {
                int limit = Math.Min(frames - 1, n + _lookAhead);
                while (pushed <= limit)
                {
                    double v = r[pushed];
                    while (count > 0)
                    {
                        int last = deque[(tail - 1 + cap) % cap];
                        if (r[last] >= v)
                        {
                            tail = (tail - 1 + cap) % cap;
                            count--;
                        }
                        else break;
                    }
                    deque[tail] = pushed;
                    tail = (tail + 1) % cap;
                    count++;
                    pushed++;
                }
                double min = r[deque[head]];
                if (deque[head] == n)
                {
                    head = (head + 1) % cap;
                    count--;
                }
                r[n] = min;
            }
        }

        /// <summary>
        /// In place: release smoothing, then the (L+1)-frame trailing moving average. Virtual frames before the start take
        /// m[0] = min r[0 … L], which keeps g[n] ≤ r[n] for the first L frames too.
        /// </summary>
        void ApplyReleaseAndAverage(double[] w, double[] ring)
        {
            int len = ring.Length;
            double start = w.Length > 0 ? w[0] : 1.0;
            for (int i = 0; i < len; i++) ring[i] = start;
            double sum = start * len;
            int pos = 0;
            double h = start;
            for (int n = 0; n < w.Length; n++)
            {
                double rel = h + _releaseCoef * (1.0 - h);
                h = w[n] < rel ? w[n] : rel;
                sum += h - ring[pos];
                ring[pos] = h;
                pos++;
                if (pos == len) pos = 0;
                double g = sum / len;
                if (g > 1.0) g = 1.0;
                w[n] = g;
            }
        }
    }
}
