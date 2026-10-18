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
        /// starts at a reduced internal resolution (spec §3.7: "тир C, 1080p60"; §8.2 P5): tier C exporting 1080p at
        /// 60 fps. The output file is unaffected and the amount of reduction is a renderer decision. This is only the
        /// initial hint: the renderer must also lower the internal resolution (never the fps) at runtime whenever preview
        /// frames are missed, on any tier.
        /// </summary>
        public static bool ShouldReduceInternalResolution(DeviceTier tier, ExportResolution resolution, int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            if (!Enum.IsDefined(typeof(DeviceTier), tier)) throw new ArgumentOutOfRangeException(nameof(tier));
            if (!Enum.IsDefined(typeof(ExportResolution), resolution)) throw new ArgumentOutOfRangeException(nameof(resolution));
            return tier == DeviceTier.C && resolution == ExportResolution.P1080 && fps == OutputFps.Fps60;
        }

        /// <summary>Allowed preview audio/video drift: one frame, µs rounded up (spec §3.7: 16–33 ms). Export allows 0.</summary>
        public static long MaxAvDriftUs(int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            return StudioTimeline.CeilDiv(StudioTimeline.MicrosPerSecond, fps);
        }
    }
}
