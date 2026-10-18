using System;
using Ballverse.Core.Ads;
using Xunit;
using static Ballverse.Core.Tests.Ads.AdsTestUtil;

namespace Ballverse.Core.Tests.Ads
{
    public class RewardedExportGateTests
    {
        const string A = "project-a";
        const string B = "project-b";

        static RewardedExportGate NewGate(Action<AdPolicyConfig> tweak = null)
        {
            var c = AdPolicyConfig.CreateDefault();
            tweak?.Invoke(c);
            return new RewardedExportGate(c);
        }

        /// <summary>Decide → offer → reward; returns the gate with A holding a credit.</summary>
        static RewardedGateStep Earn(RewardedExportGate g, string project, long now)
        {
            Assert.True(g.Decide(project, now, false).IsShowOffer);
            return g.OnOfferOutcome(project, RewardedOfferOutcome.UserEarnedReward, now);
        }

        // ---------- entitlement and kill switch ----------

        [Fact]
        public void EntitlementSkipsTheAd()
        {
            var g = NewGate();
            var d = g.Decide(A, T0, hasNoExportAdsEntitlement: true);
            Assert.Equal(ExportGateDecisionKind.ExportWithoutAd, d.Kind);
            Assert.Equal(ExportWithoutAdReason.Entitlement, d.Reason);
            Assert.Null(g.OpenOfferProjectId);
        }

        [Fact]
        public void RW7_KillSwitchSkipsTheAd()
        {
            var g = NewGate(c => c.RewardedExportEnabled = false);
            Assert.Equal(ExportWithoutAdReason.GateDisabled, g.Decide(A, T0, false).Reason);
            Assert.Equal(ExportWithoutAdReason.Entitlement, g.Decide(A, T0, true).Reason);
        }

        [Fact]
        public void RW7_KillSwitchAppliedAtRuntime()
        {
            var g = NewGate();
            Assert.True(g.Decide(A, T0, false).IsShowOffer);
            g.Config = new AdPolicyConfig { RewardedExportEnabled = false };
            Assert.Equal(ExportWithoutAdReason.GateDisabled, g.Decide(A, T0, false).Reason);
            Assert.Null(g.OpenOfferProjectId);
        }

        [Fact]
        public void WithoutCreditShowsOffer()
        {
            var g = NewGate();
            var d = g.Decide(A, T0, false);
            Assert.Equal(ExportGateDecisionKind.ShowOffer, d.Kind);
            Assert.Equal(ExportWithoutAdReason.None, d.Reason);
            Assert.Equal(A, g.OpenOfferProjectId);
        }

        // ---------- RW2, RW5 ----------

        [Fact]
        public void RW2_RewardGivesOneExport_RW5_SuccessConsumesIt()
        {
            var g = NewGate();
            var step = Earn(g, A, T0);
            Assert.Equal(RewardedGateStepKind.StartExport, step.Kind);
            Assert.Equal(ExportFunding.RewardEarned, step.Funding);
            Assert.True(step.RewardedAdWasShown);
            Assert.Null(g.OpenOfferProjectId);
            Assert.True(g.TryGetCredit(A, T0, out var credit));
            Assert.Equal(RewardedCreditSource.Reward, credit.Source);
            Assert.Null(credit.ExpiresAtMs);

            var fin = g.OnExportFinished(A, ExportResult.Succeeded, T0 + 20 * Sec);
            Assert.Equal(ExportFinishKind.CreditConsumed, fin.Kind);
            Assert.False(g.TryGetCredit(A, T0 + 20 * Sec, out _));
            Assert.True(g.Decide(A, T0 + 21 * Sec, false).IsShowOffer); // the next export needs a new view
        }

        [Fact]
        public void RW2_ClosedEarlyShowsOfferAgain()
        {
            var g = NewGate();
            g.Decide(A, T0, false);
            var step = g.OnOfferOutcome(A, RewardedOfferOutcome.AdClosedEarly, T0 + 3 * Sec);
            Assert.Equal(RewardedGateStepKind.ShowOfferAgain, step.Kind);
            Assert.Equal(ExportFunding.None, step.Funding);
            Assert.True(step.RewardedAdWasShown);
            Assert.Equal(A, g.OpenOfferProjectId);
            Assert.False(g.TryGetCredit(A, T0 + 3 * Sec, out _));
            Assert.Equal(ExportFunding.RewardEarned, g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0 + 40 * Sec).Funding);
        }

        [Fact]
        public void RW2_UnusedRewardSurvivesRestart()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g = Restart(g); // app killed before the export reported a result
            Assert.Equal(ExportWithoutAdReason.EarnedCredit, g.Decide(A, T0 + 2 * 60 * Min, false).Reason);
            Assert.Equal(ExportFinishKind.CreditConsumed, g.OnExportFinished(A, ExportResult.Succeeded, T0 + 121 * Min).Kind);
            Assert.True(g.Decide(A, T0 + 122 * Min, false).IsShowOffer);
        }

        // ---------- RW3, Р14 ----------

        [Theory]
        [InlineData(RewardedOfferOutcome.AdLoadTimedOut)]
        [InlineData(RewardedOfferOutcome.AdFailed)]
        [InlineData(RewardedOfferOutcome.Offline)]
        public void RW3_AdUnavailableExportsWithoutAdAndWithoutCredit(RewardedOfferOutcome outcome)
        {
            var g = NewGate();
            g.Decide(A, T0, false);
            var step = g.OnOfferOutcome(A, outcome, T0 + 5 * Sec);
            Assert.Equal(RewardedGateStepKind.StartExport, step.Kind);
            Assert.Equal(ExportFunding.AdUnavailable, step.Funding);
            Assert.False(step.RewardedAdWasShown);
            Assert.Null(g.OpenOfferProjectId);
            Assert.Equal(0, g.CreditCount);
            Assert.Equal(ExportFinishKind.None, g.OnExportFinished(A, ExportResult.Failed, T0 + 10 * Sec).Kind);
            Assert.True(g.Decide(A, T0 + 11 * Sec, false).IsShowOffer);
        }

        [Fact]
        public void RW3_WaitTimeoutExactlyFiveSeconds()
        {
            var g = NewGate();
            Assert.False(g.HasWaitTimedOut(T0, T0));
            Assert.False(g.HasWaitTimedOut(T0, T0 + 5 * Sec - 1));
            Assert.True(g.HasWaitTimedOut(T0, T0 + 5 * Sec));
            Assert.True(g.HasWaitTimedOut(T0, T0 + 6 * Sec));
            Assert.False(g.HasWaitTimedOut(T0, T0 - 1)); // clock went back: keep waiting until the caller's own timeout
        }

        [Fact]
        public void RW3_WaitTimeoutFollowsRemoteConfig()
        {
            var g = NewGate(c => c.RewardedWaitTimeoutSec = 10);
            Assert.False(g.HasWaitTimedOut(T0, T0 + 9999));
            Assert.True(g.HasWaitTimedOut(T0, T0 + 10 * Sec));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.HasWaitTimedOut(-1, T0));
        }

        // ---------- purchase and cancel ----------

        [Fact]
        public void PurchaseChosenThenDecideAgain()
        {
            var g = NewGate();
            g.Decide(A, T0, false);
            var step = g.OnOfferOutcome(A, RewardedOfferOutcome.PurchaseRemoveExportAdsChosen, T0);
            Assert.Equal(RewardedGateStepKind.StartPurchase, step.Kind);
            Assert.Null(g.OpenOfferProjectId);
            // Purchase succeeded:
            Assert.Equal(ExportWithoutAdReason.Entitlement, g.Decide(A, T0 + 30 * Sec, true).Reason);
            // Purchase failed or cancelled: the offer comes back.
            Assert.True(g.Decide(A, T0 + 30 * Sec, false).IsShowOffer);
        }

        [Fact]
        public void CancelAborts()
        {
            var g = NewGate();
            g.Decide(A, T0, false);
            Assert.Equal(RewardedGateStepKind.Abort, g.OnOfferOutcome(A, RewardedOfferOutcome.Cancelled, T0).Kind);
            Assert.Null(g.OpenOfferProjectId);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0));
        }

        [Fact]
        public void OutcomeRequiresOpenOfferForThatProject()
        {
            var g = NewGate();
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0));
            g.Decide(A, T0, false);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(B, RewardedOfferOutcome.UserEarnedReward, T0));
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome("PROJECT-A", RewardedOfferOutcome.UserEarnedReward, T0));
            g.Decide(B, T0, false); // the user moved to another project
            Assert.Equal(B, g.OpenOfferProjectId);
            Assert.Throws<InvalidOperationException>(() => g.OnOfferOutcome(A, RewardedOfferOutcome.UserEarnedReward, T0));
        }

        // ---------- RW4, §3.8: retry credit ----------

        [Theory]
        [InlineData(ExportResult.Failed)]
        [InlineData(ExportResult.Cancelled)]
        public void RW4_RetryWithinTenMinutes_ExactBoundary(ExportResult result)
        {
            var g = NewGate();
            Earn(g, A, T0);
            long failedAt = T0 + 40 * Sec;
            var fin = g.OnExportFinished(A, result, failedAt);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, fin.Kind);
            Assert.Equal(failedAt + 10 * Min, fin.RetryExpiresAtMs);

            var atBoundary = Restart(g);
            Assert.Equal(ExportWithoutAdReason.RetryCredit, atBoundary.Decide(A, failedAt + 10 * Min, false).Reason);

            var after = Restart(g);
            Assert.True(after.Decide(A, failedAt + 10 * Min + 1, false).IsShowOffer);
            Assert.Equal(0, after.CreditCount); // pruned
        }

        [Fact]
        public void RW4_CreditNeverTransfersToAnotherProject()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Failed, T0 + Min);
            Assert.True(g.Decide(B, T0 + 2 * Min, false).IsShowOffer);
            Assert.Equal(ExportFinishKind.None, g.OnExportFinished(B, ExportResult.Succeeded, T0 + 3 * Min).Kind);
            Assert.False(g.TryGetCredit(B, T0 + 3 * Min, out _));
            Assert.Equal(ExportWithoutAdReason.RetryCredit, g.Decide(A, T0 + 4 * Min, false).Reason);
        }

        [Fact]
        public void RW4_ProjectIdsCompareOrdinal()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Failed, T0);
            Assert.True(g.Decide("Project-A", T0, false).IsShowOffer);
            Assert.True(g.Decide(A + " ", T0, false).IsShowOffer);
        }

        [Fact]
        public void RW4_SuccessfulRetryConsumesCredit()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Failed, T0 + Min);
            Assert.Equal(ExportWithoutAdReason.RetryCredit, g.Decide(A, T0 + 5 * Min, false).Reason);
            Assert.Equal(ExportFinishKind.CreditConsumed, g.OnExportFinished(A, ExportResult.Succeeded, T0 + 6 * Min).Kind);
            Assert.True(g.Decide(A, T0 + 7 * Min, false).IsShowOffer);
        }

        [Fact]
        public void RW4_ClaimedRetryCreditOutlivesWindowDuringLongExport()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Failed, T0);
            Assert.Equal(ExportWithoutAdReason.RetryCredit, g.Decide(A, T0 + 10 * Min, false).Reason); // last valid ms
            // A later call for another project must not prune the claimed credit while A exports.
            Assert.True(g.Decide(B, T0 + 12 * Min, false).IsShowOffer);
            var fin = g.OnExportFinished(A, ExportResult.Failed, T0 + 13 * Min);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, fin.Kind);
            Assert.Equal(T0 + 23 * Min, fin.RetryExpiresAtMs); // renewed from the second failure
        }

        [Fact]
        public void RW4_RewardHeldDuringLongExport()
        {
            var g = NewGate();
            Earn(g, A, T0);
            var fin = g.OnExportFinished(A, ExportResult.Failed, T0 + 30 * Min);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, fin.Kind);
            Assert.Equal(T0 + 40 * Min, fin.RetryExpiresAtMs);
        }

        [Fact]
        public void RW4_UnclaimedRetryCreditIsNotExtendedByUnrelatedFailure()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Failed, T0);
            // The user exports A with a fresh entitlement (credit not claimed) and that export fails.
            Assert.Equal(ExportWithoutAdReason.Entitlement, g.Decide(A, T0 + Min, true).Reason);
            Assert.Equal(ExportFinishKind.None, g.OnExportFinished(A, ExportResult.Failed, T0 + 2 * Min).Kind);
            Assert.True(g.TryGetCredit(A, T0 + 2 * Min, out var c));
            Assert.Equal(T0 + 10 * Min, c.ExpiresAtMs);
        }

        [Fact]
        public void RW4_AnySuccessOfTheProjectConsumesItsCredit()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g.OnExportFinished(A, ExportResult.Cancelled, T0);
            g.Decide(A, T0 + Min, true);
            Assert.Equal(ExportFinishKind.CreditConsumed, g.OnExportFinished(A, ExportResult.Succeeded, T0 + 2 * Min).Kind);
            Assert.Equal(0, g.CreditCount);
        }

        [Fact]
        public void RW4_ZeroWindowBurnsTheView()
        {
            var g = NewGate(c => c.RewardedRetryWindowMin = 0);
            Earn(g, A, T0);
            Assert.Equal(ExportFinishKind.CreditBurned, g.OnExportFinished(A, ExportResult.Cancelled, T0 + Sec).Kind);
            Assert.Equal(0, g.CreditCount);
            Assert.True(g.Decide(A, T0 + Sec, false).IsShowOffer);
        }

        [Fact]
        public void RW4_RemoteWindow()
        {
            var g = NewGate(c => c.RewardedRetryWindowMin = 3);
            Earn(g, A, T0);
            Assert.Equal(T0 + 3 * Min, g.OnExportFinished(A, ExportResult.Failed, T0).RetryExpiresAtMs);
        }

        [Fact]
        public void RW4_FullFlowAcrossSessions()
        {
            var g = NewGate();
            Earn(g, A, T0);
            g = Restart(g);
            Assert.Equal(ExportFinishKind.RetryCreditGranted, g.OnExportFinished(A, ExportResult.Failed, T0 + Min).Kind);
            g = Restart(g);
            Assert.Equal(ExportWithoutAdReason.RetryCredit, g.Decide(A, T0 + 5 * Min, false).Reason);
            g = Restart(g);
            Assert.Equal(ExportFinishKind.CreditConsumed, g.OnExportFinished(A, ExportResult.Succeeded, T0 + 30 * Min).Kind);
            g = Restart(g);
            Assert.True(g.Decide(A, T0 + 31 * Min, false).IsShowOffer);
            g = Restart(g);
            Assert.Equal(A, g.OpenOfferProjectId); // an open offer also survives (dialog restored)
            Assert.Equal(RewardedGateStepKind.Abort, g.OnOfferOutcome(A, RewardedOfferOutcome.Cancelled, T0 + 31 * Min).Kind);
        }

        [Fact]
        public void ExpiryNearMaxTimeSaturates()
        {
            var g = NewGate();
            long late = long.MaxValue - 1000;
            Earn(g, A, late);
            Assert.Equal(long.MaxValue, g.OnExportFinished(A, ExportResult.Failed, late).RetryExpiresAtMs);
        }

        [Fact]
        public void CreditsAreSortedSoJsonIsOrderIndependent()
        {
            string[] order1 = { "c", "a", "b" };
            string[] order2 = { "b", "c", "a" };
            Assert.Equal(BuildWithCredits(order1), BuildWithCredits(order2));
        }

        static string BuildWithCredits(string[] projects)
        {
            var g = NewGate();
            foreach (var p in projects)
            {
                Earn(g, p, T0);
                g.OnExportFinished(p, ExportResult.Failed, T0);
            }
            var state = g.SnapshotState();
            Assert.Equal(new[] { "a", "b", "c" }, state.Credits.ConvertAll(c => c.ProjectId));
            return g.SerializeState();
        }

        // ---------- invalid input ----------

        [Fact]
        public void InvalidInputThrows()
        {
            var g = NewGate();
            Assert.Throws<ArgumentNullException>(() => new RewardedExportGate(null));
            Assert.Throws<ArgumentException>(() => g.Decide(null, T0, false));
            Assert.Throws<ArgumentException>(() => g.Decide("", T0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.Decide(A, -1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.OnExportFinished(A, (ExportResult)42, T0));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.OnExportFinished(A, ExportResult.Failed, -1));
            Assert.Throws<ArgumentException>(() => g.TryGetCredit("", T0, out _));
            g.Decide(A, T0, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.OnOfferOutcome(A, (RewardedOfferOutcome)42, T0));
            Assert.Throws<ArgumentNullException>(() => g.Config = null);
            Assert.Throws<ArgumentNullException>(() => RewardedGateState.FromJson(null));
        }

        [Fact]
        public void StepAndDecisionToString()
        {
            Assert.Equal("ShowOffer", ExportGateDecision.ShowOffer.ToString());
            Assert.Equal("ExportWithoutAd(RetryCredit)", ExportGateDecision.ExportWithoutAd(ExportWithoutAdReason.RetryCredit).ToString());
            Assert.Equal("StartExport(AdUnavailable)", new RewardedGateStep(RewardedGateStepKind.StartExport, ExportFunding.AdUnavailable, false).ToString());
            Assert.Equal("RetryCreditGranted(5)", new ExportFinishOutcome(ExportFinishKind.RetryCreditGranted, 5).ToString());
        }
    }
}
