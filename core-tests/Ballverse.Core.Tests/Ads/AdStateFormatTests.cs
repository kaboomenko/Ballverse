using System.IO;
using Ballverse.Core.Ads;
using Xunit;
using static Ballverse.Core.Tests.Ads.AdsTestUtil;

namespace Ballverse.Core.Tests.Ads
{
    public class AdStateFormatTests
    {
        [Fact]
        public void InterstitialStateCompactJsonIsStable()
        {
            var s = new InterstitialState
            {
                CompletedGames = 2,
                TotalCompletedGames = 17,
                LastInterstitialShownMs = 1760000000000,
            };
            Assert.Equal(
                "{\"version\":1,\"completedGames\":2,\"totalCompletedGames\":17,\"lastInterstitialShownMs\":1760000000000}",
                s.ToJson());
            Assert.Equal("{\"version\":1,\"completedGames\":0,\"totalCompletedGames\":0}", new InterstitialState().ToJson());
        }

        [Fact]
        public void InterstitialStateFromTestData()
        {
            var s = InterstitialState.FromJson(File.ReadAllText(TestDataPath("interstitial_state_v1.json")));
            Assert.Equal(1, s.Version);
            Assert.Equal(2, s.CompletedGames);
            Assert.Equal(17, s.TotalCompletedGames);
            Assert.Equal(1760000000000, s.LastInterstitialShownMs);
            Assert.Equal(1760000100000, s.LastRewardedShownMs);
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault(), s);
            p.OnGameCompleted(Game());
            Assert.True(p.IsPending);
        }

        [Fact]
        public void InterstitialStateNormalizesGarbage()
        {
            var s = InterstitialState.FromJson("{\"completedGames\":-3,\"totalCompletedGames\":-9,\"lastRewardedShownMs\":-1,\"extra\":true}");
            Assert.Equal(1, s.Version);
            Assert.Equal(0, s.CompletedGames);
            Assert.Equal(0, s.TotalCompletedGames);
            Assert.Null(s.LastRewardedShownMs);
            Assert.Equal(0, InterstitialState.FromJson("null").CompletedGames);
            Assert.Equal(0, InterstitialState.FromJson("{}").CompletedGames);
        }

        [Fact]
        public void InterstitialStateRejectsMalformedJson()
        {
            Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => InterstitialState.FromJson("{not json"));
        }

        [Fact]
        public void GateStateCompactJsonIsStable()
        {
            var s = new RewardedGateState();
            s.Credits.Add(new RewardedExportCredit("p1", RewardedCreditSource.Retry, 1760000600000));
            s.Credits.Add(new RewardedExportCredit("p2", RewardedCreditSource.Reward, null));
            Assert.Equal(
                "{\"version\":1,\"credits\":[{\"projectId\":\"p1\",\"source\":\"Retry\",\"expiresAtMs\":1760000600000},{\"projectId\":\"p2\",\"source\":\"Reward\"}]}",
                s.ToJson());
        }

        [Fact]
        public void GateStateFromTestDataIsNormalized()
        {
            var s = RewardedGateState.FromJson(File.ReadAllText(TestDataPath("rewarded_gate_state_v1.json")));
            Assert.Equal("proj-z", s.OpenOfferProjectId);
            Assert.Equal(3, s.Credits.Count);
            Assert.Equal("proj-a", s.Credits[0].ProjectId);
            Assert.Equal(RewardedCreditSource.Reward, s.Credits[0].Source); // held beats timed
            Assert.Null(s.Credits[0].ExpiresAtMs);
            Assert.Equal("proj-m", s.Credits[1].ProjectId);
            Assert.Equal(1760000300000, s.Credits[1].ExpiresAtMs); // later expiry kept
            Assert.Equal("proj-z", s.Credits[2].ProjectId);

            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault(), s);
            Assert.Equal(RewardedGateStepKind.StartExport,
                g.OnOfferOutcome("proj-z", RewardedOfferOutcome.UserEarnedReward, 1760000000000).Kind);
            Assert.Equal(ExportWithoutAdReason.EarnedCredit, g.Decide("proj-a", 1760000000000, false).Reason);
        }

        [Fact]
        public void GateStateNormalizesNullsAndUnknownSources()
        {
            var s = RewardedGateState.FromJson("{\"credits\":null,\"openOfferProjectId\":\"\"}");
            Assert.Empty(s.Credits);
            Assert.Null(s.OpenOfferProjectId);
            var t = RewardedGateState.FromJson("{\"credits\":[null,{\"projectId\":\"x\",\"source\":7}]}");
            Assert.Empty(t.Credits);
            Assert.Empty(RewardedGateState.FromJson("null").Credits);
        }

        [Fact]
        public void SerializeRestoreSerializeIsIdentity()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            g.Decide("b", T0, false);
            g.OnOfferOutcome("b", RewardedOfferOutcome.UserEarnedReward, T0);
            g.Decide("a", T0, false);
            string json = g.SerializeState();
            Assert.Equal(json, RewardedExportGate.FromJson(g.Config, json).SerializeState());

            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            p.OnGameCompleted(Game());
            p.OnRewardedShown(T0);
            string pj = p.SerializeState();
            Assert.Equal(pj, InterstitialPolicy.FromJson(p.Config, pj).SerializeState());
        }
    }
}
