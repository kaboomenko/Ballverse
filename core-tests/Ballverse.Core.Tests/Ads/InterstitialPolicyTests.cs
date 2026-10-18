using System;
using Ballverse.Core.Ads;
using Xunit;
using static Ballverse.Core.Tests.Ads.AdsTestUtil;

namespace Ballverse.Core.Tests.Ads
{
    public class InterstitialPolicyTests
    {
        static InterstitialPolicy NewPolicy(Action<AdPolicyConfig> tweak = null)
        {
            var c = AdPolicyConfig.CreateDefault();
            tweak?.Invoke(c);
            return new InterstitialPolicy(c);
        }

        // ---------- IN1: counter, pending at N, persists across sessions ----------

        [Fact]
        public void IN1_PendingAfterThirdCompletedGame()
        {
            var p = NewPolicy();
            CompleteGames(p, 2);
            Assert.False(p.IsPending);
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Kind);
            CompleteGames(p, 1);
            Assert.True(p.IsPending);
            Assert.Equal(3, p.CompletedGames);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 10 * Sec)).IsShow);
        }

        [Fact]
        public void IN1_CounterPersistsAcrossSessions()
        {
            var p = NewPolicy();
            CompleteGames(p, 2);
            p = Restart(p);
            Assert.Equal(2, p.CompletedGames);
            Assert.Equal(2, p.TotalCompletedGames);
            CompleteGames(p, 1);
            p = Restart(p);
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.ToGallery, T0)).IsShow);
        }

        [Fact]
        public void IN1_PendingSurvivesRestartAfterSuppression()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            Assert.Equal(InterstitialSuppressReason.ToVideo, p.OnNaturalBreak(Break(NaturalBreakKind.ToVideo, T0)).Reason);
            p = Restart(p);
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + Min)).IsShow);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        public void IN1_RemoteN(int n)
        {
            var p = NewPolicy(c => c.InterstitialEveryNGames = n);
            CompleteGames(p, n - 1);
            Assert.False(p.IsPending);
            CompleteGames(p, 1);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void IN1_NonCompletedRunsDoNotCount()
        {
            var p = NewPolicy();
            Assert.False(p.OnGameCompleted(GameRunSummary.FinishedRegular(299)));
            Assert.False(p.OnGameCompleted(new GameRunSummary(GameRunKind.PaidModeTrialPreview, GameRunEnd.Finished, 3000)));
            Assert.False(p.OnGameCompleted(new GameRunSummary(GameRunKind.GenerationCandidate, GameRunEnd.Finished, 3000)));
            Assert.False(p.OnGameCompleted(new GameRunSummary(GameRunKind.StudioReplayPlayback, GameRunEnd.Finished, 3000)));
            Assert.False(p.OnGameCompleted(new GameRunSummary(GameRunKind.Regular, GameRunEnd.RestartedBeforeFinale, 3000)));
            Assert.Equal(0, p.CompletedGames);
            Assert.Equal(0, p.TotalCompletedGames);
            Assert.True(p.OnGameCompleted(GameRunSummary.FinishedRegular(300)));
            Assert.Equal(1, p.CompletedGames);
        }

        [Fact]
        public void IN1_ExactlyFiveSecondGamesReachPending()
        {
            var p = NewPolicy();
            for (int i = 0; i < 3; i++) Assert.True(p.OnGameCompleted(GameRunSummary.FinishedRegular(GameCompletedRule.MinDurationTicks)));
            Assert.True(p.IsPending);
        }

        [Fact]
        public void IN1_CounterKeepsCountingWhilePendingAndResetsAfterShow()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            p.OnNaturalBreak(Break(NaturalBreakKind.ToVideo, T0));
            CompleteGames(p, 2);
            Assert.Equal(5, p.CompletedGames);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            p.OnInterstitialShown(T0);
            Assert.Equal(0, p.CompletedGames);
            Assert.False(p.IsPending);
        }

        // ---------- IN2: only at natural breaks, never at the finale ----------

        [Theory]
        [InlineData(NaturalBreakKind.Retry)]
        [InlineData(NaturalBreakKind.Generate)]
        [InlineData(NaturalBreakKind.BackFromResult)]
        [InlineData(NaturalBreakKind.ToGallery)]
        public void IN2_ShownAtEveryNaturalBreak(NaturalBreakKind kind)
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            var d = p.OnNaturalBreak(Break(kind, T0));
            Assert.Equal(InterstitialDecisionKind.Show, d.Kind);
            Assert.Equal(InterstitialSuppressReason.None, d.Reason);
            Assert.False(d.CounterReset);
        }

        [Fact]
        public void IN2_GameCompletionNeverShowsAndDecisionDoesNotMutateUntilShown()
        {
            var p = NewPolicy();
            CompleteGames(p, 3); // the finale itself returns only "counted"
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            // Until the caller reports the show, the state is unchanged and asking again gives the same answer.
            Assert.True(p.IsPending);
            Assert.Null(p.LastInterstitialShownMs);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
        }

        [Fact]
        public void IN2_NotPendingGivesNone()
        {
            var p = NewPolicy();
            foreach (NaturalBreakKind kind in Enum.GetValues(typeof(NaturalBreakKind)))
            {
                var d = p.OnNaturalBreak(Break(kind, T0, adReady: false));
                Assert.Equal(InterstitialDecisionKind.None, d.Kind);
                Assert.False(d.CounterReset);
            }
        }

        // ---------- IN3: forbidden contexts keep it pending ----------

        [Fact]
        public void IN3_ToVideoNeverShowsAndKeepsPending()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            var d = p.OnNaturalBreak(Break(NaturalBreakKind.ToVideo, T0));
            Assert.Equal(InterstitialDecisionKind.Suppressed, d.Kind);
            Assert.Equal(InterstitialSuppressReason.ToVideo, d.Reason);
            Assert.False(d.CounterReset);
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Generate, T0 + Sec)).IsShow);
        }

        [Theory]
        [InlineData("studio", InterstitialSuppressReason.InStudio)]
        [InlineData("export", InterstitialSuppressReason.Exporting)]
        [InlineData("purchase", InterstitialSuppressReason.PurchaseFlow)]
        public void IN3_ForbiddenContextsKeepPending(string flag, InterstitialSuppressReason reason)
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            var ctx = Break(NaturalBreakKind.BackFromResult, T0);
            ctx.InStudio = flag == "studio";
            ctx.Exporting = flag == "export";
            ctx.InPurchaseFlow = flag == "purchase";
            var d = p.OnNaturalBreak(ctx);
            Assert.Equal(InterstitialDecisionKind.Suppressed, d.Kind);
            Assert.Equal(reason, d.Reason);
            Assert.True(p.IsPending);
            Assert.Equal(3, p.CompletedGames);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.BackFromResult, T0 + Sec)).IsShow);
        }

        [Fact]
        public void IN3_ForbiddenContextWinsOverAdNotReady_CounterNotReset()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            var ctx = Break(NaturalBreakKind.Retry, T0, adReady: false);
            ctx.Exporting = true;
            Assert.Equal(InterstitialSuppressReason.Exporting, p.OnNaturalBreak(ctx).Reason);
            Assert.True(p.IsPending);
            Assert.Equal(InterstitialSuppressReason.ToVideo, p.OnNaturalBreak(Break(NaturalBreakKind.ToVideo, T0, false)).Reason);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void IN3_QuietPeriodAfterRewarded_ExactBoundary()
        {
            var p = NewPolicy();
            p.OnRewardedShown(T0);
            CompleteGames(p, 3);
            var d = p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 30 * Sec - 1));
            Assert.Equal(InterstitialSuppressReason.RewardedQuietPeriod, d.Reason);
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 30 * Sec)).IsShow);
        }

        [Fact]
        public void IN3_QuietPeriodSurvivesRestartAndFollowsConfig()
        {
            var p = NewPolicy(c => c.InterstitialQuietAfterRewardedSec = 45);
            CompleteGames(p, 3);
            p.OnRewardedShown(T0);
            p = Restart(p);
            Assert.Equal(InterstitialSuppressReason.RewardedQuietPeriod, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 44 * Sec)).Reason);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 45 * Sec)).IsShow);
        }

        [Fact]
        public void IN3_RulesCheckedInDocumentedOrder()
        {
            var p = NewPolicy();
            p.OnInterstitialShown(T0);
            p.OnRewardedShown(T0);
            CompleteGames(p, 3);
            var ctx = Break(NaturalBreakKind.ToVideo, T0 + Sec, adReady: false);
            ctx.InStudio = ctx.Exporting = ctx.InPurchaseFlow = true;
            Assert.Equal(InterstitialSuppressReason.ToVideo, p.OnNaturalBreak(ctx).Reason);
            ctx.Kind = NaturalBreakKind.Retry;
            Assert.Equal(InterstitialSuppressReason.InStudio, p.OnNaturalBreak(ctx).Reason);
            ctx.InStudio = false;
            Assert.Equal(InterstitialSuppressReason.Exporting, p.OnNaturalBreak(ctx).Reason);
            ctx.Exporting = false;
            Assert.Equal(InterstitialSuppressReason.PurchaseFlow, p.OnNaturalBreak(ctx).Reason);
            ctx.InPurchaseFlow = false;
            Assert.Equal(InterstitialSuppressReason.RewardedQuietPeriod, p.OnNaturalBreak(ctx).Reason);
            ctx.NowMs = T0 + 30 * Sec;
            Assert.Equal(InterstitialSuppressReason.MinInterval, p.OnNaturalBreak(ctx).Reason);
            ctx.NowMs = T0 + 60 * Sec;
            Assert.Equal(InterstitialSuppressReason.AdNotReady, p.OnNaturalBreak(ctx).Reason);
        }

        // ---------- IN4: reset after show or when the ad is not ready ----------

        [Fact]
        public void IN4_ShownResetsCounter()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            p.OnInterstitialShown(T0);
            Assert.Equal(0, p.CompletedGames);
            Assert.Equal(T0, p.LastInterstitialShownMs);
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 2 * Min)).Kind);
        }

        [Fact]
        public void IN4_AdNotReadyResetsCounterAndNeverDelaysUser()
        {
            var p = NewPolicy();
            CompleteGames(p, 4);
            var d = p.OnNaturalBreak(Break(NaturalBreakKind.Generate, T0, adReady: false));
            Assert.Equal(InterstitialDecisionKind.Suppressed, d.Kind);
            Assert.Equal(InterstitialSuppressReason.AdNotReady, d.Reason);
            Assert.True(d.CounterReset);
            Assert.Equal(0, p.CompletedGames);
            Assert.False(p.IsPending);
            Assert.Null(p.LastInterstitialShownMs);
            // Next break with a ready ad: nothing pending any more, a new cycle of N games starts.
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Generate, T0 + Sec)).Kind);
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Generate, T0 + 2 * Sec)).IsShow);
        }

        [Fact]
        public void IN4_FailedPresentationResetsWithoutStartingInterval()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            p.OnInterstitialNotShown();
            Assert.Equal(0, p.CompletedGames);
            Assert.Null(p.LastInterstitialShownMs);
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + Sec)).IsShow);
        }

        // ---------- IN5: minimum interval ----------

        [Fact]
        public void IN5_MinIntervalExactBoundaryKeepsPending()
        {
            var p = NewPolicy(c => c.InterstitialEveryNGames = 1);
            CompleteGames(p, 1);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
            p.OnInterstitialShown(T0);
            CompleteGames(p, 1);
            var d = p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 60 * Sec - 1));
            Assert.Equal(InterstitialSuppressReason.MinInterval, d.Reason);
            Assert.False(d.CounterReset);
            Assert.True(p.IsPending);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 60 * Sec)).IsShow);
        }

        [Fact]
        public void IN5_MinIntervalSurvivesRestart()
        {
            var p = NewPolicy(c => c.InterstitialEveryNGames = 1);
            p.OnInterstitialShown(T0);
            p = Restart(p);
            CompleteGames(p, 1);
            Assert.Equal(InterstitialSuppressReason.MinInterval, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 59 * Sec)).Reason);
            p = Restart(p);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 61 * Sec)).IsShow);
        }

        [Fact]
        public void IN5_ZeroIntervalAllowsBackToBack()
        {
            var p = NewPolicy(c => { c.InterstitialEveryNGames = 1; c.InterstitialMinIntervalSec = 0; });
            CompleteGames(p, 1);
            p.OnInterstitialShown(T0);
            CompleteGames(p, 1);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
        }

        // ---------- IN6: grace period ----------

        [Fact]
        public void IN6_DefaultGraceIsZero_FirstInterstitialAfterThirdGame()
        {
            var p = NewPolicy();
            Assert.Equal(3, p.GamesUntilPending);
            CompleteGames(p, 3);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void IN6_GraceGamesDoNotAccumulate()
        {
            var p = NewPolicy(c => c.InterstitialGraceGames = 3);
            Assert.Equal(6, p.GamesUntilPending);
            for (int i = 1; i <= 3; i++)
            {
                CompleteGames(p, 1);
                Assert.True(p.InGracePeriod);
                Assert.Equal(0, p.CompletedGames);
                Assert.Equal(i, p.TotalCompletedGames);
                Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Kind);
            }
            CompleteGames(p, 1);
            Assert.False(p.InGracePeriod);
            Assert.Equal(1, p.CompletedGames);
            CompleteGames(p, 1);
            Assert.False(p.IsPending);
            Assert.Equal(1, p.GamesUntilPending);
            CompleteGames(p, 1);
            Assert.True(p.IsPending);
            Assert.Equal(6, p.TotalCompletedGames);
        }

        [Fact]
        public void IN6_GraceSurvivesRestart()
        {
            var p = NewPolicy(c => c.InterstitialGraceGames = 2);
            CompleteGames(p, 2);
            p = Restart(p);
            Assert.Equal(3, p.GamesUntilPending);
            CompleteGames(p, 3);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void IN6_GraceRaisedByRemoteConfigBlocksPending()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            Assert.True(p.IsPending);
            p.Config = new AdPolicyConfig { InterstitialGraceGames = 5 };
            Assert.False(p.IsPending);
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Kind);
            Assert.Equal(3, p.GamesUntilPending); // games 4, 5 are grace; game 6 ends it and the counter is already ≥ N
            CompleteGames(p, 2);
            Assert.False(p.IsPending);
            CompleteGames(p, 1);
            Assert.True(p.IsPending);
        }

        [Fact]
        public void GamesUntilPendingMatchesSimulation_Exhaustive()
        {
            for (int grace = 0; grace <= 5; grace++)
            for (int n = 1; n <= 5; n++)
            for (int counter = 0; counter <= 6; counter++)
            for (int total = 0; total <= 8; total++)
            {
                var cfg = new AdPolicyConfig { InterstitialEveryNGames = n, InterstitialGraceGames = grace };
                var state = new InterstitialState { CompletedGames = counter, TotalCompletedGames = total };
                var p = new InterstitialPolicy(cfg, state);
                long predicted = p.GamesUntilPending;
                int games = 0;
                while (!p.IsPending)
                {
                    CompleteGames(p, 1);
                    games++;
                    Assert.True(games < 100);
                }
                Assert.Equal(predicted, games);
            }
        }

        // ---------- IN7: entitlement ----------

        [Fact]
        public void IN7_EntitlementDisablesEntirely()
        {
            var p = NewPolicy();
            CompleteGames(p, 3);
            foreach (NaturalBreakKind kind in Enum.GetValues(typeof(NaturalBreakKind)))
            {
                var ctx = Break(kind, T0 + 10 * Min);
                ctx.HasNoInterstitialsEntitlement = true;
                var d = p.OnNaturalBreak(ctx);
                Assert.Equal(InterstitialDecisionKind.Suppressed, d.Kind);
                Assert.Equal(InterstitialSuppressReason.Entitlement, d.Reason);
                Assert.Equal(0, p.CompletedGames);
                Assert.False(p.IsPending);
            }
        }

        [Fact]
        public void IN7_EntitlementClearsCounterSoNothingCarriesOver()
        {
            var p = NewPolicy();
            CompleteGames(p, 5);
            var ctx = Break(NaturalBreakKind.Retry, T0);
            ctx.HasNoInterstitialsEntitlement = true;
            Assert.True(p.OnNaturalBreak(ctx).CounterReset);
            Assert.False(p.OnNaturalBreak(ctx).CounterReset);
            // Refund: entitlement gone, counting starts over.
            Assert.Equal(InterstitialDecisionKind.None, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Kind);
            CompleteGames(p, 3);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).IsShow);
        }

        // ---------- IN8 ----------

        [Fact]
        public void IN8_AdLabelIsHalfASecond()
        {
            Assert.Equal(500, InterstitialPolicy.AdLabelDurationMs);
        }

        // ---------- Clock, input validation, config ----------

        [Fact]
        public void ClockRollbackDelaysAtMostOneInterval()
        {
            var p = NewPolicy(c => c.InterstitialEveryNGames = 1);
            p.OnInterstitialShown(T0 + 60 * Min); // stored time lies in the "future" after a clock change
            CompleteGames(p, 1);
            Assert.Equal(InterstitialSuppressReason.MinInterval, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Reason);
            Assert.Equal(T0, p.LastInterstitialShownMs);
            Assert.Equal(InterstitialSuppressReason.MinInterval, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 59 * Sec)).Reason);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 60 * Sec)).IsShow);
        }

        [Fact]
        public void ClockRollbackOnRewardedTimestamp()
        {
            var p = NewPolicy();
            p.OnRewardedShown(T0 + 10 * Min);
            CompleteGames(p, 3);
            Assert.Equal(InterstitialSuppressReason.RewardedQuietPeriod, p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0)).Reason);
            Assert.Equal(T0, p.LastRewardedShownMs);
            Assert.True(p.OnNaturalBreak(Break(NaturalBreakKind.Retry, T0 + 30 * Sec)).IsShow);
        }

        [Fact]
        public void InvalidInputThrows()
        {
            var p = NewPolicy();
            Assert.Throws<ArgumentNullException>(() => new InterstitialPolicy(null));
            Assert.Throws<ArgumentNullException>(() => p.OnNaturalBreak(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.OnNaturalBreak(Break(NaturalBreakKind.Retry, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.OnInterstitialShown(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.OnRewardedShown(-5));
            Assert.Throws<ArgumentNullException>(() => p.Config = null);
            Assert.Throws<ArgumentNullException>(() => InterstitialState.FromJson(null));
        }

        [Fact]
        public void ConfigIsClampedAndCopied()
        {
            var cfg = new AdPolicyConfig { InterstitialEveryNGames = 0 };
            var p = new InterstitialPolicy(cfg);
            Assert.Equal(1, p.Config.InterstitialEveryNGames);
            cfg.InterstitialEveryNGames = 50;
            Assert.Equal(1, p.Config.InterstitialEveryNGames);
            p.Config.InterstitialEveryNGames = 50; // a copy: no effect
            Assert.Equal(1, p.Config.InterstitialEveryNGames);
        }

        [Fact]
        public void RemoteConfigChangeKeepsState()
        {
            var p = NewPolicy();
            CompleteGames(p, 2);
            p.Config = new AdPolicyConfig { InterstitialEveryNGames = 2 };
            Assert.True(p.IsPending);
        }

        [Fact]
        public void ConstructorCopiesStateAndNormalizes()
        {
            var s = new InterstitialState { CompletedGames = -4, TotalCompletedGames = -1, LastInterstitialShownMs = -10, Version = 0 };
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault(), s);
            Assert.Equal(0, p.CompletedGames);
            Assert.Equal(0, p.TotalCompletedGames);
            Assert.Null(p.LastInterstitialShownMs);
            CompleteGames(p, 1);
            Assert.Equal(-4, s.CompletedGames); // caller's object untouched
            var snap = p.SnapshotState();
            snap.CompletedGames = 99;
            Assert.Equal(1, p.CompletedGames);
        }

        [Fact]
        public void CounterSaturatesInsteadOfOverflowing()
        {
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault(),
                new InterstitialState { CompletedGames = int.MaxValue, TotalCompletedGames = long.MaxValue });
            Assert.True(p.OnGameCompleted(Game()));
            Assert.Equal(int.MaxValue, p.CompletedGames);
            Assert.Equal(long.MaxValue, p.TotalCompletedGames);
        }

        [Fact]
        public void DecisionToString()
        {
            Assert.Equal("Show", InterstitialDecision.Show.ToString());
            Assert.Equal("None", InterstitialDecision.None.ToString());
            Assert.Equal("Suppressed(MinInterval)", InterstitialDecision.Suppressed(InterstitialSuppressReason.MinInterval, false).ToString());
        }
    }
}
