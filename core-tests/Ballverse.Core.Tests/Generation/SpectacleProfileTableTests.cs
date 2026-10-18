using System;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class SpectacleProfileTableTests
    {
        [Fact]
        public void DefaultCoversAllFifteenModesWithSaneValues()
        {
            foreach (ModeInfo mode in ModeCatalog.All)
            {
                SpectacleProfile p = SpectacleProfileTable.Default.Get(mode.Code);
                Assert.NotNull(p);
                Assert.True(p.Weights.MaxTotal > 0.0);
                Assert.True(p.Weights.W5 > 0.0, mode.Id + " must penalize stagnation");
                Assert.True(p.Density.MinPerSecond > 0.0 && p.Density.MaxPerSecond > p.Density.MinPerSecond);
                Assert.InRange(p.QualityThreshold, 0.3 * p.Weights.MaxTotal, 0.8 * p.Weights.MaxTotal);
                // Interactive-only modes do not select the duration (spec §2.4); M05 has the auto controls and keeps w1.
                if (!mode.HasAutoControl) Assert.Equal(0.0, p.Weights.W1);
                else Assert.True(p.Weights.W1 > 0.0);
            }
        }

        [Fact]
        public void GetRejectsUnknownMode()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleProfileTable.Default.Get((ModeCode)0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleProfileTable.Default.Get((ModeCode)16));
        }

        [Fact]
        public void WithReplacesOneModeOnly()
        {
            var p = new SpectacleProfile(new SpectacleWeights(2, 2, 2, 2, 2), new DensityRange(1, 2), 3.0);
            var t = SpectacleProfileTable.Default.With(ModeCode.M07, p);
            Assert.Same(p, t.Get(ModeCode.M07));
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M06), t.Get(ModeCode.M06));
            Assert.NotSame(p, SpectacleProfileTable.Default.Get(ModeCode.M07));
        }

        [Fact]
        public void DescribeIsInvariant()
        {
            Assert.Equal("M01: w=(1,0.8,0.6,0.5,1.5) density=[1.5..8]/s threshold=1.6", SpectacleProfileTable.Default.Describe(ModeCode.M01));
        }

        [Fact]
        public void WeightsAndProfileValidate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(-0.1, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(0, double.NaN, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(0, 0, double.PositiveInfinity, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(0, 0, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(0, 0, 0, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleWeights(SpectacleWeights.MaxWeight + 0.001, 0, 0, 0, 0));
            new SpectacleWeights(SpectacleWeights.MaxWeight, 0, 0, 0, SpectacleWeights.MaxWeight);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleProfile(SpectacleWeights.Default, new DensityRange(0, 1), 1001));
            Assert.Throws<ArgumentException>(() => new SpectacleProfile(SpectacleWeights.Default, default(DensityRange), 1.0));
            Assert.Throws<ArgumentNullException>(() => new SpectacleProfile(null, new DensityRange(0, 1), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpectacleProfile(SpectacleWeights.Default, new DensityRange(0, 1), double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DensityRange(2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DensityRange(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DensityRange(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DensityRange(0, double.PositiveInfinity));
            Assert.Equal(new DensityRange(1, 2), new DensityRange(1, 2));
            Assert.Equal(SpectacleWeights.Default, new SpectacleWeights(1.0, 0.8, 0.8, 0.5, 1.5));
            Assert.Equal(3.1, SpectacleWeights.Default.MaxTotal, 12);
        }
    }
}
