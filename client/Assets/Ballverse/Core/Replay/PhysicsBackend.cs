namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Physics engine that produced a replay (header field <c>physicsBackend u8</c>, spec §7.4 of the release-1 spec:
    /// "1 = Core.Sim, 2 = Box2D v3 — резерв"). Stored as one byte; values unknown to this build are preserved as-is by the
    /// reader. The numbering is part of the .bvr format: never renumber, only add.
    /// </summary>
    public enum PhysicsBackend : byte
    {
        /// <summary>No physics engine (purely kinematic or custom integrator).</summary>
        None = 0,
        /// <summary>The deterministic 2D solver of Ballverse.Core.Sim (release 1: balls vs kinematic walls, fixed 1/60 s tick with substeps).</summary>
        CoreSim = 1,
        /// <summary>Box2D v3, fixed 1/60 s step, 4 sub-steps (spec §2.1). Reserved for roadmap modes with rigid-body stacks and joints.</summary>
        Box2DV3 = 2,
    }
}
