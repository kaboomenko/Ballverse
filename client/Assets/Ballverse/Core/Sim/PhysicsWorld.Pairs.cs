using System;

namespace Ballverse.Core.Sim
{
    // Ball–ball contacts: uniform grid broadphase rebuilt every substep (counting sort, cells filled in a fixed order),
    // sequential impulses with positional correction.
    //
    // Cell size: twice the "grid radius" — the largest radius not above twice the median radius of the colliding balls
    // (cached until a radius, filter or ball count changes). Balls up to that radius are binned and find their partners in
    // the 3×3 cell neighbourhood; the rare larger ones ("big" balls: grown balls, wide size spreads) stay out of the grid and
    // are paired with every other ball directly. One grown ball therefore costs O(n) checks instead of inflating every cell
    // (and every candidate list) to its size.
    //
    // Sparse grid: when the balls are spread far apart (escaped Ring Escape balls falling away, a few balls across a 40 m ring
    // stack) the cell count can exceed the ball count by orders of magnitude, and clearing / prefix-summing every cell each
    // substep dominated the step (65 536 cells × 8 substeps for 20 balls). Above SparseGridCellsPerBall cells per ball the
    // same grid (same cell size, same cell of every ball) is stored sparsely instead: the binned balls sorted by (cell, binning
    // order), and a neighbourhood row is found by binary search. Both layouts list a row's balls in the same order (cell, then
    // binning order) and the candidates are sorted by visiting rank anyway, so the pairs and their order are identical.
    //
    // Visiting order: with gravity, balls are ranked by height along −gravity (ties by id) and every pair is solved from
    // the lower ball's rank upwards ("shock propagation", Guendelman et al. 2003): for a contact whose normal points
    // mostly up, the lower ball counts as immovable for the positional correction, and also for the impulse when the
    // contact is resting (approach below the restitution threshold). One pass then carries a resting pile's weight down to
    // the floor instead of letting it sag and keep phantom velocity. Without gravity, pairs are visited in id order with
    // plain mass-weighted resolution (energetic contacts are always mass-weighted).
    public sealed partial class PhysicsWorld
    {
        /// <summary>Upper bound on grid cells; the cell size grows if the balls are spread wider.</summary>
        const int MaxGridCells = 1 << 16;
        const float MinCellSize = 0.02f;
        /// <summary>Above this many grid cells per binned ball the grid is stored sparsely (sorted cell keys).</summary>
        const int SparseGridCellsPerBall = 8;
        /// <summary>
        /// cos(78°): contacts whose normal is within 78° of "up" are stacking contacts (random packings carry weight through
        /// contacts well beyond the 60° of a hexagonal packing).
        /// </summary>
        const float StackingNormalCos = 0.2f;

        int[] _gridBalls = new int[16];
        int[] _gridCellOf = new int[16];
        int[] _cellOfBall = new int[16];
        int[] _rank = new int[16];
        int[] _order = new int[16];
        ulong[] _orderKeys = new ulong[16];
        ulong[] _orderScratch = new ulong[16];
        int[] _cellItems = new int[16];
        /// <summary>Sparse grid: cell of <see cref="_cellItems"/>[k], ascending.</summary>
        int[] _cellKeys = new int[16];
        int _gridItemCount;
        bool _gridSparse;
        ulong[] _candidates = new ulong[16];
        ulong[] _candidateScratch = new ulong[16];
        int[] _bigBalls = new int[16];
        int[] _pairStart = new int[17];
        int[] _pairBalls = new int[64];
        int _bigCount;
        float _gridRadius;
        int _gridRadiusVersion = -1;
        /// <summary>Bumped whenever a radius, filter, alive flag or the ball count changes (grid radius cache key).</summary>
        int _radiusVersion;
        int[] _cellStart = new int[64];
        int[] _cellFill = new int[64];
        int _gridDimX, _gridDimY;
        bool _shock;
        /// <summary>Warm start of <see cref="BuildOrder"/>: <see cref="_order"/> holds the previous substep's order of the same balls.</summary>
        bool _orderWarm;
        int _orderVersion = -1;
        int _orderCount;
        /// <summary>Shift budget of the warm-started insertion sort, per ball.</summary>
        const int WarmOrderShiftsPerBall = 4;
        Vec2 _up;

        void ResizeBallScratch(int size)
        {
            Array.Resize(ref _gridBalls, size);
            Array.Resize(ref _gridCellOf, size);
            Array.Resize(ref _cellOfBall, size);
            Array.Resize(ref _rank, size);
            Array.Resize(ref _order, size);
            Array.Resize(ref _orderKeys, size);
            Array.Resize(ref _orderScratch, size);
            Array.Resize(ref _cellItems, size);
            Array.Resize(ref _cellKeys, size);
            Array.Resize(ref _candidates, size);
            Array.Resize(ref _candidateScratch, size);
            Array.Resize(ref _bigBalls, size);
            Array.Resize(ref _pairStart, size + 1);
            // The dense layout is used only up to SparseGridCellsPerBall cells per binned ball: size it once here, so that
            // switching between the layouts as balls spread and gather never allocates in steady state.
            int cells = SparseGridCellsPerBall * size + 1;
            if (_cellStart.Length < cells)
            {
                _cellStart = new int[cells];
                _cellFill = new int[cells];
            }
        }

        void SolveBallPairs()
        {
            Vec2 g = _settings.Gravity;
            float gl = g.Length;
            _shock = gl > 1e-6f;
            _up = _shock ? g * (-1f / gl) : Vec2.Zero;
            int n = BuildGrid();
            if (n < 2) return;
            BuildOrder(n);
            int dimX = _gridDimX, dimY = _gridDimY;
            // Pass 1 gathers each ball's later-visited partners (grid neighbourhood + big balls, sorted by visiting rank),
            // solves them and records them; later passes replay the recorded lists — the same grid and order would give
            // the same lists (corrections move balls far less than a cell).
            int pairs = 0;
            for (int k = 0; k < n; k++)
            {
                int i = _order[k];
                _pairStart[k] = pairs;
                int cell = _cellOfBall[i];
                if (cell < 0)
                {
                    // Big ball: every later-visited ball is a candidate, already in visiting order.
                    EnsurePairCapacity(pairs + n - k);
                    for (int m = k + 1; m < n; m++)
                    {
                        int j = _order[m];
                        _pairBalls[pairs++] = j;
                        SolvePair(i, j);
                    }
                    continue;
                }
                int cy = cell / dimX;
                int cx = cell - cy * dimX;
                int count = 0;
                int y0 = cy > 0 ? cy - 1 : 0, y1 = cy + 1 < dimY ? cy + 1 : dimY - 1;
                int x0 = cx > 0 ? cx - 1 : 0, x1 = cx + 1 < dimX ? cx + 1 : dimX - 1;
                for (int y = y0; y <= y1; y++)
                {
                    if (_gridSparse)
                    {
                        int last = y * dimX + x1;
                        for (int m = FirstItemAtOrAfter(y * dimX + x0); m < _gridItemCount && _cellKeys[m] <= last; m++)
                        {
                            int rj = _rank[_cellItems[m]];
                            if (rj > k) _candidates[count++] = (ulong)rj;
                        }
                        continue;
                    }
                    for (int x = x0; x <= x1; x++)
                    {
                        int c = y * dimX + x;
                        int end = _cellStart[c + 1];
                        for (int m = _cellStart[c]; m < end; m++)
                        {
                            int rj = _rank[_cellItems[m]];
                            if (rj > k) _candidates[count++] = (ulong)rj;
                        }
                    }
                }
                for (int m = 0; m < _bigCount; m++)
                {
                    int rj = _rank[_bigBalls[m]];
                    if (rj > k) _candidates[count++] = (ulong)rj;
                }
                if (count == 0) continue;
                // Solve in visiting order (bottom-up under gravity); merge sort beyond short lists.
                SimSort.Sort(_candidates, count, _candidateScratch);
                EnsurePairCapacity(pairs + count);
                for (int m = 0; m < count; m++)
                {
                    int j = _order[(int)_candidates[m]];
                    _pairBalls[pairs++] = j;
                    SolvePair(i, j);
                }
            }
            _pairStart[n] = pairs;
            _work.BallPairCandidates += pairs;
            int iterations = _settings.BallIterations;
            for (int it = 1; it < iterations; it++)
            {
                for (int k = 0; k < n; k++)
                {
                    int i = _order[k];
                    int end = _pairStart[k + 1];
                    for (int m = _pairStart[k]; m < end; m++) SolvePair(i, _pairBalls[m]);
                }
            }
        }

        void EnsurePairCapacity(int size)
        {
            if (_pairBalls.Length < size) Array.Resize(ref _pairBalls, Math.Max(size, _pairBalls.Length * 2));
        }

        /// <summary>
        /// Fills <see cref="_order"/> (visiting order) and <see cref="_rank"/> (inverse) for the binned balls.
        /// With gravity the order is the ascending (height, id) key — unique, so any correct sort gives the same order. Balls
        /// move little within a substep, so when the set of colliding balls is unchanged since the previous substep the keys
        /// are rebuilt in the previous visiting order (nearly sorted) and insertion-sorted, falling back to the merge sort
        /// once that needs more than <see cref="WarmOrderShiftsPerBall"/> shifts per ball.
        /// </summary>
        void BuildOrder(int n)
        {
            if (_shock)
            {
                bool warm = _orderWarm && _orderVersion == _radiusVersion && _orderCount == n && !ExhaustiveBroadphase;
                for (int k = 0; k < n; k++)
                {
                    int id = warm ? _order[k] : _gridBalls[k];
                    float height = Vec2.Dot(_balls[id].Position, _up);
                    _orderKeys[k] = ((ulong)SortableBits(height) << 32) | (uint)id;
                }
                if (!warm || !InsertionSortWithin(_orderKeys, n, WarmOrderShiftsPerBall * n)) SimSort.Sort(_orderKeys, n, _orderScratch);
                for (int k = 0; k < n; k++) _order[k] = (int)(_orderKeys[k] & 0xFFFFFFFFUL);
                _orderWarm = true;
                _orderVersion = _radiusVersion;
                _orderCount = n;
            }
            else
            {
                for (int k = 0; k < n; k++) _order[k] = _gridBalls[k];
                _orderWarm = false;
            }
            for (int k = 0; k < n; k++) _rank[_order[k]] = k;
        }

        /// <summary>Insertion sort giving up (false, keys partly sorted) after <paramref name="maxShifts"/> element moves.</summary>
        static bool InsertionSortWithin(ulong[] a, int n, int maxShifts)
        {
            int shifts = 0;
            for (int i = 1; i < n; i++)
            {
                ulong v = a[i];
                int j = i - 1;
                while (j >= 0 && a[j] > v)
                {
                    a[j + 1] = a[j];
                    j--;
                    if (++shifts > maxShifts)
                    {
                        a[j + 1] = v;
                        return false;
                    }
                }
                a[j + 1] = v;
            }
            return true;
        }

        /// <summary>Maps a float to a uint with the same ordering (IEEE 754 sign-magnitude to offset binary).</summary>
        static uint SortableBits(float f)
        {
            int bits = BitConverter.SingleToInt32Bits(f);
            return bits < 0 ? ~(uint)bits : (uint)bits | 0x80000000u;
        }

        /// <summary>
        /// Bins colliding balls up to the grid radius into a grid with cells of twice that radius; larger balls go to
        /// <see cref="_bigBalls"/> (cell −1). Returns the number of colliding balls (binned + big).
        /// </summary>
        int BuildGrid()
        {
            int n = 0;
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                if (!b.Alive || b.Mask == 0u || b.Category == 0u) continue;
                _gridBalls[n++] = i;
            }
            _bigCount = 0;
            if (n < 2) return n;
            if (_gridRadiusVersion != _radiusVersion) UpdateGridRadius(n);
            float cut = _gridRadius;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int k = 0; k < n; k++)
            {
                int id = _gridBalls[k];
                ref BallState b = ref _balls[id];
                Vec2 p = b.Position;
                if (b.Radius > cut)
                {
                    _bigBalls[_bigCount++] = id;
                    continue;
                }
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            if (_bigCount == n)
            {
                // Only big balls (cannot happen with the median rule, kept for safety): an empty one-cell grid.
                minX = minY = maxX = maxY = 0f;
            }
            float cellSize = 2f * cut;
            if (cellSize < MinCellSize) cellSize = MinCellSize;
            float w = maxX - minX, hgt = maxY - minY;
            if (!SimMath.IsFinite(w) || !SimMath.IsFinite(hgt)) throw new InvalidOperationException("Ball positions are not finite");
            long dimX, dimY;
            while (true)
            {
                dimX = (long)(w / cellSize) + 1;
                dimY = (long)(hgt / cellSize) + 1;
                if (dimX * dimY <= MaxGridCells) break;
                cellSize *= 2f;
            }
            _gridDimX = (int)dimX;
            _gridDimY = (int)dimY;
            int cells = _gridDimX * _gridDimY;
            _gridSparse = cells > SparseGridCellsPerBall * n && !ExhaustiveBroadphase;
            if (_gridSparse)
            {
                BinSparse(n, minX, minY, 1f / cellSize, cut);
                return n;
            }
            if (_cellStart.Length < cells + 1)
            {
                int size = Math.Max(cells + 1, _cellStart.Length * 2);
                _cellStart = new int[size];
                _cellFill = new int[size];
            }
            for (int c = 0; c <= cells; c++) _cellStart[c] = 0;
            float inv = 1f / cellSize;
            for (int k = 0; k < n; k++)
            {
                int id = _gridBalls[k];
                if (_balls[id].Radius > cut)
                {
                    _gridCellOf[k] = -1;
                    _cellOfBall[id] = -1;
                    continue;
                }
                Vec2 p = _balls[id].Position;
                int cx = (int)((p.X - minX) * inv);
                int cy = (int)((p.Y - minY) * inv);
                if (cx >= _gridDimX) cx = _gridDimX - 1;
                if (cy >= _gridDimY) cy = _gridDimY - 1;
                if (cx < 0) cx = 0;
                if (cy < 0) cy = 0;
                int cell = cy * _gridDimX + cx;
                _gridCellOf[k] = cell;
                _cellOfBall[id] = cell;
                _cellStart[cell + 1]++;
            }
            for (int c = 0; c < cells; c++) _cellStart[c + 1] += _cellStart[c];
            for (int c = 0; c < cells; c++) _cellFill[c] = _cellStart[c];
            for (int k = 0; k < n; k++)
            {
                int cell = _gridCellOf[k];
                if (cell >= 0) _cellItems[_cellFill[cell]++] = _gridBalls[k];
            }
            return n;
        }

        /// <summary>
        /// Sparse layout of the grid <see cref="BuildGrid"/> sized: every binned ball gets exactly the cell the dense layout
        /// would give it; the balls are sorted by (cell, binning order) into <see cref="_cellItems"/> / <see cref="_cellKeys"/>.
        /// </summary>
        void BinSparse(int n, float minX, float minY, float inv, float cut)
        {
            int m = 0;
            for (int k = 0; k < n; k++)
            {
                int id = _gridBalls[k];
                if (_balls[id].Radius > cut)
                {
                    _gridCellOf[k] = -1;
                    _cellOfBall[id] = -1;
                    continue;
                }
                Vec2 p = _balls[id].Position;
                int cx = (int)((p.X - minX) * inv);
                int cy = (int)((p.Y - minY) * inv);
                if (cx >= _gridDimX) cx = _gridDimX - 1;
                if (cy >= _gridDimY) cy = _gridDimY - 1;
                if (cx < 0) cx = 0;
                if (cy < 0) cy = 0;
                int cell = cy * _gridDimX + cx;
                _gridCellOf[k] = cell;
                _cellOfBall[id] = cell;
                _orderKeys[m++] = ((ulong)(uint)cell << 32) | (uint)k;
            }
            SimSort.Sort(_orderKeys, m, _orderScratch);
            for (int j = 0; j < m; j++)
            {
                ulong key = _orderKeys[j];
                _cellItems[j] = _gridBalls[(int)(key & 0xFFFFFFFFUL)];
                _cellKeys[j] = (int)(key >> 32);
            }
            _gridItemCount = m;
        }

        /// <summary>Sparse grid: first item index whose cell is ≥ <paramref name="cell"/>.</summary>
        int FirstItemAtOrAfter(int cell)
        {
            int lo = 0, hi = _gridItemCount;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_cellKeys[mid] < cell) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Grid radius = largest radius ≤ 2 × median radius of the colliding balls (<see cref="_gridBalls"/>[0..n)).</summary>
        void UpdateGridRadius(int n)
        {
            for (int k = 0; k < n; k++)
            {
                int id = _gridBalls[k];
                _orderKeys[k] = ((ulong)SortableBits(_balls[id].Radius) << 32) | (uint)id;
            }
            SimSort.Sort(_orderKeys, n, _orderScratch);
            float median = _balls[(int)(_orderKeys[n / 2] & 0xFFFFFFFFUL)].Radius;
            float limit = 2f * median;
            float cut = median;
            for (int k = n - 1; k >= 0; k--)
            {
                float r = _balls[(int)(_orderKeys[k] & 0xFFFFFFFFUL)].Radius;
                if (r <= limit)
                {
                    cut = r;
                    break;
                }
            }
            _gridRadius = cut;
            _gridRadiusVersion = _radiusVersion;
        }

        /// <summary>Solves one pair; <paramref name="ia"/> is the earlier-visited (lower) ball.</summary>
        void SolvePair(int ia, int ib)
        {
            _work.BallPairSolves++;
            ref BallState a = ref _balls[ia];
            ref BallState b = ref _balls[ib];
            if (!a.Alive || !b.Alive) return;
            if ((a.Mask & b.Category) == 0u || (b.Mask & a.Category) == 0u) return;
            Vec2 d = b.Position - a.Position;
            float rs = a.Radius + b.Radius;
            float dist2 = d.LengthSquared;
            if (dist2 >= rs * rs) return;
            float dist = MathF.Sqrt(dist2);
            // Coincident centres: deterministic fallback axis (from the lower id to the higher id).
            Vec2 n = dist > NormalEpsilon ? d / dist : (ia < ib ? new Vec2(1f, 0f) : new Vec2(-1f, 0f));
            Vec2 t = n.Perp;
            bool rotA = (a.Flags & BallFlags.NoRotation) == 0;
            bool rotB = (b.Flags & BallFlags.NoRotation) == 0;
            // Surface velocities at the contact: a at +ra·n, b at −rb·n.
            Vec2 va = rotA ? a.Velocity + t * (a.AngularVelocity * a.Radius) : a.Velocity;
            Vec2 vb = rotB ? b.Velocity - t * (b.AngularVelocity * b.Radius) : b.Velocity;
            Vec2 vrel = vb - va;
            float vn = Vec2.Dot(vrel, n);
            Vec2 point = a.Position + n * a.Radius;

            ISimContactHook hook = ContactHook;
            if (hook != null)
            {
                var info = new ContactPoint { Point = point, Normal = ia < ib ? n : -n, ApproachSpeed = -vn };
                if (!hook.PreSolveBallHit(this, ia < ib ? ia : ib, ia < ib ? ib : ia, in info)) return;
                if (!a.Alive || !b.Alive) return;
            }

            bool stacking = _shock && Vec2.Dot(n, _up) > StackingNormalCos;
            float gs = MathF.Abs(a.GravityScale) > MathF.Abs(b.GravityScale) ? a.GravityScale : b.GravityScale;
            bool resting = -vn < RestingThreshold(gs);
            float invMa = stacking ? 0f : a.InvMass;
            float pen = rs - dist - _settings.PositionSlop;
            if (pen > 0f)
            {
                float corr = pen * _settings.PositionCorrection / (invMa + b.InvMass);
                if (invMa > 0f) a.Position = a.Position - n * (corr * invMa);
                b.Position = b.Position + n * (corr * b.InvMass);
            }
            if (vn >= 0f) return;

            // Energetic contacts always exchange momentum; resting stacking contacts treat the lower ball as fixed.
            bool freezeA = stacking && resting;
            invMa = freezeA ? 0f : a.InvMass;
            float invIa = freezeA || !rotA ? 0f : a.InvInertia;
            float invIb = rotB ? b.InvInertia : 0f;
            float invSum = invMa + b.InvMass;
            float e = a.Restitution > b.Restitution ? a.Restitution : b.Restitution;
            if (resting) e = 0f;
            float jn = -(1f + e) * vn / invSum;
            float jt = 0f;
            float mu = MathF.Sqrt(a.Friction * b.Friction);
            if (mu > 0f)
            {
                float vt = Vec2.Dot(vrel, t);
                float kt = invSum + a.Radius * a.Radius * invIa + b.Radius * b.Radius * invIb;
                jt = -vt / kt;
                float maxJt = mu * jn;
                if (jt > maxJt) jt = maxJt;
                else if (jt < -maxJt) jt = -maxJt;
            }
            Vec2 impulse = n * jn + t * jt;
            a.Velocity = a.Velocity - impulse * invMa;
            b.Velocity = b.Velocity + impulse * b.InvMass;
            a.AngularVelocity = a.AngularVelocity - a.Radius * jt * invIa;
            b.AngularVelocity = b.AngularVelocity - b.Radius * jt * invIb;
            if (ia < ib) RecordContact(SimEventKind.BallHit, ia, ib, -1, point, n, jn, -vn, 0f, 0);
            else RecordContact(SimEventKind.BallHit, ib, ia, -1, point, -n, jn, -vn, 0f, 0);
        }
    }
}
