using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// <see cref="PeakLimiter.MaxPasses"/> chained <see cref="LimiterStage"/>s plus the final hard clamp (spec §3.6). Running
    /// every stage is equivalent to "repeat while the result still exceeds the ceiling": a pass that finds nothing over the
    /// ceiling leaves every frame unchanged, so the passes after it are identities too. Input is read front to back and
    /// output is written behind the read position, so input and output may be the same buffer. Scratch memory is a few
    /// kilobytes regardless of the buffer length (spec §7.11 export memory budget).
    /// </summary>
    internal sealed class LimiterPipeline
    {
        const int BlockFrames = 1024;

        readonly LimiterStage[] _stages;
        readonly int _ch;
        readonly float _ceilingF;
        readonly float[] _inFrame;
        readonly float[] _out;
        int _outFrames;

        public LimiterPipeline(int channels, int lookAhead, double ceiling, double target, double releaseCoef, float ceilingF)
        {
            _ch = channels;
            _ceilingF = ceilingF;
            _stages = new LimiterStage[PeakLimiter.MaxPasses];
            LimiterStage next = null;
            for (int i = _stages.Length - 1; i >= 0; i--)
            {
                next = new LimiterStage(this, next, i > 0, channels, lookAhead, ceiling, target, releaseCoef);
                _stages[i] = next;
            }
            _inFrame = new float[channels];
            int latency = PeakLimiter.MaxPasses * (lookAhead + TruePeak.TapCount + 2);
            _out = new float[(BlockFrames + latency + 16) * channels];
        }

        /// <summary>
        /// Streams <paramref name="input"/> × gain through the limiter. Writes the result to <paramref name="output"/> unless it
        /// is empty, and feeds it to <paramref name="meter"/> when not null. Returns the deepest gain reduction in dB (≤ 0).
        /// Non-finite input samples count as 0.
        /// </summary>
        public double Run(ReadOnlySpan<float> input, double gain, Span<float> output, LoudnessMeter meter)
        {
            return Run(input, gain, output, meter, _stages.Length);
        }

        /// <summary>
        /// As <see cref="Run(ReadOnlySpan{float}, double, Span{float}, LoudnessMeter)"/> with only the first
        /// <paramref name="passes"/> passes (the gain search uses 1: later passes only shave inter-sample overshoots of a
        /// few hundredths of a dB and do not move the loudness measurably).
        /// </summary>
        public double Run(ReadOnlySpan<float> input, double gain, Span<float> output, LoudnessMeter meter, int passes)
        {
            int frames = TruePeak.CheckFrames(input, _ch);
            bool write = output.Length > 0;
            if (write && output.Length != input.Length) throw new ArgumentException("output must match the input length", nameof(output));
            if (passes < 1 || passes > _stages.Length) throw new ArgumentOutOfRangeException(nameof(passes));
            for (int i = 0; i < _stages.Length; i++) _stages[i].Reset();
            _stages[passes - 1].Last = true;
            _outFrames = 0;
            bool scale = gain != 1.0;
            LimiterStage first = _stages[0];
            long written = 0;
            int k = 0;
            for (int f = 0; f < frames; f++)
            {
                for (int c = 0; c < _ch; c++)
                {
                    float s = (float)TruePeak.Clean(input[k++]);
                    if (scale) s = (float)(s * gain);
                    _inFrame[c] = s;
                }
                first.Push(_inFrame, false);
                if (_outFrames >= BlockFrames) Drain(output, write, meter, ref written);
            }
            first.Flush();
            Drain(output, write, meter, ref written);

            double minGain = 1.0;
            for (int i = 0; i < passes; i++)
            {
                if (_stages[i].MinGain < minGain) minGain = _stages[i].MinGain;
            }
            return AudioMath.GainToDb(minGain, LoudnessResult.SilenceFloor);
        }

        /// <summary>Called by the last stage: hard clamp (last guard against rounding) into the output block.</summary>
        internal void Output(float[] frame)
        {
            int o = _outFrames * _ch;
            for (int c = 0; c < _ch; c++)
            {
                float s = frame[c];
                if (s > _ceilingF) s = _ceilingF;
                else if (s < -_ceilingF) s = -_ceilingF;
                _out[o + c] = s;
            }
            _outFrames++;
        }

        void Drain(Span<float> output, bool write, LoudnessMeter meter, ref long written)
        {
            if (_outFrames == 0) return;
            int n = _outFrames * _ch;
            var block = new ReadOnlySpan<float>(_out, 0, n);
            if (write) block.CopyTo(output.Slice((int)(written * _ch), n));
            if (meter != null) meter.Process(block);
            written += _outFrames;
            _outFrames = 0;
        }
    }
}
