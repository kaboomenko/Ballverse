using System;

namespace Ballverse.App
{
    /// <summary>
    /// Display frame rate policy (spec §8.2): 60 FPS cap in release 1 (120 Hz screens are not used, to save battery);
    /// screens may lower it temporarily, e.g. to 30 FPS for idle UI in Low Power Mode.
    /// </summary>
    public interface IFrameRatePolicy
    {
        int DefaultFps { get; }

        int TargetFps { get; }

        /// <summary>Sets the target, clamped to [<see cref="FrameRatePolicy.MinFps"/>, <see cref="DefaultFps"/>].</summary>
        void SetTarget(int fps);

        void ResetToDefault();
    }

    public sealed class FrameRatePolicy : IFrameRatePolicy
    {
        public const int ReleaseDefaultFps = 60;
        public const int MinFps = 15;

        readonly Action<int> _apply;

        /// <param name="apply">Applies a target to the engine; <see cref="UnityApply"/> on device.</param>
        public FrameRatePolicy(Action<int> apply, int defaultFps = ReleaseDefaultFps)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            if (defaultFps < MinFps) throw new ArgumentOutOfRangeException(nameof(defaultFps));
            DefaultFps = defaultFps;
        }

        public int DefaultFps { get; }

        public int TargetFps { get; private set; }

        public void SetTarget(int fps)
        {
            int clamped = fps < MinFps ? MinFps : fps > DefaultFps ? DefaultFps : fps;
            if (clamped == TargetFps) return;
            TargetFps = clamped;
            _apply(clamped);
        }

        public void ResetToDefault() => SetTarget(DefaultFps);

        /// <summary>
        /// vSyncCount 0 hands pacing to targetFrameRate. Mobile platforms ignore vSyncCount and always present on
        /// vertical blank, so this caps the rate without tearing; in the Editor it makes the cap visible as well.
        /// </summary>
        public static void UnityApply(int fps)
        {
            UnityEngine.QualitySettings.vSyncCount = 0;
            UnityEngine.Application.targetFrameRate = fps;
        }
    }
}
