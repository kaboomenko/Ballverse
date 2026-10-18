namespace Ballverse.Core.Generation
{
    /// <summary>How a generation ended (spec §2.4 step 5, §2.5).</summary>
    public enum GenerationOutcome
    {
        /// <summary>The best candidate that passed the quality threshold (and matched the forced winner).</summary>
        Success = 0,
        /// <summary>Nothing passed even after doubling: best available candidate, UI hint "try a longer duration".</summary>
        BelowThreshold = 1,
        /// <summary>No candidate with the forced winner: waiting for "Search longer" or "Take nearest" (spec §2.5).</summary>
        WinnerNotFound = 2,
        /// <summary>The user chose "Take nearest": best candidate with another winner.</summary>
        NearestWinner = 3,
        /// <summary>Every candidate was rejected (stuck / aborted): nothing to show.</summary>
        NoViableCandidate = 4,
        /// <summary>The user left the screen (spec §2.4 "generation is cancelled").</summary>
        Cancelled = 5,
    }
}
