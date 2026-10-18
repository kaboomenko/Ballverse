using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// One pass of <see cref="PeakLimiter"/> as a streaming stage (spec §3.6). Frames enter one at a time; frame n leaves once
    /// r[n + L] is known, so the stage holds only about L + 16 frames. Per frame: true peak p[n] (4×, same interpolator and
    /// boundary rule as <see cref="TruePeak"/>), required gain r[n] = min(1, c / p[n]), look-ahead minimum
    /// m[n] = min r[n … n+L], release h[n] = min(m[n], h[n−1] + α·(1 − h[n−1])), gain g[n] = mean of h[n−L … n] (virtual
    /// frames before the start take m[0]). Since every h in that window is ≤ r[n], g[n] ≤ r[n]. A frame whose gain is 1
    /// leaves bit-for-bit unchanged; frames that were neither changed nor over the ceiling are reported clean, which lets
    /// the next stage skip the true peak of frames whose whole interpolation neighbourhood is clean: their peak equals
    /// this stage's peak, which was within the ceiling. Deterministic; allocation-free after construction.
    /// </summary>
    internal sealed class LimiterStage
    {
        readonly LimiterPipeline _owner;
        readonly LimiterStage _next;
        readonly bool _canSkip;
        readonly int _ch;
        readonly int _lookAhead;
        readonly double _ceiling;
        readonly double _target;
        readonly double _releaseCoef;
        readonly float[] _x; // per channel: a doubled ring of _xCap frames, so 12 consecutive taps are contiguous
        readonly int _xCap;
        readonly int _xMask;
        readonly double[] _r;
        readonly int _rMask;
        readonly long[] _dq;
        readonly int _dqMask;
        readonly double[] _avg;
        readonly double[] _seg;
        readonly float[] _frame;
        int _dqHead;
        int _dqCount;
        int _avgPos;
        double _sum;
        double _h;
        bool _started;
        long _segFrame;
        long _in;
        long _peakNext;
        long _outNext;
        long _lastDirtyIn;
        bool _over;
        double _minGain;

        public LimiterStage(LimiterPipeline owner, LimiterStage next, bool canSkip, int channels, int lookAhead,
            double ceiling, double target, double releaseCoef)
        {
            _owner = owner;
            _next = next;
            _canSkip = canSkip;
            _ch = channels;
            _lookAhead = lookAhead;
            _ceiling = ceiling;
            _target = target;
            _releaseCoef = releaseCoef;
            _xCap = PowerOfTwo(lookAhead + 2 * TruePeak.TapCount + 8);
            _x = new float[2 * _xCap * channels];
            _xMask = _xCap - 1;
            int rCap = PowerOfTwo(lookAhead + 16);
            _r = new double[rCap];
            _rMask = rCap - 1;
            _dq = new long[rCap];
            _dqMask = rCap - 1;
            _avg = new double[lookAhead + 1];
            _seg = new double[channels];
            _frame = new float[channels];
            Reset();
        }

        /// <summary>Some frame exceeded the ceiling in this pass.</summary>
        public bool Over => _over;

        /// <summary>Smallest gain applied so far (1 when untouched).</summary>
        public double MinGain => _minGain;

        /// <summary>This stage ends the chain for the current run (output goes to the pipeline). Cleared by <see cref="Reset"/>.</summary>
        public bool Last;

        public void Reset()
        {
            Last = false;
            Array.Clear(_x, 0, _x.Length); // zeros before the first frame for the interpolator
            _dqHead = 0;
            _dqCount = 0;
            _avgPos = 0;
            _sum = 0;
            _h = 1.0;
            _started = false;
            _segFrame = long.MinValue;
            _in = 0;
            _peakNext = 0;
            _outNext = 0;
            _lastDirtyIn = long.MinValue;
            _over = false;
            _minGain = 1.0;
        }

        /// <summary>
        /// Feeds the next frame (<paramref name="frame"/> holds one value per channel, all finite). <paramref name="dirty"/>:
        /// the previous stage changed this frame or found it over the ceiling (always false for the first stage).
        /// </summary>
        public void Push(float[] frame, bool dirty)
        {
            long idx = _in;
            Store(idx, frame);
            if (dirty) _lastDirtyIn = idx;
            _in = idx + 1;
            if (idx - TruePeak.TapsAfter >= 0) ComputeR(idx - TruePeak.TapsAfter);
            while (_outNext + _lookAhead < _peakNext) Emit();
        }

        /// <summary>End of input: the remaining frames (zeros beyond the end for the interpolator) leave the stage.</summary>
        public void Flush()
        {
            // Zeros after the last frame for the interpolator (these ring slots held frames older than any still needed).
            for (int c = 0; c < _ch; c++) _frame[c] = 0f;
            for (int k = 0; k < TruePeak.TapsAfter; k++) Store(_in + k, _frame);
            while (_peakNext < _in)
            {
                ComputeR(_peakNext);
                while (_outNext + _lookAhead < _peakNext) Emit(); // window n … n+L only
            }
            while (_outNext < _in) Emit(); // windows clipped at the last frame
            if (_next != null && !Last) _next.Flush();
        }

        void ComputeR(long m)
        {
            double r = 1.0;
            if (_canSkip && _lastDirtyIn < m - TruePeak.TapsAfter)
            {
                _segFrame = long.MinValue;
            }
            else
            {
                double p = 0;
                bool prevValid = _segFrame == m - 1;
                for (int c = 0; c < _ch; c++)
                {
                    double prev = prevValid ? _seg[c] : Segment(m - 1, c);
                    double cur = Segment(m, c);
                    double pc = Math.Abs(Sample(m, c));
                    if (prev > pc) pc = prev;
                    if (cur > pc) pc = cur;
                    if (pc > p) p = pc;
                    _seg[c] = cur;
                }
                _segFrame = m;
                if (p > _ceiling)
                {
                    _over = true;
                    r = _target / p;
                }
            }

            _r[(int)(m & _rMask)] = r;
            while (_dqCount > 0)
            {
                long last = _dq[(_dqHead + _dqCount - 1) & _dqMask];
                if (_r[(int)(last & _rMask)] >= r) _dqCount--;
                else break;
            }
            _dq[(_dqHead + _dqCount) & _dqMask] = m;
            _dqCount++;
            _peakNext = m + 1;
        }

        void Emit()
        {
            long n = _outNext;
            while (_dq[_dqHead] < n)
            {
                _dqHead = (_dqHead + 1) & _dqMask;
                _dqCount--;
            }
            double min = _r[(int)(_dq[_dqHead] & _rMask)];
            int len = _avg.Length;
            if (!_started)
            {
                for (int i = 0; i < len; i++) _avg[i] = min;
                _sum = min * len;
                _h = min;
                _started = true;
            }
            double rel = _h + _releaseCoef * (1.0 - _h);
            _h = min < rel ? min : rel;
            _sum += _h - _avg[_avgPos];
            _avg[_avgPos] = _h;
            _avgPos++;
            if (_avgPos == len) _avgPos = 0;
            double g = _sum / len;
            if (g > 1.0) g = 1.0;
            if (g < _minGain) _minGain = g;

            int o = (int)(n & _xMask);
            bool touched = g < 1.0;
            for (int c = 0; c < _ch; c++)
            {
                float s = _x[c * 2 * _xCap + o];
                _frame[c] = touched ? (float)(s * g) : s;
            }
            _outNext = n + 1;
            // Dirty = changed here, or over the ceiling here (a gain that rounds to 1 can leave such a frame unchanged).
            bool dirty = touched || _r[(int)(n & _rMask)] < 1.0;
            if (_next != null && !Last) _next.Push(_frame, dirty);
            else _owner.Output(_frame);
        }

        void Store(long idx, float[] frame)
        {
            int pos = (int)(idx & _xMask);
            for (int c = 0; c < _ch; c++)
            {
                int b = c * 2 * _xCap + pos;
                _x[b] = frame[c];
                _x[b + _xCap] = frame[c];
            }
        }

        /// <summary>Max |interpolated| at phases 1…3 of the interval (m, m+1); taps x[m−5 … m+6], zero outside the input.</summary>
        double Segment(long m, int c)
        {
            float[] x = _x;
            int b = c * 2 * _xCap + (int)((m - TruePeak.TapsBefore) & _xMask);
            double x0 = x[b], x1 = x[b + 1], x2 = x[b + 2], x3 = x[b + 3], x4 = x[b + 4], x5 = x[b + 5];
            double x6 = x[b + 6], x7 = x[b + 7], x8 = x[b + 8], x9 = x[b + 9], x10 = x[b + 10], x11 = x[b + 11];
            double[] h1 = TruePeak.Coefficients[1];
            double[] h2 = TruePeak.Coefficients[2];
            double[] h3 = TruePeak.Coefficients[3];
            // Same summation order as TruePeak.Measure and LoudnessMeter (tap 0 … 11), so results match exactly.
            double v1 = 0, v2 = 0, v3 = 0;
            v1 += h1[0] * x0; v2 += h2[0] * x0; v3 += h3[0] * x0;
            v1 += h1[1] * x1; v2 += h2[1] * x1; v3 += h3[1] * x1;
            v1 += h1[2] * x2; v2 += h2[2] * x2; v3 += h3[2] * x2;
            v1 += h1[3] * x3; v2 += h2[3] * x3; v3 += h3[3] * x3;
            v1 += h1[4] * x4; v2 += h2[4] * x4; v3 += h3[4] * x4;
            v1 += h1[5] * x5; v2 += h2[5] * x5; v3 += h3[5] * x5;
            v1 += h1[6] * x6; v2 += h2[6] * x6; v3 += h3[6] * x6;
            v1 += h1[7] * x7; v2 += h2[7] * x7; v3 += h3[7] * x7;
            v1 += h1[8] * x8; v2 += h2[8] * x8; v3 += h3[8] * x8;
            v1 += h1[9] * x9; v2 += h2[9] * x9; v3 += h3[9] * x9;
            v1 += h1[10] * x10; v2 += h2[10] * x10; v3 += h3[10] * x10;
            v1 += h1[11] * x11; v2 += h2[11] * x11; v3 += h3[11] * x11;
            return Math.Max(Math.Abs(v1), Math.Max(Math.Abs(v2), Math.Abs(v3)));
        }

        double Sample(long idx, int c)
        {
            return _x[c * 2 * _xCap + (int)(idx & _xMask)];
        }

        static int PowerOfTwo(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }
    }
}
