namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Physics engine that produced a replay (header field <c>physicsBackend u8</c>, spec §7.4).
    /// Stored as one byte; values unknown to this build are preserved as-is by the reader.
    /// </summary>
    public enum PhysicsBackend : byte
    {
        /// <summary>No physics engine (purely kinematic or custom integrator).</summary>
        None = 0,
        /// <summary>Box2D v3, fixed 1/60 s step, 4 sub-steps (spec §2.1).</summary>
        Box2DV3 = 1,
    }
}
