using System;
using System.Collections.Generic;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class HashVerifierTests
    {
        static List<ReplayStateHash> Recorded()
        {
            return new List<ReplayStateHash>
            {
                new ReplayStateHash(0, 10),
                new ReplayStateHash(60, 11),
                new ReplayStateHash(120, 12),
            };
        }

        [Fact]
        public void IdenticalListsMatch()
        {
            var r = HashVerifier.Verify(Recorded(), Recorded());
            Assert.True(r.IsMatch);
            Assert.Null(r.FirstMismatchTick);
            Assert.Equal(3, r.VerifiedCount);
        }

        [Fact]
        public void ExtraRecomputedTicksAreIgnored()
        {
            var recomputed = new List<ReplayStateHash>();
            for (uint t = 0; t <= 130; t++) recomputed.Add(new ReplayStateHash(t, t == 0 ? 10UL : t == 60 ? 11UL : t == 120 ? 12UL : 999UL));
            Assert.True(HashVerifier.Verify(Recorded(), recomputed).IsMatch);
        }

        [Fact]
        public void FirstDifferingHashIsReported()
        {
            var recomputed = new List<ReplayStateHash>
            {
                new ReplayStateHash(0, 10), new ReplayStateHash(60, 99), new ReplayStateHash(120, 98),
            };
            var r = HashVerifier.Verify(Recorded(), recomputed);
            Assert.False(r.IsMatch);
            Assert.Equal(HashMismatchKind.HashDiffers, r.Kind);
            Assert.Equal(60u, r.FirstMismatchTick);
            Assert.Equal(11UL, r.ExpectedHash);
            Assert.Equal(99UL, r.ActualHash);
            Assert.Equal(1, r.VerifiedCount);
        }

        [Fact]
        public void SkippedRecordedTickIsMissing()
        {
            var recomputed = new List<ReplayStateHash> { new ReplayStateHash(0, 10), new ReplayStateHash(120, 12) };
            var r = HashVerifier.Verify(Recorded(), recomputed);
            Assert.Equal(HashMismatchKind.Missing, r.Kind);
            Assert.Equal(60u, r.Tick);
        }

        [Fact]
        public void UncoveredTailIsMissingUnlessSimulationStoppedEarlier()
        {
            var recomputed = new List<ReplayStateHash> { new ReplayStateHash(0, 10), new ReplayStateHash(60, 11) };
            var r = HashVerifier.Verify(Recorded(), recomputed);
            Assert.Equal(HashMismatchKind.Missing, r.Kind);
            Assert.Equal(120u, r.Tick);
            Assert.True(HashVerifier.Verify(Recorded(), recomputed, 119).IsMatch);
            Assert.False(HashVerifier.Verify(Recorded(), recomputed, 120).IsMatch);
        }

        [Fact]
        public void IncrementalFeedStopsAtFirstMismatch()
        {
            var v = new HashVerifier(Recorded());
            Assert.True(v.Feed(0, 10));
            Assert.True(v.Feed(30, 1234));
            Assert.False(v.Feed(60, 0));
            Assert.False(v.IsOk);
            Assert.False(v.Feed(120, 12));        // sticky
            Assert.Equal(60u, v.Finish().FirstMismatchTick);
        }

        [Fact]
        public void RecomputedTicksMustBeStrictlyIncreasing()
        {
            var v = new HashVerifier(Recorded());
            v.Feed(0, 10);
            Assert.Throws<ArgumentException>(() => v.Feed(0, 10));
        }

        [Fact]
        public void RecordedMustBeSorted()
        {
            var bad = new List<ReplayStateHash> { new ReplayStateHash(60, 1), new ReplayStateHash(0, 1) };
            Assert.Throws<ArgumentException>(() => new HashVerifier(bad));
            Assert.Throws<ArgumentNullException>(() => new HashVerifier(null));
            Assert.Throws<ArgumentNullException>(() => HashVerifier.Verify(Recorded(), null));
        }

        [Fact]
        public void EmptyRecordedAlwaysMatches()
        {
            Assert.True(HashVerifier.Verify(new List<ReplayStateHash>(), Recorded()).IsMatch);
            Assert.True(HashVerifier.Verify(new List<ReplayStateHash>(), new List<ReplayStateHash>()).IsMatch);
        }

        [Fact]
        public void DuplicateRecordedTicksAreAllChecked()
        {
            var rec = new List<ReplayStateHash> { new ReplayStateHash(60, 5), new ReplayStateHash(60, 6) };
            var r = HashVerifier.Verify(rec, new[] { new ReplayStateHash(60, 5) });
            Assert.Equal(HashMismatchKind.HashDiffers, r.Kind);
            Assert.Equal(6UL, r.ExpectedHash);
        }

        [Fact]
        public void VerifiesHashesReadFromAReplay()
        {
            var replay = ReplayReader.Read(ReplayWriter.WriteToArray(ReplayTestUtil.Sample()));
            Assert.True(HashVerifier.Verify(replay.Hashes, ReplayTestUtil.Sample().Hashes).IsMatch);
        }
    }
}
