using System;

namespace Ballverse.Sim
{
    /// <summary>
    /// The display rate actually achieved, for <see cref="LiveSimController.AdvanceDisplayFrame"/> (Core
    /// <c>ModeSession.AdvanceDisplayFrame(displayFrameRate)</c> takes the rate as an input so the run plays in real time on a
    /// device that renders below its target: tier C guarantees only ≥ 30 FPS, spec §8.2 P1, and thermal throttling lowers
    /// any tier). An exponential average of the frame time, ignoring hitches (pause, scene load), snapped to a common refresh
    /// rate when within 4 %, else rounded; clamped to <see cref="MinRate"/>–<see cref="MaxRate"/>. Allocation-free; main thread.
    /// </summary>
    public sealed class DisplayRateEstimator
    {
        public const int MinRate = 10;
        public const int MaxRate = 240;
        /// <summary>Frames longer than this are hitches and do not move the estimate.</summary>
        public const float HitchSeconds = 0.25f;
        /// <summary>Weight of the newest frame time in the average.</summary>
        public const double Smoothing = 0.15;

        static readonly int[] s_common = { 24, 30, 40, 45, 48, 50, 60, 72, 90, 96, 100, 120, 144, 165, 240 };

        double _frameSeconds;
        int _rate;

        public DisplayRateEstimator(int initialRate = 60)
        {
            Reset(initialRate);
        }

        /// <summary>The current estimate (frames per second).</summary>
        public int Rate => _rate;

        /// <summary>Starts over from <paramref name="rate"/> (e.g. the target rate after a resume).</summary>
        public void Reset(int rate)
        {
            _rate = Clamp(rate <= 0 ? 60 : rate);
            _frameSeconds = 1.0 / _rate;
        }

        /// <summary>Adds one frame's duration and returns the rate to step this frame with.</summary>
        public int Sample(float deltaSeconds)
        {
            if (deltaSeconds > 0f && deltaSeconds <= HitchSeconds && !float.IsNaN(deltaSeconds))
            {
                _frameSeconds += Smoothing * (deltaSeconds - _frameSeconds);
                _rate = Snap(1.0 / _frameSeconds);
            }
            return _rate;
        }

        static int Snap(double fps)
        {
            for (int i = 0; i < s_common.Length; i++)
            {
                if (Math.Abs(fps - s_common[i]) <= s_common[i] * 0.04) return s_common[i];
            }
            return Clamp((int)Math.Round(fps));
        }

        static int Clamp(int r) => r < MinRate ? MinRate : r > MaxRate ? MaxRate : r;
    }
}
