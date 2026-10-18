namespace Ballverse.Platform
{
    /// <summary>Frame-rate limits of release 1 (spec §8.2).</summary>
    public static class FrameRates
    {
        /// <summary>60 FPS cap; 120 Hz screens are not used, to save battery.</summary>
        public const int ReleaseDefaultFps = 60;

        /// <summary>Lowest target a screen may request.</summary>
        public const int MinFps = 15;
    }

    /// <summary>
    /// Display frame-rate policy (spec §8.2): <see cref="FrameRates.ReleaseDefaultFps"/> by default; screens may lower
    /// it temporarily, e.g. to 30 FPS for idle UI in Low Power Mode, and reset it when they close.
    /// </summary>
    public interface IFrameRatePolicy
    {
        int DefaultFps { get; }

        int TargetFps { get; }

        /// <summary>Sets the target, clamped to [<see cref="FrameRates.MinFps"/>, <see cref="DefaultFps"/>].</summary>
        void SetTarget(int fps);

        void ResetToDefault();
    }
}
