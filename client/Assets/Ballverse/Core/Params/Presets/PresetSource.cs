namespace Ballverse.Core.Params.Presets
{
    /// <summary>Where a preset comes from (spec §2.3).</summary>
    public enum PresetSource
    {
        /// <summary>Shipped with the mode (4–8 per mode).</summary>
        BuiltIn = 0,
        /// <summary>Saved by the user (up to 20 per mode).</summary>
        User = 1,
        /// <summary>Delivered by Remote Config <c>remote_presets</c> (spec §9.3); shown with the "Новое" badge.</summary>
        Remote = 2,
    }
}
