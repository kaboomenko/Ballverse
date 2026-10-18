namespace Ballverse.Core.Monetization
{
    /// <summary>How the export sheet presents the "Водяной знак" row (spec §3.10 "Поведение").</summary>
    public enum WatermarkRow
    {
        /// <summary>Kill switch <c>watermark_enabled = false</c>: no watermark for anyone, no row.</summary>
        Hidden = 0,
        /// <summary>No NO_WATERMARK: locked row; a tap opens the Ultimate sheet or, per the smart storefront and the release catalog, the missing part (<see cref="Storefront.WatermarkOffers"/>; release 1: No Ads or the missing ad removal). Export is never blocked.</summary>
        Locked = 1,
        /// <summary>NO_WATERMARK owned: a switch, off (no watermark) by default.</summary>
        Toggle = 2,
    }

    /// <summary>
    /// Watermark decision at export time (spec §3.10, owner decision Р13). Pure. NO_WATERMARK comes from
    /// <see cref="Entitlements.From"/>: Ultimate, the Ultimate contents by parts, or (release 1, and grandfathered after it)
    /// No Ads / both ad removals.
    /// </summary>
    public static class WatermarkPolicy
    {
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
