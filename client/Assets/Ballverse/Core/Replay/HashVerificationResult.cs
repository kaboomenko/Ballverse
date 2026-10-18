namespace Ballverse.Core.Replay
{
    /// <summary>Outcome of <see cref="HashVerifier"/>: OK or the first mismatching tick (spec §7.4).</summary>
    public readonly struct HashVerificationResult
    {
        public HashVerificationResult(HashMismatchKind kind, uint tick, ulong expected, ulong actual, int verifiedCount)
        {
            Kind = kind;
            Tick = tick;
            ExpectedHash = expected;
            ActualHash = actual;
            VerifiedCount = verifiedCount;
        }

        public bool IsMatch => Kind == HashMismatchKind.None;

        public HashMismatchKind Kind { get; }

        /// <summary>First mismatching tick; meaningful only when <see cref="IsMatch"/> is false.</summary>
        public uint Tick { get; }

        /// <summary>First mismatching tick, or null when everything matched.</summary>
        public uint? FirstMismatchTick => IsMatch ? (uint?)null : Tick;

        /// <summary>Recorded hash at the mismatching tick.</summary>
        public ulong ExpectedHash { get; }

        /// <summary>Recomputed hash at the mismatching tick (0 for <see cref="HashMismatchKind.Missing"/>).</summary>
        public ulong ActualHash { get; }

        /// <summary>Number of recorded hashes that matched before the first mismatch.</summary>
        public int VerifiedCount { get; }

        public override string ToString()
        {
            return IsMatch ? "OK (" + VerifiedCount + " hashes)" : Kind + " at tick " + Tick;
        }
    }
}
