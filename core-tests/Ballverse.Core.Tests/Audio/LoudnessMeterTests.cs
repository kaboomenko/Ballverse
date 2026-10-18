using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class LoudnessMeterTests
    {
        const int Sr = AudioConstants.SampleRate;

        static float[] Sine1k(double dbfs, double seconds, bool left = true, bool right = true)
        {
            return AudioTestUtil.Sine(1000, AudioTestUtil.DbToAmp(dbfs), (int)(seconds * Sr), 2, new[] { left, right });
        }

        static float[] Concat(params float[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var r = new float[n];
            int o = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, r, o, p.Length);
                o += p.Length;
            }
            return r;
        }

        [Fact]
        public void StereoSineAtMinus20DbfsMeasuresMinus20Lufs()
        {
            var r = LoudnessMeter.Measure(Sine1k(-20, 10));
            Assert.InRange(r.IntegratedLufs, -20.1, -19.9);
            Assert.False(r.IsSilent);
            Assert.Equal(97, r.BlockCount);
            Assert.Equal(97, r.GatedBlockCount);
        }

        [Fact]
        public void OneChannelSineMeasuresMinus23Lufs()
        {
            var r = LoudnessMeter.Measure(Sine1k(-20, 10, left: true, right: false));
            Assert.InRange(r.IntegratedLufs, -23.1, -22.9);
            var rr = LoudnessMeter.Measure(Sine1k(-20, 10, left: false, right: true));
            Assert.Equal(r.IntegratedLufs, rr.IntegratedLufs, 9);
        }

        [Fact]
        public void MonoInputIsSingleChannel()
        {
            var mono = AudioTestUtil.Sine(1000, 0.1, 10 * Sr, 1);
            var r = LoudnessMeter.Measure(mono, 1);
            Assert.InRange(r.IntegratedLufs, -23.1, -22.9);
        }

        [Fact]
        public void Ebu3341Case2Minus33()
        {
            Assert.InRange(LoudnessMeter.Measure(Sine1k(-33, 20)).IntegratedLufs, -33.1, -32.9);
        }

        [Fact]
        public void Ebu3341Case3RelativeGate()
        {
            // −36 dBFS 10 s, −23 dBFS 60 s, −36 dBFS 10 s → −23.0 ± 0.1 LUFS
            var x = Concat(Sine1k(-36, 10), Sine1k(-23, 60), Sine1k(-36, 10));
            Assert.InRange(LoudnessMeter.Measure(x).IntegratedLufs, -23.1, -22.9);
        }

        [Fact]
        public void Ebu3341Case4AbsoluteAndRelativeGate()
        {
            // −72 / −36 / −23 / −36 / −72 dBFS for 10 / 10 / 60 / 10 / 10 s → −23.0 ± 0.1 LUFS
            var x = Concat(Sine1k(-72, 10), Sine1k(-36, 10), Sine1k(-23, 60), Sine1k(-36, 10), Sine1k(-72, 10));
            Assert.InRange(LoudnessMeter.Measure(x).IntegratedLufs, -23.1, -22.9);
        }

        [Fact]
        public void SilenceIsDefinedAndFinite()
        {
            var r = LoudnessMeter.Measure(new float[2 * 3 * Sr]);
            Assert.True(r.IsSilent);
            Assert.Equal(LoudnessResult.SilenceFloor, r.IntegratedLufs);
            Assert.Equal(LoudnessResult.SilenceFloor, r.TruePeakDbtp);
            Assert.Equal(0.0, r.SamplePeak);
            Assert.Equal(27, r.BlockCount);
            Assert.Equal(0, r.GatedBlockCount);

            var empty = LoudnessMeter.Measure(new float[0]);
            Assert.True(empty.IsSilent);
            Assert.Equal(LoudnessResult.SilenceFloor, empty.IntegratedLufs);
            Assert.Equal(0, empty.BlockCount);
        }

        [Fact]
        public void BelowAbsoluteGateIsSilent()
        {
            var r = LoudnessMeter.Measure(Sine1k(-80, 3));
            Assert.True(r.IsSilent);
            Assert.Equal(LoudnessResult.SilenceFloor, r.IntegratedLufs);
            Assert.InRange(r.TruePeakDbtp, -80.2, -79.8);
        }

        [Fact]
        public void ShorterThanOneBlockIsSilentButPeaksAreReported()
        {
            var r = LoudnessMeter.Measure(Sine1k(-3, 0.39));
            Assert.Equal(0, r.BlockCount);
            Assert.True(r.IsSilent);
            Assert.InRange(r.TruePeakDbtp, -3.1, -2.9);
            Assert.False(double.IsNaN(r.IntegratedLufs) || double.IsInfinity(r.IntegratedLufs));
        }

        [Fact]
        public void NonFiniteSamplesCountAsZero()
        {
            var x = Sine1k(-20, 5);
            x[1000] = float.NaN;
            x[2001] = float.PositiveInfinity;
            x[3000] = float.NegativeInfinity;
            var r = LoudnessMeter.Measure(x);
            Assert.InRange(r.IntegratedLufs, -20.1, -19.9);
            Assert.True(r.SamplePeak <= 0.1 + 1e-6);
            var silent = new float[2 * Sr];
            silent[5] = float.NaN;
            Assert.True(LoudnessMeter.Measure(silent).IsSilent);
        }

        [Fact]
        public void ChunkedProcessingIsBitIdentical()
        {
            var rng = Rng.FromSeed(1770UL);
            var x = new float[2 * 7 * Sr + 2 * 1234];
            for (int i = 0; i < x.Length; i++) x[i] = (rng.NextFloat() * 2f - 1f) * (i % 9000 < 4500 ? 0.3f : 0.02f);
            var reference = LoudnessMeter.Measure(x);
            for (int trial = 0; trial < 4; trial++)
            {
                var m = new LoudnessMeter();
                int pos = 0;
                while (pos < x.Length / 2)
                {
                    int n = Math.Min(x.Length / 2 - pos, rng.NextInt(0, 7000));
                    m.Process(new ReadOnlySpan<float>(x, pos * 2, n * 2));
                    pos += n;
                }
                var r = m.GetResult();
                Assert.Equal(reference.IntegratedLufs, r.IntegratedLufs);
                Assert.Equal(reference.TruePeak, r.TruePeak);
                Assert.Equal(reference.SamplePeak, r.SamplePeak);
                Assert.Equal(reference.BlockCount, r.BlockCount);
                Assert.Equal(reference.GatedBlockCount, r.GatedBlockCount);
            }
        }

        [Fact]
        public void StreamingTruePeakMatchesBatch()
        {
            var rng = Rng.FromSeed(4UL);
            var x = new float[2 * 20000];
            for (int i = 0; i < x.Length; i++) x[i] = rng.NextFloat() * 2f - 1f;
            var r = LoudnessMeter.Measure(x);
            Assert.Equal(TruePeak.Measure(x, 2), r.TruePeak);
            Assert.True(r.TruePeak >= r.SamplePeak);
        }

        [Fact]
        public void ResetAndValidation()
        {
            var m = new LoudnessMeter();
            m.Process(Sine1k(-10, 1));
            m.Reset();
            m.Process(Sine1k(-20, 5));
            Assert.InRange(m.GetResult().IntegratedLufs, -20.1, -19.9);
            Assert.Throws<ArgumentOutOfRangeException>(() => new LoudnessMeter(3));
            Assert.Throws<ArgumentException>(() => m.Process(new float[3]));
        }
    }
}
