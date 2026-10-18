namespace Ballverse.Core.Generation
{
    /// <summary>One instruction of the generation planner (spec §2.4 step 3).</summary>
    public readonly struct GenerationStep
    {
        public GenerationStepKind Kind { get; }

        /// <summary>Candidate to run (RunCandidate only, otherwise -1).</summary>
        public int CandidateIndex { get; }

        /// <summary>Simulation seed of the candidate (RunCandidate only).</summary>
        public ulong Seed { get; }

        /// <summary>Headless run limit in ticks: target × 1.3 capped at 180 s, or 180 s without a target (spec §2.4 step 3, §2.1).</summary>
        public int MaxTicks { get; }

        /// <summary>
        /// Generation time (ms since the session started, the clock of <see cref="GenerationSession.Report"/>) at which the current
        /// budget runs out (spec §2.4 "time budget"). A run still going at that moment should be aborted and reported with
        /// <c>finished = false</c>, <c>endedByLimit = false</c>: it then counts as <see cref="RejectReason.Incomplete"/> and its
        /// report ends the phase. A later doubling or "Search longer" may extend the deadline; re-read it from the next step.
        /// </summary>
        public long DeadlineMs { get; }

        public GenerationStep(GenerationStepKind kind, int candidateIndex, ulong seed, int maxTicks, long deadlineMs)
        {
            Kind = kind;
            CandidateIndex = candidateIndex;
            Seed = seed;
            MaxTicks = maxTicks;
            DeadlineMs = deadlineMs;
        }

        public bool IsRun => Kind == GenerationStepKind.RunCandidate;
    }
}
