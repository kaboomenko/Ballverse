namespace Ballverse.Core.Monetization
{
    /// <summary>How the export sheet presents the "Водяной знак" row (spec §3.10 "Поведение").</summary>
    public enum WatermarkRow
    {
        /// <summary>Kill switch <c>watermark_enabled = false</c>: no watermark for anyone, no row.</summary>
        Hidden = 0,
        /// <summary>No NO_WATERMARK: locked row; a tap opens the No Ads sheet or the missing ad removal (after release 1 also Ultimate where sold), per the smart storefront and the release catalog (<see cref="Storefront.WatermarkOffers"/>, spec §3.10, §5.0.3). Export is never blocked.</summary>
        Locked = 1,
        /// <summary>NO_WATERMARK owned: a switch, off (no watermark) by default.</summary>
        Toggle = 2,
    }

    /// <summary>
    /// Watermark decision at export time (spec §3.10, owner decisions Р13, Р21). Pure. NO_WATERMARK comes from
    /// <see cref="Entitlements.From"/> by the permanent rule of spec §5.0.2: Ultimate, or NO_EXPORT_ADS + NO_INTERSTITIALS
    /// from any products (No Ads Bundle or both ad removals).
    /// </summary>
    public static class WatermarkPolicy
    {
        /// <summary>The export-sheet "Водяной знак" row for these entitlements (spec §3.10 "Поведение").</summary>
        /// <param name="watermarkEnabled">Remote Config kill switch <c>watermark_enabled</c> (spec §9.3).</param>
        public static WatermarkRow GetRow(EntitlementSet entitlements, bool watermarkEnabled = true)
        {
            if (!watermarkEnabled) return WatermarkRow.Hidden;
            return entitlements.Contains(Entitlement.NoWatermark) ? WatermarkRow.Toggle : WatermarkRow.Locked;
        }

        /// <summary>
        /// Whether the watermark is drawn in preview and export. Checked at export time, so a refund (spec §5.4) brings it
        /// back for the next exports; never baked into the project.
        /// </summary>
        /// <param name="userWantsWatermark">The NO_WATERMARK owner's switch; null = default (no watermark). Ignored without the right.</param>
        /// <param name="watermarkEnabled">Remote Config kill switch <c>watermark_enabled</c> (spec §9.3).</param>
        public static bool ShouldApply(EntitlementSet entitlements, bool? userWantsWatermark = null, bool watermarkEnabled = true)
        {
            if (!watermarkEnabled) return false;
            if (!entitlements.Contains(Entitlement.NoWatermark)) return true;
            return userWantsWatermark ?? false;
        }
    }
}
