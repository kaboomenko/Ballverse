using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class AudioMathTests
    {
        [Fact]
        public void ConstantsMatchSpec()
        {
            Assert.Equal(48000, AudioConstants.SampleRate);
            Assert.Equal(800, AudioConstants.FramesPerTick);
            Assert.Equal(32, AudioConstants.MaxVoices);
            Assert.Equal(2400, AudioConstants.PerBodyMinIntervalFrames);
            Assert.Equal(40, AudioConstants.MaxSoundsPerWindow);
            Assert.Equal(20, AudioConstants.MaxMelodyNotesPerWindow);
            Assert.Equal(3840, AudioConstants.HapticsMinIntervalFrames);
            Assert.Equal(800L * 123, AudioConstants.TickToFrame(123));
        }

        [Theory]
        [InlineData(0L, 0L)]
        [InlineData(1_000_000L, 48000L)]
        [InlineData(20L, 0L)]
        [InlineData(21L, 1L)]
        [InlineData(-21L, -2L)]
        [InlineData(30_000_000L, 1_440_000L)]
        public void MicrosToFramesFloors(long micros, long frames)
        {
            Assert.Equal(frames, AudioConstants.MicrosToFrames(micros));
        }

        [Fact]
        public void SecondsToFramesHandlesEdges()
        {
            Assert.Equal(96000, AudioConstants.SecondsToFrames(2.0));
            Assert.Equal(0, AudioConstants.SecondsToFrames(-1));
            Assert.Equal(0, AudioConstants.SecondsToFrames(double.NaN));
            Assert.Equal(int.MaxValue, AudioConstants.SecondsToFrames(double.PositiveInfinity));
        }

        [Fact]
        public void ImpulseVolumeFollowsSpecFormula()
        {
            // v = clamp((impulse − i0) / (i1 − i0), 0.15, 1); below i0 silent.
            Assert.Equal(0f, AudioMath.ImpulseVolume(0.999f, 1f, 11f));
            Assert.Equal(0.15f, AudioMath.ImpulseVolume(1f, 1f, 11f));
            Assert.Equal(0.15f, AudioMath.ImpulseVolume(2.5f, 1f, 11f));
            Assert.Equal(0.5f, AudioMath.ImpulseVolume(6f, 1f, 11f), 6);
            Assert.Equal(1f, AudioMath.ImpulseVolume(11f, 1f, 11f));
            Assert.Equal(1f, AudioMath.ImpulseVolume(1000f, 1f, 11f));
            Assert.Equal(1f, AudioMath.ImpulseVolume(float.PositiveInfinity, 1f, 11f));
            Assert.Equal(0f, AudioMath.ImpulseVolume(float.NaN, 1f, 11f));
            Assert.Equal(0f, AudioMath.ImpulseVolume(float.NegativeInfinity, 1f, 11f));
        }

        [Fact]
        public void SemitoneRatiosMatchPowAndAreExactOnOctaves()
        {
            for (int s = -48; s <= 48; s++)
            {
                double expected = Math.Pow(2.0, s / 12.0);
                Assert.True(Math.Abs(AudioMath.SemitoneRatio(s) / expected - 1.0) < 1e-14, "semitone " + s);
            }
            Assert.Equal(2.0, AudioMath.SemitoneRatio(12));
            Assert.Equal(0.25, AudioMath.SemitoneRatio(-24));
            Assert.Equal(16.0, AudioMath.SemitoneRatio(100));
            Assert.Equal(1.0 / 16, AudioMath.SemitoneRatio(-100));
        }

        [Fact]
        public void PanIsConstantPower()
        {
            for (int i = -10; i <= 10; i++)
            {
                AudioMath.ConstantPowerPan(i / 10f, out float l, out float r);
                Assert.Equal(1.0, (double)l * l + (double)r * r, 5);
            }
            AudioMath.ConstantPowerPan(-1f, out float l1, out float r1);
            Assert.Equal(1f, l1, 6);
            Assert.Equal(0f, r1, 6);
            AudioMath.ConstantPowerPan(1f, out float l2, out float r2);
            Assert.Equal(0f, l2, 6);
            Assert.Equal(1f, r2, 6);
            AudioMath.ConstantPowerPan(0f, out float l3, out float r3);
            Assert.Equal(Math.Sqrt(0.5), l3, 6);
            Assert.Equal(l3, r3);
            AudioMath.ConstantPowerPan(float.NaN, out float l4, out float r4);
            Assert.Equal(l3, l4);
            AudioMath.ConstantPowerPan(5f, out float l5, out float r5);
            Assert.Equal(l2, l5);
            Assert.Equal(r2, r5);
        }

        [Fact]
        public void GainToDbNeverLeaksInfinity()
        {
            Assert.Equal(-144.0, AudioMath.GainToDb(0, -144));
            Assert.Equal(-144.0, AudioMath.GainToDb(-1, -144));
            Assert.Equal(-144.0, AudioMath.GainToDb(double.NaN, -144));
            Assert.Equal(-144.0, AudioMath.GainToDb(1e-12, -144));
            Assert.Equal(0.0, AudioMath.GainToDb(1, -144));
            Assert.False(double.IsInfinity(AudioMath.GainToDb(double.PositiveInfinity, -144)));
            Assert.Equal(-6.0206, AudioMath.GainToDb(0.5, -144), 3);
            Assert.Equal(0.5, AudioMath.DbToGain(-6.0206), 4);
        }

        [Fact]
        public void SanitizeGainClampsAndReplacesNaN()
        {
            Assert.Equal(2f, AudioMath.SanitizeGain(5f, 0f, 2f, 1f));
            Assert.Equal(0f, AudioMath.SanitizeGain(-1f, 0f, 2f, 1f));
            Assert.Equal(1f, AudioMath.SanitizeGain(float.NaN, 0f, 2f, 1f));
            Assert.Equal(1.5f, AudioMath.SanitizeGain(1.5f, 0f, 2f, 1f));
        }
    }
}
