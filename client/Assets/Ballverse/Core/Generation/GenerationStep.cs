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

        public GenerationStep(GenerationStepKind kind, int candidateIndex, ulong seed, int maxTicks)
        {
            Kind = kind;
            CandidateIndex = candidateIndex;
            Seed = seed;
            MaxTicks = maxTicks;
        }

        public bool IsRun => Kind == GenerationStepKind.RunCandidate;
    }
}
