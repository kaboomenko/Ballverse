using System;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class LoudnessNormalizerTests
    {
        const int Sr = AudioConstants.SampleRate;

        [Fact]
        public void QuietSineIsRaisedToMinus14Lufs()
        {
            var x = AudioTestUtil.Sine(1000, 0.1, 5 * Sr, 2); // −20 LUFS
            var r = LoudnessNormalizer.Process(x);
            Assert.InRange(r.Input.IntegratedLufs, -20.1, -19.9);
            Assert.InRange(r.GainDb, 5.9, 6.1);
            Assert.InRange(r.Output.IntegratedLufs, -14.05, -13.95);
            Assert.Equal(0.0, r.LimiterReductionDb);
        }

        [Fact]
        public void LoudNoiseIsLoweredAndLimited()
        {
            var rng = Rng.FromSeed(14UL);
            var x = new float[2 * 4 * Sr];
            for (int i = 0; i < x.Length; i++) x[i] = (float)((rng.NextDouble() * 2 - 1) * 1.5);
            var r = LoudnessNormalizer.Process(x);
            Assert.True(r.GainDb < 0);
            Assert.InRange(r.Output.IntegratedLufs, -14.5, -13.9);
            Assert.True(r.Output.TruePeakDbtp <= -1.0 + 0.05);
        }

        [Fact]
        public void PeakyMaterialHitsLimiterAndStaysUnderCeiling()
        {
            // Quiet bed with sharp clicks: the +gain pushes the clicks far over −1 dBTP.
            var rng = Rng.FromSeed(15UL);
            var x = AudioTestUtil.Sine(440, 0.03, 6 * Sr, 2);
            for (int k = 0; k < 60; k++)
            {
                int f = rng.NextInt(0, 6 * Sr - 10);
                for (int j = 0; j < 10; j++)
                {
                    x[2 * (f + j)] = 0.9f;
                    x[2 * (f + j) + 1] = -0.9f;
                }
            }
            var r = LoudnessNormalizer.Process(x);
            Assert.True(r.LimiterReductionDb < -3);
            Assert.True(r.Output.TruePeakDbtp <= -1.0 + 0.05);
            Assert.True(r.Output.IntegratedLufs <= -13.9);
            foreach (float s in x) Assert.True(Math.Abs(s) <= Math.Pow(10, -1.0 / 20));
        }

        [Fact]
        public void BoostIsCapped()
        {
            var x = AudioTestUtil.Sine(1000, AudioTestUtil.DbToAmp(-45), 5 * Sr, 2); // −45 LUFS
            var r = LoudnessNormalizer.Process(x);
            Assert.Equal(24.0, r.GainDb);
            Assert.InRange(r.Output.IntegratedLufs, -21.1, -20.9);
            var uncapped = AudioTestUtil.Sine(1000, AudioTestUtil.DbToAmp(-45), 5 * Sr, 2);
            var r2 = LoudnessNormalizer.Process(uncapped, new NormalizationSettings { MaxGainDb = 40 });
            Assert.InRange(r2.Output.IntegratedLufs, -14.1, -13.9);
        }

        [Fact]
        public void SilenceStaysSilentWithFiniteReport()
        {
            var x = new float[2 * 2 * Sr];
            x[100] = float.NaN;
            var r = LoudnessNormalizer.Process(x);
            Assert.True(r.Input.IsSilent);
            Assert.True(r.Output.IsSilent);
            Assert.Equal(0.0, r.GainDb);
            Assert.Equal(LoudnessResult.SilenceFloor, r.Output.IntegratedLufs);
            Assert.All(x, s => Assert.Equal(0f, s));
        }

        [Fact]
        public void ShortLoudClickIsLimitedEvenThoughMeasuredSilent()
        {
            var x = new float[2 * Sr / 10];
            for (int i = 0; i < 200; i++) x[2000 + i] = 3f;
            var r = LoudnessNormalizer.Process(x);
            Assert.True(r.Input.IsSilent);
            Assert.Equal(0.0, r.GainDb);
            foreach (float s in x) Assert.True(Math.Abs(s) <= Math.Pow(10, -1.0 / 20));
        }

        [Fact]
        public void InvalidSettings()
        {
            Assert.Throws<ArgumentException>(() => LoudnessNormalizer.Process(new float[4], new NormalizationSettings { TargetLufs = double.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => LoudnessNormalizer.Process(new float[4], new NormalizationSettings { CeilingDbtp = 3 }));
        }
    }
}
