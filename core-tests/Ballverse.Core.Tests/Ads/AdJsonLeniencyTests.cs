using System.IO;
using Ballverse.Core.Ads;
using Xunit;

namespace Ballverse.Core.Tests.Ads
{
    /// <summary>
    /// Pins the documented leniency of the Ads state/config reader (Newtonsoft's JsonTextReader): files written by ToJson never
    /// use these forms, so a Newtonsoft upgrade that changes them must be noticed here rather than in users' state files.
    /// </summary>
    public class AdJsonLeniencyTests
    {
        [Fact]
        public void LeadingZeroIntegersAreReadAsOctal()
        {
            var s = InterstitialState.FromJson("{\"completedGames\":010,\"totalCompletedGames\":00012}");
            Assert.Equal(8, s.CompletedGames);
            Assert.Equal(10, s.TotalCompletedGames);
        }

        [Fact]
        public void HexUnquotedNamesAndTrailingCommasParse()
        {
            var c = AdPolicyConfig.FromJson("{interstitialEveryNGames:0x5, 'rewardedRetryWindowMin':017,}");
            Assert.Equal(5, c.InterstitialEveryNGames);
            Assert.Equal(15, c.RewardedRetryWindowMin);
        }

        [Fact]
        public void NestingDeeperThan64FailsTheWholeDocument()
        {
            string deep = new string('[', 70) + new string(']', 70);
            Assert.Throws<InvalidDataException>(() => RewardedGateState.FromJson("{\"future\":" + deep + "}"));
        }
    }
}
