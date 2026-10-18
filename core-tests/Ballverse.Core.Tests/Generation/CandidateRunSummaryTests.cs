using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class CandidateRunSummaryTests
    {
        static CandidateRunSummary Make(int duration = 600, bool finished = true, bool limit = false, IReadOnlyList<int> events = null,
            double coverage = 0.5, double tension = 0.5, int winner = -1, int participants = 0, IReadOnlyList<int> leaders = null,
            IReadOnlyList<int> last = null)
        {
            return new CandidateRunSummary(duration, finished, limit, events, 0, coverage, tension, winner, participants, leaders, false, last);
        }

        [Fact]
        public void StoresValuesAndTreatsNullListsAsEmpty()
        {
            var s = new CandidateRunSummary(600, true, false, null, 7, 0.25, 0.75, 1, 3, null, true);
            Assert.Equal(600, s.DurationTicks);
            Assert.True(s.Finished);
            Assert.False(s.EndedByLimit);
            Assert.Empty(s.SignificantEventTicks);
            Assert.Equal(7, s.CollisionCount);
            Assert.Equal(0.25, s.Coverage);
            Assert.Equal(0.75, s.FinishTension);
            Assert.Equal(1, s.WinnerIndex);
            Assert.Equal(3, s.ParticipantCount);
            Assert.Empty(s.LeaderBySecond);
            Assert.Empty(s.LastPlaceBySecond);
            Assert.True(s.Stuck);
        }

        [Fact]
        public void CopiesInputLists()
        {
            var events = new List<int> { 10, 20 };
            var leaders = new List<int> { 0, 1 };
            var s = Make(events: events, leaders: leaders, participants: 2);
            events[0] = 500;
            leaders[0] = 1;
            Assert.Equal(10, s.SignificantEventTicks[0]);
            Assert.Equal(0, s.LeaderBySecond[0]);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(SimConstants.MaxDurationTicks + 1)]
        public void RejectsDurationOutOfRange(int duration)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(duration: duration));
        }

        [Fact]
        public void AcceptsDurationBounds()
        {
            Make(duration: 0);
            Make(duration: SimConstants.MaxDurationTicks);
        }

        [Fact]
        public void RejectsFinishedAndEndedByLimitTogether()
        {
            Assert.Throws<ArgumentException>(() => Make(finished: true, limit: true));
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void RejectsMetricsOutsideUnitRange(double v)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(coverage: v));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(tension: v));
        }

        [Fact]
        public void RejectsUnsortedOrOutOfRangeEvents()
        {
            Assert.Throws<ArgumentException>(() => Make(events: new[] { 20, 10 }));
            Assert.Throws<ArgumentException>(() => Make(events: new[] { -1 }));
            Assert.Throws<ArgumentException>(() => Make(duration: 100, events: new[] { 101 }));
            Make(duration: 100, events: new[] { 0, 0, 100 });
        }

        [Fact]
        public void DuplicateEventTicksAreCollapsedSoTheListIsBounded()
        {
            var s = Make(duration: 100, events: new[] { 0, 0, 5, 5, 5, 100, 100 });
            Assert.Equal(new[] { 0, 5, 100 }, s.SignificantEventTicks);
            var dense = new int[50000];
            for (int i = 0; i < dense.Length; i++) dense[i] = i / 10;
            var big = Make(duration: SimConstants.MaxDurationTicks, events: dense);
            Assert.Equal(5000, big.SignificantEventTicks.Count);
            Assert.True(big.SignificantEventTicks.Count <= big.DurationTicks + 1);
        }

        [Fact]
        public void RejectsNegativeCollisionCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateRunSummary(600, true, false, null, -1, 0.5, 0.5, -1, 0, null, false));
        }

        [Fact]
        public void ReachedFinaleIncludesTheGlobalCapButNotTheTargetLimit()
        {
            Assert.True(Make(finished: true).ReachedFinale);
            Assert.False(Make(duration: 2340, finished: false, limit: true).ReachedFinale);
            Assert.True(Make(duration: SimConstants.MaxDurationTicks, finished: false, limit: true).ReachedFinale);
            Assert.False(Make(finished: false, limit: false).ReachedFinale);
        }

        [Fact]
        public void RejectsBadParticipantIndices()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(participants: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(winner: 2, participants: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(winner: -2, participants: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(winner: 0, participants: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(participants: 2, leaders: new[] { 0, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(participants: 2, last: new[] { -2 }));
            Make(winner: 1, participants: 2, leaders: new[] { -1, 0, 1 }, last: new[] { 1, -1 });
        }

        [Fact]
        public void RoundTripsThroughCoreJson()
        {
            var s = new CandidateRunSummary(1800, false, true, new[] { 5, 60, 900 }, 321, 0.4, 0.9, 2, 4, new[] { 0, 1, -1 }, false, new[] { 2, 2, 3 });
            string json = CoreJson.Serialize(s);
            Assert.Contains("\"durationTicks\"", json);
            Assert.Contains("\"endedByLimit\"", json);
            var back = CoreJson.Deserialize<CandidateRunSummary>(json);
            Assert.Equal(s.DurationTicks, back.DurationTicks);
            Assert.Equal(s.Finished, back.Finished);
            Assert.Equal(s.EndedByLimit, back.EndedByLimit);
            Assert.Equal(s.SignificantEventTicks, back.SignificantEventTicks);
            Assert.Equal(321, back.CollisionCount);
            Assert.DoesNotContain("reachedFinale", json);
            Assert.Equal(s.Coverage, back.Coverage);
            Assert.Equal(s.FinishTension, back.FinishTension);
            Assert.Equal(s.WinnerIndex, back.WinnerIndex);
            Assert.Equal(s.ParticipantCount, back.ParticipantCount);
            Assert.Equal(s.LeaderBySecond, back.LeaderBySecond);
            Assert.Equal(s.LastPlaceBySecond, back.LastPlaceBySecond);
            Assert.Equal(s.Stuck, back.Stuck);
        }

        [Fact]
        public void JsonWithoutOptionalListsDeserializes()
        {
            var back = CoreJson.Deserialize<CandidateRunSummary>(
                "{\"durationTicks\":600,\"finished\":true,\"coverage\":0.5,\"finishTension\":0.1,\"winnerIndex\":-1,\"participantCount\":0}");
            Assert.Equal(600, back.DurationTicks);
            Assert.Empty(back.SignificantEventTicks);
            Assert.Empty(back.LeaderBySecond);
            Assert.Empty(back.LastPlaceBySecond);
        }
    }
}
