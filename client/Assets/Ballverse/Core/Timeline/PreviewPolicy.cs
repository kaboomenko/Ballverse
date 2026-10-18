using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Studio preview rules (spec §3.7, §8.2 P5). Preview samples the same <see cref="StudioTimeline"/> as export
    /// (frame-based or via the µs-based methods for scrubbing), so preview and export show identical frames.
    /// </summary>
    public static class PreviewPolicy
    {
        /// <summary>Longest scrub-to-frame latency target, ms (spec §3.7).</summary>
        public const int MaxScrubLatencyMs = 100;

        /// <summary>Preview plays at the export fps; it never lowers the frame rate (spec §3.7).</summary>
        public static int TargetFps(int exportFps)
        {
            OutputFps.Validate(exportFps, nameof(exportFps));
            return exportFps;
        }

        /// <summary>
        /// True when the device is expected not to sustain the preview at full internal resolution, so the renderer
        /// starts at a reduced internal resolution (spec §3.7: tier C at 60 fps; §8.2 P5). The output file is unaffected.
        /// The amount of reduction is a renderer decision.
        /// </summary>
        public static bool ShouldReduceInternalResolution(DeviceTier tier, int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            if (!Enum.IsDefined(typeof(DeviceTier), tier)) throw new ArgumentOutOfRangeException(nameof(tier));
            return tier == DeviceTier.C && fps == OutputFps.Fps60;
        }

        /// <summary>Allowed preview audio/video drift: one frame, µs rounded up (spec §3.7: 16–33 ms). Export allows 0.</summary>
        public static long MaxAvDriftUs(int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            return StudioTimeline.CeilDiv(StudioTimeline.MicrosPerSecond, fps);
        }
    }
}
