using System;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class GenerationAcceptanceTests
    {
        [Theory]
        [InlineData(1800, true)]
        [InlineData(1620, true)]  // -3 s
        [InlineData(1980, true)]  // +3 s
        [InlineData(1619, false)]
        [InlineData(1981, false)]
        public void WithinTargetUsesThreeSecondTolerance(int duration, bool expected)
        {
            Assert.Equal(expected, GenerationAcceptance.FinishedWithinTarget(Runs.Make(durationTicks: duration), 1800));
        }

        [Fact]
        public void RunStoppedByLimitNeverCounts()
        {
            Assert.False(GenerationAcceptance.FinishedWithinTarget(Runs.Make(durationTicks: 1800, finished: false, endedByLimit: true), 1800));
        }

        [Fact]
        public void FractionCountsHitsAndTreatsNullAsMiss()
        {
            var runs = new[] { Runs.Make(durationTicks: 1800), Runs.Make(durationTicks: 2000), null, Runs.Make(durationTicks: 1700) };
            Assert.Equal(0.5, GenerationAcceptance.FractionFinishedWithinTarget(runs, 1800));
            Assert.Equal(0.0, GenerationAcceptance.FractionFinishedWithinTarget(new CandidateRunSummary[0], 1800));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationAcceptance.FractionFinishedWithinTarget(runs, 0));
            Assert.Throws<ArgumentNullException>(() => GenerationAcceptance.FractionFinishedWithinTarget((CandidateRunSummary[])null, 1800));
        }

        [Fact]
        public void FractionOverSessionResults()
        {
            var results = new GenerationResult[3];
            for (int i = 0; i < 2; i++)
            {
                var session = new GenerationSession(Runs.Options());
                int duration = i == 0 ? 1800 : 2300; // 2300 is within the 2340-tick run limit but misses 30 ± 3 s
                Runs.Drive(session, k => Runs.Make(durationTicks: duration));
                results[i] = session.Result;
            }
            Assert.Equal(1.0 / 3.0, GenerationAcceptance.FractionFinishedWithinTarget(results, 1800), 12);
            Assert.True(GenerationAcceptance.RequiredDurationHitRate > 1.0 / 3.0);
        }

        [Fact]
        public void FractionWithinTime()
        {
            Assert.Equal(0.75, GenerationAcceptance.FractionWithinTime(new long[] { 100, 2500, 2501, 900 }, 2500));
            Assert.Equal(0.0, GenerationAcceptance.FractionWithinTime(new long[0], 2500));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationAcceptance.FractionWithinTime(new long[] { 1 }, 0));
            Assert.Throws<ArgumentNullException>(() => GenerationAcceptance.FractionWithinTime(null, 10));
            Assert.Equal(0.95, GenerationAcceptance.RequiredTimeHitRate);
        }
    }
}
