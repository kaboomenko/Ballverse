using System;
using System.Collections.Generic;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Visual state of one tick: the bodies (quantized) plus the HUD values (spec §7.4 "покадровое визуальное состояние").
    /// A reusable container: <see cref="Clear"/> keeps the backing arrays, so a frame can be refilled every tick
    /// without allocating once it reached its peak size.
    /// <para>
    /// The writer accepts bodies and HUD entries in any order; the stream stores them sorted by body id / HUD key,
    /// so frames returned by <see cref="RenderStreamReader"/> are always in ascending id and key order
    /// (use <see cref="Sort"/> to bring a hand-built frame into the same canonical order before comparing).
    /// </para>
    /// </summary>
    public sealed class RenderFrame
    {
        static readonly BodyIdComparer s_bodyComparer = new BodyIdComparer();
        static readonly HudKeyComparer s_hudComparer = new HudKeyComparer();

        BodyState[] _bodies;
        int _bodyCount;
        HudEntry[] _hud;
        int _hudCount;

        public RenderFrame()
            : this(16, 4)
        {
        }

        public RenderFrame(int bodyCapacity, int hudCapacity)
        {
            if (bodyCapacity < 0) throw new ArgumentOutOfRangeException(nameof(bodyCapacity));
            if (hudCapacity < 0) throw new ArgumentOutOfRangeException(nameof(hudCapacity));
            _bodies = new BodyState[Math.Max(1, bodyCapacity)];
            _hud = new HudEntry[Math.Max(1, hudCapacity)];
        }

        public int BodyCount => _bodyCount;
        public int HudCount => _hudCount;

        /// <summary>The bodies, valid until the frame is next modified.</summary>
        public ReadOnlySpan<BodyState> Bodies => new ReadOnlySpan<BodyState>(_bodies, 0, _bodyCount);

        /// <summary>The HUD entries, valid until the frame is next modified.</summary>
        public ReadOnlySpan<HudEntry> Hud => new ReadOnlySpan<HudEntry>(_hud, 0, _hudCount);

        public BodyState GetBody(int index)
        {
            if ((uint)index >= (uint)_bodyCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _bodies[index];
        }

        public HudEntry GetHud(int index)
        {
            if ((uint)index >= (uint)_hudCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _hud[index];
        }

        /// <summary>Removes all bodies and HUD entries, keeping capacity.</summary>
        public void Clear()
        {
            _bodyCount = 0;
            _hudCount = 0;
        }

        /// <summary>Appends a body (any order; ids must be unique when the frame is written).</summary>
        public void AddBody(BodyState body)
        {
            if (_bodyCount == _bodies.Length) Array.Resize(ref _bodies, _bodies.Length * 2);
            _bodies[_bodyCount++] = body;
        }

        /// <summary>Appends a HUD entry (any order; keys must be unique and in [0, <see cref="HudEntry.MaxKey"/>] when written).</summary>
        public void AddHud(int key, int value)
        {
            if (_hudCount == _hud.Length) Array.Resize(ref _hud, _hud.Length * 2);
            _hud[_hudCount++] = new HudEntry(key, value);
        }

        /// <summary>Finds a body by id (linear scan). Returns false if absent.</summary>
        public bool TryGetBody(int id, out BodyState body)
        {
            for (int i = 0; i < _bodyCount; i++)
            {
                if (_bodies[i].Id == id)
                {
                    body = _bodies[i];
                    return true;
                }
            }
            body = default;
            return false;
        }

        /// <summary>Finds a HUD value by key. Returns false if absent.</summary>
        public bool TryGetHud(int key, out int value)
        {
            for (int i = 0; i < _hudCount; i++)
            {
                if (_hud[i].Key == key)
                {
                    value = _hud[i].Value;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        /// <summary>Sorts bodies by id and HUD entries by key (the canonical stream order).</summary>
        public void Sort()
        {
            if (!IsBodiesSorted()) Array.Sort(_bodies, 0, _bodyCount, s_bodyComparer);
            if (!IsHudSorted()) Array.Sort(_hud, 0, _hudCount, s_hudComparer);
        }

        /// <summary>Replaces this frame's content with a copy of <paramref name="other"/>.</summary>
        public void CopyFrom(RenderFrame other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(other, this)) return;
            EnsureBodyCapacity(other._bodyCount);
            EnsureHudCapacity(other._hudCount);
            Array.Copy(other._bodies, _bodies, other._bodyCount);
            Array.Copy(other._hud, _hud, other._hudCount);
            _bodyCount = other._bodyCount;
            _hudCount = other._hudCount;
        }

        /// <summary>Element-wise, order-sensitive equality of bodies and HUD entries.</summary>
        public bool ContentEquals(RenderFrame other)
        {
            if (other == null) return false;
            return Bodies.SequenceEqual(other.Bodies) && Hud.SequenceEqual(other.Hud);
        }

        internal BodyState[] BodyArray => _bodies;
        internal HudEntry[] HudArray => _hud;

        internal bool IsBodiesSorted()
        {
            for (int i = 1; i < _bodyCount; i++)
            {
                if (_bodies[i - 1].Id > _bodies[i].Id) return false;
            }
            return true;
        }

        internal bool IsHudSorted()
        {
            for (int i = 1; i < _hudCount; i++)
            {
                if (_hud[i - 1].Key > _hud[i].Key) return false;
            }
            return true;
        }

        internal void EnsureBodyCapacity(int count)
        {
            if (_bodies.Length < count) Array.Resize(ref _bodies, Math.Max(count, _bodies.Length * 2));
        }

        internal void EnsureHudCapacity(int count)
        {
            if (_hud.Length < count) Array.Resize(ref _hud, Math.Max(count, _hud.Length * 2));
        }

        internal void SetCounts(int bodyCount, int hudCount)
        {
            _bodyCount = bodyCount;
            _hudCount = hudCount;
        }

        sealed class BodyIdComparer : IComparer<BodyState>
        {
            public int Compare(BodyState a, BodyState b)
            {
                return a.Id.CompareTo(b.Id);
            }
        }

        sealed class HudKeyComparer : IComparer<HudEntry>
        {
            public int Compare(HudEntry a, HudEntry b)
            {
                return a.Key.CompareTo(b.Key);
            }
        }
    }
}
