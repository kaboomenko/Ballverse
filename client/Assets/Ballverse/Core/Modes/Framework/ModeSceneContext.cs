using System;
using Ballverse.Core.Params;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// What a scene gets from the framework when it is built (see <see cref="IModeDefinition.BuildScene"/>): the run's
    /// parameters and seed, the visual table for render-stream shapes and labels, and the event buffer the session drains
    /// after every tick. One context per run; not shared between threads.
    /// </summary>
    public sealed class ModeSceneContext
    {
        public ModeSceneContext(IModeDefinition mode, ParamSet values, ulong seed, ModeVisualTable visuals = null, ModeEventBuffer events = null)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Seed = seed;
            Visuals = visuals ?? new ModeVisualTable();
            Events = events ?? new ModeEventBuffer();
        }

        public IModeDefinition Mode { get; }

        /// <summary>Complete, sanitized and constrained parameters at the start of the run (live changes arrive through <see cref="IModeScene.ApplyLiveParam"/>).</summary>
        public ParamSet Values { get; }

        /// <summary>Run seed (spec §7.4 rule 2). Derive every random stream from it with <see cref="Rng.FromSeed"/> and a mode-chosen stream id.</summary>
        public ulong Seed { get; }

        /// <summary>Shape and label table of the render stream (ids are stable for the run).</summary>
        public ModeVisualTable Visuals { get; }

        /// <summary>Per-tick render / sound / replay events.</summary>
        public ModeEventBuffer Events { get; }
    }
}
