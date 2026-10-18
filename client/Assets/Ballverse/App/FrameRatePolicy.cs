using System;
using Ballverse.Platform;

namespace Ballverse.App
{
    /// <summary><see cref="IFrameRatePolicy"/> applied to the engine through a callback (<see cref="UnityApply"/> on device).</summary>
    public sealed class FrameRatePolicy : IFrameRatePolicy
    {
        public const int ReleaseDefaultFps = FrameRates.ReleaseDefaultFps;
        public const int MinFps = FrameRates.MinFps;

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
