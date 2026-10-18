namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Simulation event kinds stored in EVNT records (spec §2.7, §7.4). Stored as u8; numeric values are persisted and must not change.
    /// The reader rejects values not defined here (a new kind requires a new format version).
    /// </summary>
    public enum ReplayEventType : byte
    {
        /// <summary>Collision; drives the sound packs and haptics (spec §2.7).</summary>
        Hit = 0,
        /// <summary>Something breaks (rings, blocks, glass).</summary>
        Break = 1,
        /// <summary>A participant is eliminated.</summary>
        Eliminate = 2,
        /// <summary>A participant finishes / wins.</summary>
        Finish = 3,
        Spawn = 4,
        Despawn = 5,
        /// <summary>Score change.</summary>
        Score = 6,
        /// <summary>Mode-specific event; meaning carried in <see cref="ReplayEvent.Extra"/>.</summary>
        Custom = 7,
    }
}
