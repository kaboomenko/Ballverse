using System;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;

namespace Ballverse.App
{
    /// <summary>
    /// A 🎲 whose forced winner was not found within the budget (spec §2.5 «Если за бюджет нужный победитель не найден …
    /// приложение предлагает «Искать дольше» (ещё один бюджет) или «Взять ближайший вариант»»): the Core planner waiting for the
    /// user's decision, with what is needed to continue it. <see cref="ModeGenerator.Generate"/> runs the first budget with
    /// <c>AutoTakeNearest = false</c> and returns the session in <see cref="GenerationStatus.AwaitingDecision"/>;
    /// <see cref="SearchLonger"/> continues that same session (new candidate indices, one more phase budget, the time spent waiting
    /// for the user excluded) and <see cref="TakeNearest"/> completes it. A candidate's parameters are rebuilt from its seed:
    /// <see cref="IModeDefinition.GenerateCandidate"/> is a pure function of the request (CONTRACT rule 5).
    /// </summary>
    public sealed class GenerationDecision
    {
        public GenerationDecision(IModeDefinition mode, ModeGenerationRequest request, GenerationSession session)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            BaseValues = ModeParams.Prepare(mode, request.BaseValues, request.Tier).Values;
            Participants = mode.GetParticipantCount(BaseValues);
        }

        public IModeDefinition Mode { get; }
        public ModeGenerationRequest Request { get; }
        public GenerationSession Session { get; }
        /// <summary>The request's base values prepared for its tier (as <see cref="ModeGenerator.Generate"/> uses them).</summary>
        public ParamSet BaseValues { get; }
        public int Participants { get; }

        /// <summary>The dialog showing the choice (closed when the decision becomes stale).</summary>
        internal IDisposable Dialog { get; set; }

        public bool IsPending => Session.Status == GenerationStatus.AwaitingDecision;

        /// <summary>
        /// «Взять ближайший вариант» (main thread, cheap): completes the session with <see cref="GenerationOutcome.NearestWinner"/>
        /// (or NoViableCandidate) and returns the chosen candidate's parameters, or null when every candidate was rejected.
        /// </summary>
        public ParamSet TakeNearest(out ulong seed)
        {
            Session.TakeNearest();
            return Selected(out seed);
        }

        /// <summary>
        /// «Искать дольше» (worker thread): one more phase budget on the same session, driven the way
        /// <see cref="ModeGenerator.Generate"/> drives the first one. Returns the chosen candidate's parameters when the session
        /// completed with a selection; null when it is waiting for the decision again (<see cref="IsPending"/>), was cancelled, or
        /// found nothing.
        /// </summary>
        /// <param name="elapsedMs">Generation time since the press, excluding the time spent waiting for the user (non-decreasing).</param>
        public ParamSet SearchLonger(Func<bool> isCancelled, Func<long> elapsedMs, out ulong seed)
        {
            seed = 0UL;
            Session.SearchLonger();
            while (true)
            {
                if (isCancelled != null && isCancelled())
                {
                    Session.Cancel();
                    return null;
                }
                GenerationStep step = Session.NextRequest();
                if (step.Kind == GenerationStepKind.Done || step.Kind == GenerationStepKind.AwaitDecision) break;
                if (step.Kind != GenerationStepKind.RunCandidate)
                    throw new InvalidOperationException("Sequential driver got " + step.Kind + " with no run outstanding");
                ParamSet values = CandidateValues(step.Seed);
                CandidateRunSummary summary = ModeGenerator.RunCandidate(Mode, values, step.Seed, step.MaxTicks);
                Session.Report(step.CandidateIndex, summary, elapsedMs != null ? elapsedMs() : 0L);
            }
            return Selected(out seed);
        }

        ParamSet Selected(out ulong seed)
        {
            GenerationResult result = Session.Result;
            if (result == null || result.Selected == null)
            {
                seed = 0UL;
                return null;
            }
            seed = result.Selected.Seed;
            return CandidateValues(seed);
        }

        /// <summary>The parameters <see cref="ModeGenerator.Generate"/> gave the candidate with this seed.</summary>
        public ParamSet CandidateValues(ulong seed)
        {
            if (Request.KeepParams) return BaseValues;
            return Mode.GenerateCandidate(new ModeCandidateRequest
            {
                BaseValues = BaseValues,
                Seed = seed,
                Style = Request.Style,
                Tier = Request.Tier,
                // A decision exists only with a forced winner: the participant count stays fixed (as in ModeGenerator).
                FixedParticipants = Participants,
            });
        }
    }
}
