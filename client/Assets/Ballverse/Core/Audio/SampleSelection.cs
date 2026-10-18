namespace Ballverse.Core.Audio
{
    /// <summary>How a rule with several sample ids picks one (spec §2.7). Both are pure functions of the event.</summary>
    public enum SampleSelection
    {
        /// <summary>Deterministic hash of (tick, bodyId, type): variety without any random state.</summary>
        Hash = 0,
        /// <summary>By body size: the largest body gets the first sample (e.g. kick), the smallest the last (e.g. hi-hat).</summary>
        BySize = 1,
    }
}
