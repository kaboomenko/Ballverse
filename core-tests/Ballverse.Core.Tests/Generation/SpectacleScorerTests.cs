using System;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class SpectacleScorerTests
    {
        const double Eps = 1e-12;
        static readonly DensityRange Range = new DensityRange(1.0, 8.0);

        // ---- fitDuration ----

        [Fact]
        public void FitIsOneAtExactTarget()
        {
            Assert.Equal(1.0, SpectacleScorer.FitDuration(1800, 1800));
        }

        [Theory]
        [InlineData(2070)] // +15%
        [InlineData(1530)] // -15%
        public void FitIsHalfAtFifteenPercent(int duration)
        {
            Assert.Equal(0.5, SpectacleScorer.FitDuration(duration, 1800), 12);
        }

        [Theory]
        [InlineData(2340)] // +30%
        [InlineData(1260)] // -30%
        [InlineData(2341)]
        [InlineData(0)]
        [InlineData(SimConstants.MaxDurationTicks)]
        public void FitIsZeroAtAndBeyondThirtyPercent(int duration)
        {
            Assert.Equal(0.0, SpectacleScorer.FitDuration(duration, 1800));
        }

        [Fact]
        public void FitIsLinearInsideTolerance()
        {
            for (int d = 1261; d < 2340; d++)
            {
                double expected = 1.0 - Math.Abs(d - 1800) / (0.3 * 1800);
                Assert.Equal(expected, SpectacleScorer.FitDuration(d, 1800), 9);
                Assert.True(SpectacleScorer.FitDuration(d, 1800) > 0.0);
            }
        }

        [Fact]
        public void FitIsOneWithoutTarget()
        {
            Assert.Equal(1.0, SpectacleScorer.FitDuration(5, null));
            Assert.Equal(1.0, SpectacleScorer.FitDuration(Runs.Make(durationTicks: 100, eventEveryTicks: 10), null));
        }

        [Fact]
        public void FitIsZeroWhenEndedByLimitWithTarget()
        {
            var s = Runs.Make(durationTicks: 1800, finished: false, endedByLimit: true);
            Assert.Equal(0.0, SpectacleScorer.FitDuration(s, 1800));
            Assert.Equal(1.0, SpectacleScorer.FitDuration(s, null));
        }

        [Fact]
        public void FitRejectsNonPositiveTarget()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.FitDuration(100, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.FitDuration(100, -5));
            var limited = Runs.Make(finished: false, endedByLimit: true);
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.FitDuration(limited, 0));
        }

        // ---- eventDensity ----

        [Theory]
        [InlineData(1.0)]
        [InlineData(4.0)]
        [InlineData(8.0)]
        public void DensityIsOneInsideRangeIncludingBounds(double rate)
        {
            Assert.Equal(1.0, SpectacleScorer.DensityScore(rate, Range));
        }

        [Fact]
        public void DensityHalvesAtFactorTwoAndDropsFastAtFactorFour()
        {
            Assert.Equal(0.5, SpectacleScorer.DensityScore(0.5, Range), 12);
            Assert.Equal(0.5, SpectacleScorer.DensityScore(16.0, Range), 12);
            Assert.Equal(0.0625, SpectacleScorer.DensityScore(0.25, Range), 12);
            Assert.Equal(0.0625, SpectacleScorer.DensityScore(32.0, Range), 12);
        }

        [Fact]
        public void DensityIsZeroWithoutEvents()
        {
            Assert.Equal(0.0, SpectacleScorer.DensityScore(0.0, Range));
            Assert.Equal(0.0, SpectacleScorer.EventDensity(0, 1800, Range));
            Assert.Equal(0.0, SpectacleScorer.EventDensity(0, 0, Range));
            Assert.Equal(0.0, SpectacleScorer.DensityScore(double.PositiveInfinity, Range));
        }

        [Fact]
        public void DensityWithZeroMinAcceptsSilence()
        {
            var r = new DensityRange(0.0, 2.0);
            Assert.Equal(1.0, SpectacleScorer.DensityScore(0.0, r));
            Assert.Equal(1.0, SpectacleScorer.EventDensity(0, 0, r));
        }

        [Fact]
        public void DensityIsContinuousAndMonotonicOutside()
        {
            Assert.True(SpectacleScorer.DensityScore(0.9999999, Range) > 0.999999);
            Assert.True(SpectacleScorer.DensityScore(8.0000001, Range) > 0.999999);
            double prev = 1.0;
            for (double rate = 8.0; rate < 100.0; rate += 0.5)
            {
                double v = SpectacleScorer.DensityScore(rate, Range);
                Assert.True(v <= prev + Eps);
                Assert.InRange(v, 0.0, 1.0);
                prev = v;
            }
            prev = 1.0;
            for (double rate = 1.0; rate > 0.001; rate *= 0.9)
            {
                double v = SpectacleScorer.DensityScore(rate, Range);
                Assert.True(v <= prev + Eps);
                prev = v;
            }
        }

        [Fact]
        public void EventsPerSecondUsesTickRate()
        {
            Assert.Equal(2.0, SpectacleScorer.EventsPerSecond(60, 1800));
            Assert.Equal(0.0, SpectacleScorer.EventsPerSecond(10, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.EventsPerSecond(-1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.EventsPerSecond(1, -10));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.DensityScore(-1.0, Range));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.DensityScore(double.NaN, Range));
        }

        // ---- stagnation ----

        [Fact]
        public void StagnationIsWholeRunWithoutEvents()
        {
            Assert.Equal(1.0, SpectacleScorer.Stagnation(new int[0], 600));
        }

        [Fact]
        public void ShortRunWithoutEventsIsNotStagnant()
        {
            Assert.Equal(0.0, SpectacleScorer.Stagnation(new int[0], 180));
            Assert.Equal(1.0, SpectacleScorer.Stagnation(new int[0], 181));
        }

        [Fact]
        public void StagnationZeroWithRegularEvents()
        {
            Assert.Equal(0.0, SpectacleScorer.Stagnation(Runs.EvenEvents(1800, 60), 1800));
            Assert.Equal(0.0, SpectacleScorer.Stagnation(Runs.EvenEvents(1800, 180), 1800));
        }

        [Fact]
        public void GapOfExactlyThreeSecondsIsNotStagnation()
        {
            Assert.Equal(0.0, SpectacleScorer.Stagnation(new[] { 100, 280 }, 300));
            // 100 -> 281 is 181 ticks: counted in full.
            Assert.Equal(181.0 / 300.0, SpectacleScorer.Stagnation(new[] { 100, 281 }, 300), 12);
        }

        [Fact]
        public void StagnationCountsStartAndEndGaps()
        {
            // start gap 0..240 (240), middle 240..300 (60), end gap 300..600 (300)
            Assert.Equal(540.0 / 600.0, SpectacleScorer.Stagnation(new[] { 240, 300 }, 600), 12);
        }

        [Fact]
        public void StagnationHandlesDuplicateAndBoundaryTicks()
        {
            Assert.Equal(0.0, SpectacleScorer.Stagnation(new[] { 0, 0, 150, 150, 300 }, 300));
            Assert.Equal(0.0, SpectacleScorer.Stagnation(new int[0], 0));
        }

        [Fact]
        public void StagnationRejectsBadInput()
        {
            Assert.Throws<ArgumentException>(() => SpectacleScorer.Stagnation(new[] { 50, 40 }, 100));
            Assert.Throws<ArgumentException>(() => SpectacleScorer.Stagnation(new[] { 150 }, 100));
            Assert.Throws<ArgumentNullException>(() => SpectacleScorer.Stagnation(null, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.Stagnation(new int[0], -1));
        }

        // ---- total ----

        [Fact]
        public void TotalFollowsFormula()
        {
            var w = new SpectacleWeights(1.5, 0.7, 0.9, 0.3, 2.0);
            // 30 s run, events at 2..10 s then nothing: stagnation = 20 s / 30 s
            var events = new[] { 120, 240, 360, 480, 600 };
            var s = new CandidateRunSummary(1800, true, false, events, 0.6, 0.4, -1, 0, null, false);
            SpectacleBreakdown b = SpectacleScorer.Score(s, 2070, w, Range);
            double fit = 1.0 - (270.0 / 2070.0) / 0.3;
            double density = SpectacleScorer.DensityScore(5.0 / 30.0, Range);
            double stagnation = 1200.0 / 1800.0;
            Assert.Equal(fit, b.FitDuration, 12);
            Assert.Equal(density, b.EventDensity, 12);
            Assert.Equal(0.4, b.FinishTension);
            Assert.Equal(0.6, b.Coverage);
            Assert.Equal(stagnation, b.Stagnation, 12);
            Assert.Equal(1.5 * fit + 0.7 * density + 0.9 * 0.4 + 0.3 * 0.6 - 2.0 * stagnation, b.Total, 12);
            Assert.False(b.IsRejected);
        }

        [Fact]
        public void PerfectRunReachesMaxTotal()
        {
            var w = SpectacleWeights.Default;
            var s = Runs.Make(coverage: 1.0, tension: 1.0);
            SpectacleBreakdown b = SpectacleScorer.Score(s, Runs.Target, w, Range);
            Assert.Equal(w.MaxTotal, b.Total, 12);
        }

        [Fact]
        public void StuckCandidateIsRejectedButScored()
        {
            var s = Runs.Make(stuck: true);
            SpectacleBreakdown b = SpectacleScorer.Score(s, Runs.Target, SpectacleWeights.Default, Range);
            Assert.Equal(RejectReason.Stuck, b.Rejected);
            Assert.True(b.IsRejected);
            Assert.True(b.Total > 2.0);
        }

        [Fact]
        public void AbortedCandidateIsRejected()
        {
            var s = Runs.Make(finished: false, endedByLimit: false);
            Assert.Equal(RejectReason.Incomplete, SpectacleScorer.Score(s, null, SpectacleWeights.Default, Range).Rejected);
            var limited = Runs.Make(finished: false, endedByLimit: true);
            Assert.Equal(RejectReason.None, SpectacleScorer.Score(limited, null, SpectacleWeights.Default, Range).Rejected);
        }

        [Fact]
        public void StuckWinsOverIncompleteAsReason()
        {
            var s = Runs.Make(finished: false, stuck: true);
            Assert.Equal(RejectReason.Stuck, SpectacleScorer.Score(s, null, SpectacleWeights.Default, Range).Rejected);
        }

        [Fact]
        public void ScoreValidatesArguments()
        {
            Assert.Throws<ArgumentNullException>(() => SpectacleScorer.Score(null, null, SpectacleWeights.Default, Range));
            Assert.Throws<ArgumentNullException>(() => SpectacleScorer.Score(Runs.Good(), null, null, Range));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpectacleScorer.Score(Runs.Good(), 0, SpectacleWeights.Default, Range));
        }

        [Fact]
        public void EmptyRunScoresWithoutDivisionByZero()
        {
            var s = new CandidateRunSummary(0, true, false, null, 0.0, 0.0, -1, 0, null, false);
            SpectacleBreakdown b = SpectacleScorer.Score(s, 1800, SpectacleWeights.Default, Range);
            Assert.Equal(0.0, b.FitDuration);
            Assert.Equal(0.0, b.EventDensity);
            Assert.Equal(0.0, b.Stagnation);
            Assert.Equal(0.0, b.Total);
        }

        [Fact]
        public void RandomSummariesStayWithinBounds()
        {
            var rng = Rng.FromSeed(2024UL, 7);
            var w = new SpectacleWeights(1.2, 0.9, 0.7, 0.4, 1.6);
            for (int i = 0; i < 2000; i++)
            {
                CandidateRunSummary s = RandomSummary(ref rng);
                int? target = rng.NextBool() ? (int?)rng.NextInt(600, SimConstants.MaxDurationTicks + 1) : null;
                SpectacleBreakdown b = SpectacleScorer.Score(s, target, w, Range);
                Assert.InRange(b.FitDuration, 0.0, 1.0);
                Assert.InRange(b.EventDensity, 0.0, 1.0);
                Assert.InRange(b.Stagnation, 0.0, 1.0);
                Assert.InRange(b.Total, -w.W5 - Eps, w.MaxTotal + Eps);
                // Pure: scoring again gives the identical result.
                Assert.Equal(b.Total, SpectacleScorer.Score(s, target, w, Range).Total);
            }
        }

        internal static CandidateRunSummary RandomSummary(ref Rng rng)
        {
            int duration = rng.NextInt(0, SimConstants.MaxDurationTicks + 1);
            int eventCount = rng.NextInt(0, 400);
            var events = new int[eventCount];
            for (int e = 0; e < eventCount; e++) events[e] = rng.NextInt(0, duration + 1);
            Array.Sort(events);
            bool finished = rng.NextBool();
            return new CandidateRunSummary(duration, finished, !finished && rng.NextBool(), events, rng.NextDouble(), rng.NextDouble(),
                -1, 0, null, rng.NextUInt(10) == 0);
        }
    }
}
