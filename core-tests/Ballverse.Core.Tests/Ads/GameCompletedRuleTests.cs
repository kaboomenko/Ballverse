using Ballverse.Core.Ads;
using Xunit;

namespace Ballverse.Core.Tests.Ads
{
    public class GameCompletedRuleTests
    {
        [Fact]
        public void MinimumIsFiveSecondsAt60Hz()
        {
            Assert.Equal(5, GameCompletedRule.MinDurationSeconds);
            Assert.Equal(300, GameCompletedRule.MinDurationTicks);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(299, false)]
        [InlineData(300, true)]
        [InlineData(301, true)]
        [InlineData(SimConstants.MaxDurationTicks, true)]
        [InlineData(int.MaxValue, true)]
        [InlineData(-1, false)]
        [InlineData(int.MinValue, false)]
        public void DurationBoundary(int ticks, bool expected)
        {
            Assert.Equal(expected, GameCompletedRule.IsGameCompleted(GameRunSummary.FinishedRegular(ticks)));
        }

        [Theory]
        [InlineData(GameRunKind.PaidModeTrialPreview)]
        [InlineData(GameRunKind.GenerationCandidate)]
        [InlineData(GameRunKind.StudioReplayPlayback)]
        public void ExcludedKindsNeverCount(GameRunKind kind)
        {
            Assert.False(GameCompletedRule.IsGameCompleted(new GameRunSummary(kind, GameRunEnd.Finished, 10_000)));
        }

        [Theory]
        [InlineData(GameRunEnd.RestartedBeforeFinale)]
        [InlineData(GameRunEnd.Abandoned)]
        public void RunsThatDidNotReachFinishedNeverCount(GameRunEnd end)
        {
            Assert.False(GameCompletedRule.IsGameCompleted(new GameRunSummary(GameRunKind.Regular, end, 10_000)));
        }

        [Fact]
        public void UnknownEnumValuesNeverCount()
        {
            Assert.False(GameCompletedRule.IsGameCompleted(new GameRunSummary((GameRunKind)99, GameRunEnd.Finished, 1000)));
            Assert.False(GameCompletedRule.IsGameCompleted(new GameRunSummary(GameRunKind.Regular, (GameRunEnd)99, 1000)));
        }

        [Fact]
        public void DefaultSummaryIsNotCompleted()
        {
            Assert.False(GameCompletedRule.IsGameCompleted(default(GameRunSummary)));
        }

        [Fact]
        public void ExhaustiveKindEndMatrix()
        {
            foreach (GameRunKind kind in System.Enum.GetValues(typeof(GameRunKind)))
            {
                foreach (GameRunEnd end in System.Enum.GetValues(typeof(GameRunEnd)))
                {
                    bool expected = kind == GameRunKind.Regular && end == GameRunEnd.Finished;
                    Assert.Equal(expected, GameCompletedRule.IsGameCompleted(new GameRunSummary(kind, end, 300)));
                    Assert.False(GameCompletedRule.IsGameCompleted(new GameRunSummary(kind, end, 299)));
                }
            }
        }
    }
}
