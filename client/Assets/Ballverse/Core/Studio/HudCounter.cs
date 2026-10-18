using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// The live value of the "Осталось: {n}" text template (spec §3.5 "счётчик «Осталось: N» (живой, связан с HUD симуляции)") from a
    /// render frame's HUD. Uses the shared HUD keys of the mode framework (<c>Ballverse.Core.Modes.HudKeys</c>: Progress = 2,
    /// ProgressTotal = 3; persisted in render streams, never renumbered): remaining = total − progress when the mode has a total
    /// (rings, floors), otherwise the progress counter itself (balls in M02). Null when the frame carries neither.
    /// </summary>
    public static class HudCounter
    {
        /// <summary>HUD key of the progress counter (rings passed, floors passed, balls).</summary>
        public const int ProgressKey = 2;
        /// <summary>HUD key of the progress total (0 when open-ended).</summary>
        public const int ProgressTotalKey = 3;

        public static int? Remaining(RenderFrame frame)
        {
            if (frame == null) return null;
            bool hasProgress = frame.TryGetHud(ProgressKey, out int progress);
            bool hasTotal = frame.TryGetHud(ProgressTotalKey, out int total);
            if (hasTotal && total > 0) return Math.Max(0, total - (hasProgress ? progress : 0));
            return hasProgress ? progress : (int?)null;
        }
    }
}
