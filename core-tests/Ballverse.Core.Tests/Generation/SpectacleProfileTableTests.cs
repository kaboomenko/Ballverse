using System;
using System.Collections.Generic;
using System.IO;
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
                if (mode.Interactive) Assert.Equal(0.0, p.Weights.W1);
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
        public void RemoteJsonOverridesPartiallyByIdAndCode()
        {
            string json = "{ \"ring_escape\": { \"w1\": 2.5, \"densityMax\": 9 }, \"M03\": { \"qualityThreshold\": 1.1 } }";
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Empty(warnings);
            SpectacleProfile m01 = t.Get(ModeCode.M01);
            SpectacleProfile d01 = SpectacleProfileTable.Default.Get(ModeCode.M01);
            Assert.Equal(2.5, m01.Weights.W1);
            Assert.Equal(d01.Weights.W2, m01.Weights.W2);
            Assert.Equal(d01.Density.MinPerSecond, m01.Density.MinPerSecond);
            Assert.Equal(9.0, m01.Density.MaxPerSecond);
            Assert.Equal(d01.QualityThreshold, m01.QualityThreshold);
            Assert.Equal(1.1, t.Get(ModeCode.M03).QualityThreshold);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M02), t.Get(ModeCode.M02));
            // The source table is immutable.
            Assert.Equal(d01.Weights.W1, SpectacleProfileTable.Default.Get(ModeCode.M01).Weights.W1);
        }

        [Fact]
        public void InvalidEntriesAreSkippedWithSortedWarnings()
        {
            string json = "{ \"zzz\": {}, \"orbit\": { \"w5\": -1 }, \"color_war\": { \"densityMin\": 5, \"densityMax\": 2 }, "
                + "\"M02\": null, \"smash\": { \"w2\": 0.1 } }";
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Equal(4, warnings.Count);
            Assert.StartsWith("generation_weights.M02:", warnings[0]);
            Assert.StartsWith("generation_weights.color_war:", warnings[1]);
            Assert.StartsWith("generation_weights.orbit:", warnings[2]);
            Assert.StartsWith("generation_weights.zzz:", warnings[3]);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M14), t.Get(ModeCode.M14));
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M08), t.Get(ModeCode.M08));
            Assert.Equal(0.1, t.Get(ModeCode.M11).Weights.W2);
        }

        [Theory]
        [InlineData("{ not json")]
        [InlineData("[1,2,3]")]
        [InlineData("42")]
        public void MalformedJsonKeepsTable(string json)
        {
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Same(SpectacleProfileTable.Default, t);
            Assert.Single(warnings);
            Assert.Contains("malformed", warnings[0]);
        }

        [Fact]
        public void RemoteWeightsOneBadEntryKeepsOthers()
        {
            // QA probe P6: a type error in one mode must not discard the valid entries.
            var t = SpectacleProfileTable.Default.ApplyRemoteJson("{\"M01\":{\"w1\":\"abc\"},\"M02\":{\"w1\":2.5},\"orbit\":5}",
                out IReadOnlyList<string> warnings);
            Assert.Equal(2, warnings.Count);
            Assert.StartsWith("generation_weights.M01: invalid entry", warnings[0]);
            Assert.StartsWith("generation_weights.orbit: entry must be an object", warnings[1]);
            Assert.Equal(2.5, t.Get(ModeCode.M02).Weights.W1);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M01), t.Get(ModeCode.M01));
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M14), t.Get(ModeCode.M14));
        }

        [Fact]
        public void RemoteWeightsAndThresholdAreBounded()
        {
            // QA probe P14: 1e308 weights would overflow the total to +Infinity.
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(
                "{\"M01\":{\"w1\":1e308,\"w2\":1e308},\"M02\":{\"w5\":100.5},\"M03\":{\"qualityThreshold\":-1e6},\"M04\":{\"w2\":100}}",
                out IReadOnlyList<string> warnings);
            Assert.Equal(3, warnings.Count);
            Assert.StartsWith("generation_weights.M01:", warnings[0]);
            Assert.StartsWith("generation_weights.M02:", warnings[1]);
            Assert.StartsWith("generation_weights.M03:", warnings[2]);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M01), t.Get(ModeCode.M01));
            Assert.Equal(100.0, t.Get(ModeCode.M04).Weights.W2);
            SpectacleProfile m01 = t.Get(ModeCode.M01);
            double total = SpectacleScorer.Score(Runs.Good(), Runs.Target, m01.Weights, m01.Density).Total;
            Assert.False(double.IsInfinity(total));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("null")]
        public void EmptyJsonKeepsTable(string json)
        {
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Same(SpectacleProfileTable.Default, t);
            Assert.Empty(warnings);
        }

        [Fact]
        public void ToJsonRoundTrips()
        {
            var modified = SpectacleProfileTable.Default.With(ModeCode.M09,
                new SpectacleProfile(new SpectacleWeights(0.25, 0.5, 0.75, 1, 1.25), new DensityRange(0.5, 3), 1.75));
            string json = modified.ToJson();
            var back = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Empty(warnings);
            foreach (ModeInfo m in ModeCatalog.All)
            {
                SpectacleProfile a = modified.Get(m.Code), b = back.Get(m.Code);
                Assert.Equal(a.Weights, b.Weights);
                Assert.Equal(a.Density, b.Density);
                Assert.Equal(a.QualityThreshold, b.QualityThreshold);
            }
            Assert.Equal(json, back.ToJson());
            Assert.True(json.IndexOf("\"ball_battle\"", StringComparison.Ordinal) < json.IndexOf("\"ring_escape\"", StringComparison.Ordinal));
        }

        [Fact]
        public void SampleRemoteConfigFileApplies()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Generation", "remote_generation_weights.json"));
            var t = SpectacleProfileTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Empty(warnings);
            Assert.Equal(1.4, t.Get(ModeCode.M03).Weights.W3);
            Assert.Equal(2.0, t.Get(ModeCode.M06).QualityThreshold);
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
