using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Integrated loudness per ITU-R BS.1770-4 / EBU R128 at 48 kHz (spec §3.6 "normalize to −14 LUFS", §7.8 "EBU R128").
    /// <list type="bullet">
    /// <item>K-weighting: shelf pre-filter + RLB high-pass with the standard 48 kHz coefficients, per channel.</item>
    /// <item>400 ms gating blocks with 75 % overlap (100 ms hop); only complete blocks count.</item>
    /// <item>Block loudness −0.691 + 10·log10(Σ G·z), G = 1 for L/R (mono: the single channel, no dual-mono boost).</item>
    /// <item>Absolute gate −70 LUFS, relative gate −10 LU below the loudness of the absolutely gated blocks.</item>
    /// </list>
    /// Streaming: <see cref="Process"/> may be called with any chunking and gives bit-identical results.
    /// Also tracks the sample peak and the 4× oversampled true peak (<see cref="TruePeak"/>).
    /// Non-finite input samples are treated as 0.
    /// </summary>
    public sealed class LoudnessMeter
    {
        /// <summary>Absolute gate, LUFS.</summary>
        public const double AbsoluteGateLufs = -70.0;
        /// <summary>Relative gate, LU below the absolutely gated loudness.</summary>
        public const double RelativeGateLu = -10.0;
        /// <summary>Hop between gating blocks: 100 ms.</summary>
        public const int HopFrames = AudioConstants.SampleRate / 10;
        /// <summary>Gating block: 400 ms.</summary>
        public const int BlockFrames = 4 * HopFrames;

        const double LoudnessOffset = -0.691;

        readonly int _channels;
        readonly Biquad[] _shelf;
        readonly Biquad[] _highPass;
        readonly double[][] _history; // per channel, doubled ring of 12 for the true-peak interpolator
        readonly int[] _historyPos;
        readonly double[] _prevSeg;
        readonly List<double> _hops = new List<double>();
        double _hopSum;
        int _hopFrames;
        long _frames;
        double _samplePeak;
        double _truePeak;

        /// <param name="channels">1 (mono) or 2 (stereo L, R; interleaved input).</param>
        public LoudnessMeter(int channels = 2)
        {
            if (channels != 1 && channels != 2) throw new ArgumentOutOfRangeException(nameof(channels), "channels must be 1 or 2");
            _channels = channels;
            _shelf = new Biquad[channels];
            _highPass = new Biquad[channels];
            _history = new double[channels][];
            _historyPos = new int[channels];
            _prevSeg = new double[channels];
            for (int c = 0; c < channels; c++) _history[c] = new double[2 * TruePeak.TapCount];
            Reset();
        }

        public int Channels => _channels;

        /// <summary>One-shot measurement of a whole buffer.</summary>
        public static LoudnessResult Measure(ReadOnlySpan<float> interleaved, int channels = 2)
        {
            var m = new LoudnessMeter(channels);
            m.Process(interleaved);
            return m.GetResult();
        }

        /// <summary>Feeds interleaved samples (length must be a whole number of frames).</summary>
        public void Process(ReadOnlySpan<float> interleaved)
        {
            int frames = TruePeak.CheckFrames(interleaved, _channels);
            int i = 0;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                for (int c = 0; c < _channels; c++)
                {
                    double x = TruePeak.Clean(interleaved[i++]);
                    double ax = Math.Abs(x);
                    if (ax > _samplePeak) _samplePeak = ax;
                    PushTruePeak(c, x, _frames + f);
                    double y = _highPass[c].Process(_shelf[c].Process(x));
                    sum += y * y;
                }
                _hopSum += sum;
                _hopFrames++;
                if (_hopFrames == HopFrames)
                {
                    _hops.Add(_hopSum);
                    _hopSum = 0;
                    _hopFrames = 0;
                }
            }
            _frames += frames;
        }

        /// <summary>Result for everything fed so far (does not change the meter state).</summary>
        public LoudnessResult GetResult()
        {
            var r = new LoudnessResult { Frames = _frames, SamplePeak = _samplePeak };
            double tp = Math.Max(_truePeak, FlushTruePeak());
            if (_samplePeak > tp) tp = _samplePeak;
            r.TruePeak = tp;
            r.TruePeakDbtp = AudioMath.GainToDb(tp, LoudnessResult.SilenceFloor);

            int blocks = _hops.Count >= 4 ? _hops.Count - 3 : 0;
            r.BlockCount = blocks;

            double absSum = 0;
            int absCount = 0;
            for (int j = 0; j < blocks; j++)
            {
                double z = BlockPower(j);
                if (BlockLoudness(z) > AbsoluteGateLufs)
                {
                    absSum += z;
                    absCount++;
                }
            }
            if (absCount == 0)
            {
                r.IsSilent = true;
                r.IntegratedLufs = LoudnessResult.SilenceFloor;
                return r;
            }

            double relativeGate = BlockLoudness(absSum / absCount) + RelativeGateLu;
            double sum = 0;
            int count = 0;
            for (int j = 0; j < blocks; j++)
            {
                double z = BlockPower(j);
                double l = BlockLoudness(z);
                if (l > AbsoluteGateLufs && l > relativeGate)
                {
                    sum += z;
                    count++;
                }
            }
            r.GatedBlockCount = count;
            r.IntegratedLufs = BlockLoudness(sum / count);
            return r;
        }

        public void Reset()
        {
            for (int c = 0; c < _channels; c++)
            {
                _shelf[c] = Biquad.KWeightingShelf();
                _highPass[c] = Biquad.KWeightingHighPass();
                Array.Clear(_history[c], 0, _history[c].Length);
                _historyPos[c] = 0;
                _prevSeg[c] = 0;
            }
            _hops.Clear();
            _hopSum = 0;
            _hopFrames = 0;
            _frames = 0;
            _samplePeak = 0;
            _truePeak = 0;
        }

        double BlockPower(int j)
        {
            return (_hops[j] + _hops[j + 1] + _hops[j + 2] + _hops[j + 3]) / BlockFrames;
        }

        static double BlockLoudness(double power)
        {
            if (!(power > 0)) return double.NegativeInfinity; // internal only; gated out, never returned
            return LoudnessOffset + 10.0 * Math.Log10(power);
        }

        // History holds x[n-11..n] (zeros before the start). After pushing x[n] the interpolator evaluates the interval
        // (m, m+1) with m = n - 6: value(m + p/4) uses taps x[m-5..m+6] = x[n-11..n]. Like TruePeak.FramePeaks, only the
        // intervals m = -1 … frames-1 are evaluated, so streaming and batch results are identical.
        void PushTruePeak(int c, double x, long n)
        {
            double[] h = _history[c];
            int pos = _historyPos[c];
            h[pos] = x;
            h[pos + TruePeak.TapCount] = x;
            pos++;
            if (pos == TruePeak.TapCount) pos = 0;
            _historyPos[c] = pos;
            if (n - 6 < -1) return;
            double seg = Interval(h, pos);
            if (seg > _truePeak) _truePeak = seg;
        }

        double FlushTruePeak()
        {
            // Six more zero samples complete the intervals up to (frames-1, frames).
            double best = 0;
            var tmp = new double[2 * TruePeak.TapCount];
            for (int c = 0; c < _channels; c++)
            {
                Array.Copy(_history[c], tmp, tmp.Length);
                int pos = _historyPos[c];
                for (int k = 0; k < TruePeak.TapsAfter; k++)
                {
                    tmp[pos] = 0;
                    tmp[pos + TruePeak.TapCount] = 0;
                    pos++;
                    if (pos == TruePeak.TapCount) pos = 0;
                    if (_frames + k - 6 < -1) continue;
                    double seg = Interval(tmp, pos);
                    if (seg > best) best = seg;
                }
            }
            return best;
        }

        /// <summary>Max |interpolated| at phases 1..3 over the 12 values starting at ring position <paramref name="start"/> (oldest).</summary>
        static double Interval(double[] ring, int start)
        {
            double[] h1 = TruePeak.Coefficients[1];
            double[] h2 = TruePeak.Coefficients[2];
            double[] h3 = TruePeak.Coefficients[3];
            double v1 = 0, v2 = 0, v3 = 0;
            for (int k = 0; k < TruePeak.TapCount; k++)
            {
                double s = ring[start + k];
                v1 += h1[k] * s;
                v2 += h2[k] * s;
                v3 += h3[k] * s;
            }
            return Math.Max(Math.Abs(v1), Math.Max(Math.Abs(v2), Math.Abs(v3)));
        }
    }
}
