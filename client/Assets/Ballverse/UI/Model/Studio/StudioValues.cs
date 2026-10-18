using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.UI.Studio
{
    /// <summary>
    /// Mapping between studio controls and the Core values they edit (spec §3.3–§3.5). Controls never cast a segment
    /// index to a Core enum: the order shown on screen is listed here and mapped by name. Pure.
    /// </summary>
    public static class StudioValues
    {
        // ---- L1/L2 split (Core RatioMagnets) ----

        /// <summary>Slider range of the split in percent (Core <c>LayoutSpec.MinRatio</c> / <c>MaxRatio</c>).</summary>
        public const int RatioMinPercent = 30;
        public const int RatioMaxPercent = 70;

        /// <summary>Committed Core ratio for a slider position in percent: 1% steps, magnets exactly 1/3, 1/2, 2/3.</summary>
        public static float RatioFromPercent(double percent)
        {
            if (double.IsNaN(percent) || double.IsInfinity(percent)) percent = 50;
            return RatioMagnets.Snap((float)(percent / 100.0));
        }

        /// <summary>Slider position (whole percent) that shows a Core ratio.</summary>
        public static int PercentFromRatio(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return 50;
            int p = (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
            return Math.Max(RatioMinPercent, Math.Min(RatioMaxPercent, p));
        }

        // ---- Finale slow-mo (Core ClipTiming) ----

        /// <summary>Shortest finale slow-mo, s (Core <c>ClipTiming.MinSlowMoTicks</c>).</summary>
        public static double SlowMoMinSeconds => ClipTiming.MinSlowMoTicks / (double)SimConstants.TickRate;

        /// <summary>Longest finale slow-mo, s (Core <c>ClipTiming.MaxSlowMoTicks</c>).</summary>
        public static double SlowMoMaxSeconds => ClipTiming.MaxSlowMoTicks / (double)SimConstants.TickRate;

        /// <summary>Slider step of the slow-mo length, s.</summary>
        public const double SlowMoStepSeconds = 0.5;

        /// <summary>
        /// Value sent to the view model for the slow-mo switch and length slider: 0 when off, otherwise the length clamped
        /// to 1–3 s, so Core never receives a value between 0 and 1 s.
        /// </summary>
        public static float SlowMoSeconds(bool enabled, double seconds)
        {
            if (!enabled) return 0f;
            if (double.IsNaN(seconds) || seconds < SlowMoMinSeconds) seconds = SlowMoMinSeconds;
            if (seconds > SlowMoMaxSeconds) seconds = SlowMoMaxSeconds;
            return (float)seconds;
        }

        // ---- Segment orders ----

        /// <summary>Alignment segments as shown (left, center, right icons).</summary>
        public static readonly TextAlign[] AlignOrder = { TextAlign.Left, TextAlign.Center, TextAlign.Right };

        /// <summary>Plate segments as shown ("Нет", "Плашка", "Стекло").</summary>
        public static readonly TextPlate[] PlateOrder = { TextPlate.None, TextPlate.Solid, TextPlate.Glass };

        /// <summary>PiP shape segments as shown (rounded rectangle, circle).</summary>
        public static readonly PipShape[] PipShapeOrder = { PipShape.RoundedRect, PipShape.Circle };

        /// <summary>Fit-margin segments as shown (blurred copy, colour, theme colour).</summary>
        public static readonly FitBackgroundKind[] FitBackgroundOrder =
            { FitBackgroundKind.Blur, FitBackgroundKind.Color, FitBackgroundKind.Theme };

        /// <summary>"Если V короче ролика" choices as shown.</summary>
        public static readonly VideoAfterEnd[] VideoAfterEndOrder =
            { VideoAfterEnd.Freeze, VideoAfterEnd.Loop, VideoAfterEnd.Black, VideoAfterEnd.ExpandSim };

        /// <summary>Reel duration modes as shown ("Авто", "= V", "Вручную").</summary>
        public static readonly DurationMode[] DurationModeOrder = { DurationMode.Auto, DurationMode.MatchVideo, DurationMode.Manual };

        /// <summary>Index of <paramref name="value"/> in a segment order; 0 for an unknown value.</summary>
        public static int IndexIn<T>(T[] order, T value) where T : struct
        {
            int i = Array.IndexOf(order, value);
            return i < 0 ? 0 : i;
        }

        /// <summary>Value of segment <paramref name="index"/>; the first value for an index out of range.</summary>
        public static T At<T>(T[] order, int index) where T : struct
        {
            return index >= 0 && index < order.Length ? order[index] : order[0];
        }
    }
}
