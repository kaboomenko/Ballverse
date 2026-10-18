namespace Ballverse.Core.Generation
{
    /// <summary>
    /// JSON shape of one mode entry in Remote Config <c>generation_weights</c> (spec §9.3). Every field is optional:
    /// a missing field keeps the current value.
    /// </summary>
    internal sealed class SpectacleProfileDto
    {
        public double? W1 { get; set; }
        public double? W2 { get; set; }
        public double? W3 { get; set; }
        public double? W4 { get; set; }
        public double? W5 { get; set; }
        public double? DensityMin { get; set; }
        public double? DensityMax { get; set; }
        public double? QualityThreshold { get; set; }
    }
}
