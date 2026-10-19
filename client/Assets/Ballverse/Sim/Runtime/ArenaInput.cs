using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Sim
{
    /// <summary>Touch phase on the arena (mirrors the UI's arena pointer phases without depending on the UI assembly).</summary>
    public enum ArenaTouchPhase
    {
        Down = 0,
        Move = 1,
        Up = 2,
        Cancel = 3,
    }

    /// <summary>Kind of an <see cref="ArenaGesture"/>.</summary>
    public enum ArenaGestureKind : byte
    {
        /// <summary>A finger touched the arena.</summary>
        Press = 0,
        /// <summary>The (smoothed) horizontal finger velocity changed while the finger is down.</summary>
        Drag = 1,
        /// <summary>The finger was lifted (or the touch cancelled); <see cref="ArenaGesture.Velocity"/> is the fling velocity.</summary>
        Release = 2,
        /// <summary>A short press that barely moved (sent after its <see cref="Release"/>).</summary>
        Tap = 3,
    }

    /// <summary>
    /// A device-independent arena gesture. Never sent to the simulation as is: the mode's <see cref="IArenaInputMapper"/> turns
    /// it into the mode's own <c>SimInput</c> encoding (CONTRACT.md §3 rule 5: "SimInput.Type = mode-defined byte").
    /// </summary>
    public readonly struct ArenaGesture
    {
        public ArenaGesture(ArenaGestureKind kind, float velocity, float u, float v)
        {
            Kind = kind;
            Velocity = velocity;
            U = u;
            V = v;
        }

        public ArenaGestureKind Kind { get; }

        /// <summary>Horizontal finger velocity in arena widths per second, right positive (Drag, Release).</summary>
        public float Velocity { get; }

        /// <summary>Finger position, 0–1 over the arena, origin bottom-left.</summary>
        public float U { get; }
        public float V { get; }
    }

    /// <summary>One mode-encoded input to enqueue at the next tick boundary (<c>ModeSession.EnqueueInput(type, a, b, c, d)</c>).</summary>
    public readonly struct ArenaCommand
    {
        public ArenaCommand(byte type, int a, int b = 0, int c = 0, int d = 0)
        {
            Type = type;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public byte Type { get; }
        public int A { get; }
        public int B { get; }
        public int C { get; }
        public int D { get; }
    }

    /// <summary>
    /// The mode-owned gesture → <c>SimInput</c> mapping (spec §2.6, CONTRACT.md §3 rule 5). A mode receives player input only
    /// when a mapper is registered for its id in <see cref="ArenaInputMapperRegistry"/> and <see cref="AcceptsInput"/> says
    /// so for the run's parameters; every other mode gets no INPT at all. Main thread; one instance per controller.
    /// </summary>
    public interface IArenaInputMapper
    {
        /// <summary>The run takes player input with these parameters (e.g. M05 only with <c>control = player</c>).</summary>
        bool AcceptsInput(ParamSet values);

        /// <summary>A new run starts (drop per-run state).</summary>
        void BeginRun();

        /// <summary>
        /// Maps one gesture at a tick boundary. <paramref name="values"/> are the run's current parameters (live edits applied),
        /// <paramref name="pointsPerArenaWidth"/> the arena's displayed width in points (iOS pt / Android dp). False = no input.
        /// </summary>
        bool TryMap(in ArenaGesture gesture, ParamSet values, float pointsPerArenaWidth, out ArenaCommand command);
    }

    /// <summary>Mode-id → input mapper. <see cref="CreateRelease1"/> has the release-1 interactive mode (M05 Helix Drop).</summary>
    public sealed class ArenaInputMapperRegistry
    {
        readonly Dictionary<string, IArenaInputMapper> _mappers = new Dictionary<string, IArenaInputMapper>(StringComparer.Ordinal);

        /// <summary>The mappers of release 1: <see cref="HelixDropInputMapper"/> for <c>helix_drop</c>.</summary>
        public static ArenaInputMapperRegistry CreateRelease1()
        {
            var r = new ArenaInputMapperRegistry();
            r.Register(HelixDropInputMapper.ModeId, new HelixDropInputMapper());
            return r;
        }

        public void Register(string modeId, IArenaInputMapper mapper)
        {
            if (string.IsNullOrEmpty(modeId)) throw new ArgumentException("modeId must not be empty", nameof(modeId));
            _mappers[modeId] = mapper ?? throw new ArgumentNullException(nameof(mapper));
        }

        public IArenaInputMapper Find(string modeId)
        {
            return modeId != null && _mappers.TryGetValue(modeId, out IArenaInputMapper m) ? m : null;
        }
    }

    /// <summary>The <c>control</c> parameter of modes with automatic / bot / player variants (M05, spec §2.9).</summary>
    public static class ArenaControl
    {
        public const string Param = "control";
        public const string Player = "player";

        /// <summary>
        /// True when the set has no <c>control</c> parameter (always player-driven) or it is exactly <c>player</c>
        /// (<c>auto</c> and <c>bot</c> are driven by the rules and record no touches).
        /// </summary>
        public static bool IsPlayer(ParamSet values)
        {
            if (values == null) return false;
            if (!values.TryGet(Param, out ParamValue v)) return true;
            return v.Kind == ParamValueKind.String && string.Equals(v.AsString, Player, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// M05 «Хеликс-дроп» «Игрок» input (spec §2.9 M05 «Управление»; M05 <c>HelixDropInput</c>): while the finger is down the
    /// tower turns at the commanded speed — <see cref="Spin"/> (A = mrad/s) on press (0: the finger holds the tower) and
    /// whenever the swipe speed changes; on release <see cref="Release"/> (A = fling speed, mrad/s; the mode decays it to 0 over
    /// 9 ticks). Taps do nothing. Speed φ' = −Δx_pt/s · 0.6 °/pt · swipeSensitivity (dragging right turns the front right),
    /// clamped to ±360 °/s and quantized to mrad/s — the same formula and constants as M05's <c>HelixDropInput.FromSwipe</c>,
    /// duplicated here because M05 lives on its own branch (see the App README "Open issues").
    /// </summary>
    public sealed class HelixDropInputMapper : IArenaInputMapper
    {
        public const string ModeId = "helix_drop";
        public const byte Spin = 1;
        public const byte Release = 2;
        public const int UnitsPerRadian = 1000;
        public const float DegreesPerPoint = 0.6f;
        public const float MaxSpinDeg = 360f;
        public const string SwipeSensitivityParam = "swipeSensitivity";

        const int None = int.MinValue;
        int _lastSpin = None;

        public bool AcceptsInput(ParamSet values) => ArenaControl.IsPlayer(values);

        public void BeginRun()
        {
            _lastSpin = None;
        }

        public bool TryMap(in ArenaGesture gesture, ParamSet values, float pointsPerArenaWidth, out ArenaCommand command)
        {
            command = default;
            float sensitivity = values != null && values.TryGet(SwipeSensitivityParam, out ParamValue s) && s.Kind == ParamValueKind.Float ? (float)s.AsFloat : 1f;
            switch (gesture.Kind)
            {
                case ArenaGestureKind.Press:
                    return SpinAt(0, out command);
                case ArenaGestureKind.Drag:
                    return SpinAt(FromSwipe(gesture.Velocity * pointsPerArenaWidth, sensitivity), out command);
                case ArenaGestureKind.Release:
                    _lastSpin = None;
                    command = new ArenaCommand(Release, FromSwipe(gesture.Velocity * pointsPerArenaWidth, sensitivity));
                    return true;
                default:
                    return false;
            }
        }

        bool SpinAt(int units, out ArenaCommand command)
        {
            if (units == _lastSpin)
            {
                command = default;
                return false;
            }
            _lastSpin = units;
            command = new ArenaCommand(Spin, units);
            return true;
        }

        /// <summary>Swipe speed (points per second, right positive) → quantized tower speed in mrad/s (M05 <c>FromSwipe</c>).</summary>
        public static int FromSwipe(float pointsPerSecond, float sensitivity)
        {
            if (float.IsNaN(pointsPerSecond) || float.IsNaN(sensitivity)) return 0;
            float sens = SimMath.Clamp(sensitivity, 0.5f, 2f);
            float max = MaxSpinDeg * SimMath.DegToRad;
            float v = SimMath.Clamp(-pointsPerSecond * DegreesPerPoint * sens * SimMath.DegToRad, -max, max);
            return ReplayQuantization.QuantizeFixed(v, UnitsPerRadian);
        }
    }

    /// <summary>
    /// Turns raw arena touches (normalized 0–1 coordinates over the render target, origin bottom-left, as the UI sends them)
    /// into <see cref="ArenaGesture"/>s: a press, the smoothed horizontal finger velocity while dragging (emitted when it
    /// changed, at most once per flush), the release with the fling velocity, and taps (short press that barely moved).
    /// One finger at a time; other pointers are ignored. Allocation-free; main thread.
    /// </summary>
    public sealed class ArenaGestureTracker
    {
        /// <summary>A press shorter than this that moved less than <see cref="TapSlop"/> is a tap.</summary>
        public const double TapMaxSeconds = 0.3;
        public const float TapSlop = 0.02f;
        /// <summary>A finger that has not moved for this long has zero velocity.</summary>
        public const double StillSeconds = 0.06;
        /// <summary>Smoothing of the velocity estimate per move (exponential moving average weight of the newest sample).</summary>
        public const float Smoothing = 0.5f;
        /// <summary>Velocity changes smaller than this (arena widths per second) are not re-emitted.</summary>
        public const float VelocityEpsilon = 0.001f;
        /// <summary>Largest velocity magnitude emitted, arena widths per second (a fast flick is ~10).</summary>
        public const float MaxVelocity = 20f;

        const int Capacity = 16;
        readonly ArenaGesture[] _queue = new ArenaGesture[Capacity];
        int _head;
        int _count;

        int _pointer = -1;
        float _downU, _downV;
        double _downTime;
        float _lastU, _lastV;
        double _lastTime;
        float _velocity;
        float _sentVelocity;
        bool _moved;
        bool _dragDirty;

        public bool IsTouching => _pointer >= 0;

        /// <summary>Gestures waiting for the next flush.</summary>
        public int Pending => _count;

        public void OnTouch(ArenaTouchPhase phase, float u, float v, int pointerId, double timeSeconds)
        {
            switch (phase)
            {
                case ArenaTouchPhase.Down:
                    if (_pointer >= 0) return;
                    _pointer = pointerId;
                    _downU = _lastU = u;
                    _downV = _lastV = v;
                    _downTime = _lastTime = timeSeconds;
                    _velocity = 0f;
                    _sentVelocity = 0f;
                    _moved = false;
                    _dragDirty = false;
                    Enqueue(new ArenaGesture(ArenaGestureKind.Press, 0f, Clamp01(u), Clamp01(v)));
                    break;
                case ArenaTouchPhase.Move:
                    if (pointerId != _pointer) return;
                    double dt = timeSeconds - _lastTime;
                    if (dt > 1e-4)
                    {
                        float sample = (float)((u - _lastU) / dt);
                        _velocity = _velocity + Smoothing * (sample - _velocity);
                        _lastU = u;
                        _lastV = v;
                        _lastTime = timeSeconds;
                    }
                    if (Math.Abs(u - _downU) > TapSlop || Math.Abs(v - _downV) > TapSlop) _moved = true;
                    _dragDirty = true;
                    break;
                case ArenaTouchPhase.Up:
                case ArenaTouchPhase.Cancel:
                    if (pointerId != _pointer) return;
                    bool tap = phase == ArenaTouchPhase.Up && !_moved && timeSeconds - _downTime <= TapMaxSeconds;
                    float fling = timeSeconds - _lastTime > StillSeconds ? 0f : _velocity;
                    Enqueue(new ArenaGesture(ArenaGestureKind.Release, ClampVelocity(fling), Clamp01(u), Clamp01(v)));
                    if (tap) Enqueue(new ArenaGesture(ArenaGestureKind.Tap, 0f, Clamp01(u), Clamp01(v)));
                    _pointer = -1;
                    _dragDirty = false;
                    _velocity = 0f;
                    break;
            }
        }

        /// <summary>
        /// Called once per display frame before the ticks of that frame: emits the current drag velocity when it changed
        /// (a still finger decays to 0 after <see cref="StillSeconds"/>).
        /// </summary>
        public void Update(double timeSeconds)
        {
            if (_pointer < 0) return;
            if (timeSeconds - _lastTime > StillSeconds && _velocity != 0f)
            {
                _velocity = 0f;
                _dragDirty = true;
            }
            if (!_dragDirty) return;
            _dragDirty = false;
            float v = ClampVelocity(_velocity);
            if (Math.Abs(v - _sentVelocity) < VelocityEpsilon && !(v == 0f && _sentVelocity != 0f)) return;
            _sentVelocity = v;
            Enqueue(new ArenaGesture(ArenaGestureKind.Drag, v, Clamp01(_lastU), Clamp01(_lastV)));
        }

        public bool TryDequeue(out ArenaGesture gesture)
        {
            if (_count == 0)
            {
                gesture = default;
                return false;
            }
            gesture = _queue[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }

        /// <summary>Drops the touch and every pending gesture (new run, screen hidden).</summary>
        public void Reset()
        {
            _pointer = -1;
            _count = 0;
            _head = 0;
            _velocity = 0f;
            _dragDirty = false;
        }

        void Enqueue(in ArenaGesture g)
        {
            if (_count == Capacity)
            {
                // Keep the newest state: drop the oldest gesture.
                _head = (_head + 1) % Capacity;
                _count--;
            }
            _queue[(_head + _count) % Capacity] = g;
            _count++;
        }

        static float Clamp01(float x) => float.IsNaN(x) ? 0f : x < 0f ? 0f : x > 1f ? 1f : x;

        static float ClampVelocity(float x) => float.IsNaN(x) ? 0f : x > MaxVelocity ? MaxVelocity : x < -MaxVelocity ? -MaxVelocity : x;
    }
}
