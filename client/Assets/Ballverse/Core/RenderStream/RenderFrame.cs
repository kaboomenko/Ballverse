using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Visual state of one tick: the bodies (quantized), the HUD values and, for streams with a grid layer, the grid of
    /// palette indices (spec §7.4 "покадровое визуальное состояние", §2.9 M08). A reusable container: <see cref="Clear"/> keeps the backing arrays, so a frame can be refilled every tick
    /// without allocating once it reached its peak size.
    /// <para>
    /// The writer accepts bodies and HUD entries in any order; the stream stores them sorted by body id / HUD key,
    /// so frames returned by <see cref="RenderStreamReader"/> are always in ascending id and key order
    /// (use <see cref="Sort"/> to bring a hand-built frame into the same canonical order before comparing).
    /// </para>
    /// <para>
    /// Grid layer: <see cref="SetGridSize"/> gives the frame a grid (row-major, index = y · width + x, see
    /// <see cref="RenderGridSpec"/>); its size must match the stream header's <see cref="RenderStreamHeader.Grid"/>.
    /// <see cref="Clear"/> keeps the grid (size and cells), because grid modes usually repaint only a few cells per tick.
    /// </para>
    /// </summary>
    public sealed class RenderFrame
    {

        BodyState[] _bodies;
        int _bodyCount;
        HudEntry[] _hud;
        int _hudCount;
        byte[] _grid = Array.Empty<byte>();
        int _gridWidth;
        int _gridHeight;

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

        /// <summary>True when the frame carries a grid layer.</summary>
        public bool HasGrid => _gridWidth > 0;

        /// <summary>Grid columns (0 without a grid).</summary>
        public int GridWidth => _gridWidth;

        /// <summary>Grid rows (0 without a grid).</summary>
        public int GridHeight => _gridHeight;

        /// <summary>The grid cells (row-major palette indices), writable; empty without a grid. Valid until the grid is resized.</summary>
        public Span<byte> Grid => new Span<byte>(_grid, 0, _gridWidth * _gridHeight);

        /// <summary>
        /// Gives the frame a <paramref name="width"/> × <paramref name="height"/> grid (both 0 removes it). Width must be in
        /// [1, <see cref="RenderStreamLimits.MaxGridWidth"/>] and height in [1, <see cref="RenderStreamLimits.MaxGridHeight"/>].
        /// Same size as now: no-op (cells kept); a different size: all cells become 0. Capacity is kept.
        /// </summary>
        public void SetGridSize(int width, int height)
        {
            if (width == 0 && height == 0)
            {
                _gridWidth = 0;
                _gridHeight = 0;
                return;
            }
            if (width < 1 || width > RenderStreamLimits.MaxGridWidth) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1 || height > RenderStreamLimits.MaxGridHeight) throw new ArgumentOutOfRangeException(nameof(height));
            if (width == _gridWidth && height == _gridHeight) return;
            SetGridRaw(width, height);
            Array.Clear(_grid, 0, width * height);
        }

        /// <summary>Palette index of cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public byte GetCell(int x, int y)
        {
            return _grid[CellIndex(x, y)];
        }

        /// <summary>Sets the palette index of cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public void SetCell(int x, int y, byte value)
        {
            _grid[CellIndex(x, y)] = value;
        }

        int CellIndex(int x, int y)
        {
            if ((uint)x >= (uint)_gridWidth) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)_gridHeight) throw new ArgumentOutOfRangeException(nameof(y));
            return y * _gridWidth + x;
        }

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

        /// <summary>Removes all bodies and HUD entries, keeping capacity. The grid layer is not touched.</summary>
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

        /// <summary>
        /// Sorts bodies by id and HUD entries by key (the canonical stream order). In-place heap sort: no allocation
        /// (<c>Array.Sort</c> with a comparer allocates a delegate per call on .NET), the same result on every runtime.
        /// </summary>
        public void Sort()
        {
            if (!IsBodiesSorted()) SortBodies(_bodies, _bodyCount);
            if (!IsHudSorted()) SortHud(_hud, _hudCount);
        }

        static void SortBodies(BodyState[] a, int n)
        {
            for (int i = n / 2 - 1; i >= 0; i--) SiftBody(a, i, n);
            for (int end = n - 1; end > 0; end--)
            {
                BodyState t = a[0];
                a[0] = a[end];
                a[end] = t;
                SiftBody(a, 0, end);
            }
        }

        static void SiftBody(BodyState[] a, int root, int n)
        {
            BodyState item = a[root];
            while (true)
            {
                int child = 2 * root + 1;
                if (child >= n) break;
                if (child + 1 < n && a[child + 1].Id > a[child].Id) child++;
                if (a[child].Id <= item.Id) break;
                a[root] = a[child];
                root = child;
            }
            a[root] = item;
        }

        static void SortHud(HudEntry[] a, int n)
        {
            for (int i = n / 2 - 1; i >= 0; i--) SiftHud(a, i, n);
            for (int end = n - 1; end > 0; end--)
            {
                HudEntry t = a[0];
                a[0] = a[end];
                a[end] = t;
                SiftHud(a, 0, end);
            }
        }

        static void SiftHud(HudEntry[] a, int root, int n)
        {
            HudEntry item = a[root];
            while (true)
            {
                int child = 2 * root + 1;
                if (child >= n) break;
                if (child + 1 < n && a[child + 1].Key > a[child].Key) child++;
                if (a[child].Key <= item.Key) break;
                a[root] = a[child];
                root = child;
            }
            a[root] = item;
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
            SetGridRaw(other._gridWidth, other._gridHeight);
            Array.Copy(other._grid, _grid, other._gridWidth * other._gridHeight);
        }

        /// <summary>Element-wise, order-sensitive equality of bodies and HUD entries, plus equal grid size and cells.</summary>
        public bool ContentEquals(RenderFrame other)
        {
            if (other == null) return false;
            return Bodies.SequenceEqual(other.Bodies) && Hud.SequenceEqual(other.Hud)
                && _gridWidth == other._gridWidth && _gridHeight == other._gridHeight
                && ((ReadOnlySpan<byte>)Grid).SequenceEqual(other.Grid);
        }

        internal BodyState[] BodyArray => _bodies;
        internal HudEntry[] HudArray => _hud;
        internal byte[] GridArray => _grid;

        /// <summary>Sets the grid size without validation or clearing (cells beyond the old content are undefined until written).</summary>
        internal void SetGridRaw(int width, int height)
        {
            int cells = width * height;
            if (_grid.Length < cells) _grid = new byte[cells];
            _gridWidth = width;
            _gridHeight = height;
        }

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
    }
}
