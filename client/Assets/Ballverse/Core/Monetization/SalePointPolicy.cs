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
        /// <summary>The mode does not ship in this release (<see cref="MonetizationConfig.ReleasedModes"/>; release 1: M06–M15).</summary>
        NotReleased = 5,
        /// <summary>No sellable product unlocks the mode (<see cref="MonetizationConfig.IsModeSold"/>): a demo would sell nothing.</summary>
        NotSold = 6,
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
    /// <para>
    /// Release catalog: offers come from <see cref="Storefront"/>, so only <see cref="MonetizationConfig.SellableProductIds"/>
    /// are ever offered. With <see cref="MonetizationConfig.Release1"/> the first-video quest offer never appears (All Modes
    /// is not sold), trial demos never start (no paid mode ships) and the Done-screen line leads to No Ads or the missing ad
    /// removal (<see cref="DoneScreenLineOffers"/>).
    /// </para>
    /// </summary>
    public static class SalePointPolicy
    {
        /// <summary>Exports 1 and 2 never show the Done-screen line (spec §5.2 "не в первые 2 экспорта").</summary>
        public const int DoneScreenFirstEligibleExport = 3;

        /// <summary>Minimum distance in exports between two Done-screen lines (spec §5.2 "Не чаще 1 раза в 3 экспорта").</summary>
        public const int DoneScreenMinExportsBetween = 3;

        /// <summary>
        /// Whether the Done screen after an export may show the "Убрать водяной знак — …" line (spec §5.2, §3.10); the
        /// product it names comes from <see cref="DoneScreenLineOffers"/> (release 1: No Ads or the missing ad removal;
        /// Ultimate only where it is sold).
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
        /// smart storefront hides Ultimate. This check does not know the release catalog; prefer
        /// <see cref="DoneScreenLineOffers"/>, which also suppresses the line when nothing sellable removes the watermark. After showing the line the caller persists <paramref name="completedExports"/>
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
        /// The Done-screen line with its target (spec §5.2 "Экран «Готово» после экспорта", §3.10), limited to sellable
        /// products: empty when the line must not show (<see cref="ShouldShowDoneScreenLine"/>) or when no sellable product
        /// can remove the watermark; otherwise the offers of <see cref="Storefront.WatermarkOffers"/>, reordered so that the
        /// products removing the watermark on their own come cheapest first (base USD price, ties in display order); parts
        /// that only remove it together keep display order. The spec wording "Убрать водяной знак — Ultimate" names Ultimate,
        /// but the line must name the first returned offer: the cheapest way to remove the watermark (release 1: No Ads, or
        /// the missing ad removal as the last step; a later release: the $5.99 missing ad removal before Ultimate for a
        /// grandfathered user), never an unsold product. The caller persists
        /// <paramref name="completedExports"/> as the new <paramref name="lastShownAtExport"/> only when the result is not empty.
        /// </summary>
        /// <param name="config">Release catalog and rules (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        /// <param name="watermarkGrandfathered">Persisted <see cref="WatermarkGrandfathering"/> flag.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ownedProductIds"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A negative export number.</exception>
        public static IReadOnlyList<StorefrontOffer> DoneScreenLineOffers(int completedExports, int? lastShownAtExport,
            IEnumerable<string> ownedProductIds, MonetizationConfig config, IEnumerable<ModeCode> grandfathered,
            bool watermarkGrandfathered, bool watermarkEnabled = true, bool interstitialJustShown = false)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            if (config == null) throw new ArgumentNullException(nameof(config));
            var owned = new List<string>(ownedProductIds);
            var entitlements = Entitlements.From(owned, config, watermarkGrandfathered);
            if (!ShouldShowDoneScreenLine(completedExports, lastShownAtExport, entitlements, watermarkEnabled, interstitialJustShown))
                return Array.Empty<StorefrontOffer>();
            var offers = Storefront.WatermarkOffers(owned, config, grandfathered, watermarkGrandfathered);
            var standalone = new List<StorefrontOffer>();
            var parts = new List<StorefrontOffer>();
            foreach (var o in offers) (o.RemovesWatermark ? standalone : parts).Add(o);
            // Stable insertion sort by price: ties keep display order (List.Sort is not stable).
            for (int i = 1; i < standalone.Count; i++)
            {
                var o = standalone[i];
                int j = i - 1;
                while (j >= 0 && standalone[j].Product.BasePriceUsdCents > o.Product.BasePriceUsdCents)
                {
                    standalone[j + 1] = standalone[j];
                    j--;
                }
                standalone[j + 1] = o;
            }
            standalone.AddRange(parts);
            return standalone;
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
        /// <para>
        /// Precedence: NotPaid, NotReleased, NotLocked, NotSold, Disabled, UsedToday, Allowed. Release 1 ships no paid mode,
        /// so every paid mode reports <see cref="TrialPreviewAccess.NotReleased"/>.
        /// </para>
        /// </summary>
        /// <param name="lastDemoDay">Day index of this mode's last demo; null = never.</param>
        /// <param name="todayIndex">Caller's current day index.</param>
        /// <param name="trialEnabled">Remote Config <c>paid_mode_trial_enabled</c> (spec §9.3, default true).</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        /// <param name="config">Release catalog and rules (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        public static TrialPreviewAccess CanStartTrialPreview(ModeCode mode, EntitlementSet entitlements, int? lastDemoDay,
            int todayIndex, MonetizationConfig config, bool trialEnabled = true, IEnumerable<ModeCode> grandfathered = null)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (ProductCatalog.ForMode(mode) == null) return TrialPreviewAccess.NotPaid;
            if (!config.IsReleased(mode)) return TrialPreviewAccess.NotReleased;
            if (ModeAvailability.IsAvailable(mode, entitlements, grandfathered, config)) return TrialPreviewAccess.NotLocked;
            if (!config.IsModeSold(mode)) return TrialPreviewAccess.NotSold;
            if (!trialEnabled) return TrialPreviewAccess.Disabled;
            if (lastDemoDay.HasValue && lastDemoDay.Value == todayIndex) return TrialPreviewAccess.UsedToday;
            return TrialPreviewAccess.Allowed;
        }

        /// <summary>
        /// The offer block of the one-time screen after the first-video quest (spec §5.2 "После выполнения квеста первого
        /// ролика — Один экран с All Modes — Один раз"; spec §4.3 "без давления, кнопка «Позже» равнозначна по размеру"):
        /// All Modes when not yet shown and the smart storefront shows it (so never when All Modes is not sellable, e.g.
        /// release 1); otherwise <see cref="FirstVideoQuestScreen.None"/>.
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
        /// <param name="config">Release catalog and rules (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        /// <param name="grandfathered">Persisted <c>grandfathered_modes</c> (spec §5.1); null means none.</param>
        /// <param name="watermarkGrandfathered">Persisted <see cref="WatermarkGrandfathering"/> flag.</param>
        public static FirstVideoQuestScreen FirstVideoQuest(bool alreadyShown, IEnumerable<string> ownedProductIds,
            MonetizationConfig config, bool interstitialJustShown, IEnumerable<ModeCode> grandfathered,
            bool watermarkGrandfathered)
        {
            if (ownedProductIds == null) throw new ArgumentNullException(nameof(ownedProductIds));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (alreadyShown || interstitialJustShown) return FirstVideoQuestScreen.None;
            var all = Storefront.Build(ownedProductIds, config, grandfathered, watermarkGrandfathered).Get(ProductIds.AllModes);
            return all.Visible ? new FirstVideoQuestScreen(all.WithEmphasis(false)) : FirstVideoQuestScreen.None;
        }
    }
}
