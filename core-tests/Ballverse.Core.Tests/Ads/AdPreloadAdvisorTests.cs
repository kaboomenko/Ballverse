using System;
using Ballverse.Core.Ads;
using Xunit;

namespace Ballverse.Core.Tests.Ads
{
    public class AdPreloadAdvisorTests
    {
        [Fact]
        public void RW1_RewardedTruthTable()
        {
            foreach (bool inStudio in new[] { false, true })
            foreach (bool ent in new[] { false, true })
            foreach (bool enabled in new[] { false, true })
            {
                var cfg = new AdPolicyConfig { RewardedExportEnabled = enabled };
                bool expected = inStudio && !ent && enabled;
                Assert.Equal(expected, AdPreloadAdvisor.ShouldPreloadRewarded(inStudio, ent, cfg));
            }
        }

        [Fact]
        public void IN7_NoInterstitialLoadingWithEntitlement()
        {
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            Assert.True(AdPreloadAdvisor.ShouldPreloadInterstitial(p, false));
            Assert.False(AdPreloadAdvisor.ShouldPreloadInterstitial(p, true));
        }

        [Fact]
        public void InterstitialPreloadStartsOneCycleBeforePending()
        {
            var p = new InterstitialPolicy(new AdPolicyConfig { InterstitialGraceGames = 4 });
            // GamesUntilPending: 7, 6, 5, 4, 3 ...; preload once ≤ N (3).
            bool[] expected = { false, false, false, false, true, true, true };
            foreach (bool e in expected)
            {
                Assert.Equal(e, AdPreloadAdvisor.ShouldPreloadInterstitial(p, false));
                p.OnGameCompleted(AdsTestUtil.Game());
            }
            Assert.True(p.IsPending);
            Assert.True(AdPreloadAdvisor.ShouldPreloadInterstitial(p, false));
        }

        [Fact]
        public void NullArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => AdPreloadAdvisor.ShouldPreloadRewarded(true, false, null));
            Assert.Throws<ArgumentNullException>(() => AdPreloadAdvisor.ShouldPreloadInterstitial(null, false));
        }
    }
}
