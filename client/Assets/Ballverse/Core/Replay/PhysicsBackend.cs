namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Physics engine that produced a replay (header field <c>physicsBackend u8</c>, spec §7.4: "0 = нет, 1 = Box2D v3 — резерв
    /// волны 3, 2 = Core.Sim"). Stored as one byte; values unknown to this build are preserved as-is by the reader.
    /// The values are persisted in every .bvr and project and are never renumbered, only added: <c>Box2DV3 = 1</c> is the value
    /// of the accepted Replay module, <c>CoreSim = 2</c> was added for release 1. Release-1 replays write 2; clip recompute
    /// selects the solver by this byte.
    /// </summary>
    public enum PhysicsBackend : byte
    {
        /// <summary>No physics engine (purely kinematic or custom integrator).</summary>
        None = 0,
        /// <summary>Box2D v3, fixed 1/60 s step, 4 sub-steps (spec §2.1). Reserved for roadmap (wave 3) modes with rigid-body stacks and joints.</summary>
        Box2DV3 = 1,
        /// <summary>The deterministic 2D solver of Ballverse.Core.Sim (release 1: balls vs kinematic walls, fixed 1/60 s tick with substeps). Release-1 replays write this value.</summary>
        CoreSim = 2,
    }
}
