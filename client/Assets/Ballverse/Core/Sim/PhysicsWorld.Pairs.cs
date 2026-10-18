using System;

namespace Ballverse.Core.Sim
{
    // Ball–ball contacts: uniform grid broadphase rebuilt every substep (counting sort, cells filled in a fixed order),
    // sequential impulses with positional correction.
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
        int[] _candidates = new int[16];
        int[] _cellStart = new int[64];
        int[] _cellFill = new int[64];
        int _gridDimX, _gridDimY;
        bool _shock;
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
            Array.Resize(ref _candidates, size);
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
            // Later passes reuse the grid and order: corrections move balls far less than a cell.
            int iterations = _settings.BallIterations;
            for (int it = 0; it < iterations; it++)
            {
                for (int k = 0; k < n; k++)
                {
                    int i = _order[k];
                    int cell = _cellOfBall[i];
                    int cy = cell / dimX;
                    int cx = cell - cy * dimX;
                    int count = 0;
                    int y0 = cy > 0 ? cy - 1 : 0, y1 = cy + 1 < dimY ? cy + 1 : dimY - 1;
                    int x0 = cx > 0 ? cx - 1 : 0, x1 = cx + 1 < dimX ? cx + 1 : dimX - 1;
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int c = y * dimX + x;
                            int end = _cellStart[c + 1];
                            for (int m = _cellStart[c]; m < end; m++)
                            {
                                int rj = _rank[_cellItems[m]];
                                if (rj > k) _candidates[count++] = rj;
                            }
                        }
                    }
                    if (count == 0) continue;
                    SimSort.InsertionSort(_candidates, 0, count);
                    for (int m = 0; m < count; m++) SolvePair(i, _order[_candidates[m]]);
                }
            }
        }

        /// <summary>Fills <see cref="_order"/> (visiting order) and <see cref="_rank"/> (inverse) for the binned balls.</summary>
        void BuildOrder(int n)
        {
            if (_shock)
            {
                for (int k = 0; k < n; k++)
                {
                    int id = _gridBalls[k];
                    float height = Vec2.Dot(_balls[id].Position, _up);
                    _orderKeys[k] = ((ulong)SortableBits(height) << 32) | (uint)id;
                }
                SimSort.Sort(_orderKeys, n, _orderScratch);
                for (int k = 0; k < n; k++) _order[k] = (int)(_orderKeys[k] & 0xFFFFFFFFUL);
            }
            else
            {
                for (int k = 0; k < n; k++) _order[k] = _gridBalls[k];
            }
            for (int k = 0; k < n; k++) _rank[_order[k]] = k;
        }

        /// <summary>Maps a float to a uint with the same ordering (IEEE 754 sign-magnitude to offset binary).</summary>
        static uint SortableBits(float f)
        {
            int bits = BitConverter.SingleToInt32Bits(f);
            return bits < 0 ? ~(uint)bits : (uint)bits | 0x80000000u;
        }

        /// <summary>Bins colliding balls into a grid with cells ≥ the largest diameter. Returns the number of binned balls.</summary>
        int BuildGrid()
        {
            int n = 0;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxR = 0f;
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                if (!b.Alive || b.Mask == 0u || b.Category == 0u) continue;
                _gridBalls[n++] = i;
                Vec2 p = b.Position;
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
                if (b.Radius > maxR) maxR = b.Radius;
            }
            if (n < 2) return n;
            float cellSize = 2f * maxR;
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
            for (int k = 0; k < n; k++) _cellItems[_cellFill[_gridCellOf[k]]++] = _gridBalls[k];
            return n;
        }

        /// <summary>Solves one pair; <paramref name="ia"/> is the earlier-visited (lower) ball.</summary>
        void SolvePair(int ia, int ib)
        {
            ref BallState a = ref _balls[ia];
            ref BallState b = ref _balls[ib];
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
            }

            bool stacking = _shock && Vec2.Dot(n, _up) > StackingNormalCos;
            bool resting = -vn < _settings.RestitutionThreshold;
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
