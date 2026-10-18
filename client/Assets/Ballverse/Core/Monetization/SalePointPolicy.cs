using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Whether a paid-mode trial preview may start (spec §5.2 "Пробный просмотр платного режима", question В2).</summary>
    public enum TrialPreviewAccess
    {
        /// <summary>Start the demo: default preset, <c>paid_mode_trial_seconds</c> (30 s), "ПРЕВЬЮ" badge, no settings, no export.</summary>
        Allowed = 0,
        /// <summary>Remote Config <c>paid_mode_trial_enabled = false</c> (spec §9.3).</summary>
        Disabled = 1,
        /// <summary>The mode is playable (free, grandfathered or entitled): open it normally, no demo needed.</summary>
        NotLocked = 2,
        /// <summary>The mode has no product (outside M06–M15), so there is nothing to preview for sale.</summary>
        NotPaid = 3,
        /// <summary>This mode's demo was already used on the caller-supplied day: "1 демо на режим в сутки".</summary>
        UsedToday = 4,
    }

    /// <summary>
    /// Frequency rules of the system-initiated sale points (spec §5.2 "Точки продажи (без давления)", owner decision Р6,
    /// pillar С5). Pure: counters, flags and the day index are persisted and supplied by the caller (no clock in Core).
    /// <para>
    /// User-initiated sale points have no frequency limit and need no rule here: the paid-card sheet
    /// (<see cref="Storefront.ModeSheet"/>, "Всегда"), the export-sheet watermark row (<see cref="Storefront.WatermarkOffers"/>,
    /// "Только по тапу") and the Shop tab (<see cref="Storefront.Build"/>, "Всегда"). The rewarded-gate "$6,99" option belongs
    /// to the ads rules (spec §5.3.1). Startup pop-ups, repeating windows, timers and "last chance" offers do not exist.
    /// </para>
    /// <para>
    /// "После interstitial — Никогда": every system-initiated offer here takes <c>interstitialJustShown</c> and returns
    /// nothing when it is true. The caller sets it when an interstitial closed immediately before this screen.
    /// </para>
    /// </summary>
    public static class SalePointPolicy
    {
        /// <summary>Exports 1 and 2 never show the Done-screen line (spec §5.2 "не в первые 2 экспорта").</summary>
        public const int DoneScreenFirstEligibleExport = 3;

        /// <summary>Minimum distance in exports between two Done-screen lines (spec §5.2 "Не чаще 1 раза в 3 экспорта").</summary>
        public const int DoneScreenMinExportsBetween = 3;

        /// <summary>
        /// Whether the Done screen after an export shows the line "Убрать водяной знак — Ultimate" (spec §5.2, §3.10).
        /// <para>
        /// Interpretation (pinned by tests): <paramref name="completedExports"/> is the 1-based number of the export just
        /// finished (successful exports including this one). The line shows when all hold: the watermark exists
        /// (<paramref name="watermarkEnabled"/>), NO_WATERMARK is not owned, the export number is at least 3, and the line
        /// was never shown or was last shown at least 3 exports earlier (<c>completedExports - lastShownAtExport &gt;= 3</c>).
        /// With the line shown whenever allowed, it appears on exports 3, 6, 9, … A <paramref name="lastShownAtExport"/>
        /// greater than <paramref name="completedExports"/> (inconsistent persisted state) yields a negative distance and
        /// suppresses the line until the distance is 3 again: when in doubt, less pressure (pillar С5).
        /// </para>
        /// <para>
        /// The line's target is <see cref="Storefront.WatermarkOffers"/>, which falls back to the cheaper missing parts when the
        /// smart storefront hides Ultimate. After showing the line the caller persists <paramref name="completedExports"/>
        /// as the new <paramref name="lastShownAtExport"/>.
        /// </para>
        /// </summary>
        /// <param name="completedExports">Number of the export just completed (0 = none yet).</param>
        /// <param name="lastShownAtExport">Export number at which the line was last shown; null = never.</param>
        /// <param name="watermarkEnabled">Remote Config kill switch <c>watermark_enabled</c> (spec §9.3): false means no watermark to remove.</param>
        /// <param name="interstitialJustShown">An interstitial closed right before this screen (spec §5.2 "После interstitial").</param>
        /// <exception cref="ArgumentOutOfRangeException">A negative export number.</exception>
        public static bool ShouldShowDoneScreenLine(int completedExports, int? lastShownAtExport, EntitlementSet entitlements,
            bool watermarkEnabled = true, bool interstitialJustShown = false)
        {
            if (completedExports < 0) throw new ArgumentOutOfRangeException(nameof(completedExports));
            if (lastShownAtExport.HasValue && lastShownAtExport.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(lastShownAtExport));
            if (interstitialJustShown || !watermarkEnabled) return false;
            if (entitlements.Contains(Entitlement.NoWatermark)) return false;
            if (completedExports < DoneScreenFirstEligibleExport) return false;
            if (!lastShownAtExport.HasValue) return true;
            return (long)completedExports - lastShownAtExport.Value >= DoneScreenMinExportsBetween;
        }

        /// <summary>
        /// Whether a demo of <paramref name="mode"/> may start (spec §5.2 "Пробный просмотр платного режима": "По умолчанию
        /// включено (вопрос В2), 1 демо на режим в сутки").
        /// <para>
        /// "В сутки" is read as one demo per mode per day index supplied by the caller (e.g. the local calendar day number);
        /// Core has no clock. A demo is allowed when the mode's last demo day differs from <paramref name="todayIndex"/>,
        /// so a clock moved backwards never locks demos for days. After starting a demo the caller persists
        /// <paramref name="todayIndex"/> as that mode's last demo day. Whether a preview counts as a game for interstitials
        /// is the ads rules' concern (spec §5.3.2), not this module's.
        /// </para>
        /// </summary>
        /// <param name="lastDemoDay">Day index of this mode's last demo; null = never.</param>
        /// <param name="todayIndex">Caller's current day index.</param>
        /// <param name="trialEnabled">Remote Config <c>paid_mode_trial_enabled</c> (spec §9.3, default true).</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        public static TrialPreviewAccess CanStartTrialPreview(ModeCode mode, EntitlementSet entitlements, int? lastDemoDay,
            int todayIndex, bool trialEnabled = true, IEnumerable<ModeCode> grandfathered = null, MonetizationConfig config = null)
        {
            if (ProductCatalog.ForMode(mode) == null) return TrialPreviewAccess.NotPaid;
            if (ModeAvailability.IsAvailable(mode, entitlements, grandfathered, config)) return TrialPreviewAccess.NotLocked;
            if (!trialEnabled) return TrialPreviewAccess.Disabled;
            if (lastDemoDay.HasValue && lastDemoDay.Value == todayIndex) return TrialPreviewAccess.UsedToday;
            return TrialPreviewAccess.Allowed;
        }

        /// <summary>
        /// The offer block of the one-time screen after the first-video quest (spec §5.2 "После выполнения квеста первого
        /// ролика — Один экран с All Modes — Один раз"; spec §4.3 "без давления, кнопка «Позже» равнозначна по размеру"):
        /// All Modes when not yet shown and the smart storefront shows it; otherwise <see cref="FirstVideoQuestScreen.None"/>.
        /// <para>
        /// Scope: only the purchase offer depends on this result. The §4.3 celebration ("короткий праздничный экран") is
        /// outside Core and shown whenever the quest completes, including for All Modes / Ultimate owners and right after an
        /// interstitial (§5.2 "После interstitial — Не предлагается" forbids only the purchase).
        /// </para>
        /// <para>
        /// "Без давления": unlike the paid-card sheet, the offer is not <see cref="StorefrontOffer.Emphasized"/>, and the
        /// offer block carries a «Позже» button of the same size (<see cref="FirstVideoQuestScreen.LaterButtonSameSizeAsOffer"/>).
        /// </para>
        /// <para>
        /// Decision (§4.3 vs §5.2 wording): §4.3 "предложение попробовать платный режим" is read as the §5.2 screen
        /// "Один экран с All Modes", the monetization chapter being authoritative for sale points; "попробовать" is taken as
        /// marketing wording, not as a trial entry point. The trial demo (§5.2 "Пробный просмотр платного режима") stays
        /// reachable from the paid mode card (<see cref="CanStartTrialPreview"/>), and is not added to this screen. Open
        /// owner question: should the post-quest screen also offer a trial demo of a locked paid mode?
        /// </para>
        /// <para>
        /// Decision: when the storefront hides All Modes (owned, nothing new, every mode already available, or only one mode
        /// missing so the single is the cheaper equivalent) the offer is skipped rather than replaced by a single or a trial,
        /// consistent with <see cref="Storefront.ModeSheet"/> and the §5.2 "Один экран с All Modes" wording; a single locked
        /// paid mode remains reachable through its own card. The caller persists <paramref name="alreadyShown"/> = true once
        /// the offer is displayed (whether bought or dismissed with «Позже»); a skipped offer may be persisted as shown too,
        /// so it never appears later. An offer suppressed only by <paramref name="interstitialJustShown"/> is not consumed.
        /// </para>
        /// </summary>
        /// <param name="alreadyShown">Persisted one-shot flag.</param>
        /// <param name="interstitialJustShown">An interstitial closed right before this screen (spec §5.2 "После interstitial").</param>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        public static FirstVideoQuestScreen FirstVideoQuest(bool alreadyShown, IEnumerable<string> ownedProductIds,
            bool interstitialJustShown = false, MonetizationConfig config = null, IEnumerable<ModeCode> grandfathered = null)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            if (alreadyShown || interstitialJustShown) return FirstVideoQuestScreen.None;
            var all = Storefront.Build(ownedProductIds, config, grandfathered).Get(ProductIds.AllModes);
            return all.Visible ? new FirstVideoQuestScreen(all.WithEmphasis(false)) : FirstVideoQuestScreen.None;
        }
    }
}
