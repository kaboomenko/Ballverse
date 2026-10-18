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
        static readonly EntitlementSet WithWatermark = Entitlements.From(new[] { ProductIds.Ultimate }, MonetizationConfig.FullCatalog);

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

        /// <summary>
        /// Boundary of "не в первые 2 экспорта": exports 1–2 never show the line, export 3 is the first that may, whatever the
        /// persisted state; kills mutants of the first-eligible constant or comparison (2 or 4 instead of 3, &lt;= for &lt;).
        /// </summary>
        [Fact]
        public void DoneScreenLine_FirstEligibleIsExactlyExport3()
        {
            Assert.Equal(3, SalePointPolicy.DoneScreenFirstEligibleExport);
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(1, null, Nothing));
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(2, null, Nothing));
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, null, Nothing));
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(4, null, Nothing));
            // A persisted "shown at export 0" (never really shown) must not unlock exports 1–2 either.
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(2, 0, Nothing));
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, 0, Nothing));
        }

        /// <summary>
        /// Boundary of "не чаще 1 раза в 3 экспорта" for every last-shown export 3…30: distances 0, 1, 2 hide, 3 and more
        /// show. Kills mutants of the cadence constant or comparison (2 or 4 instead of 3, &gt; for &gt;=, == for &gt;=).
        /// </summary>
        [Fact]
        public void DoneScreenLine_MinDistanceIsExactly3Exports()
        {
            Assert.Equal(3, SalePointPolicy.DoneScreenMinExportsBetween);
            for (int last = 3; last <= 30; last++)
            {
                Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(last, last, Nothing));
                Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(last + 1, last, Nothing));
                Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(last + 2, last, Nothing));
                Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(last + 3, last, Nothing));
                Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(last + 4, last, Nothing));
                Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(last + 10, last, Nothing));
            }
        }

        /// <summary>
        /// A last-shown export ahead of the current one (inconsistent persisted state) suppresses the line for any gap; kills
        /// an absolute-distance mutant (|completed - last| &gt;= 3), which would show it when the gap is 3 or more.
        /// </summary>
        [Theory]
        [InlineData(3, 4)]
        [InlineData(3, 6)]
        [InlineData(5, 9)]
        [InlineData(10, 20)]
        [InlineData(3, 1000)]
        public void DoneScreenLine_LastShownInFutureSuppresses(int export, int lastShown)
        {
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(export, lastShown, Nothing));
        }

        /// <summary>The cadence is measured from the last export that actually showed the line, not from the last eligible one.</summary>
        [Fact]
        public void DoneScreenLine_SkippedEligibleExportDoesNotResetCadence()
        {
            // Eligible on 3 but the Done screen was not reached: the caller does not persist, so export 4 still shows.
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(4, null, Nothing));
            // Shown on 4: next on 7, not 6.
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(6, 4, Nothing));
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(7, 4, Nothing));
        }

        /// <summary>Exhaustive oracle over exports 0…40 and last-shown null/0…40.</summary>
        [Fact]
        public void DoneScreenLine_ExhaustiveOracle()
        {
            for (int export = 0; export <= 40; export++)
            {
                Assert.Equal(export >= 3, SalePointPolicy.ShouldShowDoneScreenLine(export, null, Nothing));
                for (int last = 0; last <= 40; last++)
                {
                    bool expected = export >= 3 && export - last >= 3;
                    Assert.Equal(expected, SalePointPolicy.ShouldShowDoneScreenLine(export, last, Nothing));
                }
            }
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
            var byParts = Entitlements.From(new[] { ProductIds.AllModes, ProductIds.NoAds }, MonetizationConfig.FullCatalog);
            Assert.False(SalePointPolicy.ShouldShowDoneScreenLine(3, null, byParts));
            // All Modes alone does not remove the watermark: the line still shows.
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, null, Entitlements.From(new[] { ProductIds.AllModes }, MonetizationConfig.FullCatalog)));
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
            var e = Entitlements.From(owned, MonetizationConfig.FullCatalog);
            Assert.True(SalePointPolicy.ShouldShowDoneScreenLine(3, null, e));
            var target = Storefront.WatermarkOffers(owned, MonetizationConfig.FullCatalog, null).Select(o => o.ProductId).ToArray();
            Assert.DoesNotContain(ProductIds.Ultimate, target);
            Assert.Contains(Entitlement.NoWatermark, Entitlements.From(owned.Concat(target), MonetizationConfig.FullCatalog).ToArray());
        }

        // ---- Trial preview: "По умолчанию включено (вопрос В2), 1 демо на режим в сутки" ----

        [Fact]
        public void TrialPreview_LockedPaidMode_AllowedOncePerDayPerMode()
        {
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 100, MonetizationConfig.FullCatalog, null));
            Assert.Equal(TrialPreviewAccess.UsedToday, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 100, 100, MonetizationConfig.FullCatalog, null));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 99, 100, MonetizationConfig.FullCatalog, null));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 100, 101, MonetizationConfig.FullCatalog, null));
        }

        /// <summary>
        /// "1 демо на режим в сутки" for every paid mode and day index around a used day: only the same day is refused.
        /// Kills mutants comparing with &lt;=, &gt;=, or todayIndex ± 1, or ignoring the day.
        /// </summary>
        [Fact]
        public void TrialPreview_OnlySameDayIsRefused_AllModesAndNeighbourDays()
        {
            for (int m = 6; m <= 15; m++)
            {
                var mode = (ModeCode)m;
                foreach (int today in new[] { int.MinValue, -1, 0, 1, 365, int.MaxValue })
                {
                    Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(mode, Nothing, null, today, MonetizationConfig.FullCatalog, null));
                    Assert.Equal(TrialPreviewAccess.UsedToday, SalePointPolicy.CanStartTrialPreview(mode, Nothing, today, today, MonetizationConfig.FullCatalog, null));
                    if (today != int.MinValue)
                        Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(mode, Nothing, today - 1, today, MonetizationConfig.FullCatalog, null));
                    if (today != int.MaxValue)
                        Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(mode, Nothing, today + 1, today, MonetizationConfig.FullCatalog, null));
                }
            }
        }

        /// <summary>A simulated month: the caller persists the day after each demo; each mode gets at most one demo per day.</summary>
        [Fact]
        public void TrialPreview_Simulation_AtMostOneDemoPerModePerDay()
        {
            var rng = Rng.FromSeed(0x7121A1UL, 9UL);
            var lastDemoDay = new Dictionary<ModeCode, int>();
            var demos = new Dictionary<(ModeCode, int), int>();
            int allowedCount = 0;
            for (int day = 0; day < 30; day++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    var mode = (ModeCode)(6 + (int)rng.NextUInt(10));
                    int? last = lastDemoDay.TryGetValue(mode, out var d) ? d : (int?)null;
                    var access = SalePointPolicy.CanStartTrialPreview(mode, Nothing, last, day, MonetizationConfig.FullCatalog, null);
                    bool alreadyToday = demos.ContainsKey((mode, day));
                    Assert.Equal(alreadyToday ? TrialPreviewAccess.UsedToday : TrialPreviewAccess.Allowed, access);
                    if (access == TrialPreviewAccess.Allowed)
                    {
                        demos[(mode, day)] = 1;
                        lastDemoDay[mode] = day;
                        allowedCount++;
                    }
                }
            }
            Assert.Equal(demos.Count, allowedCount);
        }

        /// <summary>
        /// Precedence of the results is pinned: NotPaid, then NotLocked, then Disabled, then UsedToday. Kills mutants that
        /// reorder the checks (e.g. reporting UsedToday for a playable mode, or Disabled for a free mode).
        /// </summary>
        [Fact]
        public void TrialPreview_ResultPrecedence()
        {
            var entitled = Entitlements.From(new[] { ProductIds.ModeColorWar }, MonetizationConfig.FullCatalog);
            // NotPaid wins over everything.
            Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview(ModeCode.M01, Nothing, 5, 5, MonetizationConfig.FullCatalog, null, trialEnabled: false));
            // NotLocked wins over Disabled and UsedToday.
            Assert.Equal(TrialPreviewAccess.NotLocked, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, entitled, 5, 5, MonetizationConfig.FullCatalog, null, trialEnabled: false));
            Assert.Equal(TrialPreviewAccess.NotLocked, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, entitled, 5, 5, MonetizationConfig.FullCatalog, null));
            // Disabled wins over UsedToday.
            Assert.Equal(TrialPreviewAccess.Disabled, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 5, 5, MonetizationConfig.FullCatalog, null, trialEnabled: false));
            Assert.Equal(TrialPreviewAccess.Disabled, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 4, 5, MonetizationConfig.FullCatalog, null, trialEnabled: false));
        }

        [Fact]
        public void TrialPreview_DemoDaysAreTrackedPerMode()
        {
            var lastDemoDay = new Dictionary<ModeCode, int> { { ModeCode.M08, 7 } };
            int? Last(ModeCode m) => lastDemoDay.TryGetValue(m, out var d) ? d : (int?)null;
            Assert.Equal(TrialPreviewAccess.UsedToday, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, Last(ModeCode.M08), 7, MonetizationConfig.FullCatalog, null));
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M09, Nothing, Last(ModeCode.M09), 7, MonetizationConfig.FullCatalog, null));
        }

        [Fact]
        public void TrialPreview_ClockMovedBackDoesNotLock()
        {
            Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, 120, 100, MonetizationConfig.FullCatalog, null));
        }

        [Fact]
        public void TrialPreview_DisabledByRemoteConfig()
        {
            Assert.Equal(TrialPreviewAccess.Disabled,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 100, MonetizationConfig.FullCatalog, null, trialEnabled: false));
        }

        [Fact]
        public void TrialPreview_PlayableModesNeedNoDemo()
        {
            var entitled = Entitlements.From(new[] { ProductIds.ModeColorWar }, MonetizationConfig.FullCatalog);
            Assert.Equal(TrialPreviewAccess.NotLocked, SalePointPolicy.CanStartTrialPreview(ModeCode.M08, entitled, null, 1, MonetizationConfig.FullCatalog, null));
            Assert.Equal(TrialPreviewAccess.NotLocked,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 1, MonetizationConfig.FullCatalog, new[] { ModeCode.M08 }, true));
            Assert.Equal(TrialPreviewAccess.NotLocked,
                SalePointPolicy.CanStartTrialPreview(ModeCode.M08, Nothing, null, 1, new MonetizationConfig(new[] { ModeCode.M08 }), null, true));
            Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview(ModeCode.M01, Nothing, null, 1, MonetizationConfig.FullCatalog, null));
        }

        [Fact]
        public void TrialPreview_EveryPaidModeAllowedWhenLocked()
        {
            for (int m = 6; m <= 15; m++)
            {
                Assert.Equal(TrialPreviewAccess.Allowed, SalePointPolicy.CanStartTrialPreview((ModeCode)m, Nothing, null, 0, MonetizationConfig.FullCatalog, null));
            }
            for (int m = 1; m <= 5; m++)
            {
                Assert.Equal(TrialPreviewAccess.NotPaid, SalePointPolicy.CanStartTrialPreview((ModeCode)m, Nothing, null, 0, MonetizationConfig.FullCatalog, null));
            }
        }

        // ---- First-video quest: "Один экран с All Modes — Один раз"; §4.3 "без давления, «Позже» равнозначна" ----

        static FirstVideoQuestScreen Quest(bool alreadyShown, IEnumerable<string> owned, bool interstitial = false,
            MonetizationConfig config = null, IEnumerable<ModeCode> grandfathered = null)
        {
            return SalePointPolicy.FirstVideoQuest(alreadyShown, owned, config ?? MonetizationConfig.FullCatalog, interstitial, grandfathered);
        }

        [Fact]
        public void FirstVideoQuest_ShowsAllModesOnce()
        {
            var screen = Quest(false, new string[0]);
            Assert.True(screen.ShowOffer);
            Assert.Equal(ProductIds.AllModes, screen.Offer.ProductId);
            Assert.Single(screen.Offers);
            Assert.Same(screen.Offer, screen.Offers[0]);
            Assert.False(Quest(true, new string[0]).ShowOffer);
        }

        [Fact]
        public void FirstVideoQuest_OfferIsNotEmphasized()
        {
            // §4.3 "без давления": no visual priority, unlike the paid-card sheet where All Modes is emphasized.
            Assert.False(Quest(false, new string[0]).Offer.Emphasized);
            Assert.False(Quest(false, new[] { ProductIds.ModeColorWar }).Offer.Emphasized);
            Assert.True(Storefront.ModeSheet(ModeCode.M06, new string[0], MonetizationConfig.FullCatalog, null).Single(o => o.ProductId == ProductIds.AllModes).Emphasized);
        }

        [Fact]
        public void FirstVideoQuest_LaterButtonSameSizeAsOffer()
        {
            Assert.True(Quest(false, new string[0]).LaterButtonSameSizeAsOffer);
            Assert.True(Quest(false, new[] { ProductIds.ModeColorWar }).LaterButtonSameSizeAsOffer);
        }

        /// <summary>
        /// The internal guard is the last line of defence against §4.3 "давление": even if the storefront started to emphasize
        /// All Modes or the policy stopped clearing the flag, an emphasized offer can never reach the quest screen.
        /// </summary>
        [Fact]
        public void FirstVideoQuestScreen_RejectsEmphasizedOffer()
        {
            var offer = Storefront.Build(new string[0], MonetizationConfig.FullCatalog, null).Get(ProductIds.AllModes);
            Assert.Throws<ArgumentException>(() => new FirstVideoQuestScreen(offer.WithEmphasis(true)));
            var screen = new FirstVideoQuestScreen(offer.WithEmphasis(false));
            Assert.True(screen.ShowOffer);
            Assert.False(screen.Offer.Emphasized);
        }

        [Fact]
        public void FirstVideoQuest_NoneHasNoOfferAndNoButtons()
        {
            var none = FirstVideoQuestScreen.None;
            Assert.False(none.ShowOffer);
            Assert.Null(none.Offer);
            Assert.Empty(none.Offers);
            Assert.False(none.LaterButtonSameSizeAsOffer);
            Assert.Same(none, Quest(true, new string[0]));
        }

        [Fact]
        public void FirstVideoQuest_OffersListIsReadOnly()
        {
            var offers = Quest(false, new string[0]).Offers;
            Assert.False(offers is StorefrontOffer[]);
            Assert.True(((ICollection<StorefrontOffer>)offers).IsReadOnly);
        }

        [Fact]
        public void FirstVideoQuest_PartialOwnerSeesModeCount()
        {
            var screen = Quest(false, new[] { ProductIds.ModeColorWar });
            Assert.True(screen.ShowOffer);
            Assert.Equal(1, screen.Offer.OwnedModeCount);
            Assert.True(screen.Offer.PartiallyOwned);
        }

        [Fact]
        public void FirstVideoQuest_SkippedWhenStorefrontHidesAllModes()
        {
            Assert.False(Quest(false, new[] { ProductIds.AllModes }).ShowOffer);
            Assert.False(Quest(false, new[] { ProductIds.Ultimate }).ShowOffer);
            var nineSingles = ProductCatalog.All.Where(p => p.Kind == ProductKind.SingleMode && p.Mode != ModeCode.M15)
                .Select(p => p.Id).ToArray();
            Assert.False(Quest(false, nineSingles).ShowOffer);
            var allFree = new MonetizationConfig(Enumerable.Range(6, 10).Select(m => (ModeCode)m));
            Assert.False(Quest(false, new string[0], false, allFree).ShowOffer);
            var allGrandfathered = Enumerable.Range(6, 10).Select(m => (ModeCode)m).ToArray();
            Assert.False(Quest(false, new string[0], false, null, allGrandfathered).ShowOffer);
        }

        [Fact]
        public void FirstVideoQuest_NeverRightAfterInterstitial()
        {
            Assert.False(Quest(false, new string[0], interstitial: true).ShowOffer);
            Assert.False(Quest(true, new string[0], interstitial: true).ShowOffer);
        }

        /// <summary>
        /// Kills mutants of the one-shot flag: for every owned-product combination, an already shown screen (or one right after
        /// an interstitial) is never shown again, and an unshown one equals the smart storefront's All Modes offer.
        /// </summary>
        [Fact]
        public void FirstVideoQuest_Exhaustive_OneShotAndMatchesStorefront()
        {
            for (int mask = 0; mask < Subsets.Count; mask++)
            {
                var owned = Subsets.Ids(mask);
                Assert.False(Quest(true, owned).ShowOffer);
                Assert.False(Quest(true, owned, interstitial: true).ShowOffer);
                Assert.False(Quest(false, owned, interstitial: true).ShowOffer);
                if ((mask & 0x3F) != 0) continue; // the storefront comparison below on 1/64 of the subsets keeps this fast
                var screen = Quest(false, owned);
                var all = Storefront.Build(owned, MonetizationConfig.FullCatalog, null).Get(ProductIds.AllModes);
                Assert.Equal(all.Visible, screen.ShowOffer);
                if (screen.ShowOffer)
                {
                    Assert.False(screen.Offer.Emphasized);
                    Assert.Equal(all.OwnedModeCount, screen.Offer.OwnedModeCount);
                    Assert.Equal(all.Gain.ToArray(), screen.Offer.Gain.ToArray());
                }
            }
        }

        /// <summary>A simulated app lifetime: the screen appears exactly once, on the first opportunity not right after an interstitial.</summary>
        [Fact]
        public void FirstVideoQuest_LifetimeSimulation_ShownExactlyOnce()
        {
            var rng = Rng.FromSeed(0x9E57UL, 5UL);
            for (int iter = 0; iter < 100; iter++)
            {
                bool shownFlag = false;
                int shownCount = 0;
                int firstEligible = -1;
                int shownAt = -1;
                for (int step = 0; step < 20; step++)
                {
                    bool interstitial = rng.NextUInt(3) == 0;
                    if (!interstitial && firstEligible < 0) firstEligible = step;
                    var screen = Quest(shownFlag, new string[0], interstitial);
                    if (screen.ShowOffer)
                    {
                        shownCount++;
                        shownAt = step;
                        shownFlag = true;
                    }
                }
                if (firstEligible < 0)
                {
                    Assert.Equal(0, shownCount);
                }
                else
                {
                    Assert.Equal(1, shownCount);
                    Assert.Equal(firstEligible, shownAt);
                }
            }
        }

        [Fact]
        public void FirstVideoQuest_NullOwnedThrows()
        {
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(false, null, MonetizationConfig.FullCatalog, false, null));
            Assert.Throws<ArgumentNullException>(() => SalePointPolicy.FirstVideoQuest(true, null, MonetizationConfig.FullCatalog, false, null));
        }
    }
}
