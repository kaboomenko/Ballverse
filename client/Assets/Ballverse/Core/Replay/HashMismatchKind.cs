namespace Ballverse.Core.Replay
{
    /// <summary>Why a hash verification failed (spec §7.4).</summary>
    public enum HashMismatchKind
    {
        /// <summary>No mismatch.</summary>
        None = 0,
        /// <summary>The recomputed hash at a recorded tick differs.</summary>
        HashDiffers = 1,
        /// <summary>The re-simulation produced no hash at a recorded tick it should have covered.</summary>
        Missing = 2,
    }
}
