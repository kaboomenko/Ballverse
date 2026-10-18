using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>One recorded trail point: world position of a body at a tick.</summary>
    public struct TrailSample
    {
        public int Tick;
        public float X;
        public float Y;
        /// <summary>Body radius (circle) or a nominal 0.1 m for other shapes; sets the ribbon width.</summary>
        public float Radius;
        public byte Color;
        public BodyFlags Flags;
    }

    /// <summary>
    /// Ring buffers of recent positions of trail bodies (spec §7.7 "кольцевой буфер позиций на тело"). Only visible
    /// bodies with <see cref="BodyFlags.Trail"/> are recorded. A body that skips a tick (hidden, despawned) starts a new
    /// trail. Fed one tick at a time by <see cref="RenderComposer"/>'s history sync, so the trail at a tick is a function of
    /// the stream only. Fixed memory: <c>maxBodies × capacityTicks</c> samples; allocation-free after construction
    /// (the id map is pre-sized).
    /// </summary>
    public sealed class TrailTracker
    {
        readonly int _maxBodies;
        readonly int _capacity;
        readonly TrailSample[] _samples;
        readonly int[] _bodyId;
        readonly int[] _head;        // index of the newest sample
        readonly int[] _count;
        readonly int[] _lastTick;
        readonly bool[] _used;
        readonly Dictionary<int, int> _slotOf;
        readonly int[] _free;
        readonly int[] _sorted;
        int _freeCount;

        public TrailTracker(int maxBodies, int capacityTicks)
        {
            if (maxBodies < 1) throw new ArgumentOutOfRangeException(nameof(maxBodies));
            if (capacityTicks < 2) throw new ArgumentOutOfRangeException(nameof(capacityTicks));
            _maxBodies = maxBodies;
            _capacity = capacityTicks;
            _samples = new TrailSample[maxBodies * capacityTicks];
            _bodyId = new int[maxBodies];
            _head = new int[maxBodies];
            _count = new int[maxBodies];
            _lastTick = new int[maxBodies];
            _used = new bool[maxBodies];
            _slotOf = new Dictionary<int, int>(maxBodies * 2);
            _free = new int[maxBodies];
            _sorted = new int[maxBodies];
            Reset();
        }

        public int MaxBodies => _maxBodies;
        public int CapacityTicks => _capacity;

        /// <summary>Bodies with a live trail.</summary>
        public int ActiveCount => _maxBodies - _freeCount;

        public void Reset()
        {
            _slotOf.Clear();
            for (int i = 0; i < _maxBodies; i++)
            {
                _used[i] = false;
                _count[i] = 0;
                _free[i] = _maxBodies - 1 - i;
            }
            _freeCount = _maxBodies;
        }

        /// <summary>Appends the trail bodies of <paramref name="frame"/> (the frame of <paramref name="tick"/>).</summary>
        public void Record(int tick, RenderFrame frame, RenderQuantization quantization, IReadOnlyList<ShapeDefinition> shapes)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            // Retire trails that ended more than a full buffer ago.
            for (int s = 0; s < _maxBodies; s++)
            {
                if (_used[s] && tick - _lastTick[s] >= _capacity) Release(s);
            }
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & (BodyFlags.Visible | BodyFlags.Trail)) != (BodyFlags.Visible | BodyFlags.Trail)) continue;
                if (!_slotOf.TryGetValue(b.Id, out int slot))
                {
                    if (_freeCount == 0) continue;   // over budget: this body gets no trail
                    slot = _free[--_freeCount];
                    _used[slot] = true;
                    _bodyId[slot] = b.Id;
                    _count[slot] = 0;
                    _slotOf.Add(b.Id, slot);
                }
                if (_count[slot] > 0 && tick != _lastTick[slot] + 1)
                {
                    if (tick == _lastTick[slot]) continue;   // same tick recorded twice
                    _count[slot] = 0;                        // gap or rewind: new trail
                }
                bool circle = shapes == null || ((uint)b.ShapeId < (uint)shapes.Count && shapes[b.ShapeId].Kind == ShapeKind.Circle);
                int head = _count[slot] == 0 ? 0 : (_head[slot] + 1) % _capacity;
                _samples[slot * _capacity + head] = new TrailSample
                {
                    Tick = tick,
                    X = quantization.DequantizePosition(b.X),
                    Y = quantization.DequantizePosition(b.Y),
                    Radius = circle ? quantization.DequantizeScale(b.Scale) : 0.1f,
                    Color = b.Color,
                    Flags = b.Flags,
                };
                _head[slot] = head;
                if (_count[slot] < _capacity) _count[slot]++;
                _lastTick[slot] = tick;
            }
        }

        void Release(int slot)
        {
            _slotOf.Remove(_bodyId[slot]);
            _used[slot] = false;
            _count[slot] = 0;
            _free[_freeCount++] = slot;
        }

        /// <summary>Slot index range for iteration: slots in [0, <see cref="MaxBodies"/>) with <see cref="IsActive"/>.</summary>
        public bool IsActive(int slot) => _used[slot] && _count[slot] > 0;

        public int BodyIdOf(int slot) => _bodyId[slot];

        public int LastTickOf(int slot) => _lastTick[slot];

        /// <summary>Samples of <paramref name="slot"/> available.</summary>
        public int SampleCount(int slot) => _count[slot];

        /// <summary>The <paramref name="age"/>-th newest sample of <paramref name="slot"/> (0 = newest).</summary>
        public TrailSample GetSample(int slot, int age)
        {
            if ((uint)age >= (uint)_count[slot]) throw new ArgumentOutOfRangeException(nameof(age));
            int i = (_head[slot] - age + _capacity) % _capacity;
            return _samples[slot * _capacity + i];
        }

        public bool TryGetSlot(int bodyId, out int slot) => _slotOf.TryGetValue(bodyId, out slot);

        /// <summary>
        /// Active slots ordered by body id, so ribbons are drawn in the same order however the history was reached (slot
        /// numbers depend on when a body was first seen). Valid until the next call; allocation-free.
        /// </summary>
        public ReadOnlySpan<int> ActiveSlotsById()
        {
            int n = 0;
            for (int s = 0; s < _maxBodies; s++)
            {
                if (!IsActive(s)) continue;
                int id = _bodyId[s];
                int k = n++;
                while (k > 0 && _bodyId[_sorted[k - 1]] > id)
                {
                    _sorted[k] = _sorted[k - 1];
                    k--;
                }
                _sorted[k] = s;
            }
            return new ReadOnlySpan<int>(_sorted, 0, n);
        }
    }
}
