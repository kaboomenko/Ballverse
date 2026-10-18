using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release-1 catalog with the No Ads watermark rule turned off (<see cref="Release1ExhaustiveTests.Check"/>): no sellable
    /// path to NO_WATERMARK unless grandfathered (owner decision; spec §5.2). Not a planned state, so a strided sample
    /// (disjoint residues for the two flag values) keeps the suite fast.
    /// </summary>
    public class Release1RuleOffExhaustiveTests
    {
        static readonly MonetizationConfig Off = MonetizationConfig.Release1.WithWatermarkWithNoAds(false);

        [Fact]
        public void InvariantsHold_RuleOff()
        {
            for (int mask = 0; mask < Subsets.Count; mask += 4) Release1ExhaustiveTests.Check(mask, Off, false);
        }

        [Fact]
        public void InvariantsHold_RuleOffWatermarkGrandfathered()
        {
            for (int mask = 1; mask < Subsets.Count; mask += 4) Release1ExhaustiveTests.Check(mask, Off, true);
        }
    }
}
