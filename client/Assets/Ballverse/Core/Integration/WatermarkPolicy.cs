using System;
using Ballverse.Core.Config;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Whether the compositor draws the "Made with Ballverse" watermark (spec §3.10, owner decisions Р13, Р21), for both preview
    /// and export (<c>FrameRequest.showWatermark</c>):
    /// <list type="bullet">
    /// <item>Remote Config <c>watermark_enabled</c> = false removes it for everyone (kill switch).</item>
    /// <item>Without the <c>NO_WATERMARK</c> entitlement the watermark is always drawn; the export sheet only shows a locked row.</item>
    /// <item>With <c>NO_WATERMARK</c> the export sheet toggle decides, default off (no watermark).</item>
    /// </list>
    /// The entitlement itself (release 1: No Ads Bundle, or both ad removals; later also Ultimate) is computed by Monetization's
    /// entitlement rule and passed in; it is checked at export time and never stored in the project.
    /// </summary>
    public static class WatermarkPolicy
    {
        /// <summary>Default of the export-sheet toggle for users who own <c>NO_WATERMARK</c> (spec §3.10: "по умолчанию без знака").</summary>
        public const bool DefaultKeepWatermarkWithEntitlement = false;

        /// <param name="watermarkEnabled">Remote Config <c>watermark_enabled</c>.</param>
        /// <param name="hasNoWatermark">The user owns the <c>NO_WATERMARK</c> entitlement.</param>
        /// <param name="keepWatermark">The export-sheet toggle; only read when <paramref name="hasNoWatermark"/> is true.</param>
        public static bool ShouldShow(bool watermarkEnabled, bool hasNoWatermark, bool keepWatermark = DefaultKeepWatermarkWithEntitlement)
        {
            if (!watermarkEnabled) return false;
            return !hasNoWatermark || keepWatermark;
        }

        /// <summary><see cref="ShouldShow(bool, bool, bool)"/> with <c>watermark_enabled</c> taken from the snapshot.</summary>
        public static bool ShouldShow(RemoteConfigValues values, bool hasNoWatermark, bool keepWatermark = DefaultKeepWatermarkWithEntitlement)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return ShouldShow(values.WatermarkEnabled, hasNoWatermark, keepWatermark);
        }

        /// <summary>The export sheet shows the watermark row as a toggle (true) or as a locked upsell row (false), spec §3.10.</summary>
        public static bool IsToggleAvailable(bool hasNoWatermark)
        {
            return hasNoWatermark;
        }
    }
}
