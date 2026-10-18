namespace Ballverse.Core.Generation
{
    /// <summary>Lifecycle of a <see cref="GenerationSession"/>.</summary>
    public enum GenerationStatus
    {
        Running = 0,
        /// <summary>Forced winner not found; <see cref="GenerationSession.SearchLonger"/> or <see cref="GenerationSession.TakeNearest"/>.</summary>
        AwaitingDecision = 1,
        Completed = 2,
        Cancelled = 3,
    }
}
