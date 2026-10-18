using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.UI.Components;
using Ballverse.UI.Layout;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Studio timeline (spec §3.4, §4.3): ruler with scrubbing, tracks S (simulation clip), V (user video), text and
    /// music; trim handles on S and V, V can be dragged to shift it against S. Edits are previewed while dragging and
    /// committed on release through the events.
    /// </summary>
    public sealed class TimelineView : VisualElement
    {
        const float HandleWidth = 18f;

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
        bool _editing;

        /// <summary>(time, dragging): scrub request.</summary>
        public event Action<long, bool> Seek;
        public event Action<long, long> SimTrimCommitted;
        public event Action<long, long> VideoTrimCommitted;
        public event Action<long> VideoStartCommitted;
        public event Action<int> TextSelected;
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
            if (_data == null) return;
            float w = TrackWidth;
            if (float.IsNaN(w) || w <= 0) return;
            _geo.SetViewWidth(w);
            _geo.SetTotal(Math.Max(_data.DurationUs, TimelineGeometry.MinClipUs));
            _geo.FitAll();
            _simPreview = new TrimRange(_data.Sim.TrimStartUs, _data.Sim.TrimEndUs);
            _videoPreview = new TrimRange(_data.Video.TrimStartUs, _data.Video.TrimEndUs);
            _videoStartPreview = _data.Video.TimelineStartUs;
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
            seg.Add(thumbs);
            var left = Handle(seg, true);
            var right = Handle(seg, false);
            seg.Add(left);
            seg.Add(right);
            track.Add(seg);
            PlaceSegment(seg, isSim);
            new TrimDrag(this, left, isSim, true);
            new TrimDrag(this, right, isSim, false);
            if (!isSim) new ShiftDrag(this, seg);
        }

        static VisualElement Handle(VisualElement seg, bool left)
        {
            var h = new VisualElement();
            h.AddToClassList("bv-timeline__handle");
            h.AddToClassList(left ? "bv-timeline__handle--left" : "bv-timeline__handle--right");
            var grip = new VisualElement();
            grip.AddToClassList("bv-timeline__grip");
            grip.pickingMode = PickingMode.Ignore;
            h.Add(grip);
            return h;
        }

        void PlaceSegment(VisualElement seg, bool isSim)
        {
            long start = isSim ? _data.Sim.TimelineStartUs : _videoStartPreview;
            var range = isSim ? _simPreview : _videoPreview;
            if (isSim && range.StartUs != _data.Sim.TrimStartUs)
            {
                // While the start handle moves, show the change at the left edge of the clip.
                start += range.StartUs - _data.Sim.TrimStartUs;
            }
            seg.style.left = _geo.ToX(start);
            seg.style.width = Mathf.Max(HandleWidth * 2f, _geo.Width(range.LengthUs));
        }

        void BuildTextTrack()
        {
            _textTrack.Clear();
            foreach (var item in _data.Texts)
            {
                var pill = new VisualElement();
                pill.AddToClassList("bv-timeline__text");
                pill.style.left = _geo.ToX(item.StartUs);
                pill.style.width = Mathf.Max(24f, _geo.Width(item.EndUs - item.StartUs));
                pill.Add(UiContext.TextLabel(item.Text, "bv-timeline__text-label"));
                int index = item.LayerIndex;
                Tap.On(pill, () => TextSelected?.Invoke(index));
                _textTrack.Add(pill);
            }
        }

        void BuildMusicTrack()
        {
            _musicTrack.Clear();
            var m = _data.Music;
            _musicTrack.parent.style.display = m != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m == null) return;
            var bar = new VisualElement();
            bar.AddToClassList("bv-timeline__music");
            bar.style.left = _geo.ToX(m.StartUs);
            bar.style.width = Mathf.Max(24f, _geo.Width(m.EndUs - m.StartUs));
            bar.Add(UiContext.TextLabel(_ctx.L(m.Name), "bv-timeline__text-label"));
            _musicTrack.Add(bar);
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

        internal void BeginEdit()
        {
            _editing = true;
        }

        internal void PreviewTrim(bool isSim, bool startHandle, long deltaUs)
        {
            if (_data == null) return;
            if (isSim)
            {
                var r = new TrimRange(_data.Sim.TrimStartUs, _data.Sim.TrimEndUs);
                _simPreview = startHandle ? TimelineGeometry.DragStart(r, deltaUs, _data.Sim.SourceUs)
                    : TimelineGeometry.DragEnd(r, deltaUs, _data.Sim.SourceUs);
                PlaceSegment(_simTrack.Q<VisualElement>(className: "bv-timeline__segment"), true);
            }
            else
            {
                var r = new TrimRange(_data.Video.TrimStartUs, _data.Video.TrimEndUs);
                _videoPreview = startHandle ? TimelineGeometry.DragStart(r, deltaUs, _data.Video.SourceUs)
                    : TimelineGeometry.DragEnd(r, deltaUs, _data.Video.SourceUs);
                if (startHandle) _videoStartPreview = Math.Max(0, _data.Video.TimelineStartUs + (_videoPreview.StartUs - r.StartUs));
                PlaceSegment(_videoTrack.Q<VisualElement>(className: "bv-timeline__segment"), false);
            }
        }

        internal void CommitTrim(bool isSim, bool startHandle)
        {
            _editing = false;
            if (_data == null) return;
            if (isSim) SimTrimCommitted?.Invoke(_simPreview.StartUs, _simPreview.EndUs);
            else
            {
                VideoTrimCommitted?.Invoke(_videoPreview.StartUs, _videoPreview.EndUs);
                if (startHandle && _videoStartPreview != _data.Video.TimelineStartUs) VideoStartCommitted?.Invoke(_videoStartPreview);
            }
        }

        internal void CancelEdit()
        {
            _editing = false;
            Relayout();
        }

        internal void PreviewShift(long deltaUs)
        {
            if (_data == null) return;
            long max = Math.Max(0, _data.DurationUs - TimelineGeometry.MinClipUs);
            _videoStartPreview = Math.Max(0, Math.Min(max, _data.Video.TimelineStartUs + deltaUs));
            PlaceSegment(_videoTrack.Q<VisualElement>(className: "bv-timeline__segment"), false);
        }

        internal void CommitShift()
        {
            _editing = false;
            if (_data != null && _videoStartPreview != _data.Video.TimelineStartUs) VideoStartCommitted?.Invoke(_videoStartPreview);
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

        /// <summary>Drag of a trim handle.</summary>
        sealed class TrimDrag
        {
            readonly TimelineView _view;
            readonly VisualElement _handle;
            readonly bool _isSim;
            readonly bool _start;
            int _pointer = -1;
            float _x0;

            public TrimDrag(TimelineView view, VisualElement handle, bool isSim, bool start)
            {
                _view = view;
                _handle = handle;
                _isSim = isSim;
                _start = start;
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
                _handle.CapturePointer(_pointer);
                _handle.AddToClassList("bv-timeline__handle--active");
                _view.BeginEdit();
                e.StopPropagation();
            }

            void OnMove(PointerMoveEvent e)
            {
                if (e.pointerId != _pointer) return;
                _view.PreviewTrim(_isSim, _start, _view.UsPerPixel(e.position.x - _x0));
                e.StopPropagation();
            }

            void OnUp(PointerUpEvent e)
            {
                if (e.pointerId != _pointer) return;
                _view.PreviewTrim(_isSim, _start, _view.UsPerPixel(e.position.x - _x0));
                Finish(true);
                e.StopPropagation();
            }

            void Finish(bool commit)
            {
                if (_pointer < 0) return;
                int id = _pointer;
                _pointer = -1;
                _handle.RemoveFromClassList("bv-timeline__handle--active");
                if (_handle.HasPointerCapture(id)) _handle.ReleasePointer(id);
                if (commit) _view.CommitTrim(_isSim, _start);
                else _view.CancelEdit();
            }
        }

        /// <summary>Drag of the V segment body: shifts the video against the simulation.</summary>
        sealed class ShiftDrag
        {
            const float StartThreshold = 8f;
            readonly TimelineView _view;
            readonly VisualElement _segment;
            int _pointer = -1;
            float _x0;
            bool _moved;

            public ShiftDrag(TimelineView view, VisualElement segment)
            {
                _view = view;
                _segment = segment;
                segment.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (_pointer >= 0) return;
                    _pointer = e.pointerId;
                    _x0 = e.position.x;
                    _moved = false;
                    _segment.CapturePointer(_pointer);
                    _view.BeginEdit();
                });
                segment.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    float dx = e.position.x - _x0;
                    if (!_moved && Mathf.Abs(dx) < StartThreshold) return;
                    _moved = true;
                    _segment.AddToClassList("bv-timeline__segment--dragging");
                    _view.PreviewShift(_view.UsPerPixel(dx));
                });
                segment.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    Release();
                    _view.CommitShift();
                });
                segment.RegisterCallback<PointerCancelEvent>(e =>
                {
                    Release();
                    _view.CommitShift();
                });
            }

            void Release()
            {
                int id = _pointer;
                _pointer = -1;
                _segment.RemoveFromClassList("bv-timeline__segment--dragging");
                if (id >= 0 && _segment.HasPointerCapture(id)) _segment.ReleasePointer(id);
            }
        }
    }
}
