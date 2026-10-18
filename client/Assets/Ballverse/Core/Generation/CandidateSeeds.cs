using System;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Deterministic candidate seeds (spec §2.4 step 2, §7.4 rule 2: a dedicated generation Rng stream derived from the seed).
    /// Candidate k of a session always gets the same seed for the same session seed, independent of how many
    /// candidates were run before it (random access, no shared mutable stream).
    /// </summary>
    public static class CandidateSeeds
    {
        /// <summary>Rng stream id base reserved for candidate seeds ("GEN" tag in the high bits); candidate k uses base + k.</summary>
        public const ulong StreamBase = 0x47454E0000000000UL;

        /// <summary>Seed of candidate <paramref name="candidateIndex"/> in a session seeded with <paramref name="sessionSeed"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">candidateIndex is negative.</exception>
        public static ulong ForCandidate(ulong sessionSeed, int candidateIndex)
        {
            if (candidateIndex < 0) throw new ArgumentOutOfRangeException(nameof(candidateIndex));
            Rng rng = Rng.FromSeed(sessionSeed, StreamBase + (ulong)candidateIndex);
            return rng.NextULong();
        }
    }
}
