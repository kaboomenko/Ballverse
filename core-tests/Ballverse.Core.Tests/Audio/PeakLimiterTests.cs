using System;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class PeakLimiterTests
    {
        static readonly double Ceiling = Math.Pow(10, -1.0 / 20);

        static float[] RandomSignal(ref Rng rng, int frames)
        {
            var x = new float[frames * 2];
            int kind = rng.NextInt(0, 5);
            double amp = rng.NextDouble() * 4.0 + 0.2;
            double freq = 20 + rng.NextDouble() * 20000;
            for (int i = 0; i < frames; i++)
            {
                double l, r;
                switch (kind)
                {
                    case 0: // white noise
                        l = (rng.NextDouble() * 2 - 1) * amp;
                        r = (rng.NextDouble() * 2 - 1) * amp;
                        break;
                    case 1: // sine with inter-sample peaks
                        l = amp * Math.Sin(2 * Math.PI * freq * i / 48000 + 0.7);
                        r = -l;
                        break;
                    case 2: // square wave, alternating sign every sample burst
                        l = ((i / 7) % 2 == 0 ? 1 : -1) * amp;
                        r = l * 0.5;
                        break;
                    case 3: // sparse clicks over quiet noise
                        l = (rng.NextDouble() * 2 - 1) * 0.05 + (rng.NextInt(0, 500) == 0 ? amp * 3 : 0);
                        r = (rng.NextDouble() * 2 - 1) * 0.05;
                        break;
                    default: // bursts of alternating full-scale samples (Nyquist)
                        l = ((i / 2400) % 2 == 0) ? ((i & 1) == 0 ? amp : -amp) : 0.01 * Math.Sin(i * 0.01);
                        r = (rng.NextDouble() * 2 - 1) * amp * 0.3;
                        break;
                }
                x[2 * i] = (float)l;
                x[2 * i + 1] = (float)r;
            }
            return x;
        }

        [Fact]
        public void NeverExceedsCeilingOnRandomSignals()
        {
            var rng = Rng.FromSeed(0x11A17E2UL);
            var limiter = new PeakLimiter();
            for (int trial = 0; trial < 25; trial++)
            {
                int frames = rng.NextInt(1, 24000);
                var x = RandomSignal(ref rng, frames);
                double reduction = limiter.Process(x);
                Assert.True(reduction <= 0);
                foreach (float s in x) Assert.True(Math.Abs(s) <= Ceiling, "sample peak " + s);
                double tp = TruePeak.Measure(x, 2);
                Assert.True(20 * Math.Log10(Math.Max(tp, 1e-12)) <= -1.0 + 0.05, "trial " + trial + " true peak " + 20 * Math.Log10(tp));
            }
        }

        [Fact]
        public void SignalWithinCeilingIsUntouched()
        {
            var x = AudioTestUtil.Sine(1000, 0.5, 48000, 2);
            var copy = (float[])x.Clone();
            Assert.Equal(0.0, new PeakLimiter().Process(x));
            AudioTestUtil.AssertBitIdentical(copy, x);
        }

        [Fact]
        public void GainChangesSmoothly()
        {
            // 1 kHz sine 6 dB over full scale, starting abruptly after silence.
            var x = new float[2 * 48000];
            var s = AudioTestUtil.Sine(1000, 2.0, 24000, 2);
            Array.Copy(s, 0, x, 2 * 12000, s.Length);
            var input = (float[])x.Clone();
            double reduction = new PeakLimiter().Process(x);
            Assert.InRange(reduction, -7.5, -6.5);
            double prev = 1;
            for (int i = 0; i < x.Length; i += 2)
            {
                if (Math.Abs(input[i]) < 0.5) continue;
                double g = x[i] / input[i];
                Assert.InRange(g, 0, 1);
                if (prev < 1) Assert.True(Math.Abs(g - prev) < 0.02, "gain jump at " + i / 2);
                prev = g;
            }
            // Steady state: the sine sits just under the ceiling.
            double peak = 0;
            for (int i = 2 * 20000; i < 2 * 30000; i++) peak = Math.Max(peak, Math.Abs(x[i]));
            Assert.InRange(20 * Math.Log10(peak), -1.3, -1.0);
        }

        [Fact]
        public void LookAheadAttacksBeforeThePeak()
        {
            var x = new float[2 * 4800];
            x[2 * 2400] = 4f;
            x[2 * 2400 + 1] = 4f;
            new PeakLimiter().Process(x);
            Assert.True(Math.Abs(x[2 * 2400]) <= Ceiling);
            Assert.True(Math.Abs(x[2 * 2400]) > 0.5);
        }

        [Fact]
        public void DeterministicAndEdgeCases()
        {
            var rng = Rng.FromSeed(8UL);
            var a = RandomSignal(ref rng, 5000);
            var b = (float[])a.Clone();
            new PeakLimiter().Process(a);
            new PeakLimiter().Process(b);
            AudioTestUtil.AssertBitIdentical(a, b);

            Assert.Equal(0.0, new PeakLimiter().Process(new float[0]));
            var nan = new[] { float.NaN, float.PositiveInfinity, 5f, -5f };
            new PeakLimiter().Process(nan);
            Assert.Equal(0f, nan[0]);
            Assert.Equal(0f, nan[1]);
            Assert.True(Math.Abs(nan[2]) <= Ceiling);

            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakLimiter(0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakLimiter(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakLimiter(-1, 5, 0));
            Assert.Throws<ArgumentException>(() => new PeakLimiter().Process(new float[3]));
            Assert.Equal(240, new PeakLimiter().LookAheadFrames);
        }
    }
}
