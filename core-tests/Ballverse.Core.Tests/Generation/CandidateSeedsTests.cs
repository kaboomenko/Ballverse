using System;
using System.Collections.Generic;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class CandidateSeedsTests
    {
        [Fact]
        public void SameSessionSeedAndIndexGiveSameSeed()
        {
            for (int k = 0; k < 100; k++)
                Assert.Equal(CandidateSeeds.ForCandidate(777UL, k), CandidateSeeds.ForCandidate(777UL, k));
        }

        [Fact]
        public void RandomAccessDoesNotDependOnOrder()
        {
            ulong late = CandidateSeeds.ForCandidate(5UL, 50);
            for (int k = 0; k < 50; k++) CandidateSeeds.ForCandidate(5UL, k);
            Assert.Equal(late, CandidateSeeds.ForCandidate(5UL, 50));
        }

        [Fact]
        public void SeedsAreDistinctWithinAndAcrossSessions()
        {
            var seen = new HashSet<ulong>();
            for (ulong session = 0; session < 4; session++)
            {
                for (int k = 0; k < 2500; k++) Assert.True(seen.Add(CandidateSeeds.ForCandidate(session, k)));
            }
        }

        [Fact]
        public void SeedValuesArePinned()
        {
            // Replays and "same seed" re-runs depend on these values; changing the derivation is a breaking change.
            Assert.Equal(0xe6114200b7e05862UL, CandidateSeeds.ForCandidate(42UL, 0));
            Assert.Equal(0x0e1e293b51dacd83UL, CandidateSeeds.ForCandidate(42UL, 1));
            Assert.Equal(Expected(42UL, 7), CandidateSeeds.ForCandidate(42UL, 7));
            Assert.NotEqual(CandidateSeeds.ForCandidate(42UL, 0), CandidateSeeds.ForCandidate(42UL, 1));
            Assert.NotEqual(CandidateSeeds.ForCandidate(42UL, 0), CandidateSeeds.ForCandidate(43UL, 0));
        }

        static ulong Expected(ulong session, int k)
        {
            Rng rng = Rng.FromSeed(session, CandidateSeeds.StreamBase + (ulong)k);
            return rng.NextULong();
        }

        [Fact]
        public void NegativeIndexThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateSeeds.ForCandidate(1UL, -1));
        }
    }
}
