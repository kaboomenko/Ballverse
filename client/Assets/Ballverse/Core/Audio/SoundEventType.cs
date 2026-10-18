namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Kind of simulation event that may produce a sound (spec §2.7: "sounds are produced by events — collision, break,
    /// elimination, finish"). Numeric values are stable, index sound pack rules and equal the replay EVNT type codes
    /// (spec §7.4), so a replay event type converts by value. Built-in packs have no Despawn rule (silent).
    /// </summary>
    public enum SoundEventType
    {
        /// <summary>Collision of a body with another body or a wall.</summary>
        Hit = 0,
        /// <summary>Something breaks (ring segment, glass block).</summary>
        Break = 1,
        /// <summary>A body leaves the game.</summary>
        Eliminate = 2,
        /// <summary>Finale / winner reached the goal.</summary>
        Finish = 3,
        /// <summary>A body appears (multiply gates, spawners).</summary>
        Spawn = 4,
        /// <summary>A body is removed without being eliminated (replay parity; no built-in pack plays it).</summary>
        Despawn = 5,
        /// <summary>Score / counter change.</summary>
        Score = 6,
        /// <summary>Mode-specific event.</summary>
        Custom = 7,
    }
}
