using System;

namespace Ballverse.UI.Layout
{
    /// <summary>A trimmed range on a source clip, in microseconds.</summary>
    public readonly struct TrimRange : IEquatable<TrimRange>
    {
        public long StartUs { get; }
        public long EndUs { get; }
        public long LengthUs => EndUs - StartUs;

        public TrimRange(long startUs, long endUs)
        {
            StartUs = startUs;
            EndUs = endUs;
        }

        public bool Equals(TrimRange other)
        {
            return StartUs == other.StartUs && EndUs == other.EndUs;
        }

        public override bool Equals(object obj)
        {
            return obj is TrimRange r && Equals(r);
        }

        public override int GetHashCode()
        {
            return StartUs.GetHashCode() * 397 ^ EndUs.GetHashCode();
        }

        public override string ToString()
        {
            return StartUs + ".." + EndUs;
        }
    }

    /// <summary>
    /// Time ↔ pixel mapping and trim-handle math of the studio timeline (spec §3.4). The visible window starts at
    /// <see cref="ScrollUs"/>; <see cref="PixelsPerSecond"/> is the zoom. Pure.
    /// </summary>
    public sealed class TimelineGeometry
    {
        public const long MicrosPerSecond = 1000000L;
        /// <summary>A trimmed clip keeps at least one second (spec §3.8: videos are 1–180 s).</summary>
        public const long MinClipUs = MicrosPerSecond;

        public float PixelsPerSecond { get; private set; }
        public long ScrollUs { get; private set; }
        public long TotalUs { get; private set; }
        public float ViewWidth { get; private set; }

        public TimelineGeometry(float pixelsPerSecond, long totalUs, float viewWidth)
        {
            PixelsPerSecond = Math.Max(1f, pixelsPerSecond);
            TotalUs = Math.Max(0, totalUs);
            ViewWidth = Math.Max(0f, viewWidth);
        }

        public void SetTotal(long totalUs)
        {
            TotalUs = Math.Max(0, totalUs);
            ScrollTo(ScrollUs);
        }

        public void SetViewWidth(float width)
        {
            ViewWidth = Math.Max(0f, width);
            ScrollTo(ScrollUs);
        }

        public void SetZoom(float pixelsPerSecond)
        {
            PixelsPerSecond = Math.Max(1f, pixelsPerSecond);
            ScrollTo(ScrollUs);
        }

        /// <summary>Zoom so the whole timeline fits the view.</summary>
        public void FitAll()
        {
            if (TotalUs <= 0 || ViewWidth <= 0) return;
            PixelsPerSecond = Math.Max(1f, ViewWidth / (TotalUs / (float)MicrosPerSecond));
            ScrollUs = 0;
        }

        public long VisibleUs => (long)(ViewWidth / PixelsPerSecond * MicrosPerSecond);

        public void ScrollTo(long us)
        {
            long maxScroll = Math.Max(0, TotalUs - VisibleUs);
            ScrollUs = Math.Max(0, Math.Min(maxScroll, us));
        }

        public float ToX(long us)
        {
            return (us - ScrollUs) / (float)MicrosPerSecond * PixelsPerSecond;
        }

        public long ToUs(float x)
        {
            return ScrollUs + (long)Math.Round(x / PixelsPerSecond * MicrosPerSecond);
        }

        public float Width(long durationUs)
        {
            return durationUs / (float)MicrosPerSecond * PixelsPerSecond;
        }

        /// <summary>Time of a playhead tap/drag, clamped to [0, total].</summary>
        public long ClampedTime(float x)
        {
            return Math.Max(0, Math.Min(TotalUs, ToUs(x)));
        }

        /// <summary>Keeps the playhead visible while playing (page scroll when it leaves the view).</summary>
        public void Follow(long playheadUs)
        {
            if (playheadUs < ScrollUs || playheadUs > ScrollUs + VisibleUs) ScrollTo(playheadUs - VisibleUs / 5);
        }

        /// <summary>Drags the start handle by <paramref name="deltaUs"/>; keeps ≥ <see cref="MinClipUs"/> and ≥ 0.</summary>
        public static TrimRange DragStart(TrimRange range, long deltaUs, long sourceUs)
        {
            long start = range.StartUs + deltaUs;
            long maxStart = range.EndUs - Math.Min(MinClipUs, range.LengthUs);
            start = Math.Max(0, Math.Min(maxStart, start));
            return new TrimRange(start, Math.Min(range.EndUs, sourceUs));
        }

        /// <summary>Drags the end handle by <paramref name="deltaUs"/>; keeps ≥ <see cref="MinClipUs"/> and ≤ source length.</summary>
        public static TrimRange DragEnd(TrimRange range, long deltaUs, long sourceUs)
        {
            long end = range.EndUs + deltaUs;
            long minEnd = range.StartUs + Math.Min(MinClipUs, Math.Max(0, sourceUs - range.StartUs));
            end = Math.Max(minEnd, Math.Min(sourceUs, end));
            return new TrimRange(range.StartUs, end);
        }

        /// <summary>
        /// Keeps a V offset (Core <c>UserVideoTiming.OffsetUs</c>: V start − S start, negative = V earlier) so that V and S
        /// still overlap by at least <see cref="MinClipUs"/> (or by the shorter track when it is shorter than that).
        /// </summary>
        public static long ClampVideoOffset(long offsetUs, long simLengthUs, long videoLengthUs)
        {
            long overlap = Math.Min(MinClipUs, Math.Max(0, Math.Min(simLengthUs, videoLengthUs)));
            long min = -Math.Max(0, videoLengthUs - overlap);
            long max = Math.Max(0, simLengthUs - overlap);
            return Math.Max(min, Math.Min(max, offsetUs));
        }

        /// <summary>
        /// Moves a span (text layer, spec §3.5) by <paramref name="deltaUs"/> keeping its length, inside [0, total].
        /// </summary>
        public static TrimRange ShiftSpan(TrimRange span, long deltaUs, long totalUs)
        {
            long length = Math.Max(0, Math.Min(span.LengthUs, totalUs));
            long start = Math.Max(0, Math.Min(totalUs - length, span.StartUs + deltaUs));
            return new TrimRange(start, start + length);
        }

        /// <summary>One frame at <paramref name="fps"/> in microseconds (for the ±1 frame offset buttons).</summary>
        public static long FrameUs(int fps)
        {
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
            return MicrosPerSecond / fps;
        }
    }
}
