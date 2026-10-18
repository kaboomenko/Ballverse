using Ballverse.Core.Generation;
using Ballverse.Core.Params;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// One run of a mode built by <see cref="IModeDefinition.BuildScene"/>: the Core.Sim rules (<see cref="IModeRules"/>, driven
    /// by <see cref="SimRunner"/>) plus everything the framework needs to record and present the run. See CONTRACT.md for the
    /// call order. Single-threaded; deterministic (spec §7.4 rules 1–8).
    /// <para>Events: the scene emits render, sound and replay events into <see cref="ModeSceneContext.Events"/> from
    /// <see cref="IModeRules.Start"/>, <see cref="IModeRules.Tick"/>, <see cref="IModeRules.AfterPhysics"/> and
    /// <see cref="IModeRules.OnTimeLimit"/>; the session drains the buffer after every tick.</para>
    /// <para>Steady state (every tick after the first second) must not allocate managed memory (spec §8.2, tested).</para>
    /// </summary>
    public interface IModeScene : IModeRules
    {
        /// <summary>
        /// Render-stream quantization of this run (spec §7.4: chosen per stream from the scene's extent). Fixed after
        /// <see cref="IModeDefinition.BuildScene"/>; every value the scene writes must lie inside it (bodies that would leave
        /// <c>PositionRange − 1 m</c> are removed from the frame).
        /// </summary>
        RenderQuantization Quantization { get; }

        /// <summary>Participants of the winner variant (spec §2.5), 0 when the run has no winner.</summary>
        int ParticipantCount { get; }

        /// <summary>
        /// A live parameter changed (spec §2.2 panel rules). Called at a tick boundary, before <see cref="IModeRules.Tick"/> of
        /// the tick it applies to, with a value sanitized by the schema and clamped to the mode's dynamic range; the same call
        /// happens during replay re-simulation from the PARM record. Ids that do not affect the scene are ignored.
        /// </summary>
        void ApplyLiveParam(string paramId, ParamValue value);

        /// <summary>
        /// Writes the visual state after the last completed tick (frame <c>World.Tick</c>; frame 0 is the state before the first
        /// tick): clears <paramref name="frame"/>, adds every visible body with ids from <see cref="ModeSceneContext.Visuals"/>
        /// (shapes and labels) and the HUD entries (<see cref="HudKeys"/>). Must not change simulation state.
        /// </summary>
        void WriteFrame(RenderFrame frame);

        /// <summary>
        /// Summary of a headless generation run (spec §2.4 step 3–4) after the driver stopped stepping:
        /// <paramref name="endedByLimit"/> is true when the run was cut at the headless limit before the finale. Duration is the
        /// finale tick when the finale was reached (<see cref="SimRunner.FinaleTick"/>), otherwise <see cref="SimRunner.Tick"/>.
        /// Mode-level rejections of the card (e.g. M01 "побег не наступил") are reported as <see cref="CandidateRunSummary.Stuck"/>.
        /// </summary>
        CandidateRunSummary BuildSummary(SimRunner runner, bool endedByLimit);

        /// <summary>RSLT record of the run as it stands (winner, per-participant scores, finish tick ≤ <see cref="SimRunner.Tick"/>).</summary>
        ReplayResult BuildReplayResult(SimRunner runner);
    }
}
