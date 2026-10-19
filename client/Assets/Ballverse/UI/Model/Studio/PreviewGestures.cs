using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;

namespace Ballverse.UI.Studio
{
    /// <summary>A slot of the preview as the gesture recognizer sees it (normalized rect, origin top-left).</summary>
    public readonly struct GestureSlot
    {
        public SlotKind Slot { get; }
        public FloatRect Rect { get; }
        public bool IsPip { get; }

        public GestureSlot(SlotKind slot, FloatRect rect, bool isPip)
        {
            Slot = slot;
            Rect = rect;
            IsPip = isPip;
        }

        /// <summary>Hit test on the bounding rect (also for a circular PiP, like the Core magnets).</summary>
        public bool Contains(FloatVec2 p)
        {
            if (Rect.IsEmpty) return false;
            return p.X >= Rect.X && p.X <= Rect.Right && p.Y >= Rect.Y && p.Y <= Rect.Bottom;
        }
    }

    /// <summary>What a recognized preview gesture asks the studio to do.</summary>
    public interface IPreviewGestureSink
    {
        /// <summary>PiP window follows the finger (<paramref name="final"/> on release: the VM snaps it with Core PipMagnets).</summary>
        void MovePip(FloatVec2 center, bool final);
        /// <summary>Pan (fraction of the slot since the last call) and zoom (factor since the last call) of a slot's content.</summary>
        void PanZoom(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final);
        /// <summary>Double tap: pan and zoom of the slot back to the default.</summary>
        void ResetSlot(SlotKind slot);
    }

    /// <summary>
    /// Studio preview gestures (spec §3.3, §4.6): one finger on the PiP window drags it; one finger on a slot pans its
    /// content; two fingers pinch-zoom (and pan with the midpoint) the slot under the first finger; a double tap resets the
    /// slot. Positions are preview pixels (origin top-left) with the preview size; slots are normalized. Pure: no engine,
    /// time comes from the caller.
    /// </summary>
    public sealed class PreviewGestures
    {
        /// <summary>Movement in pixels before a press becomes a drag.</summary>
        public const float Slop = 8f;
        /// <summary>Max seconds between the two taps of a double tap, and max press length of each tap.</summary>
        public const float DoubleTapSeconds = 0.3f;
        /// <summary>Max distance in pixels between the two taps of a double tap.</summary>
        public const float DoubleTapDistance = 32f;

        enum Mode
        {
            None = 0,
            Pending = 1,
            PipDrag = 2,
            Pan = 3,
            Pinch = 4,
        }

        readonly IPreviewGestureSink _sink;
        readonly Dictionary<int, FloatVec2> _pointers = new Dictionary<int, FloatVec2>();
        Mode _mode;
        int _primary = -1;
        int _secondary = -1;
        GestureSlot _slot;
        float _width = 1f;
        float _height = 1f;
        FloatVec2 _downPos;
        float _downTime;
        FloatVec2 _pipGrab;
        FloatVec2 _lastMid;
        float _lastDistance;
        bool _changed;
        bool _hasLastTap;
        FloatVec2 _lastTapPos;
        float _lastTapTime;
        SlotKind _lastTapSlot;

        public PreviewGestures(IPreviewGestureSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public bool Active => _mode != Mode.None;

        /// <param name="position">Pointer position in preview pixels.</param>
        /// <param name="previewWidth">Preview size in pixels.</param>
        /// <param name="slots">Slots bottom to top (PiP last).</param>
        /// <returns>True when the press is used by a gesture (the caller captures the pointer).</returns>
        public bool Down(int pointerId, FloatVec2 position, float previewWidth, float previewHeight, IReadOnlyList<GestureSlot> slots,
            float time)
        {
            if (!(previewWidth > 0f) || !(previewHeight > 0f)) return false;
            if (_mode == Mode.None)
            {
                _width = previewWidth;
                _height = previewHeight;
                if (!HitTest(Normalize(position), slots, out _slot)) return false;
                _pointers.Clear();
                _pointers[pointerId] = position;
                _primary = pointerId;
                _secondary = -1;
                _downPos = position;
                _downTime = time;
                _changed = false;
                _mode = Mode.Pending;
                if (_slot.IsPip)
                {
                    var c = _slot.Rect.Center;
                    var p = Normalize(position);
                    _pipGrab = new FloatVec2(p.X - c.X, p.Y - c.Y);
                }
                return true;
            }
            if (_secondary >= 0 || _pointers.ContainsKey(pointerId)) return false;
            // Second finger: pinch the slot under the first finger (a PiP drag turns into zooming the PiP content).
            _pointers[pointerId] = position;
            _secondary = pointerId;
            if (_mode == Mode.PipDrag) _sink.MovePip(PipCenter(_pointers[_primary]), true);
            _mode = Mode.Pinch;
            _lastMid = Mid();
            _lastDistance = Distance();
            return true;
        }

        public void Move(int pointerId, FloatVec2 position)
        {
            if (_mode == Mode.None || !_pointers.ContainsKey(pointerId)) return;
            _pointers[pointerId] = position;
            switch (_mode)
            {
                case Mode.Pending:
                    if (pointerId != _primary || Dist(position, _downPos) <= Slop) return;
                    _mode = _slot.IsPip ? Mode.PipDrag : Mode.Pan;
                    _lastMid = _downPos;
                    Move(pointerId, position);
                    return;
                case Mode.PipDrag:
                    if (pointerId == _primary) _sink.MovePip(PipCenter(position), false);
                    return;
                case Mode.Pan:
                    if (pointerId != _primary) return;
                    EmitPan(position, 1f);
                    return;
                case Mode.Pinch:
                {
                    float d = Distance();
                    float factor = _lastDistance > 1f && d > 1f ? d / _lastDistance : 1f;
                    _lastDistance = d;
                    EmitPan(Mid(), factor);
                    return;
                }
            }
        }

        public void Up(int pointerId, FloatVec2 position, float time)
        {
            if (_mode == Mode.None || !_pointers.ContainsKey(pointerId)) return;
            _pointers[pointerId] = position;
            switch (_mode)
            {
                case Mode.Pending:
                    Finish();
                    RegisterTap(position, time);
                    return;
                case Mode.PipDrag:
                    _sink.MovePip(PipCenter(position), true);
                    Finish();
                    return;
                case Mode.Pan:
                    if (_changed) _sink.PanZoom(_slot.Slot, FloatVec2.Zero, 1f, true);
                    Finish();
                    return;
                case Mode.Pinch:
                    _pointers.Remove(pointerId);
                    if (pointerId == _primary)
                    {
                        _primary = _secondary;
                    }
                    _secondary = -1;
                    // The remaining finger keeps panning.
                    _mode = Mode.Pan;
                    _lastMid = _pointers[_primary];
                    return;
            }
        }

        /// <summary>Pointer lost (capture lost, cancel): commits what was changed.</summary>
        public void Cancel(int pointerId)
        {
            if (_mode == Mode.None || !_pointers.ContainsKey(pointerId)) return;
            if (_mode == Mode.PipDrag) _sink.MovePip(PipCenter(_pointers[_primary]), true);
            else if ((_mode == Mode.Pan || _mode == Mode.Pinch) && _changed) _sink.PanZoom(_slot.Slot, FloatVec2.Zero, 1f, true);
            Finish();
        }

        void EmitPan(FloatVec2 position, float factor)
        {
            float w = Math.Max(1e-3f, _slot.Rect.Width * _width);
            float h = Math.Max(1e-3f, _slot.Rect.Height * _height);
            var delta = new FloatVec2((position.X - _lastMid.X) / w, (position.Y - _lastMid.Y) / h);
            _lastMid = position;
            if (delta.X == 0f && delta.Y == 0f && factor == 1f) return;
            _changed = true;
            _sink.PanZoom(_slot.Slot, delta, factor, false);
        }

        void RegisterTap(FloatVec2 position, float time)
        {
            bool shortPress = time - _downTime <= DoubleTapSeconds;
            if (!shortPress)
            {
                _hasLastTap = false;
                return;
            }
            if (_hasLastTap && time - _lastTapTime <= DoubleTapSeconds && Dist(position, _lastTapPos) <= DoubleTapDistance
                && _lastTapSlot == _slot.Slot)
            {
                _hasLastTap = false;
                _sink.ResetSlot(_slot.Slot);
                return;
            }
            _hasLastTap = true;
            _lastTapPos = position;
            _lastTapTime = time;
            _lastTapSlot = _slot.Slot;
        }

        void Finish()
        {
            _mode = Mode.None;
            _pointers.Clear();
            _primary = -1;
            _secondary = -1;
            _changed = false;
        }

        FloatVec2 PipCenter(FloatVec2 position)
        {
            var p = Normalize(position);
            return new FloatVec2(Clamp01(p.X - _pipGrab.X), Clamp01(p.Y - _pipGrab.Y));
        }

        FloatVec2 Normalize(FloatVec2 p)
        {
            return new FloatVec2(p.X / _width, p.Y / _height);
        }

        FloatVec2 Mid()
        {
            var a = _pointers[_primary];
            var b = _pointers[_secondary];
            return new FloatVec2((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
        }

        float Distance()
        {
            return Dist(_pointers[_primary], _pointers[_secondary]);
        }

        static float Dist(FloatVec2 a, FloatVec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        static bool HitTest(FloatVec2 p, IReadOnlyList<GestureSlot> slots, out GestureSlot hit)
        {
            if (slots != null)
            {
                for (int i = slots.Count - 1; i >= 0; i--)
                {
                    if (!slots[i].Contains(p)) continue;
                    hit = slots[i];
                    return true;
                }
            }
            hit = default;
            return false;
        }
    }
}
