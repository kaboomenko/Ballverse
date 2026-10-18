namespace Ballverse.Core.Generation
{
    /// <summary>What the caller of <see cref="GenerationSession.NextRequest"/> should do next.</summary>
    public enum GenerationStepKind
    {
        /// <summary>Run the candidate headless and <see cref="GenerationSession.Report"/> its summary.</summary>
        RunCandidate = 0,
        /// <summary>The forced winner was not found: ask the user "Search longer" or "Take nearest" (spec §2.5).</summary>
        AwaitDecision = 1,
        /// <summary>The session is over; read <see cref="GenerationSession.Result"/>.</summary>
        Done = 2,
    }
}
