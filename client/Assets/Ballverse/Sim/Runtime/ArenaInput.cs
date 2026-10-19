using System;

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

    /// <summary>
    /// Generic arena gestures the live runner turns into <c>Core.Sim.SimInput</c>s at tick boundaries (spec §2.6: input is
    /// quantized to the tick and recorded in the replay as INPT). The mode decides what a gesture does; the encoding is the
    /// App → mode input contract for release 1:
    /// <list type="table">
    /// <item><term><see cref="HorizontalDrag"/></term><description>A = horizontal finger velocity in 1/<see cref="Scale"/>
    /// arena widths per second (signed, +right), B = 1 while the finger is down / 0 on release (the last A on release is the
    /// fling velocity, the mode applies its own inertia), C = finger x in 1/<see cref="Scale"/> of the arena width. M05 «Игрок»:
    /// tower angular velocity φ' = A · k · swipeSensitivity (spec §2.9 M05).</description></item>
    /// <item><term><see cref="Tap"/></term><description>A = x, B = y in 1/<see cref="Scale"/> of the arena (origin bottom-left).</description></item>
    /// </list>
    /// Only modes whose catalog entry is interactive receive inputs.
    /// </summary>
    public static class ArenaInputTypes
    {
        public const byte HorizontalDrag = 1;
        public const byte Tap = 2;

        /// <summary>Fixed-point scale of positions and velocities.</summary>
        public const int Scale = 10000;

        /// <summary>Largest velocity magnitude sent, arena widths per second (a fast flick is ~10).</summary>
        public const float MaxVelocity = 20f;

        public static int QuantizeUnit(float value)
        {
            if (float.IsNaN(value)) return 0;
            if (value < 0f) value = 0f;
            if (value > 1f) value = 1f;
            return (int)Math.Round(value * Scale);
        }

        public static int QuantizeVelocity(float widthsPerSecond)
        {
            if (float.IsNaN(widthsPerSecond)) return 0;
            if (widthsPerSecond > MaxVelocity) widthsPerSecond = MaxVelocity;
            if (widthsPerSecond < -MaxVelocity) widthsPerSecond = -MaxVelocity;
            return (int)Math.Round(widthsPerSecond * Scale);
        }
    }

    /// <summary>One input to enqueue at the next tick boundary.</summary>
    public readonly struct ArenaCommand
    {
        public ArenaCommand(byte type, int a, int b, int c, int d)
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
    /// Turns raw arena touches (normalized 0–1 coordinates over the render target, origin bottom-left, as the UI sends them)
    /// into <see cref="ArenaCommand"/>s: a smoothed horizontal finger velocity while dragging (sent when its quantized value
    /// changes, at most once per flush), a release with the fling velocity, and taps (short press that barely moved).
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

        const int Capacity = 16;
        readonly ArenaCommand[] _queue = new ArenaCommand[Capacity];
        int _head;
        int _count;

        int _pointer = -1;
        float _downU, _downV;
        double _downTime;
        float _lastU;
        double _lastTime;
        float _velocity;
        int _sentVelocity;
        bool _moved;
        bool _dragDirty;

        public bool IsTouching => _pointer >= 0;

        /// <summary>Commands waiting for the next flush.</summary>
        public int Pending => _count;

        public void OnTouch(ArenaTouchPhase phase, float u, float v, int pointerId, double timeSeconds)
        {
            switch (phase)
            {
                case ArenaTouchPhase.Down:
                    if (_pointer >= 0) return;
                    _pointer = pointerId;
                    _downU = _lastU = u;
                    _downV = v;
                    _downTime = _lastTime = timeSeconds;
                    _velocity = 0f;
                    _sentVelocity = 0;
                    _moved = false;
                    _dragDirty = false;
                    Enqueue(new ArenaCommand(ArenaInputTypes.HorizontalDrag, 0, 1, ArenaInputTypes.QuantizeUnit(u), 0));
                    break;
                case ArenaTouchPhase.Move:
                    if (pointerId != _pointer) return;
                    double dt = timeSeconds - _lastTime;
                    if (dt > 1e-4)
                    {
                        float sample = (float)((u - _lastU) / dt);
                        _velocity = _velocity + Smoothing * (sample - _velocity);
                        _lastU = u;
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
                    Enqueue(new ArenaCommand(ArenaInputTypes.HorizontalDrag, ArenaInputTypes.QuantizeVelocity(fling), 0,
                        ArenaInputTypes.QuantizeUnit(u), 0));
                    if (tap) Enqueue(new ArenaCommand(ArenaInputTypes.Tap, ArenaInputTypes.QuantizeUnit(u), ArenaInputTypes.QuantizeUnit(v), 0, 0));
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
            int q = ArenaInputTypes.QuantizeVelocity(_velocity);
            if (q == _sentVelocity) return;
            _sentVelocity = q;
            Enqueue(new ArenaCommand(ArenaInputTypes.HorizontalDrag, q, 1, ArenaInputTypes.QuantizeUnit(_lastU), 0));
        }

        public bool TryDequeue(out ArenaCommand command)
        {
            if (_count == 0)
            {
                command = default;
                return false;
            }
            command = _queue[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }

        /// <summary>Drops the touch and every pending command (new run, screen hidden).</summary>
        public void Reset()
        {
            _pointer = -1;
            _count = 0;
            _head = 0;
            _velocity = 0f;
            _dragDirty = false;
        }

        void Enqueue(in ArenaCommand c)
        {
            if (_count == Capacity)
            {
                // Keep the newest state: drop the oldest command.
                _head = (_head + 1) % Capacity;
                _count--;
            }
            _queue[(_head + _count) % Capacity] = c;
            _count++;
        }
    }
}
