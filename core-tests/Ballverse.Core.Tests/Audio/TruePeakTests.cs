using System;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class TruePeakTests
    {
        [Fact]
        public void QuarterSampleRateSineRevealsInterSamplePeak()
        {
            // 12 kHz at 45°: every sample is ±0.707 but the waveform reaches 1.0 between samples.
            var x = AudioTestUtil.Sine(12000, 1.0, 4800, 2, null, Math.PI / 4);
            double sample = 0;
            foreach (float s in x) sample = Math.Max(sample, Math.Abs(s));
            Assert.InRange(20 * Math.Log10(sample), -3.02, -3.0);
            Assert.InRange(TruePeak.MeasureDb(x, 2), -0.3, 0.3); // 4x estimate: within a few tenths of a dB at fs/4
        }

        [Theory]
        [InlineData(997.0)]
        [InlineData(5000.0)]
        [InlineData(9600.0)]
        public void SinePeaksAreAccurate(double freq)
        {
            var x = AudioTestUtil.Sine(freq, 0.5, 9600, 1, null, 0.3);
            Assert.InRange(TruePeak.MeasureDb(x, 1), -6.02 - 0.15, -6.02 + 0.15);
        }

        [Fact]
        public void SilenceAndInvalidInput()
        {
            Assert.Equal(0.0, TruePeak.Measure(new float[100], 2));
            Assert.Equal(LoudnessResult.SilenceFloor, TruePeak.MeasureDb(new float[0], 2));
            Assert.Equal(0.0, TruePeak.Measure(new[] { float.NaN, float.PositiveInfinity }, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => TruePeak.Measure(new float[2], 0));
            Assert.Throws<ArgumentException>(() => TruePeak.Measure(new float[3], 2));
        }

        [Fact]
        public void InterpolatorPhasesHaveUnityDcGain()
        {
            foreach (var phase in TruePeak.Coefficients)
            {
                double sum = 0;
                foreach (double c in phase) sum += c;
                Assert.Equal(1.0, sum, 12);
            }
        }
    }
}
