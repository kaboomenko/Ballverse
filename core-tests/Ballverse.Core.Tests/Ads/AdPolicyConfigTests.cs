using System.IO;
using Ballverse.Core.Ads;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ballverse.Core.Tests.Ads
{
    public class AdPolicyConfigTests
    {
        [Fact]
        public void DefaultsMatchSpec()
        {
            var c = AdPolicyConfig.CreateDefault();
            Assert.Equal(3, c.InterstitialEveryNGames);
            Assert.Equal(60, c.InterstitialMinIntervalSec);
            Assert.Equal(0, c.InterstitialGraceGames);
            Assert.Equal(30, c.InterstitialQuietAfterRewardedSec);
            Assert.True(c.RewardedExportEnabled);
            Assert.Equal(5, c.RewardedWaitTimeoutSec);
            Assert.Equal(10, c.RewardedRetryWindowMin);
            Assert.True(c.IsValid);
            Assert.Empty(c.Validate());
        }

        [Fact]
        public void MillisecondHelpers()
        {
            var c = AdPolicyConfig.CreateDefault();
            Assert.Equal(60_000L, c.InterstitialMinIntervalMs);
            Assert.Equal(30_000L, c.InterstitialQuietAfterRewardedMs);
            Assert.Equal(5_000L, c.RewardedWaitTimeoutMs);
            Assert.Equal(600_000L, c.RewardedRetryWindowMs);
        }

        [Fact]
        public void ValidatedClampsLowValues()
        {
            var c = new AdPolicyConfig
            {
                InterstitialEveryNGames = 0,
                InterstitialMinIntervalSec = -5,
                InterstitialGraceGames = -1,
                InterstitialQuietAfterRewardedSec = -30,
                RewardedWaitTimeoutSec = 0,
                RewardedRetryWindowMin = -10,
            };
            Assert.Equal(6, c.Validate().Count);
            Assert.False(c.IsValid);
            var v = c.Validated();
            Assert.Equal(1, v.InterstitialEveryNGames);
            Assert.Equal(0, v.InterstitialMinIntervalSec);
            Assert.Equal(0, v.InterstitialGraceGames);
            Assert.Equal(0, v.InterstitialQuietAfterRewardedSec);
            Assert.Equal(1, v.RewardedWaitTimeoutSec);
            Assert.Equal(0, v.RewardedRetryWindowMin);
            Assert.True(v.IsValid);
            // The original is untouched.
            Assert.Equal(0, c.InterstitialEveryNGames);
        }

        [Fact]
        public void ValidatedClampsHighValues()
        {
            var c = new AdPolicyConfig
            {
                InterstitialEveryNGames = 1000,
                InterstitialMinIntervalSec = int.MaxValue,
                InterstitialGraceGames = 5000,
                InterstitialQuietAfterRewardedSec = 9999,
                RewardedWaitTimeoutSec = 600,
                RewardedRetryWindowMin = 100_000,
            };
            var v = c.Validated();
            Assert.Equal(20, v.InterstitialEveryNGames);
            Assert.Equal(600, v.InterstitialMinIntervalSec);
            Assert.Equal(50, v.InterstitialGraceGames);
            Assert.Equal(600, v.InterstitialQuietAfterRewardedSec);
            Assert.Equal(30, v.RewardedWaitTimeoutSec);
            Assert.Equal(60, v.RewardedRetryWindowMin);
        }

        [Fact]
        public void ValidateNamesOffendingKeysInOrder()
        {
            var c = new AdPolicyConfig { InterstitialEveryNGames = 0, RewardedRetryWindowMin = -1 };
            Assert.Equal(new[] { "interstitialEveryNGames", "rewardedRetryWindowMin" }, c.Validate());
        }

        [Fact]
        public void BoundaryValuesAreValid()
        {
            var lo = new AdPolicyConfig
            {
                InterstitialEveryNGames = 1, InterstitialMinIntervalSec = 0, InterstitialGraceGames = 0,
                InterstitialQuietAfterRewardedSec = 0, RewardedWaitTimeoutSec = 1, RewardedRetryWindowMin = 0,
            };
            var hi = new AdPolicyConfig
            {
                InterstitialEveryNGames = 20, InterstitialMinIntervalSec = 600, InterstitialGraceGames = 50,
                InterstitialQuietAfterRewardedSec = 600, RewardedWaitTimeoutSec = 30, RewardedRetryWindowMin = 60,
            };
            Assert.True(lo.IsValid);
            Assert.True(hi.IsValid);
            var over = new AdPolicyConfig
            {
                InterstitialEveryNGames = 21, InterstitialMinIntervalSec = 601, InterstitialGraceGames = 51,
                InterstitialQuietAfterRewardedSec = 601, RewardedWaitTimeoutSec = 31, RewardedRetryWindowMin = 61,
            };
            Assert.Equal(6, over.Validate().Count);
        }

        [Fact]
        public void JsonUsesCamelCaseAndSkipsDerivedValues()
        {
            var json = AdPolicyConfig.CreateDefault().ToJson(false);
            var obj = JObject.Parse(json);
            Assert.Equal(3, (int)obj["interstitialEveryNGames"]);
            Assert.Equal(60, (int)obj["interstitialMinIntervalSec"]);
            Assert.True((bool)obj["rewardedExportEnabled"]);
            Assert.Null(obj["interstitialMinIntervalMs"]);
            Assert.Null(obj["isValid"]);
            Assert.Equal(7, obj.Count);
        }

        [Fact]
        public void JsonRoundTrip()
        {
            var c = new AdPolicyConfig
            {
                InterstitialEveryNGames = 4, InterstitialMinIntervalSec = 90, InterstitialGraceGames = 3,
                InterstitialQuietAfterRewardedSec = 45, RewardedExportEnabled = false, RewardedWaitTimeoutSec = 8,
                RewardedRetryWindowMin = 15,
            };
            var back = AdPolicyConfig.FromJson(c.ToJson());
            Assert.Equal(c.ToJson(), back.ToJson());
        }

        [Fact]
        public void FromJsonKeepsDefaultsForMissingKeysAndClamps()
        {
            var c = AdPolicyConfig.FromJson("{\"interstitialEveryNGames\":0,\"rewardedExportEnabled\":false}");
            Assert.Equal(1, c.InterstitialEveryNGames);
            Assert.False(c.RewardedExportEnabled);
            Assert.Equal(60, c.InterstitialMinIntervalSec);
            Assert.Equal(10, c.RewardedRetryWindowMin);
            Assert.True(AdPolicyConfig.FromJson("null").IsValid);
        }

        [Fact]
        public void FromJsonTestDataFile()
        {
            var json = File.ReadAllText(AdsTestUtil.TestDataPath("ad_policy_config_remote.json"));
            var c = AdPolicyConfig.FromJson(json);
            Assert.Equal(5, c.InterstitialEveryNGames);
            Assert.Equal(600, c.InterstitialMinIntervalSec); // 99999 clamped
            Assert.Equal(3, c.InterstitialGraceGames);
            Assert.Equal(30, c.InterstitialQuietAfterRewardedSec); // missing → default
            Assert.False(c.RewardedExportEnabled);
            Assert.Equal(1, c.RewardedWaitTimeoutSec); // -2 clamped
            Assert.Equal(20, c.RewardedRetryWindowMin);
        }

        /// <summary>
        /// The ranges mirror the Config module's RemoteConfigLimits (spec §9.3), the only §9.3 parser, so the safety-net
        /// clamp here never disagrees with it. If this test fails, RemoteConfigLimits changed or drifted: update both.
        /// </summary>
        [Fact]
        public void RangesMatchRemoteConfigLimits()
        {
            Assert.Equal((1, 20), (AdPolicyConfig.MinInterstitialEveryNGames, AdPolicyConfig.MaxInterstitialEveryNGames));
            Assert.Equal((0, 600), (AdPolicyConfig.MinInterstitialMinIntervalSec, AdPolicyConfig.MaxInterstitialMinIntervalSec));
            Assert.Equal((0, 50), (AdPolicyConfig.MinInterstitialGraceGames, AdPolicyConfig.MaxInterstitialGraceGames));
            Assert.Equal((1, 30), (AdPolicyConfig.MinRewardedWaitTimeoutSec, AdPolicyConfig.MaxRewardedWaitTimeoutSec));
            Assert.Equal((0, 60), (AdPolicyConfig.MinRewardedRetryWindowMin, AdPolicyConfig.MaxRewardedRetryWindowMin));
        }

        /// <summary>Same inputs as the QA integration probe: the result equals what RemoteConfigParser produces.</summary>
        [Theory]
        [InlineData("interstitialEveryNGames", "50", 20)]
        [InlineData("interstitialEveryNGames", "2.5", 3)]
        [InlineData("interstitialEveryNGames", "\"2.5\"", 3)]
        [InlineData("interstitialEveryNGames", "4.0", 4)]
        [InlineData("interstitialMinIntervalSec", "900", 600)]
        [InlineData("interstitialMinIntervalSec", "\"1e3\"", 600)]
        [InlineData("interstitialGraceGames", "100", 50)]
        [InlineData("rewardedRetryWindowMin", "120", 60)]
        [InlineData("rewardedRetryWindowMin", "\"Infinity\"", 10)]
        public void FromJsonAgreesWithConfigModuleRules(string key, string literal, int expected)
        {
            var c = AdPolicyConfig.FromJson("{\"" + key + "\":" + literal + "}");
            int actual = key switch
            {
                "interstitialEveryNGames" => c.InterstitialEveryNGames,
                "interstitialMinIntervalSec" => c.InterstitialMinIntervalSec,
                "interstitialGraceGames" => c.InterstitialGraceGames,
                _ => c.RewardedRetryWindowMin,
            };
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FromJsonIgnoresRemoteConfigKeys()
        {
            // The §9.3 snake_case keys belong to the Config module; this format reads camelCase keys only.
            var c = AdPolicyConfig.FromJson("{\"rewarded_export_enabled\":false,\"interstitial_every_n_games\":5}");
            Assert.True(c.RewardedExportEnabled);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialEveryNGames, c.InterstitialEveryNGames);
        }

        [Fact]
        public void FromJsonRejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => AdPolicyConfig.FromJson(null));
        }
    }
}
