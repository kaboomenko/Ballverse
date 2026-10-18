using System;

namespace Ballverse.Core.Sim
{
    // Event collection. Contacts are merged per pair while the tick runs (per-ball linked list of the tick's events), sensor
    // transitions are computed at the end of the tick, and the result is sorted by (kind, A, B) with an allocation-free sort.
    public sealed partial class PhysicsWorld
    {
        SimEvent[] _raw = new SimEvent[64];
        int[] _rawNext = new int[64];
        int _rawCount;
        SimEvent[] _events = new SimEvent[64];
        int _eventCount;
        ulong[] _keys = new ulong[64];
        ulong[] _keyScratch = new ulong[64];
        long _droppedEvents;

        // Sensor pair sets, keys (collider << 32 | ball), sorted ascending.
        ulong[] _sensorTouched = new ulong[16];
        int _sensorTouchedCount;
        ulong[] _sensorActive = new ulong[16];
        int _sensorActiveCount;
        ulong[] _sensorNow = new ulong[16];
        int _sensorNowCount;
        ulong[] _sensorUnion = new ulong[16];
        ulong[] _sensorScratch = new ulong[16];

        /// <summary>Events of the last completed tick, sorted by (kind, A, B).</summary>
        public ReadOnlySpan<SimEvent> Events => new ReadOnlySpan<SimEvent>(_events, 0, _eventCount);

        public int EventCount => _eventCount;

        public ref readonly SimEvent GetEvent(int index)
        {
            if ((uint)index >= (uint)_eventCount) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _events[index];
        }

        /// <summary>Events dropped because a tick exceeded <see cref="MaxEventsPerTick"/> (should stay 0).</summary>
        public long DroppedEvents => _droppedEvents;

        /// <summary>True while the ball overlaps the sensor (state after the last tick).</summary>
        public bool IsTouchingSensor(int ball, int sensorCollider)
        {
            CheckBall(ball);
            CheckCollider(sensorCollider);
            ulong key = SensorKey(sensorCollider, ball);
            int lo = 0, hi = _sensorActiveCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ulong v = _sensorActive[mid];
                if (v == key) return true;
                if (v < key) lo = mid + 1;
                else hi = mid - 1;
            }
            return false;
        }

        void BeginTickEvents()
        {
            _rawCount = 0;
            _sensorTouchedCount = 0;
            for (int i = 0; i < _ballCount; i++) _ballEventHead[i] = -1;
        }

        void RecordContact(SimEventKind kind, int a, int b, int body, Vec2 point, Vec2 normal, float impulse, float speed, float localAngle, int feature)
        {
            int idx = _ballEventHead[a];
            while (idx >= 0)
            {
                if (_raw[idx].Kind == kind && _raw[idx].B == b) break;
                idx = _rawNext[idx];
            }
            if (idx < 0)
            {
                idx = NewRawEvent(kind, a, b, body);
                if (idx < 0) return;
                _rawNext[idx] = _ballEventHead[a];
                _ballEventHead[a] = idx;
            }
            ref SimEvent ev = ref _raw[idx];
            ev.Impulse += impulse;
            if (speed > ev.Speed)
            {
                ev.Speed = speed;
                ev.Point = point;
                ev.Normal = normal;
                ev.LocalAngle = localAngle;
                ev.Feature = feature;
            }
        }

        int NewRawEvent(SimEventKind kind, int a, int b, int body)
        {
            if (_rawCount >= MaxEventsPerTick)
            {
                _droppedEvents++;
                return -1;
            }
            if (_rawCount == _raw.Length)
            {
                Array.Resize(ref _raw, _raw.Length * 2);
                Array.Resize(ref _rawNext, _rawNext.Length * 2);
            }
            int idx = _rawCount++;
            _raw[idx] = new SimEvent { Kind = kind, Tick = _tick, A = a, B = b, Body = body, Speed = -1f };
            _rawNext[idx] = -1;
            return idx;
        }

        void FinishTickEvents()
        {
            int contactCount = _rawCount;
            if (_sensorCount > 0 || _sensorActiveCount > 0) UpdateSensorEvents();
            EnsureKeyCapacity(_rawCount);
            int keyCount = 0;
            float minSpeed = _settings.HitEventMinSpeed;
            for (int i = 0; i < _rawCount; i++)
            {
                ref SimEvent e = ref _raw[i];
                if (i < contactCount && e.Speed < minSpeed) continue;
                _keys[keyCount++] = ((ulong)(uint)e.Kind << 62) | ((ulong)(uint)e.A << 41) | ((ulong)(uint)e.B << 20) | (uint)i;
            }
            SimSort.Sort(_keys, keyCount, _keyScratch);
            if (_events.Length < keyCount) _events = new SimEvent[Math.Max(keyCount, _events.Length * 2)];
            for (int k = 0; k < keyCount; k++) _events[k] = _raw[(int)(_keys[k] & 0xFFFFFUL)];
            _eventCount = keyCount;
        }

        void EnsureKeyCapacity(int n)
        {
            if (_keys.Length >= n) return;
            int size = Math.Max(n, _keys.Length * 2);
            _keys = new ulong[size];
            _keyScratch = new ulong[size];
        }

        static ulong SensorKey(int collider, int ball)
        {
            return ((ulong)(uint)collider << 32) | (uint)ball;
        }

        // ------------------------------------------------------------------ sensors

        /// <summary>Per substep: records every (sensor, ball) pair whose ball overlapped or swept across the sensor.</summary>
        void DetectSensorTouches()
        {
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderData cd = ref _colliders[c];
                if (!cd.Material.IsSensor) continue;
                ref ColliderBounds cb = ref _colliderBounds[c];
                if (!cb.Active) continue;
                for (int i = 0; i < _ballCount; i++)
                {
                    ref BallState b = ref _balls[i];
                    if (!b.Alive) continue;
                    if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                    if (!MayTouch(ref cb, _ballPrev[i], b.Position, b.Radius)) continue;
                    if (!SensorTouches(i, c, true)) continue;
                    if (_sensorTouchedCount == _sensorTouched.Length) Array.Resize(ref _sensorTouched, _sensorTouched.Length * 2);
                    _sensorTouched[_sensorTouchedCount++] = SensorKey(c, i);
                }
            }
        }

        /// <summary>
        /// Sensor overlap: the ball touches the sensor shape (or, for discs and closed polylines, lies inside the area). With
        /// <paramref name="sweep"/>, a path across the shape during the substep counts too (thin crossing lines).
        /// </summary>
        bool SensorTouches(int bi, int ci, bool sweep)
        {
            ref ColliderData c = ref _colliders[ci];
            ref BodyState body = ref _bodies[c.Body];
            ref BodyFrame f = ref _frames[c.Body];
            ref BallState ball = ref _balls[bi];
            float s = body.Scale;
            Vec2 q1 = Vec2.InvRotate(ball.Position - body.Position, f.Cos, f.Sin);
            Vec2 q0 = Vec2.InvRotate(_ballPrev[bi] - f.PrevPosition, f.PrevCos, f.PrevSin);
            if (f.PrevScale != s) q0 = q0 * (s / f.PrevScale);
            float r = ball.Radius;
            float rt = r + c.HalfThickness;
            SweepHit hit = SweepHit.None;
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                    if (CapsuleOverlap(q1, c.A * s, c.B * s, rt)) return true;
                    return sweep && SweepCapsule(q0, q1, c.A * s, c.B * s, rt, 0, ref hit);
                case ColliderKind.Polyline:
                {
                    int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                    for (int k = 0; k < edges; k++)
                    {
                        Vec2 a = _vertices[c.VertexStart + k] * s;
                        Vec2 b = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                        if (CapsuleOverlap(q1, a, b, rt)) return true;
                        if (sweep && SweepCapsule(q0, q1, a, b, rt, 0, ref hit)) return true;
                    }
                    return c.Closed && PointInPolygon(ref c, q1, s);
                }
                case ColliderKind.Ring:
                {
                    Vec2 v = q1 - c.A * s;
                    float d = v.Length;
                    float radius = c.Radius * s;
                    if (d - radius < rt && radius - d < rt)
                    {
                        if (SolidArcAt(ref c, SimMath.Atan2(v.Y, v.X)) != NoArc) return true;
                        for (int k = 0; k < c.ArcCount; k++)
                        {
                            int ai = c.ArcStart + k;
                            if ((q1 - (c.A * s + _arcStartDir[ai] * radius)).LengthSquared < rt * rt) return true;
                            if ((q1 - (c.A * s + _arcEndDir[ai] * radius)).LengthSquared < rt * rt) return true;
                        }
                    }
                    return sweep && SweepRing(ref c, q0, q1, s, rt, ref hit);
                }
                case ColliderKind.Circle:
                {
                    float rc = r + c.Radius * s;
                    if ((q1 - c.A * s).LengthSquared < rc * rc) return true;
                    return sweep && SweepCircle(q0, q1 - q0, c.A * s, rc, 0, ref hit);
                }
                case ColliderKind.SpinPlatform:
                {
                    int pieces = BuildPlatformPieces(ref c, body.Spin);
                    for (int k = 0; k < pieces; k++)
                    {
                        PlatformPiece(ref c, s, k, out Vec2 a, out Vec2 b);
                        if (CapsuleOverlap(q1, a, b, rt)) return true;
                        if (sweep && SweepCapsule(q0, q1, a, b, rt, 0, ref hit)) return true;
                    }
                    return false;
                }
            }
            return false;
        }

        static bool CapsuleOverlap(Vec2 q, Vec2 a, Vec2 b, float rt)
        {
            return SegmentPointDistanceSq(a, b, q) < rt * rt;
        }

        bool PointInPolygon(ref ColliderData c, Vec2 q, float s)
        {
            bool inside = false;
            int n = c.VertexCount;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vec2 pi = _vertices[c.VertexStart + i] * s;
                Vec2 pj = _vertices[c.VertexStart + j] * s;
                if ((pi.Y > q.Y) != (pj.Y > q.Y))
                {
                    float x = pj.X + (q.Y - pj.Y) * (pi.X - pj.X) / (pi.Y - pj.Y);
                    if (q.X < x) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>End of tick: diff touched / overlapping pairs against the previous state into Enter / Exit events.</summary>
        void UpdateSensorEvents()
        {
            // Touched during the substeps: sort + dedupe.
            EnsureSensorScratch(_sensorTouchedCount);
            SimSort.Sort(_sensorTouched, _sensorTouchedCount, _sensorScratch);
            int tc = Dedupe(_sensorTouched, _sensorTouchedCount);

            // Overlapping at the end of the tick (ascending collider, then ball: already sorted).
            _sensorNowCount = 0;
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderData cd = ref _colliders[c];
                if (!cd.Material.IsSensor || !cd.Enabled || !_bodies[cd.Body].Enabled) continue;
                for (int i = 0; i < _ballCount; i++)
                {
                    ref BallState b = ref _balls[i];
                    if (!b.Alive) continue;
                    if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                    if (!SensorTouches(i, c, false)) continue;
                    if (_sensorNowCount == _sensorNow.Length) Array.Resize(ref _sensorNow, _sensorNow.Length * 2);
                    _sensorNow[_sensorNowCount++] = SensorKey(c, i);
                }
            }

            // Union of touched and now.
            int un = 0;
            if (_sensorUnion.Length < tc + _sensorNowCount) _sensorUnion = new ulong[Math.Max(tc + _sensorNowCount, _sensorUnion.Length * 2)];
            {
                int i = 0, j = 0;
                while (i < tc || j < _sensorNowCount)
                {
                    ulong v;
                    if (j >= _sensorNowCount || (i < tc && _sensorTouched[i] < _sensorNow[j])) v = _sensorTouched[i++];
                    else if (i >= tc || _sensorNow[j] < _sensorTouched[i]) v = _sensorNow[j++];
                    else
                    {
                        v = _sensorNow[j++];
                        i++;
                    }
                    _sensorUnion[un++] = v;
                }
            }

            // Enter: in union, not previously active.
            {
                int i = 0, j = 0;
                while (i < un)
                {
                    ulong u = _sensorUnion[i];
                    while (j < _sensorActiveCount && _sensorActive[j] < u) j++;
                    if (j >= _sensorActiveCount || _sensorActive[j] != u) AddSensorEvent(SimEventKind.SensorEnter, u);
                    i++;
                }
            }

            // Exit: previously active or touched this tick, and not overlapping now.
            {
                int i = 0, j = 0, k = 0;
                while (i < _sensorActiveCount || j < un)
                {
                    ulong v;
                    if (j >= un || (i < _sensorActiveCount && _sensorActive[i] < _sensorUnion[j])) v = _sensorActive[i++];
                    else if (i >= _sensorActiveCount || _sensorUnion[j] < _sensorActive[i]) v = _sensorUnion[j++];
                    else
                    {
                        v = _sensorUnion[j++];
                        i++;
                    }
                    while (k < _sensorNowCount && _sensorNow[k] < v) k++;
                    if (k >= _sensorNowCount || _sensorNow[k] != v) AddSensorEvent(SimEventKind.SensorExit, v);
                }
            }

            ulong[] t = _sensorActive;
            _sensorActive = _sensorNow;
            _sensorActiveCount = _sensorNowCount;
            _sensorNow = t;
        }

        void AddSensorEvent(SimEventKind kind, ulong key)
        {
            int collider = (int)(key >> 32);
            int ball = (int)(key & 0xFFFFFFFFUL);
            int idx = NewRawEvent(kind, ball, collider, _colliders[collider].Body);
            if (idx < 0) return;
            _raw[idx].Point = _balls[ball].Position;
            _raw[idx].Speed = 0f;
        }

        void EnsureSensorScratch(int n)
        {
            if (_sensorScratch.Length < n) _sensorScratch = new ulong[Math.Max(n, _sensorScratch.Length * 2)];
        }

        static int Dedupe(ulong[] a, int n)
        {
            if (n == 0) return 0;
            int m = 1;
            for (int i = 1; i < n; i++)
            {
                if (a[i] != a[m - 1]) a[m++] = a[i];
            }
            return m;
        }
    }
}
