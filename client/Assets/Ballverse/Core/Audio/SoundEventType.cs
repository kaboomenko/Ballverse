namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Kind of simulation event that may produce a sound (spec §2.7: "sounds are produced by events — collision, break,
    /// elimination, finish"). Numeric values are stable and used to index sound pack rules.
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
        /// <summary>Score / counter change.</summary>
        Score = 5,
        /// <summary>Mode-specific event.</summary>
        Custom = 6,
    }
}
