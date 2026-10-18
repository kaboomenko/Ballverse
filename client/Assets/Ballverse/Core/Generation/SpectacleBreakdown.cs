namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Spectacle score of one candidate with every metric (spec §2.4 step 4):
    /// <c>total = w1·fitDuration + w2·eventDensity + w3·finishTension + w4·coverage − w5·stagnation</c>.
    /// Metrics are computed even for rejected candidates so telemetry can show them.
    /// </summary>
    public sealed class SpectacleBreakdown
    {
        /// <summary>1 at the exact target, linear to 0 at ±30% (spec §2.4).</summary>
        public double FitDuration { get; }
        /// <summary>1 inside the pleasant density range, smooth falloff outside.</summary>
        public double EventDensity { get; }
        public double FinishTension { get; }
        public double Coverage { get; }
        /// <summary>Fraction of the run covered by gaps &gt; 3 s without significant events.</summary>
        public double Stagnation { get; }
        public double Total { get; }
        public RejectReason Rejected { get; }

        public bool IsRejected => Rejected != RejectReason.None;

        public SpectacleBreakdown(double fitDuration, double eventDensity, double finishTension, double coverage,
            double stagnation, double total, RejectReason rejected)
        {
            FitDuration = fitDuration;
            EventDensity = eventDensity;
            FinishTension = finishTension;
            Coverage = coverage;
            Stagnation = stagnation;
            Total = total;
            Rejected = rejected;
        }
    }
}
