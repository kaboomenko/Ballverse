using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Sim;

namespace Ballverse.App
{
    /// <summary>
    /// The director goal «Победа в последнюю секунду» in the 🎲 mini sheet (spec §2.5: release 1 — M01 «Соревнование», ≥ 2
    /// colours). The UI's generation sheet binds the style list the view model gives it (<c>GenerationStyles</c>), so the goal
    /// is offered there as one more choice, <see cref="StyleId"/>, only while the current set allows it. Choosing it generates
    /// with <see cref="DirectorGoal.LastSecond"/> over the neutral «Любой» candidate distribution (<see cref="GoalStyle"/>):
    /// Core ranks the candidates by finishTension for the goal, and no style's distribution aims at the leaders' final gap
    /// («Напряжённый финал» narrows the escape's last gap, which lowered the goal's hit rate). The user's colours are kept
    /// (<see cref="ParticipantsKeepingMode"/>).
    /// </summary>
    public static class DirectorGoals
    {
        /// <summary>Choice id of «Победа в последнюю секунду» in <c>ISimulationViewModel.GenerationStyles</c>.</summary>
        public const string StyleId = "last_second";

        /// <summary>
        /// Core style the goal generates with: «Любой», so the goal's finishTension ranking picks among neutral candidates (a
        /// QA probe over tiers A–C: goal met 112/144 with «Любой» vs 96/144 with «Напряжённый финал»).
        /// </summary>
        public const GenerationStyle GoalStyle = GenerationStyle.Any;

        /// <summary>
        /// Style shown and generated while the goal is remembered but the current set cannot take it (fewer than 2 colours):
        /// without a competition the closest intent is an escape at the last moment, «Напряжённый финал». The stored choice
        /// stays the goal.
        /// </summary>
        public const string FallbackStyleId = "tense_finale";

        /// <summary>
        /// The goal can be chosen for this mode and set: a mode Core accepts the goal for (<see cref="GenerationSessionOptions.SupportsDirectorGoal"/>),
        /// not interactive, with at least 2 participants (M01: «Соревнование» on with ≥ 2 colours).
        /// </summary>
        public static bool LastSecondAvailable(IModeDefinition mode, ParamSet values)
        {
            if (mode == null || values == null || mode.Info.Interactive) return false;
            if (!GenerationSessionOptions.SupportsDirectorGoal(mode.Info.Code, DirectorGoal.LastSecond)) return false;
            try
            {
                return mode.GetParticipantCount(values) >= 2;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A mode whose 🎲 candidates keep the user's participants (ball count, colours, «Соревнование»): Core fixes them only
    /// for a forced winner (<c>ModeCandidateRequest.FixedParticipants</c>), but a director goal of «Соревнование» needs a
    /// competition in every candidate, or a one-ball candidate of the style could win the session. Everything else is the
    /// wrapped mode; the chosen candidate's values run on the real mode unchanged (they are what the real mode generates for
    /// that request).
    /// </summary>
    public sealed class ParticipantsKeepingMode : IModeDefinition
    {
        readonly IModeDefinition _inner;

        public ParticipantsKeepingMode(IModeDefinition inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IModeDefinition Inner => _inner;
        public ModeInfo Info => _inner.Info;
        public ushort SimVersion => _inner.SimVersion;
        public ParamSchema Schema => _inner.Schema;
        public IReadOnlyList<Preset> BuiltInPresets => _inner.BuiltInPresets;

        public ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues) => _inner.Constrain(sanitized, tier, issues);

        public bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max)
            => _inner.TryGetDynamicRange(paramId, values, tier, out min, out max);

        public int GetParticipantCount(ParamSet values) => _inner.GetParticipantCount(values);

        public ParamSet GenerateCandidate(in ModeCandidateRequest request)
        {
            ModeCandidateRequest fixedRequest = request;
            if (fixedRequest.FixedParticipants <= 0 && fixedRequest.BaseValues != null)
                fixedRequest.FixedParticipants = Math.Max(0, _inner.GetParticipantCount(fixedRequest.BaseValues));
            return _inner.GenerateCandidate(in fixedRequest);
        }

        public IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context) => _inner.BuildScene(world, context);
    }
}
