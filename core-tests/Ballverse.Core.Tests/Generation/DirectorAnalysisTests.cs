using System;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class DirectorAnalysisTests
    {
        // 20 s run: first half = samples 0..9 (ticks 60..600 <= 600).
        static CandidateRunSummary Race(int winner, int[] leaders, int[] last = null, int participants = 4, int duration = 1200)
        {
            return Runs.Make(durationTicks: duration, winner: winner, participants: participants, leaders: leaders, lastPlace: last);
        }

        static int[] Fill(int length, int value, int from = 0, int to = int.MaxValue, int other = 0)
        {
            var a = new int[length];
            for (int i = 0; i < length; i++) a[i] = i >= from && i < to ? value : other;
            return a;
        }

        [Fact]
        public void FirstHalfSampleCount()
        {
            Assert.Equal(10, DirectorAnalysis.FirstHalfSampleCount(1200));
            Assert.Equal(10, DirectorAnalysis.FirstHalfSampleCount(1319));
            Assert.Equal(11, DirectorAnalysis.FirstHalfSampleCount(1320));
            Assert.Equal(0, DirectorAnalysis.FirstHalfSampleCount(119));
            Assert.Equal(1, DirectorAnalysis.FirstHalfSampleCount(120));
            Assert.Throws<ArgumentOutOfRangeException>(() => DirectorAnalysis.FirstHalfSampleCount(-1));
        }

        [Fact]
        public void TwoParticipantsWinnerWhoTrailedInFirstHalfIsComeback()
        {
            // Participant 1 leads only from second 10 onwards (second half); participant 0 led before.
            int[] leaders = Fill(20, 1, from: 10, other: 0);
            Assert.True(DirectorAnalysis.IsComeback(Race(1, leaders, participants: 2)));
        }

        [Fact]
        public void WinnerWhoLedInFirstHalfIsNotComeback()
        {
            int[] leaders = Fill(20, 1, from: 10, other: 0);
            leaders[9] = 1; // led at the last first-half sample
            Assert.False(DirectorAnalysis.IsComeback(Race(1, leaders, participants: 2)));
            leaders[9] = 0;
            leaders[0] = 1;
            Assert.False(DirectorAnalysis.IsComeback(Race(1, leaders, participants: 2)));
            // Also with last-place samples showing the winner last at some point.
            Assert.False(DirectorAnalysis.IsComeback(Race(1, leaders, Fill(20, 1), participants: 2)));
        }

        [Fact]
        public void ComebackFalseWhenFirstHalfAllTies_TwoParticipants()
        {
            // QA probe P2: a dead heat (no clear leader) for the whole first half is not "was last".
            int[] leaders = Fill(20, -1, to: 10, other: 0);
            Assert.False(DirectorAnalysis.IsComeback(Race(0, leaders, participants: 2)));
        }

        [Fact]
        public void TiesCountAsNeitherLeadingNorEvidence_TwoParticipants()
        {
            // Mostly ties, but the other participant led once: the winner trailed, so it is a comeback.
            int[] leaders = Fill(20, -1, to: 10, other: 0);
            leaders[3] = 1;
            Assert.True(DirectorAnalysis.IsComeback(Race(0, leaders, participants: 2)));
        }

        [Fact]
        public void ComebackRequiresLastPlaceForMoreThanTwo()
        {
            // QA probe P3: 4 participants, participant 1 leads the first half, the winner 2 was never last as far as we know.
            int[] leaders = Fill(20, 2, from: 10, other: 1);
            Assert.False(DirectorAnalysis.IsComeback(Race(2, leaders)));
            // Second place throughout the first half (last place is participant 3): still not a comeback.
            Assert.False(DirectorAnalysis.IsComeback(Race(2, leaders, Fill(20, 3))));
            // With last-place evidence it is.
            Assert.True(DirectorAnalysis.IsComeback(Race(2, leaders, Fill(20, 2, to: 3, other: 3))));
        }

        [Fact]
        public void LastPlaceSamplesSharpenDetection()
        {
            int[] leaders = Fill(20, 3, from: 10, other: 0);
            int[] lastWithWinner = Fill(20, 3, from: 4, to: 6, other: 1);
            int[] lastWithoutWinner = Fill(20, 1);
            lastWithoutWinner[15] = 3; // last only in the second half
            Assert.True(DirectorAnalysis.IsComeback(Race(3, leaders, lastWithWinner)));
            Assert.False(DirectorAnalysis.IsComeback(Race(3, leaders, lastWithoutWinner)));
            // Two participants: last-place samples, when reported, are the evidence.
            int[] duel = Fill(20, 1, from: 10, other: -1);
            Assert.False(DirectorAnalysis.IsComeback(Race(1, duel, Fill(20, 0), participants: 2)));
            Assert.True(DirectorAnalysis.IsComeback(Race(1, duel, Fill(20, 1, to: 1, other: -1), participants: 2)));
        }

        [Fact]
        public void NoComebackWithoutWinnerOrSamples()
        {
            int[] leaders = Fill(20, 0);
            Assert.False(DirectorAnalysis.IsComeback(Race(-1, leaders, participants: 2)));
            Assert.False(DirectorAnalysis.IsComeback(Race(1, new int[0], participants: 2)));
            // Run too short to have a first-half sample.
            Assert.False(DirectorAnalysis.IsComeback(Race(1, new[] { 0 }, participants: 2, duration: 100)));
            // Single participant cannot come back.
            Assert.False(DirectorAnalysis.IsComeback(Race(0, new[] { -1, -1, -1 }, participants: 1)));
        }

        [Fact]
        public void ShortLeaderSeriesUsesAvailableSamples()
        {
            // Only 3 samples although the first half has 10: the winner never led in them, the other did.
            Assert.True(DirectorAnalysis.IsComeback(Race(1, new[] { 0, 0, -1 }, participants: 2)));
        }

        [Fact]
        public void MatchesWinner()
        {
            var s = Runs.Make(winner: 1, participants: 3);
            Assert.True(DirectorAnalysis.MatchesWinner(s, 1));
            Assert.False(DirectorAnalysis.MatchesWinner(s, 2));
            Assert.False(DirectorAnalysis.MatchesWinner(Runs.Make(winner: -1, participants: 3), -1));
            Assert.Throws<ArgumentNullException>(() => DirectorAnalysis.MatchesWinner(null, 1));
            Assert.Throws<ArgumentNullException>(() => DirectorAnalysis.IsComeback(null));
        }
    }
}
