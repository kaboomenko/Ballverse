using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Generation style of the 🎲 button (spec §2.4 step 1). Each mode card (§2.9) gives the parameter distributions of
    /// every style; <see cref="Any"/> lets the mode pick one of the others with equal probability from the candidate seed.
    /// Numeric values are stable (they may be persisted in the last-used settings).
    /// </summary>
    public enum GenerationStyle
    {
        Any = 0,
        /// <summary>«Хаос».</summary>
        Chaos = 1,
        /// <summary>«Медитация».</summary>
        Meditation = 2,
        /// <summary>«Напряжённый финал».</summary>
        TenseFinale = 3,
        /// <summary>«Много шаров».</summary>
        ManyBalls = 4,
        /// <summary>«Минимализм».</summary>
        Minimalism = 5,
    }

    /// <summary>Input of <see cref="IModeDefinition.GenerateCandidate"/> (spec §2.4 step 2: one candidate = parameters + seed).</summary>
    public struct ModeCandidateRequest
    {
        /// <summary>The user's current parameters (sanitized and constrained); the candidate keeps what the style does not decide.</summary>
        public ParamSet BaseValues;
        /// <summary>Candidate seed from <c>GenerationSession</c>; the candidate's parameters derive only from it (deterministic).</summary>
        public ulong Seed;
        public GenerationStyle Style;
        /// <summary>Tier the parameters are sanitized for (tier caps, spec §2.9 body budgets).</summary>
        public DeviceTier Tier;
        /// <summary>
        /// When &gt; 0 the candidate must keep exactly this many participants (forced winner, spec §2.5: the participant index
        /// must keep its meaning across candidates). 0 = free.
        /// </summary>
        public int FixedParticipants;
    }

    /// <summary>
    /// One game mode as pure, deterministic Core logic (spec §2.1, §7.6 "Контракт", adapted for release 1 — see
    /// Modes/Framework/CONTRACT.md). The view is Unity-side and reads only the render stream, render events and sound events
    /// produced through <see cref="IModeScene"/>. Implementations are stateless singletons: every run state lives in the
    /// scene returned by <see cref="BuildScene"/>.
    /// </summary>
    public interface IModeDefinition
    {
        /// <summary>Catalog entry (code, stable id, winner / interactive flags), the same instance as <see cref="ModeCatalog"/>'s.</summary>
        ModeInfo Info { get; }

        /// <summary>
        /// Version of everything that changes the simulation result (physics use, rules, generation of the scene from parameters
        /// and seed). Written to the replay header (<c>modeSimVersion</c>); a replay with another version cannot be re-simulated
        /// (spec §3.9: its render stream stays the source of the clip).
        /// </summary>
        ushort SimVersion { get; }

        /// <summary>
        /// Complete parameter schema: <c>ParamSchema.Merge(CommonParams.ForMode(Info), modeLayer)</c> (spec §2.2, §7.6).
        /// </summary>
        ParamSchema Schema { get; }

        /// <summary>Built-in presets (spec §2.3, 4–8 per mode). Every preset applied with <see cref="ModeParams.Prepare"/> reports no constraint issue.</summary>
        IReadOnlyList<Preset> BuiltInPresets { get; }

        /// <summary>
        /// Cross-parameter rules of the mode card (spec §7.6 "Межпараметрические правила", §2.9 "Валидация"): returns the nearest
        /// set that satisfies every rule, appending one issue per changed parameter. <paramref name="sanitized"/> must already be
        /// a complete set sanitized by <see cref="Schema"/> for <paramref name="tier"/>; the result is too. Idempotent (a valid
        /// set comes back unchanged with no issue). Never throws for a sanitized set.
        /// </summary>
        ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues);

        /// <summary>
        /// Range of <paramref name="paramId"/> that keeps every cross-parameter rule given the other values (spec §2.9: "зависимый
        /// ползунок сужает диапазон"). False when the parameter has no dependent bounds (the schema range applies). The bounds
        /// lie inside the schema range; min ≤ max.
        /// </summary>
        bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max);

        /// <summary>
        /// Participants of the winner variant for these values (spec §2.5: release 1 counts colours), or 0 when the values give no
        /// winner. Must equal <see cref="IModeScene.ParticipantCount"/> of a scene built from the same values.
        /// </summary>
        int GetParticipantCount(ParamSet values);

        /// <summary>
        /// Candidate parameters for generation (spec §2.4 step 2): curated ranges of the style, only valid combinations (the
        /// result is sanitized for the request's tier and constrained), a pure function of the request.
        /// </summary>
        ParamSet GenerateCandidate(in ModeCandidateRequest request);

        /// <summary>
        /// Builds the run into an empty <paramref name="world"/>: solver settings, bodies, colliders, balls; registers the
        /// static shapes with <see cref="ModeSceneContext.Visuals"/>. The scene depends only on (values, seed): the same pair
        /// always builds the same world (spec §7.4, replay re-simulation).
        /// </summary>
        IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context);
    }
}
