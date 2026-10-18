using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ballverse.Core.Ads;
using Newtonsoft.Json;
using Xunit;
using static Ballverse.Core.Tests.Ads.AdsTestUtil;

namespace Ballverse.Core.Tests.Ads
{
    /// <summary>Clamping at runtime, credit claims, clock rollback, tolerant parsing and Remote Config keys (spec §5.3, §9.3).</summary>
    public class AdsRobustnessTests
    {
        const string A = "project-a";
        const string B = "project-b";

        // ---------- runtime Config setters clamp ----------

        [Fact]
        public void InterstitialConfigSetterClampsRemoteNZero()
        {
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            p.OnInterstitialShown(T0);
            p.Config = new AdPolicyConfig { InterstitialEveryNGames = 0 };
            Assert.Equal(1, p.Config.InterstitialEveryNGames);
            Assert.False(p.IsPending);
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 2 * Min)).Kind);
            CompleteGames(p, 1);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void InterstitialConfigSetterClampsNegativeInterval()
        {
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            p.Config = new AdPolicyConfig { InterstitialMinIntervalSec = -5, InterstitialGraceGames = -1, InterstitialQuietAfterRewardedSec = 99999 };
            var c = p.Config;
            Assert.Equal(0, c.InterstitialMinIntervalSec);
            Assert.Equal(0, c.InterstitialGraceGames);
            Assert.Equal(AdPolicyConfig.MaxInterstitialQuietAfterRewardedSec, c.InterstitialQuietAfterRewardedSec);
        }

        [Fact]
        public void GateConfigSetterClampsWaitTimeout()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            g.Config = new AdPolicyConfig { RewardedWaitTimeoutSec = 0, RewardedRetryWindowMin = -3 };
            Assert.Equal(AdPolicyConfig.MinRewardedWaitTimeoutSec, g.Config.RewardedWaitTimeoutSec);
            Assert.Equal(0, g.Config.RewardedRetryWindowMin);
            Assert.False(g.HasWaitTimedOut(T0, T0 + 999));
            Assert.True(g.HasWaitTimedOut(T0, T0 + 1000));
            g.Config = new AdPolicyConfig { RewardedWaitTimeoutSec = 1000 };
            Assert.False(g.HasWaitTimedOut(T0, T0 + 30 * Sec - 1));
            Assert.True(g.HasWaitTimedOut(T0, T0 + 30 * Sec));
        }

        // ---------- RW5: only the export a credit funded can convert it ----------

        static void Earn(RewardedExportGate g, string project, long now)
        {
            Assert.True(g.Decide(project, now, false).IsShowOffer);
            Assert.Equal(RewardedGateStepKind.StartExport, g.OnOfferOutcome(project, RewardedOfferOutcome.UserEarnedReward, now).Kind);
        }

        [Fact]
        public void RW5_HeldRewardSurvivesFailedExportWithGateDisabled()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Earn(g, A, T0);
            // App killed before the export reported: the credit stays held. Then the RW7 kill switch goes on.
            g = Restart(g);
            g.Config = new AdPolicyConfig { RewardedExportEnabled = false };
            Assert.Equal(ExportWithoutAdReason.GateDisabled, g.Decide(A, T0 + 10 * Sec, false).Reason);
            Assert.Equal(ExportFinishKind.None, g.OnExportFinished(A, ExportResult.Failed, T0 + 12 * Sec).Kind);
            Assert.True(g.TryGetCredit(A, T0 + 12 * Sec, out var held));
            Assert.Null(held.ExpiresAtMs);
            Assert.Equal(RewardedCreditSource.Reward, held.Source);

            g.Config = AdPolicyConfig.CreateDefault();
            Assert.Equal(ExportWithoutAdReason.EarnedCredit, g.Decide(A, T0 + Min, false).Reason);
            // Now the credit funds the export, so its failure gives the RW4 retry window.
            var fin = g.OnExportFinished(A, ExportResult.Failed, T0 + 2 * Min);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, fin.Kind);
            Assert.Equal(T0 + 12 * Min, fin.RetryExpiresAtMs);
        }

        [Fact]
        public void RW5_HeldRewardSurvivesCancelledExportWithEntitlement()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Earn(g, A, T0);
            Assert.Equal(ExportWithoutAdReason.Entitlement, g.Decide(A, T0 + Sec, true).Reason);
            Assert.Equal(ExportFinishKind.None, g.OnExportFinished(A, ExportResult.Cancelled, T0 + 2 * Sec).Kind);
            Assert.True(g.TryGetCredit(A, T0 + Day(), out var held));
            Assert.Null(held.ExpiresAtMs);
            Assert.Equal(ExportWithoutAdReason.EarnedCredit, g.Decide(A, T0 + Day(), false).Reason);
        }

        [Fact]
        public void RW5_ClaimSurvivesRestart()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Earn(g, A, T0);
            Assert.Contains("\"claimed\":true", g.SerializeState());
            g = Restart(g);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, g.OnExportFinished(A, ExportResult.Failed, T0 + Min).Kind);
            Assert.DoesNotContain("claimed", g.SerializeState()); // a timed credit is never claimed
        }

        [Fact]
        public void TimedCreditIsNeverClaimedAfterLoad()
        {
            var s = RewardedGateState.FromJson("{\"credits\":[{\"projectId\":\"a\",\"source\":\"Retry\",\"expiresAtMs\":5,\"claimed\":true}]}");
            Assert.False(s.Credits[0].Claimed);
            var t = RewardedGateState.FromJson("{\"credits\":[{\"projectId\":\"a\",\"source\":\"Reward\",\"claimed\":\"yes\"}]}");
            Assert.Single(t.Credits);
            Assert.False(t.Credits[0].Claimed);
        }

        static long Day()
        {
            return 24 * 60 * Min;
        }

        // ---------- Decide closes a stale open offer ----------

        [Fact]
        public void EntitlementClosesStaleOffer()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Assert.True(g.Decide(A, T0, false).IsShowOffer);
            Assert.Equal(ExportWithoutAdReason.Entitlement, g.Decide(A, T0 + Sec, true).Reason);
            Assert.Null(g.OpenOfferProjectId);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0 + 2 * Sec));
            Assert.Equal(0, g.CreditCount);
        }

        [Fact]
        public void GateDisabledClosesStaleOffer()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Assert.True(g.Decide(A, T0, false).IsShowOffer);
            g.Config = new AdPolicyConfig { RewardedExportEnabled = false };
            Assert.Equal(ExportWithoutAdReason.GateDisabled, g.Decide(A, T0 + Sec, false).Reason);
            Assert.Null(g.OpenOfferProjectId);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0 + 2 * Sec));
        }

        [Fact]
        public void CreditPathClosesStaleOffer()
        {
            var state = new RewardedGateState { OpenOfferProjectId = A };
            state.Credits.Add(new RewardedExportCredit(A, RewardedCreditSource.Retry, T0 + 5 * Min));
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault(), state);
            Assert.Equal(ExportWithoutAdReason.RetryCredit, g.Decide(A, T0, false).Reason);
            Assert.Null(g.OpenOfferProjectId);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0));
        }

        // ---------- RW3 clock rollback ----------

        [Fact]
        public void RW3_WaitTimesOutAfterClockStepBack()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Assert.True(g.HasWaitTimedOut(T0, T0 - 60 * Min + 10 * Sec));
            Assert.True(g.HasWaitTimedOut(T0, 0));
        }

        // ---------- IN6 grace flag and partial state ----------

        [Fact]
        public void IN6_NewUserWithZeroGraceIsNotInGrace()
        {
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            Assert.False(p.InGracePeriod);
            Assert.Equal(3, p.GamesUntilPending);
            var q = new InterstitialPolicy(new AdPolicyConfig { InterstitialGraceGames = 1 });
            Assert.True(q.InGracePeriod);
        }

        [Fact]
        public void IN1_CounterWithoutTotalIsPending()
        {
            var p = InterstitialPolicy.FromJson(AdPolicyConfig.CreateDefault(), "{\"completedGames\":3}");
            Assert.Equal(3, p.TotalCompletedGames);
            Assert.False(p.InGracePeriod);
            Assert.True(p.IsPending);
            Assert.Equal(0, p.GamesUntilPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
        }

        // ---------- tolerant parsing ----------

        [Fact]
        public void GateStateDropsOnlyCreditsWithUnknownSourceStrings()
        {
            var s = RewardedGateState.FromJson(
                "{\"credits\":[{\"projectId\":\"a\",\"source\":\"Purchase\"},{\"projectId\":\"b\",\"source\":\"Reward\"}," +
                "{\"projectId\":\"c\",\"source\":\"1\"},{\"projectId\":\"d\",\"source\":\"Reward,Retry\"}," +
                "{\"projectId\":\"e\",\"source\":\"retry\",\"expiresAtMs\":\"oops\"},{\"projectId\":7,\"source\":\"Reward\"}," +
                "{\"projectId\":\"f\",\"source\":\"retry\",\"expiresAtMs\":10}],\"openOfferProjectId\":3}");
            Assert.Equal(2, s.Credits.Count);
            Assert.Equal("b", s.Credits[0].ProjectId);
            Assert.Equal("f", s.Credits[1].ProjectId);
            Assert.Equal(RewardedCreditSource.Retry, s.Credits[1].Source);
            Assert.Null(s.OpenOfferProjectId);
            Assert.Empty(RewardedGateState.FromJson("{\"credits\":{\"a\":1}}").Credits);
        }

        [Fact]
        public void ConfigHugeAndFractionalValuesAreClamped()
        {
            var c = AdPolicyConfig.FromJson("{\"interstitialMinIntervalSec\":3000000000,\"interstitialEveryNGames\":2.5," +
                "\"interstitialGraceGames\":-1e30,\"rewardedWaitTimeoutSec\":99999999999999999999999,\"rewardedRetryWindowMin\":\"12\"}");
            Assert.Equal(AdPolicyConfig.MaxInterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            Assert.Equal(2, c.InterstitialEveryNGames);
            Assert.Equal(0, c.InterstitialGraceGames);
            Assert.Equal(AdPolicyConfig.MaxRewardedWaitTimeoutSec, c.RewardedWaitTimeoutSec);
            Assert.Equal(12, c.RewardedRetryWindowMin);
            Assert.True(c.IsValid);
        }

        [Fact]
        public void ConfigWrongTypesKeepDefaults()
        {
            var c = AdPolicyConfig.FromJson("{\"interstitialEveryNGames\":true,\"interstitialMinIntervalSec\":[1],\"rewardedExportEnabled\":\"maybe\"}");
            Assert.Equal(AdPolicyConfig.DefaultInterstitialEveryNGames, c.InterstitialEveryNGames);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            Assert.True(c.RewardedExportEnabled);
            Assert.False(AdPolicyConfig.FromJson("{\"rewardedExportEnabled\":\"false\"}").RewardedExportEnabled);
        }

        [Fact]
        public void InterstitialStateHugeAndWrongValuesAreNormalized()
        {
            var s = InterstitialState.FromJson("{\"completedGames\":3000000000,\"totalCompletedGames\":\"x\",\"lastInterstitialShownMs\":1.5e3,\"lastRewardedShownMs\":{}}");
            Assert.Equal(int.MaxValue, s.CompletedGames);
            Assert.Equal(int.MaxValue, s.TotalCompletedGames);
            Assert.Equal(1500, s.LastInterstitialShownMs);
            Assert.Null(s.LastRewardedShownMs);
        }

        // ---------- §9.3 Remote Config keys ----------

        [Fact]
        public void RemoteKeysAreTheSpecNames()
        {
            Assert.Equal("interstitial_every_n_games", AdPolicyConfig.RemoteKeyInterstitialEveryNGames);
            Assert.Equal("interstitial_min_interval_sec", AdPolicyConfig.RemoteKeyInterstitialMinIntervalSec);
            Assert.Equal("interstitial_grace_games", AdPolicyConfig.RemoteKeyInterstitialGraceGames);
            Assert.Equal("rewarded_export_enabled", AdPolicyConfig.RemoteKeyRewardedExportEnabled);
            Assert.Equal("rewarded_wait_timeout_sec", AdPolicyConfig.RemoteKeyRewardedWaitTimeoutSec);
            Assert.Equal("rewarded_retry_window_min", AdPolicyConfig.RemoteKeyRewardedRetryWindowMin);
        }

        [Fact]
        public void FromRemoteValuesTestDataFile()
        {
            var values = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(TestDataPath("remote_config_values.json")));
            var c = AdPolicyConfig.FromRemoteValues(values);
            Assert.Equal(5, c.InterstitialEveryNGames);
            Assert.Equal(90, c.InterstitialMinIntervalSec);
            Assert.Equal(3, c.InterstitialGraceGames);
            Assert.False(c.RewardedExportEnabled);
            Assert.Equal(7, c.RewardedWaitTimeoutSec);
            Assert.Equal(15, c.RewardedRetryWindowMin);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialQuietAfterRewardedSec, c.InterstitialQuietAfterRewardedSec);
        }

        [Fact]
        public void FromRemoteValuesIgnoresUnparsableAndClamps()
        {
            var c = AdPolicyConfig.FromRemoteValues(new Dictionary<string, string>
            {
                { "interstitial_every_n_games", "0" },
                { "interstitial_min_interval_sec", "abc" },
                { "interstitial_grace_games", null },
                { "rewarded_export_enabled", "nope" },
                { "rewarded_wait_timeout_sec", "1e12" },
                { "Rewarded_Retry_Window_Min", "3" },
            });
            Assert.Equal(1, c.InterstitialEveryNGames);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialGraceGames, c.InterstitialGraceGames);
            Assert.True(c.RewardedExportEnabled);
            Assert.Equal(AdPolicyConfig.MaxRewardedWaitTimeoutSec, c.RewardedWaitTimeoutSec);
            Assert.Equal(AdPolicyConfig.DefaultRewardedRetryWindowMin, c.RewardedRetryWindowMin); // keys are exact
            Assert.Throws<ArgumentNullException>(() => AdPolicyConfig.FromRemoteValues(null));
            Assert.False(AdPolicyConfig.FromRemoteValues(new Dictionary<string, string> { { "rewarded_export_enabled", "0" } }).RewardedExportEnabled);
        }

        [Fact]
        public void FromJsonAcceptsSpecKeys()
        {
            var c = AdPolicyConfig.FromJson("{\"rewarded_export_enabled\":false,\"interstitial_every_n_games\":5}");
            Assert.False(c.RewardedExportEnabled);
            Assert.Equal(5, c.InterstitialEveryNGames);
            var both = AdPolicyConfig.FromJson("{\"interstitialEveryNGames\":4,\"interstitial_every_n_games\":5}");
            Assert.Equal(4, both.InterstitialEveryNGames);
        }

        // ---------- state size: O(n log n) load, order independence, RemoveProject ----------

        [Fact]
        public void GateStateNormalizeIsOrderIndependent()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var rng = Rng.FromSeed(seed, 0x50F7);
                var credits = new List<RewardedExportCredit>();
                for (int i = 0; i < 40; i++)
                {
                    long? exp = rng.NextInt(0, 3) == 0 ? (long?)null : rng.NextInt(0, 5);
                    var src = rng.NextInt(0, 2) == 0 ? RewardedCreditSource.Reward : RewardedCreditSource.Retry;
                    credits.Add(new RewardedExportCredit("p" + rng.NextInt(0, 8), src, exp, rng.NextInt(0, 2) == 0));
                }
                string expected = null;
                for (int round = 0; round < 5; round++)
                {
                    var shuffled = new List<RewardedExportCredit>(credits);
                    for (int i = shuffled.Count - 1; i > 0; i--)
                    {
                        int j = rng.NextInt(0, i + 1);
                        var t = shuffled[i];
                        shuffled[i] = shuffled[j];
                        shuffled[j] = t;
                    }
                    var state = new RewardedGateState { Credits = shuffled };
                    string json = RewardedGateState.FromJson(state.ToJson()).ToJson();
                    if (expected == null) expected = json;
                    Assert.Equal(expected, json);
                }
                var back = RewardedGateState.FromJson(expected);
                for (int i = 1; i < back.Credits.Count; i++)
                    Assert.True(string.CompareOrdinal(back.Credits[i - 1].ProjectId, back.Credits[i].ProjectId) < 0);
            }
        }

        [Fact]
        public void LargeGateStateLoadsQuickly()
        {
            var sb = new StringBuilder("{\"credits\":[");
            for (int i = 0; i < 40000; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"projectId\":\"proj-").Append(39999 - i).Append("\",\"source\":\"Reward\"}");
            }
            sb.Append("]}");
            var g = RewardedExportGate.FromJson(AdPolicyConfig.CreateDefault(), sb.ToString());
            Assert.Equal(40000, g.CreditCount);
            Assert.Equal(ExportWithoutAdReason.EarnedCredit, g.Decide("proj-12345", T0, false).Reason);
            Assert.True(g.Decide("proj-x", T0, false).IsShowOffer);
        }

        [Fact]
        public void RemoveProjectDropsCreditAndOffer()
        {
            var g = new RewardedExportGate(AdPolicyConfig.CreateDefault());
            Earn(g, A, T0);
            Assert.True(g.Decide(B, T0, false).IsShowOffer);
            Assert.False(g.RemoveProject("other"));
            Assert.Equal(B, g.OpenOfferProjectId);
            Assert.True(g.RemoveProject(A));
            Assert.Equal(0, g.CreditCount);
            Assert.False(g.RemoveProject(B));
            Assert.Null(g.OpenOfferProjectId);
            Assert.True(g.Decide(A, T0 + Sec, false).IsShowOffer);
            Assert.Throws<ArgumentException>(() => g.RemoveProject(""));
        }
    }
}
