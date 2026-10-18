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
        public void WinnerWhoNeverLedInFirstHalfIsComeback()
        {
            // Player 2 leads only from second 10 onwards (second half).
            int[] leaders = Fill(20, 2, from: 10, other: 0);
            Assert.True(DirectorAnalysis.IsComeback(Race(2, leaders)));
        }

        [Fact]
        public void WinnerWhoLedInFirstHalfIsNotComeback()
        {
            int[] leaders = Fill(20, 2, from: 10, other: 0);
            leaders[9] = 2; // led at the last first-half sample
            Assert.False(DirectorAnalysis.IsComeback(Race(2, leaders)));
            leaders[9] = 0;
            leaders[0] = 2;
            Assert.False(DirectorAnalysis.IsComeback(Race(2, leaders)));
        }

        [Fact]
        public void TiesInLeaderSamplesDoNotCountAsLeading()
        {
            int[] leaders = Fill(20, -1, to: 10, other: 1);
            Assert.True(DirectorAnalysis.IsComeback(Race(1, leaders)));
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
        }

        [Fact]
        public void NoComebackWithoutWinnerOrSamples()
        {
            int[] leaders = Fill(20, 0);
            Assert.False(DirectorAnalysis.IsComeback(Race(-1, leaders)));
            Assert.False(DirectorAnalysis.IsComeback(Race(1, new int[0])));
            // Run too short to have a first-half sample.
            Assert.False(DirectorAnalysis.IsComeback(Race(1, new[] { 0 }, duration: 100)));
            // Single participant cannot come back.
            Assert.False(DirectorAnalysis.IsComeback(Race(0, new[] { -1, -1, -1 }, participants: 1)));
        }

        [Fact]
        public void ShortLeaderSeriesUsesAvailableSamples()
        {
            // Only 3 samples although the first half has 10: winner never led in them.
            Assert.True(DirectorAnalysis.IsComeback(Race(1, new[] { 0, 0, 2 })));
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
