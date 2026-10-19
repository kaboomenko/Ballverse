using System;
using System.Globalization;
using Ballverse.UI.Components;
using Ballverse.UI.Layout;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Studio timeline (spec §3.4–§3.6, §4.3): ruler with scrubbing, tracks S (simulation clip), V (user video), text and
    /// music. S and V have trim handles; V is dragged to shift it against S (also before S: the offset may be negative);
    /// text spans are dragged to change when they appear (body) and their start / end (handles); the music bar is dragged
    /// to choose where the track starts. Edits are previewed while dragging and committed on release through the events.
    /// Handles are 44 pt wide hit areas (spec §4.4) with a thin visible grip on the segment edge, placed by
    /// <see cref="TimelineGeometry.PlaceHandles"/>: centred on the edges, pushed outwards on short spans so part of the
    /// body stays free, and kept inside the screen. The tracks and segments do not clip (Studio.uss), so a handle reaching
    /// past the track is still hit; what a segment draws (fill, border, thumbnails, label) lives in its body element
    /// (<c>bv-timeline__body</c>), which is clipped to the track by <see cref="TimelineGeometry.ClipToTrack"/>, so a V clip
    /// before S or a text span past the reel end never paints over the badge column or the side padding. A press on a
    /// handle that does not move is a tap (on a text span it selects the layer).
    /// </summary>
    public sealed class TimelineView : VisualElement
    {
        /// <summary>Minimum drawn width of a segment so both grips stay visible.</summary>
        const float MinSegmentWidth = 28f;
        const float ShiftThreshold = 8f;
        /// <summary>Drawn part of a segment (clipped to the track); the segment element itself only holds it and the handles.</summary>
        internal const string BodyClass = "bv-timeline__body";
        const string BodyCutStartClass = "bv-timeline__body--cut-start";
        const string BodyCutEndClass = "bv-timeline__body--cut-end";
        internal const string LeftHandleClass = "bv-timeline__handle--left";
        internal const string RightHandleClass = "bv-timeline__handle--right";

        readonly UiContext _ctx;
        readonly VisualElement _ruler;
        readonly VisualElement _area;
        readonly VisualElement _simTrack;
        readonly VisualElement _videoTrack;
        readonly VisualElement _textTrack;
        readonly VisualElement _musicTrack;
        readonly VisualElement _playhead;
        readonly TimelineGeometry _geo = new TimelineGeometry(20f, 0, 0);
        StudioTimelineData _data;
        long _position;
        TrimRange _simPreview;
        TrimRange _videoPreview;
        long _videoStartPreview;
        int _textEditIndex = -1;
        TrimRange _textPreview;
        long _musicStartPreview;
        bool _editing;

        /// <summary>(time, dragging): scrub request.</summary>
        public event Action<long, bool> Seek;
        public event Action<long, long> SimTrimCommitted;
        public event Action<long, long> VideoTrimCommitted;
        /// <summary>New V offset (Core <c>UserVideoTiming.OffsetUs</c>, may be negative).</summary>
        public event Action<long> VideoOffsetCommitted;
        public event Action<int> TextSelected;
        /// <summary>(layer index, start, end) of a dragged text span.</summary>
        public event Action<int, long, long> TextTimingCommitted;
        /// <summary>New start of the music inside its track, µs.</summary>
        public event Action<long> MusicStartCommitted;
        public event Action AddVideoRequested;

        public TimelineView(UiContext ctx)
        {
            _ctx = ctx;
            AddToClassList("bv-timeline");
            _ruler = new VisualElement();
            _ruler.AddToClassList("bv-timeline__ruler");
            Add(_ruler);
            _area = new VisualElement();
            _area.AddToClassList("bv-timeline__area");
            Add(_area);
            _simTrack = Track("studio.track.sim", "bv-timeline__track--sim");
            _videoTrack = Track("studio.track.video", "bv-timeline__track--video");
            _textTrack = Track("studio.track.text", "bv-timeline__track--text");
            _musicTrack = Track(null, "bv-timeline__track--music");
            _playhead = new VisualElement();
            _playhead.AddToClassList("bv-timeline__playhead");
            _playhead.pickingMode = PickingMode.Ignore;
            Add(_playhead);
            RegisterCallback<GeometryChangedEvent>(e => Relayout());
            new ScrubDrag(_ruler, this);
        }

        /// <param name="letterKey">Key of the one-letter track badge (S / V / T); null shows the music icon.</param>
        VisualElement Track(string letterKey, string modifier)
        {
            var row = new VisualElement();
            row.AddToClassList("bv-timeline__row");
            var badge = new VisualElement();
            badge.AddToClassList("bv-timeline__badge");
            if (letterKey != null) badge.Add(_ctx.KeyLabel(letterKey, "bv-timeline__badge-label"));
            else badge.Add(Icons.Create(IconKind.Music, "bv-timeline__badge-icon"));
            row.Add(badge);
            var track = new VisualElement();
            track.AddToClassList("bv-timeline__track");
            track.AddToClassList(modifier);
            row.Add(track);
            _area.Add(row);
            return track;
        }

        float TrackWidth => _simTrack.layout.width;

        public void SetData(StudioTimelineData data, long positionUs)
        {
            bool structural = _data == null || data == null || !ReferenceEquals(_data, data);
            _data = data;
            _position = positionUs;
            if (structural && !_editing) Relayout();
            else UpdatePlayhead();
        }

        void Relayout()
        {
            Build(TrackWidth);
        }

        /// <summary>Rebuilds the tracks for a track width in panel units (no-op before layout or without data).</summary>
        internal void Build(float trackWidth)
        {
            if (_data == null) return;
            float w = trackWidth;
            if (float.IsNaN(w) || w <= 0) return;
            _geo.SetViewWidth(w);
            _geo.SetTotal(Math.Max(_data.DurationUs, TimelineGeometry.MinClipUs));
            _geo.FitAll();
            _simPreview = new TrimRange(_data.Sim.TrimStartUs, _data.Sim.TrimEndUs);
            _videoPreview = new TrimRange(_data.Video.TrimStartUs, _data.Video.TrimEndUs);
            _videoStartPreview = _data.Video.TimelineStartUs;
            _musicStartPreview = _data.Music?.TrackStartUs ?? 0;
            _textEditIndex = -1;
            BuildRuler();
            BuildMediaTrack(_simTrack, _data.Sim, true);
            BuildMediaTrack(_videoTrack, _data.Video, false);
            BuildTextTrack();
            BuildMusicTrack();
            UpdatePlayhead();
        }

        void BuildRuler()
        {
            _ruler.Clear();
            float seconds = _data.DurationUs / (float)TimelineGeometry.MicrosPerSecond;
            int step = seconds <= 15 ? 1 : seconds <= 40 ? 5 : seconds <= 90 ? 10 : 30;
            for (int s = 0; s <= seconds + 0.001f; s += step)
            {
                var tick = new VisualElement();
                tick.AddToClassList("bv-timeline__tick");
                tick.style.left = RulerOffset() + _geo.ToX(s * TimelineGeometry.MicrosPerSecond);
                tick.pickingMode = PickingMode.Ignore;
                var label = UiContext.TextLabel(s.ToString(CultureInfo.InvariantCulture), "bv-timeline__tick-label");
                label.pickingMode = PickingMode.Ignore;
                tick.Add(label);
                _ruler.Add(tick);
            }
        }

        /// <summary>X of the track area inside this element (badge column width).</summary>
        float RulerOffset()
        {
            float x = _simTrack.worldBound.x - worldBound.x;
            return float.IsNaN(x) ? 0f : x;
        }

        void BuildMediaTrack(VisualElement track, TimelineTrackData data, bool isSim)
        {
            track.Clear();
            if (!data.Present)
            {
                if (!isSim)
                {
                    var add = new VisualElement();
                    add.AddToClassList("bv-timeline__add");
                    add.Add(Icons.Create(IconKind.Plus, "bv-timeline__add-icon"));
                    add.Add(_ctx.KeyLabel("studio.add_video", "bv-timeline__add-label"));
                    Tap.On(add, () => AddVideoRequested?.Invoke());
                    track.Add(add);
                }
                return;
            }
            var seg = new VisualElement();
            seg.AddToClassList("bv-timeline__segment");
            seg.AddToClassList(isSim ? "bv-timeline__segment--sim" : "bv-timeline__segment--video");
            // The segment spans the true clip (it may reach outside the track) and is not hit itself: its body is.
            seg.pickingMode = PickingMode.Ignore;
            var body = Body(seg);
            var thumbs = new VisualElement();
            thumbs.AddToClassList("bv-timeline__thumbs");
            thumbs.pickingMode = PickingMode.Ignore;
            foreach (var t in data.Thumbnails)
            {
                var th = new VisualElement();
                th.AddToClassList("bv-timeline__thumb");
                th.pickingMode = PickingMode.Ignore;
                TextureSlot.Set(th, t);
                thumbs.Add(th);
            }
            body.Add(thumbs);
            var left = Handle(true);
            var right = Handle(false);
            seg.Add(left);
            seg.Add(right);
            track.Add(seg);
            PlaceSegment(seg, isSim);
            var kind = isSim ? EditTarget.SimTrim : EditTarget.VideoTrim;
            new EdgeDrag(this, left, kind, true, null);
            new EdgeDrag(this, right, kind, false, null);
            if (!isSim) new BodyDrag(this, body, EditTarget.VideoShift, -1, null);
        }

        /// <summary>Adds the drawn, clipped part of a segment (first child, so the handles stay on top of it).</summary>
        static VisualElement Body(VisualElement segment)
        {
            var body = new VisualElement();
            body.AddToClassList(BodyClass);
            segment.Add(body);
            return body;
        }

        static VisualElement Handle(bool left)
        {
            var h = new VisualElement();
            h.AddToClassList("bv-timeline__handle");
            h.AddToClassList(left ? LeftHandleClass : RightHandleClass);
            var grip = new VisualElement();
            grip.AddToClassList("bv-timeline__grip");
            grip.pickingMode = PickingMode.Ignore;
            h.Add(grip);
            return h;
        }

        void PlaceSegment(VisualElement seg, bool isSim)
        {
            if (seg == null) return;
            long start = isSim ? _data.Sim.TimelineStartUs : _videoStartPreview;
            var range = isSim ? _simPreview : _videoPreview;
            if (isSim && range.StartUs != _data.Sim.TrimStartUs)
            {
                // While the start handle moves, show the change at the left edge of the clip.
                start += range.StartUs - _data.Sim.TrimStartUs;
            }
            Place(seg, start, range.LengthUs);
        }

        /// <summary>
        /// Positions a segment (S / V clip, text span, music bar) at its true place, clips its body to the track and
        /// places its handles. Call it once the segment and its handles are in the track: the handles are found inside the
        /// segment and their pickable band is measured from the track.
        /// </summary>
        void Place(VisualElement element, long startUs, long lengthUs)
        {
            float x = _geo.ToX(startUs);
            float width = Mathf.Max(MinSegmentWidth, _geo.Width(lengthUs));
            element.style.left = x;
            element.style.width = width;
            var clip = TimelineGeometry.ClipToTrack(x, width, _geo.ViewWidth);
            ClipBody(element, width, clip);
            PlaceHandles(element, x, width, clip);
        }

        /// <summary>Draws only the part of the segment inside the track (its fill, border, thumbnails, label).</summary>
        static void ClipBody(VisualElement segment, float width, SegmentClip clip)
        {
            var body = segment.Q<VisualElement>(className: BodyClass);
            if (body == null) return;
            body.style.display = clip.Visible ? DisplayStyle.Flex : DisplayStyle.None;
            body.style.left = clip.BodyX;
            body.style.width = clip.BodyWidth;
            // A cut side is the track edge, not the clip edge: it has no border.
            body.EnableInClassList(BodyCutStartClass, clip.CutStart);
            body.EnableInClassList(BodyCutEndClass, clip.CutEnd);
            var thumbs = body.Q<VisualElement>(className: "bv-timeline__thumbs");
            if (thumbs != null)
            {
                // The thumbnails keep the whole clip's scale; the body cuts off what lies outside the track.
                thumbs.style.left = -clip.BodyX;
                thumbs.style.width = width;
            }
        }

        /// <summary>Positions the segment's trim handles (if it has any) and their grips on the segment edges.</summary>
        void PlaceHandles(VisualElement segment, float x, float width, SegmentClip clip)
        {
            var left = segment.Q<VisualElement>(className: LeftHandleClass);
            var right = segment.Q<VisualElement>(className: RightHandleClass);
            if (left == null || right == null) return;
            HandleBand(segment.parent, out float bandMin, out float bandMax);
            var p = TimelineGeometry.PlaceHandles(x, width, bandMin, bandMax);
            SetHandle(left, p.LeftX, TimelineGeometry.GripInHandle(0f, p.LeftX, clip));
            SetHandle(right, p.RightX, TimelineGeometry.GripInHandle(width, p.RightX, clip));
        }

        /// <summary>
        /// Pickable band of a track's handles, in track coordinates: up to the screen edges (no ancestor between the track
        /// and the screen layer clips; QaRound2StaticChecks keeps it so). Measured against the page root, which fills the
        /// screen layer and moves with the track, so a push transition's translate does not skew it. Unbounded while the
        /// track or the page has no layout.
        /// </summary>
        internal void HandleBand(VisualElement track, out float bandMin, out float bandMax)
        {
            bandMin = float.NegativeInfinity;
            bandMax = float.PositiveInfinity;
            if (track == null) return;
            var screen = PageRoot().worldBound;
            var tb = track.worldBound;
            if (float.IsNaN(tb.x) || float.IsNaN(screen.x) || !(screen.width > 0f)) return;
            bandMin = screen.xMin - tb.xMin;
            bandMax = screen.xMax - tb.xMin;
        }

        /// <summary>The screen this timeline is on (class <c>bv-page</c>, full screen), else the app root.</summary>
        VisualElement PageRoot()
        {
            for (var e = parent; e != null; e = e.parent)
                if (e.ClassListContains("bv-page")) return e;
            return _ctx.AppRoot;
        }

        static void SetHandle(VisualElement handle, float x, float edge)
        {
            handle.style.left = x;
            handle.style.right = StyleKeyword.Auto;
            var grip = handle.Q<VisualElement>(className: "bv-timeline__grip");
            if (grip != null) grip.style.translate = new Translate(edge - TimelineGeometry.HandleWidth / 2f, 0);
        }

        void BuildTextTrack()
        {
            _textTrack.Clear();
            foreach (var item in _data.Texts)
            {
                var pill = new VisualElement();
                pill.AddToClassList("bv-timeline__text");
                pill.userData = item.LayerIndex;
                pill.pickingMode = PickingMode.Ignore;
                var body = Body(pill);
                var label = UiContext.TextLabel(item.Text, "bv-timeline__text-label");
                label.pickingMode = PickingMode.Ignore;
                body.Add(label);
                var left = Handle(true);
                var right = Handle(false);
                pill.Add(left);
                pill.Add(right);
                _textTrack.Add(pill);
                // Placed only now: PlaceHandles finds the handles inside the pill and measures their band from the track
                // (QA round 3: placed before, the handles kept their USS offsets and covered short pills).
                Place(pill, item.StartUs, item.EndUs - item.StartUs);
                int index = item.LayerIndex;
                // Tap selects the layer (opens the text tool), drag moves it in time, the edges change in / out.
                new BodyDrag(this, body, EditTarget.TextShift, index, () => TextSelected?.Invoke(index));
                // A handle that is pressed without moving acts like the body: on short spans the handles cover most of it.
                new EdgeDrag(this, left, EditTarget.TextTrim, true, () => TextSelected?.Invoke(index), index);
                new EdgeDrag(this, right, EditTarget.TextTrim, false, () => TextSelected?.Invoke(index), index);
            }
        }

        void BuildMusicTrack()
        {
            _musicTrack.Clear();
            var m = _data.Music;
            _musicTrack.parent.style.display = m != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m == null) return;
            // The music track clips (its bar slides while dragged) and the bar has no handles: it is its own body.
            var bar = new VisualElement();
            bar.AddToClassList("bv-timeline__music");
            var label = UiContext.TextLabel(_ctx.L(m.Name), "bv-timeline__text-label");
            label.pickingMode = PickingMode.Ignore;
            bar.Add(label);
            _musicTrack.Add(bar);
            Place(bar, m.StartUs, m.EndUs - m.StartUs);
            if (m.TrackLengthUs > 0) new BodyDrag(this, bar, EditTarget.MusicShift, -1, null);
        }

        void UpdatePlayhead()
        {
            if (_data == null) return;
            _playhead.style.left = RulerOffset() + _geo.ToX(_position);
        }

        internal long UsPerPixel(float px)
        {
            return (long)(px / _geo.PixelsPerSecond * TimelineGeometry.MicrosPerSecond);
        }

        internal void ScrubTo(float localX, bool dragging)
        {
            long t = _geo.ClampedTime(localX - RulerOffset());
            _position = t;
            UpdatePlayhead();
            Seek?.Invoke(t, dragging);
        }

        internal enum EditTarget
        {
            SimTrim = 0,
            VideoTrim = 1,
            VideoShift = 2,
            TextTrim = 3,
            TextShift = 4,
            MusicShift = 5,
        }

        internal void BeginEdit()
        {
            _editing = true;
        }

        TimelineTextItem TextItem(int layerIndex)
        {
            if (_data == null) return null;
            foreach (var t in _data.Texts)
                if (t.LayerIndex == layerIndex) return t;
            return null;
        }

        VisualElement TextPill(int layerIndex)
        {
            VisualElement found = null;
            _textTrack.Query<VisualElement>(className: "bv-timeline__text").ForEach(p =>
            {
                if (p.userData is int i && i == layerIndex) found = p;
            });
            return found;
        }

        internal void Preview(EditTarget target, bool startEdge, long deltaUs, int layerIndex)
        {
            if (_data == null) return;
            switch (target)
            {
                case EditTarget.SimTrim:
                {
                    var r = new TrimRange(_data.Sim.TrimStartUs, _data.Sim.TrimEndUs);
                    _simPreview = startEdge ? TimelineGeometry.DragStart(r, deltaUs, _data.Sim.SourceUs)
                        : TimelineGeometry.DragEnd(r, deltaUs, _data.Sim.SourceUs);
                    PlaceSegment(_simTrack.Q<VisualElement>(className: "bv-timeline__segment"), true);
                    break;
                }
                case EditTarget.VideoTrim:
                {
                    var r = new TrimRange(_data.Video.TrimStartUs, _data.Video.TrimEndUs);
                    _videoPreview = startEdge ? TimelineGeometry.DragStart(r, deltaUs, _data.Video.SourceUs)
                        : TimelineGeometry.DragEnd(r, deltaUs, _data.Video.SourceUs);
                    // Trimming the start keeps the remaining frames where they were on the timeline (no clamp at 0:
                    // V may start before S).
                    if (startEdge) _videoStartPreview = _data.Video.TimelineStartUs + (_videoPreview.StartUs - r.StartUs);
                    PlaceSegment(_videoTrack.Q<VisualElement>(className: "bv-timeline__segment"), false);
                    break;
                }
                case EditTarget.VideoShift:
                {
                    long offset = TimelineGeometry.ClampVideoOffset(_data.VideoOffsetUs + deltaUs, _data.Sim.LengthUs, _data.Video.LengthUs);
                    _videoStartPreview = _data.Sim.TimelineStartUs + offset;
                    PlaceSegment(_videoTrack.Q<VisualElement>(className: "bv-timeline__segment"), false);
                    break;
                }
                case EditTarget.TextTrim:
                case EditTarget.TextShift:
                {
                    var item = TextItem(layerIndex);
                    if (item == null) return;
                    long total = Math.Max(_data.DurationUs, item.EndUs);
                    var span = new TrimRange(item.StartUs, item.EndUs);
                    if (target == EditTarget.TextShift) _textPreview = TimelineGeometry.ShiftSpan(span, deltaUs, total);
                    else _textPreview = startEdge ? TimelineGeometry.DragStart(span, deltaUs, total) : TimelineGeometry.DragEnd(span, deltaUs, total);
                    _textEditIndex = layerIndex;
                    var pill = TextPill(layerIndex);
                    if (pill != null) Place(pill, _textPreview.StartUs, _textPreview.LengthUs);
                    break;
                }
                case EditTarget.MusicShift:
                {
                    var m = _data.Music;
                    if (m == null || m.TrackLengthUs <= 0) return;
                    // Dragging the bar right reveals an earlier part of the song (the song slides under the reel).
                    long maxStart = Math.Max(0, m.TrackLengthUs - TimelineGeometry.MinClipUs);
                    _musicStartPreview = Math.Max(0, Math.Min(maxStart, m.TrackStartUs - deltaUs));
                    var bar = _musicTrack.Q<VisualElement>(className: "bv-timeline__music");
                    if (bar != null) bar.style.translate = new Translate(_geo.Width(m.TrackStartUs - _musicStartPreview), 0);
                    break;
                }
            }
        }

        internal void Commit(EditTarget target, bool startEdge, int layerIndex)
        {
            _editing = false;
            if (_data == null) return;
            switch (target)
            {
                case EditTarget.SimTrim:
                    SimTrimCommitted?.Invoke(_simPreview.StartUs, _simPreview.EndUs);
                    break;
                case EditTarget.VideoTrim:
                    VideoTrimCommitted?.Invoke(_videoPreview.StartUs, _videoPreview.EndUs);
                    if (startEdge && _videoStartPreview != _data.Video.TimelineStartUs)
                        VideoOffsetCommitted?.Invoke(_videoStartPreview - _data.Sim.TimelineStartUs);
                    break;
                case EditTarget.VideoShift:
                    if (_videoStartPreview != _data.Video.TimelineStartUs)
                        VideoOffsetCommitted?.Invoke(_videoStartPreview - _data.Sim.TimelineStartUs);
                    break;
                case EditTarget.TextTrim:
                case EditTarget.TextShift:
                {
                    var item = TextItem(layerIndex);
                    if (item != null && _textEditIndex == layerIndex && (_textPreview.StartUs != item.StartUs || _textPreview.EndUs != item.EndUs))
                        TextTimingCommitted?.Invoke(layerIndex, _textPreview.StartUs, _textPreview.EndUs);
                    _textEditIndex = -1;
                    break;
                }
                case EditTarget.MusicShift:
                {
                    var bar = _musicTrack.Q<VisualElement>(className: "bv-timeline__music");
                    if (bar != null) bar.style.translate = new Translate(0, 0);
                    if (_data.Music != null && _musicStartPreview != _data.Music.TrackStartUs) MusicStartCommitted?.Invoke(_musicStartPreview);
                    break;
                }
            }
        }

        /// <summary>A press that turned out to be a tap: nothing changed.</summary>
        internal void EndEdit()
        {
            _editing = false;
        }

        internal void CancelEdit()
        {
            _editing = false;
            Relayout();
        }

        /// <summary>Pointer drag on the ruler → scrubbing.</summary>
        sealed class ScrubDrag
        {
            readonly VisualElement _target;
            readonly TimelineView _view;
            int _pointer = -1;

            public ScrubDrag(VisualElement target, TimelineView view)
            {
                _target = target;
                _view = view;
                target.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (_pointer >= 0) return;
                    _pointer = e.pointerId;
                    _target.CapturePointer(_pointer);
                    _view.ScrubTo(_view.WorldToLocal(e.position).x, true);
                    e.StopPropagation();
                });
                target.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (e.pointerId == _pointer) _view.ScrubTo(_view.WorldToLocal(e.position).x, true);
                });
                target.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    _view.ScrubTo(_view.WorldToLocal(e.position).x, false);
                    Release();
                });
                target.RegisterCallback<PointerCancelEvent>(e => Release());
            }

            void Release()
            {
                int id = _pointer;
                _pointer = -1;
                if (id >= 0 && _target.HasPointerCapture(id)) _target.ReleasePointer(id);
            }
        }

        /// <summary>
        /// Drag of an edge handle (trim of S / V, start / end of a text span). Movement below
        /// <see cref="ShiftThreshold"/> is a tap: nothing is trimmed and <c>tap</c> runs (if any).
        /// </summary>
        sealed class EdgeDrag
        {
            readonly TimelineView _view;
            readonly VisualElement _handle;
            readonly EditTarget _target;
            readonly bool _start;
            readonly int _layer;
            readonly Action _tap;
            int _pointer = -1;
            float _x0;
            bool _moved;

            public EdgeDrag(TimelineView view, VisualElement handle, EditTarget target, bool start, Action tap, int layer = -1)
            {
                _view = view;
                _handle = handle;
                _target = target;
                _start = start;
                _tap = tap;
                _layer = layer;
                handle.RegisterCallback<PointerDownEvent>(OnDown);
                handle.RegisterCallback<PointerMoveEvent>(OnMove);
                handle.RegisterCallback<PointerUpEvent>(OnUp);
                handle.RegisterCallback<PointerCancelEvent>(e => Finish(false));
            }

            void OnDown(PointerDownEvent e)
            {
                if (_pointer >= 0) return;
                _pointer = e.pointerId;
                _x0 = e.position.x;
                _moved = false;
                _handle.CapturePointer(_pointer);
                _handle.AddToClassList("bv-timeline__handle--active");
                _view.BeginEdit();
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (e.pointerId != _pointer) return;
                e.StopPropagation();
                float dx = e.position.x - _x0;
                if (!_moved && Mathf.Abs(dx) < ShiftThreshold) return;
                _moved = true;
                _view.Preview(_target, _start, _view.UsPerPixel(dx), _layer);
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.pointerId != _pointer) return;
                e.StopPropagation();
                if (!_moved)
                {
                    Release();
                    _view.EndEdit();
                    _tap?.Invoke();
                    return;
                }
                _view.Preview(_target, _start, _view.UsPerPixel(e.position.x - _x0), _layer);
                Finish(true);
            }

            void Release()
            {
                int id = _pointer;
                _pointer = -1;
                _handle.RemoveFromClassList("bv-timeline__handle--active");
                if (id >= 0 && _handle.HasPointerCapture(id)) _handle.ReleasePointer(id);
            }

            void Finish(bool commit)
            {
                if (_pointer < 0) return;
                Release();
                if (commit) _view.Commit(_target, _start, _layer);
                else _view.CancelEdit();
            }
        }

        /// <summary>
        /// Drag of a whole span (V shift, text span shift, music start). Movement below <see cref="ShiftThreshold"/> is a
        /// tap and runs <c>tap</c> instead.
        /// </summary>
        sealed class BodyDrag
        {
            readonly TimelineView _view;
            readonly VisualElement _element;
            readonly EditTarget _target;
            readonly int _layer;
            readonly Action _tap;
            int _pointer = -1;
            float _x0;
            bool _moved;

            public BodyDrag(TimelineView view, VisualElement element, EditTarget target, int layer, Action tap)
            {
                _view = view;
                _element = element;
                _target = target;
                _layer = layer;
                _tap = tap;
                element.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (_pointer >= 0) return;
                    _pointer = e.pointerId;
                    _x0 = e.position.x;
                    _moved = false;
                    _element.CapturePointer(_pointer);
                    _view.BeginEdit();
                    e.StopPropagation();
                });
                element.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    float dx = e.position.x - _x0;
                    if (!_moved && Mathf.Abs(dx) < ShiftThreshold) return;
                    _moved = true;
                    _element.AddToClassList("bv-timeline__segment--dragging");
                    _view.Preview(_target, false, _view.UsPerPixel(dx), _layer);
                });
                element.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    bool moved = _moved;
                    Release();
                    if (moved) _view.Commit(_target, false, _layer);
                    else
                    {
                        _view.EndEdit();
                        _tap?.Invoke();
                    }
                });
                element.RegisterCallback<PointerCancelEvent>(e =>
                {
                    if (_pointer < 0) return;
                    Release();
                    _view.CancelEdit();
                });
            }

            void Release()
            {
                int id = _pointer;
                _pointer = -1;
                _element.RemoveFromClassList("bv-timeline__segment--dragging");
                if (id >= 0 && _element.HasPointerCapture(id)) _element.ReleasePointer(id);
            }
        }
    }
}
