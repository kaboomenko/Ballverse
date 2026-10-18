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
        static readonly double Ceiling = new PeakLimiter().Ceiling;

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
            Assert.True(TruePeak.Measure(x, 2) <= Ceiling);
            Assert.True(r.Output.TruePeak <= Ceiling);
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
            Assert.True(TruePeak.Measure(x, 2) <= Ceiling);
            Assert.True(r.Output.TruePeak <= Ceiling);
            Assert.InRange(r.Output.IntegratedLufs, -14.1, -13.9);
            Assert.True(r.Trials > 0);
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

        /// <summary>
        /// Sparse percussive simulation tracks have a high peak-to-loudness ratio: the limiter removes several dB, and one
        /// static gain would land 2–3.5 LU under −14 LUFS. The gain search must reach the target (spec §3.6).
        /// </summary>
        [Theory]
        [InlineData(SoundPackId.Notes, 1)]
        [InlineData(SoundPackId.Asmr, 1)]
        [InlineData(SoundPackId.Drums, 3)]
        [InlineData(SoundPackId.Notes, 10)]
        [InlineData(SoundPackId.Glass, 40)]
        public void NormalizationReachesTargetOnSparseHits(SoundPackId id, int hitsPerSecond)
        {
            var bank = AudioTestUtil.FullBank(2400);
            const int seconds = 6;
            var rng = Rng.FromSeed((ulong)(hitsPerSecond * 31 + (int)id));
            var events = new System.Collections.Generic.List<SoundEvent>();
            int total = hitsPerSecond * seconds;
            for (int i = 0; i < total; i++)
            {
                int tick = (int)((long)i * seconds * 60 / total) + rng.NextInt(0, 3);
                events.Add(SoundEvent.Hit(tick, rng.NextInt(0, 30), rng.NextFloat(0.5f, 7f), rng.NextFloat(0.1f, 1.5f), rng.NextFloat(-1f, 1f)));
            }
            float[] sim = EventMixer.RenderOffline(SoundPackCatalog.Get(id), bank, null, events, seconds * Sr);
            var input = LoudnessMeter.Measure(sim);
            var r = LoudnessNormalizer.Process(sim);
            Assert.Equal(r.Output.IntegratedLufs, LoudnessMeter.Measure(sim).IntegratedLufs); // report = what was written
            Assert.True(TruePeak.Measure(sim, 2) <= Ceiling);
            if (r.GainDb < 24.0)
            {
                Assert.InRange(r.Output.IntegratedLufs, -14.2, -13.8);
            }
            else
            {
                // Extremely sparse clicks: reaching −14 would need more than the +24 dB boost cap (documented); the search
                // still gets as close as the cap allows.
                Assert.Equal(24.0, r.GainDb);
                Assert.InRange(r.Output.IntegratedLufs, -17.0, -14.0);
            }
            if (hitsPerSecond <= 3)
            {
                // One static gain (target − input) would have missed by more than 1 LU here.
                var single = new float[sim.Length];
                EventMixer.RenderOffline(SoundPackCatalog.Get(id), bank, null, events, seconds * Sr).CopyTo(single, 0);
                double g = AudioMath.DbToGain(-14.0 - input.IntegratedLufs);
                for (int i = 0; i < single.Length; i++) single[i] = (float)(single[i] * g);
                new PeakLimiter().Process(single);
                Assert.True(LoudnessMeter.Measure(single).IntegratedLufs < -15.0);
                Assert.True(r.Trials > 1 && r.LimiterReductionDb < -3, "limiter " + r.LimiterReductionDb + " trials " + r.Trials);
            }
        }

        [Fact]
        public void BoostCapStillLimitsAndStopsTheSearch()
        {
            // A lone click every 2 s: needs far more than +6 dB, so the search stops at the cap.
            var x = new float[2 * 10 * Sr];
            for (int k = 0; k < 5; k++)
            {
                for (int j = 0; j < 200; j++) x[2 * (k * 2 * Sr + j)] = 0.05f * (j % 2 == 0 ? 1 : -1);
            }
            var r = LoudnessNormalizer.Process(x, new NormalizationSettings { MaxGainDb = 6 });
            Assert.Equal(6.0, r.GainDb, 9);
            Assert.True(r.Output.IntegratedLufs < -14.5);
            Assert.True(TruePeak.Measure(x, 2) <= Ceiling);
        }

        [Fact]
        public void MonoNormalization()
        {
            var x = AudioTestUtil.Sine(1000, 0.05, 4 * Sr, 1);
            for (int k = 0; k < 40; k++) x[k * 4000] = 0.9f;
            var r = LoudnessNormalizer.Process(x, null, 1);
            Assert.InRange(r.Output.IntegratedLufs, -14.1, -13.9);
            Assert.True(TruePeak.Measure(x, 1) <= Ceiling);
        }
    }
}
