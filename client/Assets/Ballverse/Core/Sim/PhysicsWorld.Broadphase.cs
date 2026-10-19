using System;

namespace Ballverse.Core.Sim
{
    // Collider broadphase: a radial index updated every substep, a pure culling step in front of the per-collider bounds test.
    // (Also here: the end-of-tick sensor bound pre-check and the polyline edge frames; the sparse ball grid and the warm-started
    // visiting order live in PhysicsWorld.Pairs.cs. None of them changes a result: ExhaustiveBroadphase switches all of them
    // off, and BroadphaseEquivalenceTests compares both paths bit for bit after every tick.)
    //
    // Every collider's per-substep bounds (ColliderBounds: a circle of radius Outer around Centre, minus a ring's empty inner
    // disc of radius Inner) lie inside an annulus around one pivot point O: with d = |Centre − O|, every point x the collider
    // can reach has d − Outer ≤ |x − O| ≤ d + Outer, and a ring's hole contains the disc |x − O| < Inner − d. So the collider
    // maps to a radial interval [lo, hi] = [max(d − Outer, Inner − d), d + Outer]. A ball's path p0→p1 covers the distances
    // [dmin, dmax] from O (dmin: distance from O to the segment; dmax: the farther end), so the path inflated by a reach ρ can
    // overlap the collider's bounds only if [dmin − ρ, dmax + ρ] overlaps [lo, hi] (triangle inequality). Colliders whose
    // interval misses the ball's are exactly ones the bounds test (MayTouch) would reject; the survivors still go through
    // every filter of the exhaustive scan (active, sensor, collision mask, MayTouch) and are listed in ascending id, so the
    // candidate set and its order — and therefore every result — are those of the exhaustive scan. Both interval ends get a
    // relative slack of 1e-5 (float rounding is ~6e-8 relative), which keeps the culling conservative.
    //
    // The pivot is the centre of the active collider with the largest bounds (ties: lowest id): for Ring Escape every ring
    // and ring sensor shares it, so the index is an exact annulus search over concentric rings (a ball only meets the rings
    // at its own radius); for a Helix Drop tower it sorts the platforms by height; a Geometry Bounce arena is one collider
    // (the linear scan is used below RadialIndexMinColliders).
    //
    // Index: the colliders sorted by lo (keys sorted with the allocation-free SimSort, only when some interval or membership
    // changed), with the running maximum of hi. A query binary-searches the first position whose running maximum reaches the
    // ball's lower end and scans while lo stays below its upper end: O(log n + k) for nested or separated shapes, never worse
    // than the linear scan's O(n).
    public sealed partial class PhysicsWorld
    {
        /// <summary>Below this many active colliders of a kind (walls / sensors) the plain linear scan is used.</summary>
        const int RadialIndexMinColliders = 8;
        /// <summary>Relative slack of the radial intervals and queries (float rounding is ~6e-8 relative).</summary>
        const float RadialSlack = 1e-5f;

        /// <summary>
        /// Test / debug switch: true forces the reference (pre-broadphase) paths — every ball against every collider's bounds
        /// each substep, every sensor–ball pair tested at the end of the tick, the dense ball–ball grid whatever its size, the
        /// visiting order merge-sorted from scratch and polyline edges recomputed per ball — instead of the radial index, the
        /// sensor bound pre-check, the sparse grid, the warm-started order and the edge frames. Results are identical either
        /// way; the determinism tests compare both paths tick by tick.
        /// </summary>
        internal bool ExhaustiveBroadphase { get; set; }

        /// <summary>Test switch: minimum active colliders of a kind for the radial index (default 8; 0 = always when not exhaustive).</summary>
        internal int RadialIndexThreshold { get; set; } = RadialIndexMinColliders;

        /// <summary>Test diagnostics: the last substep gathered wall candidates / sensor touches through the radial index.</summary>
        internal bool WallIndexUsed => _wallIndex.Usable;
        internal bool SensorIndexUsed => _sensorIndex.Usable;
        /// <summary>Test diagnostics: the last substep's ball–ball grid was stored sparsely (PhysicsWorld.Pairs.cs).</summary>
        internal bool SparseGridUsed => _gridSparse;

        /// <summary>Test diagnostics: index entries of the last substep whose radial interval meets the path inflated by <paramref name="reach"/> (−1 if the index is not in use).</summary>
        internal int CountRadialCandidates(Vec2 p0, Vec2 p1, float reach, bool sensors)
        {
            RadialIndex ix = sensors ? _sensorIndex : _wallIndex;
            if (!ix.Usable || !RadialRange(p0, p1, reach, out float a, out float b)) return -1;
            int n = 0;
            for (int k = ix.FirstReaching(a); k < ix.Count && ix.Lo[k] <= b; k++)
            {
                if (ix.Hi[k] >= a) n++;
            }
            return n;
        }

        sealed class RadialIndex
        {
            public int Count;
            /// <summary>Collider ids sorted by <see cref="Lo"/>.</summary>
            public int[] Ids = new int[8];
            public float[] Lo = new float[8];
            public float[] Hi = new float[8];
            /// <summary>Running maximum of <see cref="Hi"/> over positions 0..k.</summary>
            public float[] MaxHi = new float[8];
            public ulong[] Keys = new ulong[8];
            public ulong[] Scratch = new ulong[8];
            /// <summary>Built this substep with finite intervals and at least the threshold count: queries may use it.</summary>
            public bool Usable;

            public void EnsureCapacity(int n)
            {
                if (Ids.Length >= n) return;
                int size = Math.Max(n, Ids.Length * 2);
                Ids = new int[size];
                Lo = new float[size];
                Hi = new float[size];
                MaxHi = new float[size];
                Keys = new ulong[size];
                Scratch = new ulong[size];
            }

            /// <summary>First position whose running maximum of hi reaches <paramref name="a"/> (Count if none).</summary>
            public int FirstReaching(float a)
            {
                int lo = 0, hi = Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (MaxHi[mid] < a) lo = mid + 1;
                    else hi = mid;
                }
                return lo;
            }
        }

        readonly RadialIndex _wallIndex = new RadialIndex();
        readonly RadialIndex _sensorIndex = new RadialIndex();
        float[] _radialLo = new float[8];
        float[] _radialHi = new float[8];
        /// <summary>Per collider: index it was in at the last build (<see cref="RadialNone"/> / <see cref="RadialWall"/> / <see cref="RadialSensor"/>).</summary>
        byte[] _radialMember = new byte[8];
        /// <summary>The indexes hold the last build of <see cref="_radialLo"/> / <see cref="_radialHi"/> / <see cref="_radialMember"/>.</summary>
        bool _radialReusable;
        bool _radialHadWalls, _radialHadSensors;
        Vec2 _radialPivot;
        const byte RadialNone = 0, RadialWall = 1, RadialSensor = 2;

        /// <summary>Grows the per-collider radial arrays and both indexes; true if they were reallocated (contents lost).</summary>
        bool EnsureRadialCapacity(int colliders)
        {
            if (_radialLo.Length >= colliders) return false;
            int size = Math.Max(colliders, _radialLo.Length * 2);
            _radialLo = new float[size];
            _radialHi = new float[size];
            _radialMember = new byte[size];
            _wallIndex.EnsureCapacity(size);
            _sensorIndex.EnsureCapacity(size);
            _radialReusable = false;
            return true;
        }

        /// <summary>
        /// Rebuilds both radial indexes from this substep's collider bounds (called at the end of
        /// <see cref="UpdateColliderBounds"/> with the counts of active walls / sensors and the pivot: the collider with the
        /// largest bounds, ties to the lowest id). The
        /// sort is skipped when no collider's interval or membership changed since the last build (rings spinning about their
        /// own centre, static walls): the previous indexes then hold exactly what a rebuild would produce.
        /// </summary>
        void BuildRadialIndexes(int walls, int sensors, int pivot)
        {
            _wallIndex.Usable = false;
            _sensorIndex.Usable = false;
            if (ExhaustiveBroadphase)
            {
                _radialReusable = false;
                return;
            }
            int threshold = RadialIndexThreshold;
            bool wantWalls = walls > 0 && walls >= threshold;
            bool wantSensors = sensors > 0 && sensors >= threshold;
            if (!wantWalls && !wantSensors)
            {
                _radialReusable = false;
                return;
            }
            if (EnsureRadialCapacity(_colliderCount)) _radialReusable = false;
            bool changed = !_radialReusable || wantWalls != _radialHadWalls || wantSensors != _radialHadSensors;
            Vec2 o = _colliderBounds[pivot].Centre;
            _radialPivot = o;
            bool finite = true;
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderBounds cb = ref _colliderBounds[c];
                byte member = cb.Member;
                if (member != _radialMember[c])
                {
                    _radialMember[c] = member;
                    changed = true;
                }
                if (member == RadialNone) continue;
                float d = (cb.Centre - o).Length;
                float lo = d - cb.Outer;
                if (cb.Inner > 0f && cb.Inner - d > lo) lo = cb.Inner - d;
                float hi = d + cb.Outer;
                float slack = RadialSlack * (d + cb.Outer) + RadialSlack;
                lo -= slack;
                hi += slack;
                // NaN or infinite bounds: fall back to the linear scan this substep.
                if (!(lo > float.NegativeInfinity && hi < float.PositiveInfinity)) finite = false;
                if (lo != _radialLo[c] || hi != _radialHi[c])
                {
                    _radialLo[c] = lo;
                    _radialHi[c] = hi;
                    changed = true;
                }
            }
            if (!finite)
            {
                _radialReusable = false;
                return;
            }
            if (changed)
            {
                if (wantWalls) FillRadialIndex(_wallIndex, false);
                if (wantSensors) FillRadialIndex(_sensorIndex, true);
            }
            _wallIndex.Usable = wantWalls;
            _sensorIndex.Usable = wantSensors;
            _radialReusable = true;
            _radialHadWalls = wantWalls;
            _radialHadSensors = wantSensors;
        }

        void FillRadialIndex(RadialIndex ix, bool sensors)
        {
            int n = 0;
            for (int c = 0; c < _colliderCount; c++)
            {
                if (_colliderBounds[c].Member != (sensors ? RadialSensor : RadialWall)) continue;
                ix.Keys[n++] = ((ulong)SortableBits(_radialLo[c]) << 32) | (uint)c;
            }
            SimSort.Sort(ix.Keys, n, ix.Scratch);
            float maxHi = float.NegativeInfinity;
            for (int k = 0; k < n; k++)
            {
                int c = (int)(ix.Keys[k] & 0xFFFFFFFFUL);
                ix.Ids[k] = c;
                ix.Lo[k] = _radialLo[c];
                float hi = _radialHi[c];
                ix.Hi[k] = hi;
                if (hi > maxHi) maxHi = hi;
                ix.MaxHi[k] = maxHi;
            }
            ix.Count = n;
        }

        /// <summary>
        /// Radial range [<paramref name="a"/>, <paramref name="b"/>] around the pivot covered by the path
        /// <paramref name="p0"/>→<paramref name="p1"/> inflated by <paramref name="reach"/> (with slack). False for non-finite
        /// input (the caller then scans linearly).
        /// </summary>
        bool RadialRange(Vec2 p0, Vec2 p1, float reach, out float a, out float b)
        {
            Vec2 o = _radialPivot;
            float d0 = (p0 - o).LengthSquared;
            float d1 = (p1 - o).LengthSquared;
            float dmax = MathF.Sqrt(d0 > d1 ? d0 : d1);
            float dmin = MathF.Sqrt(SegmentPointDistanceSq(p0, p1, o));
            float slack = RadialSlack * (dmax + reach) + RadialSlack;
            a = dmin - reach - slack;
            b = dmax + reach + slack;
            return a > float.NegativeInfinity && b < float.PositiveInfinity;
        }

        /// <summary>Indexed form of <see cref="GatherWallCandidates"/>: same filters, same ascending-id result.</summary>
        int GatherWallCandidatesIndexed(int bi, Vec2 from, Vec2 to, int n, float rCap, float a, float bEnd)
        {
            ref BallState b = ref _balls[bi];
            RadialIndex ix = _wallIndex;
            int count = ix.Count;
            for (int k = ix.FirstReaching(a); k < count && ix.Lo[k] <= bEnd; k++)
            {
                if (ix.Hi[k] < a) continue;
                int c = ix.Ids[k];
                if (_wallMark[c] == _wallStamp) continue;
                ref ColliderBounds cb = ref _colliderBounds[c];
                if (!cb.Active) continue;
                ref ColliderData cd = ref _colliders[c];
                if (cd.Material.IsSensor) continue;
                if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                if (!MayTouch(ref cb, from, to, rCap)) continue;
                _wallMark[c] = _wallStamp;
                int j = n++;
                while (j > 0 && _wallCandidates[j - 1] > c)
                {
                    _wallCandidates[j] = _wallCandidates[j - 1];
                    j--;
                }
                _wallCandidates[j] = c;
            }
            return n;
        }

        /// <summary>Indexed form of <see cref="DetectSensorTouches"/> (pairs are sorted and deduplicated at the end of the tick, so the order of recording does not matter).</summary>
        void DetectSensorTouchesIndexed()
        {
            RadialIndex ix = _sensorIndex;
            int count = ix.Count;
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                if (!b.Alive) continue;
                Vec2 p0 = _ballPrev[i], p1 = b.Position;
                if (!RadialRange(p0, p1, b.Radius, out float a, out float bEnd))
                {
                    DetectSensorTouchesOfBall(i);
                    continue;
                }
                for (int k = ix.FirstReaching(a); k < count && ix.Lo[k] <= bEnd; k++)
                {
                    if (ix.Hi[k] < a) continue;
                    int c = ix.Ids[k];
                    ref ColliderBounds cb = ref _colliderBounds[c];
                    if (!cb.Active) continue;
                    ref ColliderData cd = ref _colliders[c];
                    if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                    if (!MayTouch(ref cb, p0, p1, b.Radius)) continue;
                    if (!SensorTouches(i, c, true)) continue;
                    AddSensorTouched(c, i);
                }
            }
        }

        /// <summary>Linear scan of every sensor for one ball (non-finite radial range).</summary>
        void DetectSensorTouchesOfBall(int i)
        {
            ref BallState b = ref _balls[i];
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderData cd = ref _colliders[c];
                if (!cd.Material.IsSensor) continue;
                ref ColliderBounds cb = ref _colliderBounds[c];
                if (!cb.Active) continue;
                if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                if (!MayTouch(ref cb, _ballPrev[i], b.Position, b.Radius)) continue;
                if (!SensorTouches(i, c, true)) continue;
                AddSensorTouched(c, i);
            }
        }

        void AddSensorTouched(int c, int i)
        {
            if (_sensorTouchedCount == _sensorTouched.Length) Array.Resize(ref _sensorTouched, _sensorTouched.Length * 2);
            _sensorTouched[_sensorTouchedCount++] = SensorKey(c, i);
        }

        /// <summary>
        /// Bounds of a sensor at its current pose for the end-of-tick overlap test: centre and outer radius of everything
        /// <see cref="SensorTouches"/> without sweep can accept before the ball radius is added (the bounding circle of the
        /// geometry plus half the thickness; a closed polyline's or a disc's area lies inside it too), and for rings the
        /// radius of the empty inner disc (−1 otherwise); <paramref name="slack"/> covers the different rounding of the two
        /// computations (add a share of the ball position's magnitude per ball). False for spin platforms: their slab
        /// accepts corners outside that circle once the ball radius exceeds √(2·halfWidth·halfThickness), so they are not
        /// pre-checked.
        /// </summary>
        bool SensorBoundsNow(int c, out Vec2 centre, out float outer, out float inner, out float slack)
        {
            ref ColliderData cd = ref _colliders[c];
            ref PhysicsBodyState b = ref _bodies[cd.Body];
            ref BodyFrame f = ref _frames[cd.Body];
            float s = b.Scale;
            centre = b.Position + Vec2.Rotate(cd.BoundCentre * s, f.Cos, f.Sin);
            float geom = cd.BoundRadius * s;
            slack = RadialSlack * (MathF.Abs(b.Position.X) + MathF.Abs(b.Position.Y) + MathF.Abs(centre.X) + MathF.Abs(centre.Y) + geom + cd.HalfThickness) + RadialSlack;
            inner = -1f;
            switch (cd.Kind)
            {
                case ColliderKind.Circle:
                    outer = geom;
                    return true;
                case ColliderKind.Ring:
                    outer = geom + cd.HalfThickness;
                    inner = geom - cd.HalfThickness;
                    return true;
                case ColliderKind.Segment:
                case ColliderKind.Polyline:
                    outer = geom + cd.HalfThickness;
                    return true;
                default:
                    outer = 0f;
                    return false;
            }
        }

        /// <summary>
        /// End-of-tick pre-check for one (sensor, ball) pair: false only if the ball cannot overlap the sensor (outside its
        /// bounding circle, or inside a ring's empty disc, by more than the slack).
        /// </summary>
        static bool MayOverlapNow(Vec2 p, float r, Vec2 centre, float outer, float inner, float slack)
        {
            float e = slack + RadialSlack * (MathF.Abs(p.X) + MathF.Abs(p.Y) + r);
            float d2 = (p - centre).LengthSquared;
            float o = outer + r + e;
            if (d2 > o * o) return false;
            float i = inner - r - e;
            return !(i > 0f && d2 < i * i);
        }

        // Polyline edge frames: the scaled edge end points, direction, squared length and unit normal of every polyline edge,
        // computed with exactly the expressions the sweep and the overlap gather use (so the cached values are the same bits)
        // and refreshed only when the body's scale changes. A Geometry Bounce arena's six edges were recomputed — including a
        // square root and two divisions each — for every ball in every substep.

        struct EdgeFrame
        {
            public Vec2 A, B, D, N;
            public float L2;
        }

        /// <summary>Edge frames by vertex-pool index of the edge's first vertex.</summary>
        EdgeFrame[] _edgeFrames = new EdgeFrame[16];
        /// <summary>Per collider: the body scale the edge frames were computed for (NaN: none).</summary>
        float[] _edgeScale = NaNs(8);

        static float[] NaNs(int n)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = float.NaN;
            return a;
        }

        void RefreshEdgeFrames(int ci, float s)
        {
            ref ColliderData c = ref _colliders[ci];
            int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
            for (int k = 0; k < edges; k++)
            {
                ref EdgeFrame e = ref _edgeFrames[c.VertexStart + k];
                e.A = _vertices[c.VertexStart + k] * s;
                e.B = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                e.D = e.B - e.A;
                e.L2 = e.D.LengthSquared;
                e.N = e.L2 > 1e-14f ? e.D.Perp / MathF.Sqrt(e.L2) : default;
            }
            _edgeScale[ci] = s;
        }

        /// <summary>The edge frames of polyline <paramref name="ci"/> match its body's current scale (a hook may rescale a body mid-substep).</summary>
        bool EdgeFramesValid(int ci, float s)
        {
            return _edgeScale[ci] == s && !ExhaustiveBroadphase;
        }
    }
}
