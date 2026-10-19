using System;
using System.Collections.Generic;
using Ballverse.Core.Generation;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>One press of 🎲 for a mode (spec §2.4, §2.5).</summary>
    public sealed class ModeGenerationRequest
    {
        /// <summary>The user's current parameters (any raw set; prepared for <see cref="Tier"/> before use).</summary>
        public ParamSet BaseValues;
        public DeviceTier Tier = DeviceTier.B;
        /// <summary>Session seed (the app supplies a random one; Core is pure). Candidate seeds derive from it.</summary>
        public ulong SessionSeed;
        public GenerationStyle Style = GenerationStyle.Any;
        /// <summary>
        /// Keep the base parameters and search seeds only (e.g. "find a seed where colour 2 wins" on a preset); otherwise every
        /// candidate gets its own parameters from <see cref="IModeDefinition.GenerateCandidate"/>.
        /// </summary>
        public bool KeepParams;
        /// <summary>
        /// Forced winner as a participant index, or null to read the mode's <c>forcedWinner</c> parameter (0 = any, else
        /// 1-based). Ignored when the values give no participants.
        /// </summary>
        public int? ForcedWinner;
        public DirectorGoal Goal = DirectorGoal.None;
        public SpectacleProfileTable Profiles;
        public GenerationBudgetTable Budgets;
        /// <summary>Overrides the tier budget (tests, "search longer" experiments).</summary>
        public GenerationBudget? Budget;
        /// <summary>
        /// Generation time in ms since the press, measured by the caller (Core has no clock); null = the time budget never runs out
        /// and only the candidate count limits the search (deterministic; tests).
        /// </summary>
        public Func<long> ElapsedMs;
        /// <summary>When the forced winner is not found: "Искать дольше" this many times before deciding (spec §2.5).</summary>
        public int AutoSearchLonger;
        /// <summary>After the automatic searches: true = "Взять ближайший вариант"; false = return with the decision pending.</summary>
        public bool AutoTakeNearest = true;
        /// <summary>Polled between candidates; true cancels (the user left the screen, spec §2.4).</summary>
        public Func<bool> IsCancelled;
    }

    /// <summary>Result of <see cref="ModeGenerator.Generate"/>.</summary>
    public sealed class ModeGenerationResult
    {
        internal ModeGenerationResult(GenerationSession session, ParamSet values, ulong seed)
        {
            Session = session;
            Values = values;
            Seed = seed;
        }

        /// <summary>The planner with every evaluation; <see cref="GenerationSession.Status"/> may be AwaitingDecision.</summary>
        public GenerationSession Session { get; }

        public GenerationResult Result => Session.Result;

        public bool HasSelection => Session.Result != null && Session.Result.Selected != null;

        /// <summary>Parameters of the chosen candidate (prepared, ready for <see cref="ModeSession"/>), or null.</summary>
        public ParamSet Values { get; }

        /// <summary>Run seed of the chosen candidate (0 when nothing was chosen).</summary>
        public ulong Seed { get; }
    }

    /// <summary>
    /// Sequential driver of <see cref="GenerationSession"/> for a mode (spec §2.4 steps 2–5, §2.5): asks the planner for
    /// candidates, builds each candidate's parameters, runs it headless with the same simulation code as a live run (so the
    /// chosen candidate replays identically, §2.4 "тот же код симуляции") and reports the scene's summary. Run it on a worker
    /// thread in the app; independent candidates could also run in parallel through <see cref="RunCandidate"/>.
    /// </summary>
    public static class ModeGenerator
    {
        public static ModeGenerationResult Generate(IModeDefinition mode, ModeGenerationRequest request)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.BaseValues == null) throw new ArgumentException("BaseValues are required", nameof(request));
            ParamSet baseValues = ModeParams.Prepare(mode, request.BaseValues, request.Tier).Values;

            int participants = mode.GetParticipantCount(baseValues);
            int forced = GenerationSessionOptions.NoForcedWinner;
            if (participants >= 2)
            {
                if (request.ForcedWinner.HasValue) forced = request.ForcedWinner.Value;
                else if (baseValues.TryGet(CommonParams.ForcedWinner, out ParamValue fw) && fw.AsInt > 0) forced = (int)fw.AsInt - 1;
                if (forced >= participants) forced = GenerationSessionOptions.NoForcedWinner;
            }

            int? target = TargetDurationTicks(baseValues);
            GenerationSessionOptions options = GenerationSessionOptions.ForMode(mode.Info.Code, request.Tier, request.SessionSeed, target,
                request.Profiles, request.Budgets);
            if (request.Budget.HasValue) options.Budget = request.Budget.Value;
            if (forced >= 0)
            {
                options.ForcedWinner = forced;
                options.ParticipantCount = participants;
            }
            if (request.Goal != DirectorGoal.None && participants >= 2) options.Goal = request.Goal;
            var session = new GenerationSession(options);

            var candidateValues = new List<ParamSet>();
            int searchesLeft = Math.Max(0, request.AutoSearchLonger);
            while (true)
            {
                if (request.IsCancelled != null && request.IsCancelled())
                {
                    session.Cancel();
                    break;
                }
                GenerationStep step = session.NextRequest();
                if (step.Kind == GenerationStepKind.Done) break;
                if (step.Kind == GenerationStepKind.AwaitDecision)
                {
                    if (searchesLeft > 0)
                    {
                        searchesLeft--;
                        session.SearchLonger();
                        continue;
                    }
                    if (request.AutoTakeNearest) session.TakeNearest();
                    break;
                }
                if (step.Kind != GenerationStepKind.RunCandidate)
                    throw new InvalidOperationException("Sequential driver got " + step.Kind + " with no run outstanding");

                ParamSet values = request.KeepParams
                    ? baseValues
                    : mode.GenerateCandidate(new ModeCandidateRequest
                    {
                        BaseValues = baseValues,
                        Seed = step.Seed,
                        Style = request.Style,
                        Tier = request.Tier,
                        FixedParticipants = forced >= 0 ? participants : 0,
                    });
                while (candidateValues.Count <= step.CandidateIndex) candidateValues.Add(null);
                candidateValues[step.CandidateIndex] = values;
                CandidateRunSummary summary = RunCandidate(mode, values, step.Seed, step.MaxTicks);
                session.Report(step.CandidateIndex, summary, request.ElapsedMs != null ? request.ElapsedMs() : 0L);
            }

            GenerationResult result = session.Result;
            if (result != null && result.Selected != null)
                return new ModeGenerationResult(session, candidateValues[result.Selected.Index], result.Selected.Seed);
            return new ModeGenerationResult(session, null, 0UL);
        }

        /// <summary>
        /// Runs one candidate headless (no sinks, no frames) until its finale or <paramref name="maxTicks"/>, and returns the
        /// scene's summary (spec §2.4 step 3). Pure: the same arguments give the same summary.
        /// </summary>
        public static CandidateRunSummary RunCandidate(IModeDefinition mode, ParamSet values, ulong seed, int maxTicks)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (maxTicks < 1 || maxTicks > SimConstants.MaxDurationTicks) throw new ArgumentOutOfRangeException(nameof(maxTicks));
            var events = new ModeEventBuffer { WantsRender = false, WantsSound = false, WantsReplay = false };
            var world = new PhysicsWorld();
            IModeScene scene = mode.BuildScene(world, new ModeSceneContext(mode, values, seed, new ModeVisualTable(), events));
            var runner = new SimRunner(world, scene);
            runner.Start();
            while (runner.Tick < maxTicks && runner.Phase == SimPhase.Running && !runner.IsFinished)
            {
                events.Tick = runner.Tick + 1;
                runner.Step();
                events.Clear();
            }
            bool endedByLimit = runner.Phase == SimPhase.Running;
            return scene.BuildSummary(runner, endedByLimit);
        }

        /// <summary><c>targetDuration</c> in ticks, or null for "unlimited" (spec §2.2).</summary>
        public static int? TargetDurationTicks(ParamSet values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (!values.TryGet(CommonParams.TargetDuration, out ParamValue v) || v.IsUnlimited) return null;
            return (int)v.AsInt * SimConstants.TickRate;
        }
    }
}
