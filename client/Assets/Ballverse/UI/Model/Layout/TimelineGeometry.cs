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

        /// <summary>Width of a trim handle's hit area (spec §4.4: 44 × 44 pt touch zones).</summary>
        public const float HandleWidth = 44f;
        /// <summary>Part of a short segment's body left uncovered by its handles, so a tap selects it and a drag moves it.</summary>
        public const float MinExposedBody = 16f;

        /// <summary>
        /// Places the two edge handles of a segment (S / V clip, text span), each <see cref="HandleWidth"/> wide.
        /// Normally a handle is centred on its edge (half inside the segment). Each handle stays whole inside the pickable
        /// band [<paramref name="bandMin"/>, <paramref name="bandMax"/>]: the horizontal span, in track coordinates, where a
        /// point can still reach it (no clipping ancestor cuts it off; the screen edge does), so near the screen edge it
        /// moves further into the segment. On a short segment the handles reach less far into it, so at least
        /// <see cref="MinExposedBody"/> of the body stays uncovered; when the band forces one handle inwards the other moves
        /// outwards to compensate. Only a short span at the very end of the reel may end up covered (a press on a handle
        /// that does not move still selects it). Handles never overlap each other. Positions are panel units, track
        /// coordinates in, segment coordinates out. Pure.
        /// </summary>
        /// <param name="segmentX">Left edge of the segment in track coordinates.</param>
        /// <param name="segmentWidth">Drawn width of the segment.</param>
        public static HandlePlacement PlaceHandles(float segmentX, float segmentWidth, float bandMin, float bandMax)
        {
            float w = Math.Max(0f, segmentWidth);
            // How far each handle reaches into the segment.
            float budget = Math.Max(0f, w - MinExposedBody);
            float half = HandleWidth / 2f;
            float minInnerLeft = Math.Max(0f, bandMin - segmentX + HandleWidth);
            float minInnerRight = Math.Max(0f, segmentX + w + HandleWidth - bandMax);
            float innerLeft = Math.Max(Math.Min(half, budget / 2f), minInnerLeft);
            float innerRight = Math.Max(Math.Min(half, budget / 2f), minInnerRight);
            float excess = innerLeft + innerRight - budget;
            if (excess > 0f)
            {
                float r = Math.Min(excess, innerRight - minInnerRight);
                innerRight -= r;
                excess -= r;
                innerLeft -= Math.Min(excess, innerLeft - minInnerLeft);
            }
            float left = innerLeft - HandleWidth;
            float right = w - innerRight;
            if (left + HandleWidth > right)
            {
                // Short span against the right screen edge: the start handle moves out to the left of the end handle.
                left = right - HandleWidth;
                if (segmentX + left < bandMin)
                {
                    // A band too narrow for two handles (not the shipped layout): keep the start one, end one after it.
                    left = bandMin - segmentX;
                    right = left + HandleWidth;
                }
            }
            return new HandlePlacement(left, right, w);
        }

        /// <summary>
        /// The drawn part of a segment: the segment clipped to its track [0, <paramref name="trackWidth"/>]. The segment
        /// element itself keeps its true position (a V clip may start before S, a text span may end after the reel), so
        /// its trim handles stay on the true edges and keep their 44 pt hit areas, while its fill, border, thumbnails and
        /// label are drawn only inside the track and never over the badge column or the side padding. Positions are panel
        /// units, track coordinates in, segment coordinates out. Pure.
        /// </summary>
        public static SegmentClip ClipToTrack(float segmentX, float segmentWidth, float trackWidth)
        {
            float w = Math.Max(0f, segmentWidth);
            float tw = Math.Max(0f, trackWidth);
            float start = Math.Min(Math.Max(segmentX, 0f), tw);
            float end = Math.Max(Math.Min(segmentX + w, tw), start);
            return new SegmentClip(start - segmentX, end - start, segmentX < 0f, segmentX + w > tw);
        }

        /// <summary>
        /// Where a trim handle draws its grip (handle coordinates): on the segment edge it trims, moved onto the drawn
        /// part of the segment when that edge lies outside the track, and kept inside the handle.
        /// </summary>
        /// <param name="edgeInSegment">The edge in segment coordinates (0 = start, segment width = end).</param>
        /// <param name="handleX">Left of the handle in segment coordinates.</param>
        public static float GripInHandle(float edgeInSegment, float handleX, SegmentClip clip)
        {
            float edge = Math.Max(clip.BodyX, Math.Min(clip.BodyX + clip.BodyWidth, edgeInSegment));
            return Math.Max(0f, Math.Min(HandleWidth, edge - handleX));
        }

        /// <summary>One frame at <paramref name="fps"/> in microseconds (for the ±1 frame offset buttons).</summary>
        public static long FrameUs(int fps)
        {
            if (fps <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
            return MicrosPerSecond / fps;
        }
    }

    /// <summary>Where a segment's two trim handles sit, relative to the segment's left edge (see <see cref="TimelineGeometry.PlaceHandles"/>).</summary>
    public readonly struct HandlePlacement
    {
        /// <summary>Left of the start handle.</summary>
        public float LeftX { get; }
        /// <summary>Left of the end handle.</summary>
        public float RightX { get; }
        readonly float _segmentWidth;

        public HandlePlacement(float leftX, float rightX, float segmentWidth)
        {
            LeftX = leftX;
            RightX = rightX;
            _segmentWidth = segmentWidth;
        }

        /// <summary>Segment start edge inside the start handle (where its grip is drawn), clamped to the handle.</summary>
        public float LeftEdgeInHandle => Clamp(-LeftX);

        /// <summary>Segment end edge inside the end handle, clamped to the handle.</summary>
        public float RightEdgeInHandle => Clamp(_segmentWidth - RightX);

        /// <summary>Width of the body not covered by either handle.</summary>
        public float ExposedBody => Math.Max(0f, Math.Min(_segmentWidth, RightX) - Math.Max(0f, LeftX + TimelineGeometry.HandleWidth));

        static float Clamp(float x)
        {
            return Math.Max(0f, Math.Min(TimelineGeometry.HandleWidth, x));
        }
    }

    /// <summary>The drawn part of a timeline segment (see <see cref="TimelineGeometry.ClipToTrack"/>).</summary>
    public readonly struct SegmentClip
    {
        /// <summary>Left of the drawn part, relative to the segment's left edge (&gt; 0 when the segment starts before the track).</summary>
        public float BodyX { get; }
        /// <summary>Width of the drawn part (0 when the segment lies entirely outside the track).</summary>
        public float BodyWidth { get; }
        /// <summary>The segment starts before the track: its start edge is not drawn.</summary>
        public bool CutStart { get; }
        /// <summary>The segment ends after the track: its end edge is not drawn.</summary>
        public bool CutEnd { get; }
        public bool Visible => BodyWidth > 0f;

        public SegmentClip(float bodyX, float bodyWidth, bool cutStart, bool cutEnd)
        {
            BodyX = bodyX;
            BodyWidth = bodyWidth;
            CutStart = cutStart;
            CutEnd = cutEnd;
        }
    }
}
