using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Spec §5.2 "Точки продажи (без давления)": frequency rules of the system-initiated sale points.</summary>
    public class SalePointPolicyTests
    {
        static readonly EntitlementSet Nothing = EntitlementSet.Empty;
        static readonly EntitlementSet WithWatermark = Entitlements.From(new[] { ProductIds.Ultimate });

        // ---- Done screen: "Не чаще 1 раза в 3 экспорта, не в первые 2 экспорта" ----

        /// <summary>Pinned interpretation: (export number, last shown at) → shown.</summary>
        [Theory]
        [InlineData(0, null, false)]
        [InlineData(1, null, false)]
        [InlineData(2, null, false)]
        [InlineData(3, null, true)]
        [InlineData(7, null, true)]
        [InlineData(4, 3, false)]
        [InlineData(5, 3, false)]
        [InlineData(6, 3, true)]
        [InlineData(9, 3, true)]
        [InlineData(10, 9, false)]
        [InlineData(12, 9, true)]
        [InlineData(3, 5, false)] // inconsistent persisted state: suppressed
        [InlineData(8, 5, true)]
        public void DoneScreenLine_Table(int export, int? lastShown, bool expected)
        {
            Assert.Equal(expected, SalePointPolicy.ShouldShowDoneScreenLine(export, lastShown, Nothing));
        }

        [Fact]
        public void DoneScreenLine_ShownWheneverAllowed_AppearsOnExports3_6_9()
        {
            int? last = null;
            var shown = new List<int>();
            for (int export = 1; export <= 12; export++)
            {
                if (SalePointPolicy.ShouldShowDoneScreenLine(export, last, Nothing))
                {
                    shown.Add(export);
                    last = export;
                }
            }
            Assert.Equal(new[] { 3, 6, 9, 12 }, shown);
        }

        [Fact]
        public void DoneScreenLine_NeverOnTwoOfAnyThreeConsecutiveExports()
        {
            var rng = Rng.FromSeed(0xD0E5CEEUL, 3UL);
            for (int iter = 0; iter < 200; iter++)
            {
                int? last = null;
                var shown = new List<int>();
                for (int export = 1; export <= 40; export++)
                {
                    // The UI may skip an allowed line (e.g. Done screen not reached); the rule must still hold.
                    if (SalePointPolicy.ShouldShowDoneScreenLine(export, last, Nothing) && rng.NextUInt(2) == 0)
                    {
                        shown.Add(export);
                        last = export;
                    }
                }
                Assert.True(shown.Count == 0 || shown[0] >= 3);
                for (int i = 1; i < shown.Count; i++) Assert.True(shown[i] - shown[i - 1] >= 3);
            }
        }

        [Fact]
        public void DoneScreenLine_NotShownWithNoWatermark()
        {
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(3, null, WithWatermark));
            var byParts = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds });
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(3, null, byParts));
            // All Modes alone does not remove the watermark: the line still shows.
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, null, Entitlements.From(new[] { ProductIds.AllModes })));
        }

        [Fact]
        public void DoneScreenLine_NotShownWhenWatermarkKillSwitchOff()
        {
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(3, null, Nothing, watermarkEnabled: false));
        }

        [Fact]
        public void DoneScreenLine_NeverRightAfterInterstitial()
        {
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(3, null, Nothing, interstitialJustShown: true));
        }

        [Fact]
        public void DoneScreenLine_LargeCountersDoNotOverflow()
        {
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(int.MaxValue, 0, Nothing));
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(0, int.MaxValue, Nothing));
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(int.MaxValue, int.MaxValue - 2, Nothing));
        }

        [Fact]
        public void DoneScreenLine_NegativeInputThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SalePointPolicy.ShouldShowDoneScreenLine(-1, null, Nothing));
            Assert.Throws<ArgumentOutOfRangeException>(() => SalePointPolicy.ShouldShowDoneScreenLine(5, -1, Nothing));
        }

        [Fact]
        public void DoneScreenLine_TargetIsCheaperPartsWhenUltimateHidden()
        {
            // 9 singles: Ultimate hidden as CheaperViaParts; the line opens the missing parts instead (spec §5.2).
            var owned = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode && p.Mode != ModeCode.M15)
                .Select(p => p.Id).ToArray();
            var e = Entitlements.From(owned);
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, null, e));
            var target = Storefront.WatermarkOffers(owned).Select(o => o.ProductId).ToArray();
            Assert.DoesNotContain(ProductIds.Ultimate, target);
            Assert.Contains(Entitlement.NoWatermark, Entitlements.From(owned.Concat(target)).ToArray());
        }

        // ---- Trial preview: "По умолчанию включено (вопрос В2), 1 демо на режим в сутки" ----

        [Fact]
        public void TrialPreview_LockedPaidMode_AllowedOncePerDayPerMode()
        {
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 100));
            Assert.Equal(TrialPreviewAccess.UsedToday, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 100, 100));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 99, 100));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 100, 101));
        }

        [Fact]
        public void TrialPreview_DemoDaysAreTrackedPerMode()
        {
            var lastDemoDay = new Dictionary<ModeCode, int> { { ModeCode.M08, 7 } };
            int? Last(ModeCode m) => lastDemoDay.TryGetValue(m, out var d) ? d : (int?)null;
            Assert.Equal(TrialPreviewAccess.UsedToday, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, Last(ModeCode.M08), 7));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M09, Nothing, Last(ModeCode.M09), 7));
        }

        [Fact]
        public void TrialPreview_ClockMovedBackDoesNotLock()
        {
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 120, 100));
        }

        [Fact]
        public void TrialPreview_DisabledByRemoteConfig()
        {
            Assert.Equal(TrialPreviewAccess.Disabled,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 100, trialEnabled: false));
        }

        [Fact]
        public void TrialPreview_PlayableModesNeedNoDemo()
        {
            var entitled = Entitlements.From(new[] { ProductIds.ModeColorWar });
            Assert.Equal(TrialPreviewAccess.NotLocked, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, entitled, null, 1));
            Assert.Equal(TrialPreviewAccess.NotLocked,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 1, true, new[] { ModeCode.M08 }));
            Assert.Equal(TrialPreviewAccess.NotLocked,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 1, true, null, new MonetizationConfig(new[] { ModeCode.M08 })));
            Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview(ModeCode.M01, Nothing, null, 1));
        }

        [Fact]
        public void TrialPreview_EveryPaidModeAllowedWhenLocked()
        {
            for (int m = 6; m <= 15; m++)
            {
                Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview((ModeCode)m, Nothing, null, 0));
            }
            for (int m = 1; m <= 5; m++)
            {
                Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview((ModeCode)m, Nothing, null, 0));
            }
        }

        // ---- First-video quest: "Один экран с All Modes — Один раз" ----

        [Fact]
        public void FirstVideoQuest_ShowsEmphasizedAllModesOnce()
        {
            var offers = SalePointPolicy.FirstVideoQuestOffers(false, new string[0]);
            Assert.Single(offers);
            Assert.Equal(ProductIds.AllModes, offers[0].ProductId);
            Assert.True(offers[0].Emphasized);
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(true, new string[0]));
        }

        [Fact]
        public void FirstVideoQuest_PartialOwnerSeesModeCount()
        {
            var offers = SalePointPolicy.FirstVideoQuestOffers(false, new[] { ProductIds.ModeColorWar });
            Assert.Single(offers);
            Assert.Equal(1, offers[0].OwnedModeCount);
            Assert.True(offers[0].PartiallyOwned);
        }

        [Fact]
        public void FirstVideoQuest_SkippedWhenStorefrontHidesAllModes()
        {
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(false, new[] { ProductIds.AllModes }));
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(false, new[] { ProductIds.Ultimate }));
            var nineSingles = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode && p.Mode != ModeCode.M15)
                .Select(p => p.Id).ToArray();
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(false, nineSingles));
            var allFree = new MonetizationConfig(Enumerable.Range(6, 10).Select(m => (ModeCode)m));
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(false, new string[0], false, allFree));
        }

        [Fact]
        public void FirstVideoQuest_NeverRightAfterInterstitial()
        {
            Assert.Empty(SalePointPolicy.FirstVideoQuestOffers(false, new string[0], interstitialJustShown: true));
        }

        [Fact]
        public void FirstVideoQuest_NullOwnedThrows()
        {
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuestOffers(false, null));
        }
    }
}
