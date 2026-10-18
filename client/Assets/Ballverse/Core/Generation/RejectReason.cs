namespace Ballverse.Core.Generation
{
    /// <summary>Why a candidate can never be selected, whatever its score (spec §2.4, §2.9 M03 anti-stuck check).</summary>
    public enum RejectReason
    {
        None = 0,
        /// <summary>A body got stuck (<see cref="CandidateRunSummary.Stuck"/>).</summary>
        Stuck = 1,
        /// <summary>The run neither finished nor reached the headless limit (it was aborted), so its metrics are meaningless.</summary>
        Incomplete = 2,
    }
}
