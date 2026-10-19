using System;
using System.Collections.Generic;
using Ballverse.Core.Integration;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// M01 «Побег из колец» / Ring Escape (<c>ring_escape</c>), release 1, free (spec §2.9 M01). Stateless singleton.
    /// Implements <see cref="IModeSoundMapping"/>: the export wraps its render-stream sound source in
    /// <see cref="RingEscapeSound.BodySource"/> (pitch progression, ring / bodiless emitters).
    /// </summary>
    public sealed class RingEscapeMode : IModeDefinition, IModeSoundMapping
    {
        /// <summary>
        /// Simulation version written to replays. 1 = release 1 (solver Core.Sim, rules of the M01 card as of this build).
        /// Bump on any change that alters results for the same parameters and seed.
        /// </summary>
        public const ushort CurrentSimVersion = 1;

        public static readonly RingEscapeMode Instance = new RingEscapeMode();

        RingEscapeMode()
        {
        }

        public ModeInfo Info => ModeCatalog.Get(ModeCode.M01);

        public ushort SimVersion => CurrentSimVersion;

        public ParamSchema Schema => RingEscapeParams.Schema;

        public IReadOnlyList<Preset> BuiltInPresets => RingEscapePresets.All;

        public ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues)
        {
            return RingEscapeRules.Constrain(sanitized, tier, issues);
        }

        public bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max)
        {
            return RingEscapeRules.TryGetDynamicRange(paramId, values, tier, out min, out max);
        }

        public int GetParticipantCount(ParamSet values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            RingEscapeConfig c = RingEscapeConfig.From(values);
            return c.CompetitionActive ? c.TeamCount : 0;
        }

        public ParamSet GenerateCandidate(in ModeCandidateRequest request)
        {
            return RingEscapeGenerator.Generate(request);
        }

        public IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context)
        {
            return new RingEscapeScene(world, context);
        }

        public ISoundBodySource WrapSoundBodySource(ISoundBodySource inner)
        {
            return new RingEscapeSound.BodySource(inner);
        }
    }
}
