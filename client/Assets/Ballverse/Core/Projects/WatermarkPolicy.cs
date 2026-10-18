namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Watermark decision at export/preview time (spec §3.10, owner decision Р13). The project only stores the user's
    /// opt-in "keep the watermark" toggle (<see cref="OutputSettings.Watermark"/>, default false); the entitlement and the
    /// remote kill switch are checked on every export, so the watermark is never "baked" into a project and an old project
    /// re-exported after buying Ultimate goes out without it.
    /// </summary>
    public static class WatermarkPolicy
    {
        /// <summary>
        /// True when the watermark must be drawn.
        /// Kill switch <c>watermark_enabled = false</c> removes it for everyone; without <c>NO_WATERMARK</c> it is always on;
        /// with the entitlement the user's toggle decides.
        /// </summary>
        public static bool Resolve(bool projectToggle, bool hasNoWatermarkEntitlement, bool remoteWatermarkEnabled = true)
        {
            if (!remoteWatermarkEnabled) return false;
            if (!hasNoWatermarkEntitlement) return true;
            return projectToggle;
        }
    }
}
