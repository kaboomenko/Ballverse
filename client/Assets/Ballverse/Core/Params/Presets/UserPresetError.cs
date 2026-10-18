namespace Ballverse.Core.Params.Presets
{
    /// <summary>Outcome of a <see cref="UserPresetLibrary"/> operation (spec §2.3 user preset rules).</summary>
    public enum UserPresetError
    {
        None = 0,
        InvalidId = 1,
        DuplicateId = 2,
        /// <summary>Name is not 1–32 characters after trimming.</summary>
        InvalidName = 3,
        InvalidMode = 4,
        /// <summary>The mode already has <see cref="UserPresetLibrary.MaxPerMode"/> user presets.</summary>
        LimitReached = 5,
        NotFound = 6,
    }
}
