using System;

namespace Ballverse.Core.Sim
{
    // Ball vs walls: continuous sweep against ALL colliders at once (anti-tunnelling), then discrete overlap resolution.
    //
    // All tests run in the collider body's rotated frame ("q space": world offsets rotated by −angle, in metres; body scale
    // applied to the geometry, never to thickness or ball radius). The ball's position at the start of the swept interval is
    // mapped through the body's pose at that time and its end position through the current pose, so the swept chord q0→q1 is
    // the motion RELATIVE to the moving wall: a fast-rotating polygon sweeping over a ball is caught like a ball flying into a
    // wall.
    //
    // Sweep: the earliest time of first contact over every collider the ball's path may touch (walls inflated by the ball
    // radius; ties go to the lower collider id). That contact is resolved at its time with the wall's interpolated pose, the
    // ball travels the rest of the substep with its new velocity, and the remaining path is swept again against every
    // collider — including the ones already passed — so corners built from separate colliders and closely spaced rings stay
    // continuous. The number of resolved impacts per substep is bounded (MaxSweepPasses); the last allowed impact stops the
    // ball at the contact for the rest of the substep (conservative advancement) instead of letting it travel unswept. If
    // the ball already overlaps a wall and its centre crosses the wall's centre line, it is put back on the side it came
    // from ("corrected" hit). Finally every candidate collider (ascending id) and its primitives are checked for overlap in a
    // fixed order (Gauss–Seidel), which handles resting contacts.
    public sealed partial class PhysicsWorld
    {
        const float NormalEpsilon = 1e-7f;
        /// <summary>Resolved impacts per ball and substep before the ball is held at the last contact.</summary>
        const int MaxSweepPasses = 8;
        /// <summary>
        /// A ball within this distance (0.1 mm) of touching a primitive at the start of the substep is "in contact": it is resolved by
        /// discrete projection (keeps sliding and solver corrections) instead of a time of impact, which would rewind it to
        /// its start position. The centre-line crossing test still guards such balls against tunnelling.
        /// </summary>
        const float ContactSlop = 0.0001f;

        // Per-ball wall candidates of the current substep (ascending collider id) and per-collider stamps.
        int[] _wallCandidates = new int[16];
        int[] _wallMark = new int[8];
        int[] _wallRejected = new int[8];
        int _wallStamp;
        /// <summary>World ball centre at the last resolved contact (start of the remaining path).</summary>
        Vec2 _contactPos;

        struct SweepHit
        {
            /// <summary>Fraction of the swept chord at first contact (2 = none).</summary>
            public float T;
            /// <summary>Ball centre at contact (q space).</summary>
            public Vec2 Pos;
            /// <summary>Normal from the wall towards the ball (q space).</summary>
            public Vec2 N;
            public int Feature;
            /// <summary>Overlap correction (centre-line crossing) rather than a time of impact.</summary>
            public bool Corrected;

            public static SweepHit None => new SweepHit { T = 2f };
        }

        void EnsureColliderScratch()
        {
            if (_wallMark.Length >= _colliderCount) return;
            int size = Math.Max(_colliderCount, _wallMark.Length * 2);
            Array.Resize(ref _wallMark, size);
            Array.Resize(ref _wallRejected, size);
            Array.Resize(ref _wallCandidates, size);
        }

        void CollideBallWithWalls(int bi, float h)
        {
            ref BallState ball = ref _balls[bi];
            if (_wallStamp == int.MaxValue)
            {
                Array.Clear(_wallMark, 0, _wallMark.Length);
                Array.Clear(_wallRejected, 0, _wallRejected.Length);
                _wallStamp = 0;
            }
            _wallStamp++;
            int n = GatherWallCandidates(bi, _ballPrev[bi], ball.Position, 0);
            if (n == 0) return;

            float tStart = 0f;
            Vec2 wStart = _ballPrev[bi];
            int impacts = 0;
            while (true)
            {
                SweepHit best = SweepHit.None;
                int bestC = -1;
                for (int k = 0; k < n; k++)
                {
                    int c = _wallCandidates[k];
                    if (_wallRejected[c] == _wallStamp) continue;
                    if (impacts > 0 && !MayTouch(ref _colliderBounds[c], wStart, ball.Position, ball.Radius)) continue;
                    if (SweepWall(bi, c, tStart, wStart, ref best)) bestC = c;
                }
                if (bestC < 0) break;
                if (best.Corrected)
                {
                    if (!ResolveContact(bi, bestC, best.Pos, best.N, best.Feature, -1f, h, false))
                    {
                        if (!ball.Alive) return;
                        _wallRejected[bestC] = _wallStamp;
                        continue;
                    }
                    break;
                }
                float tAbs = tStart + (1f - tStart) * best.T;
                bool last = impacts == MaxSweepPasses - 1;
                if (!ResolveContact(bi, bestC, best.Pos, best.N, best.Feature, tAbs, h, !last))
                {
                    if (!ball.Alive) return;
                    _wallRejected[bestC] = _wallStamp;
                    continue;
                }
                impacts++;
                if (last) break;
                tStart = tAbs;
                wStart = _contactPos;
                // The new path may reach colliders the original one did not.
                n = GatherWallCandidates(bi, wStart, ball.Position, n);
            }
            if (!ball.Alive) return;

            for (int k = 0; k < n; k++)
            {
                int c = _wallCandidates[k];
                if (_wallRejected[c] == _wallStamp) continue;
                DiscreteWall(bi, c, h);
                if (!ball.Alive) return;
            }
        }

        /// <summary>
        /// Adds every eligible collider not yet listed whose bounds the path <paramref name="from"/>→<paramref name="to"/> may
        /// touch, keeping <see cref="_wallCandidates"/>[0..n) in ascending id order. Returns the new count.
        /// </summary>
        int GatherWallCandidates(int bi, Vec2 from, Vec2 to, int n)
        {
            ref BallState b = ref _balls[bi];
            float r = b.Radius;
            for (int c = 0; c < _colliderCount; c++)
            {
                if (_wallMark[c] == _wallStamp) continue;
                ref ColliderBounds cb = ref _colliderBounds[c];
                if (!cb.Active) continue;
                ref ColliderData cd = ref _colliders[c];
                if (cd.Material.IsSensor) continue;
                if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                if (!MayTouch(ref cb, from, to, r)) continue;
                _wallMark[c] = _wallStamp;
                int k = n++;
                while (k > 0 && _wallCandidates[k - 1] > c)
                {
                    _wallCandidates[k] = _wallCandidates[k - 1];
                    k--;
                }
                _wallCandidates[k] = c;
            }
            return n;
        }

        /// <summary>
        /// Pose of a body at fraction <paramref name="t"/> of the current substep: exactly the start pose at 0, otherwise
        /// position and scale interpolated linearly and the rotation interpolated on the unit circle.
        /// </summary>
        void PoseAt(int body, float t, out Vec2 position, out float cos, out float sin, out float scale)
        {
            ref BodyState b = ref _bodies[body];
            ref BodyFrame f = ref _frames[body];
            if (t <= 0f)
            {
                position = f.PrevPosition;
                cos = f.PrevCos;
                sin = f.PrevSin;
                scale = f.PrevScale;
                return;
            }
            position = Vec2.Lerp(f.PrevPosition, b.Position, t);
            cos = f.PrevCos + (f.Cos - f.PrevCos) * t;
            sin = f.PrevSin + (f.Sin - f.PrevSin) * t;
            float inv = 1f / MathF.Sqrt(cos * cos + sin * sin);
            cos *= inv;
            sin *= inv;
            scale = f.PrevScale + (b.Scale - f.PrevScale) * t;
        }

        /// <summary>World point → q space of <paramref name="body"/> at substep fraction <paramref name="t"/> (geometry at the current scale).</summary>
        Vec2 ToBodySpaceAt(int body, Vec2 world, float t)
        {
            PoseAt(body, t, out Vec2 pos, out float cos, out float sin, out float scale);
            Vec2 q = Vec2.InvRotate(world - pos, cos, sin);
            float s = _bodies[body].Scale;
            if (scale != s) q = q * (s / scale);
            return q;
        }

        /// <summary>Sweeps the ball's remaining path (from <paramref name="wStart"/> at <paramref name="tStart"/> to its current position) against one collider.</summary>
        bool SweepWall(int bi, int ci, float tStart, Vec2 wStart, ref SweepHit best)
        {
            ref ColliderData c = ref _colliders[ci];
            ref BodyState body = ref _bodies[c.Body];
            ref BodyFrame f = ref _frames[c.Body];
            ref BallState ball = ref _balls[bi];
            Vec2 q1 = Vec2.InvRotate(ball.Position - body.Position, f.Cos, f.Sin);
            Vec2 q0 = ToBodySpaceAt(c.Body, wStart, tStart);
            float r = ball.Radius;
            int pieceCount = c.Kind == ColliderKind.SpinPlatform ? BuildPlatformPieces(ref c, body.Spin) : 0;
            return SweepCollider(ref c, q0, q1, body.Scale, r, r + c.HalfThickness, pieceCount, ref best);
        }

        /// <summary>Discrete overlap resolution against every primitive of one collider (current pose).</summary>
        void DiscreteWall(int bi, int ci, float h)
        {
            ref ColliderData c = ref _colliders[ci];
            ref BodyState body = ref _bodies[c.Body];
            ref BodyFrame f = ref _frames[c.Body];
            ref BallState ball = ref _balls[bi];
            float s = body.Scale;
            Vec2 q1 = Vec2.InvRotate(ball.Position - body.Position, f.Cos, f.Sin);
            // Start-of-substep position: tells which side a ball exactly on a centre line came from.
            Vec2 qStart = ToBodySpaceAt(c.Body, _ballPrev[bi], 0f);
            float r = ball.Radius;
            float rt = r + c.HalfThickness;
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                    DiscreteCapsule(bi, ci, ref q1, qStart, c.A * s, c.B * s, rt, 0, h);
                    break;
                case ColliderKind.Polyline:
                {
                    int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                    for (int k = 0; k < edges; k++)
                    {
                        Vec2 a = _vertices[c.VertexStart + k] * s;
                        Vec2 b = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                        if (!DiscreteCapsule(bi, ci, ref q1, qStart, a, b, rt, k, h)) return;
                    }
                    break;
                }
                case ColliderKind.Ring:
                    DiscreteRing(bi, ci, ref c, ref q1, qStart, s, rt, h);
                    break;
                case ColliderKind.Circle:
                {
                    Vec2 o = c.A * s;
                    float rc = r + c.Radius * s;
                    Vec2 v = q1 - o;
                    float d2 = v.LengthSquared;
                    if (d2 < rc * rc)
                    {
                        float d = MathF.Sqrt(d2);
                        Vec2 n = d > NormalEpsilon ? v / d : FallbackNormal(qStart - o);
                        Vec2 pos = o + n * rc;
                        if (ResolveContact(bi, ci, pos, n, 0, -1f, h, false)) q1 = pos;
                    }
                    break;
                }
                case ColliderKind.SpinPlatform:
                {
                    int pieceCount = BuildPlatformPieces(ref c, body.Spin);
                    for (int k = 0; k < pieceCount; k++)
                    {
                        PlatformPiece(ref c, s, k, out Vec2 a, out Vec2 b);
                        if (!DiscreteCapsule(bi, ci, ref q1, qStart, a, b, rt, 0, h)) return;
                    }
                    break;
                }
            }
        }

        /// <summary>Earliest sweep hit of the chord q0→q1 against all primitives of the collider.</summary>
        bool SweepCollider(ref ColliderData c, Vec2 q0, Vec2 q1, float s, float r, float rt, int pieceCount, ref SweepHit hit)
        {
            bool found = false;
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                    found = SweepCapsule(q0, q1, c.A * s, c.B * s, rt, 0, ref hit);
                    break;
                case ColliderKind.Polyline:
                {
                    int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                    for (int k = 0; k < edges; k++)
                    {
                        Vec2 a = _vertices[c.VertexStart + k] * s;
                        Vec2 b = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                        if (SweepCapsule(q0, q1, a, b, rt, k, ref hit)) found = true;
                    }
                    break;
                }
                case ColliderKind.Ring:
                    found = SweepRing(ref c, q0, q1, s, rt, ref hit);
                    break;
                case ColliderKind.Circle:
                    found = SweepCircle(q0, q1 - q0, c.A * s, r + c.Radius * s, 0, ref hit);
                    break;
                case ColliderKind.SpinPlatform:
                    for (int k = 0; k < pieceCount; k++)
                    {
                        PlatformPiece(ref c, s, k, out Vec2 a, out Vec2 b);
                        if (SweepCapsule(q0, q1, a, b, rt, 0, ref hit)) found = true;
                    }
                    break;
            }
            return found;
        }

        /// <summary>Overlap test against one capsule; resolves and updates <paramref name="q"/>. False when the hook rejected the contact.</summary>
        bool DiscreteCapsule(int bi, int ci, ref Vec2 q, Vec2 q0, Vec2 a, Vec2 b, float rt, int feature, float h)
        {
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            float u = 0f;
            if (l2 > 1e-14f)
            {
                u = Vec2.Dot(q - a, d) / l2;
                u = u < 0f ? 0f : (u > 1f ? 1f : u);
            }
            Vec2 cp = a + d * u;
            Vec2 v = q - cp;
            float dist2 = v.LengthSquared;
            if (dist2 >= rt * rt) return true;
            float dist = MathF.Sqrt(dist2);
            Vec2 n;
            if (dist > NormalEpsilon)
            {
                n = v / dist;
            }
            else if (l2 > 1e-14f)
            {
                // Centre exactly on the wall: go back to the side the ball came from.
                Vec2 ln = d.Perp / MathF.Sqrt(l2);
                n = Vec2.Dot(q0 - a, ln) >= 0f ? ln : -ln;
            }
            else
            {
                n = FallbackNormal(q0 - a);
            }
            Vec2 pos = cp + n * rt;
            if (!ResolveContact(bi, ci, pos, n, feature, -1f, h, false)) return false;
            q = pos;
            return true;
        }

        void DiscreteRing(int bi, int ci, ref ColliderData c, ref Vec2 q, Vec2 q0, float s, float rt, float h)
        {
            Vec2 o = c.A * s;
            float radius = c.Radius * s;
            Vec2 v = q - o;
            float d = v.Length;
            float gap = d - radius;
            if (gap >= rt || gap <= -rt) return;
            float theta = SimMath.Atan2(v.Y, v.X);
            int arc = SolidArcAt(ref c, theta);
            if (arc != NoArc)
            {
                if (d <= NormalEpsilon) return;
                float side;
                if (gap > 0f) side = 1f;
                else if (gap < 0f) side = -1f;
                else side = (q0 - o).Length >= radius ? 1f : -1f;
                Vec2 dir = v / d;
                Vec2 pos = o + dir * (radius + side * rt);
                if (ResolveContact(bi, ci, pos, dir * side, arc, -1f, h, false)) q = pos;
                return;
            }
            // Inside a gap: only the round end caps of the neighbouring solid arcs can touch.
            for (int k = 0; k < c.ArcCount; k++)
            {
                int ai = c.ArcStart + k;
                if (!DiscreteCap(bi, ci, ref q, q0, o + _arcStartDir[ai] * radius, rt, k, h)) return;
                if (!DiscreteCap(bi, ci, ref q, q0, o + _arcEndDir[ai] * radius, rt, k, h)) return;
            }
        }

        bool DiscreteCap(int bi, int ci, ref Vec2 q, Vec2 q0, Vec2 centre, float rt, int feature, float h)
        {
            Vec2 v = q - centre;
            float d2 = v.LengthSquared;
            if (d2 >= rt * rt) return true;
            float d = MathF.Sqrt(d2);
            Vec2 n = d > NormalEpsilon ? v / d : FallbackNormal(q0 - centre);
            Vec2 pos = centre + n * rt;
            if (!ResolveContact(bi, ci, pos, n, feature, -1f, h, false)) return false;
            q = pos;
            return true;
        }

        static Vec2 FallbackNormal(Vec2 preferred)
        {
            float l = preferred.Length;
            return l > NormalEpsilon ? preferred / l : new Vec2(0f, 1f);
        }

        const int NoArc = int.MinValue;

        /// <summary>Solid arc index containing the body-local angle, −1 for a full ring, <see cref="NoArc"/> inside a gap.</summary>
        int SolidArcAt(ref ColliderData c, float theta)
        {
            if (c.Full) return -1;
            for (int k = 0; k < c.ArcCount; k++)
            {
                int ai = c.ArcStart + k;
                if (SimMath.AngleInArc(theta, _arcStart[ai], _arcSpan[ai])) return k;
            }
            return NoArc;
        }

        // ------------------------------------------------------------------ sweeps

        /// <summary>Sweep of the centre q0→q1 against a capsule (a, b, radius rt); keeps the earliest candidate in <paramref name="best"/>.</summary>
        static bool SweepCapsule(Vec2 q0, Vec2 q1, Vec2 a, Vec2 b, float rt, int feature, ref SweepHit best)
        {
            Vec2 motion = q1 - q0;
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            bool found = false;
            if (l2 > 1e-14f)
            {
                float len = MathF.Sqrt(l2);
                Vec2 n = d.Perp / len;
                float s0 = Vec2.Dot(q0 - a, n);
                float s1 = Vec2.Dot(q1 - a, n);
                // The whole chord stays farther than rt from the capsule's line on one side: neither the face nor the end
                // caps (which lie on the line) can be reached.
                if ((s0 > rt && s1 > rt) || (s0 < -rt && s1 < -rt)) return false;
                float side = s0 > 0f ? 1f : (s0 < 0f ? -1f : (s1 > 0f ? -1f : 1f));
                float g0 = side * s0;
                float g1 = side * s1;
                if (g0 >= rt + ContactSlop)
                {
                    if (g1 < rt)
                    {
                        // First touch of the face offset by rt.
                        float t = (g0 - rt) / (g0 - g1);
                        if (t < best.T)
                        {
                            Vec2 x = q0 + motion * t;
                            float u = Vec2.Dot(x - a, d) / l2;
                            if (u >= 0f && u <= 1f)
                            {
                                best = new SweepHit { T = t, Pos = x, N = n * side, Feature = feature };
                                found = true;
                            }
                        }
                    }
                }
                else if (g1 < 0f)
                {
                    // Already overlapping and the centre crosses the centre line: back to the entry side.
                    float t = g0 / (g0 - g1);
                    if (t < best.T)
                    {
                        Vec2 x = q0 + motion * t;
                        float u = Vec2.Dot(x - a, d) / l2;
                        if (u >= 0f && u <= 1f)
                        {
                            best = new SweepHit { T = t, Pos = a + d * u + n * (side * rt), N = n * side, Feature = feature, Corrected = true };
                            found = true;
                        }
                    }
                }
            }
            if (SweepCircle(q0, motion, a, rt, feature, ref best)) found = true;
            if (SweepCircle(q0, motion, b, rt, feature, ref best)) found = true;
            return found;
        }

        /// <summary>Earliest entry of the moving point into the disc (centre, rt), if it starts outside.</summary>
        static bool SweepCircle(Vec2 q0, Vec2 motion, Vec2 centre, float rt, int feature, ref SweepHit best)
        {
            Vec2 m = q0 - centre;
            float m2 = m.LengthSquared;
            float contact = rt + ContactSlop;
            if (m2 <= contact * contact) return false;
            float cq = m2 - rt * rt;
            float bq = Vec2.Dot(m, motion);
            if (bq >= 0f) return false;
            float aq = motion.LengthSquared;
            float disc = bq * bq - aq * cq;
            if (disc < 0f) return false;
            float t = (-bq - MathF.Sqrt(disc)) / aq;
            if (t < 0f || t > 1f || t >= best.T) return false;
            Vec2 x = q0 + motion * t;
            best = new SweepHit { T = t, Pos = x, N = FallbackNormal(x - centre), Feature = feature };
            return true;
        }

        bool SweepRing(ref ColliderData c, Vec2 q0, Vec2 q1, float s, float rt, ref SweepHit best)
        {
            Vec2 o = c.A * s;
            float radius = c.Radius * s;
            Vec2 v0 = q0 - o;
            Vec2 v1 = q1 - o;
            Vec2 motion = v1 - v0;
            float aq = motion.LengthSquared;
            if (aq < 1e-14f) return false;
            float d0 = v0.Length;
            float d1 = v1.Length;
            float side = d0 >= radius ? 1f : -1f;
            float g0 = side * (d0 - radius);
            float g1 = side * (d1 - radius);
            bool found = false;
            if (g0 >= rt + ContactSlop && g1 < rt)
            {
                float ro = radius + side * rt;
                if (ro > 0f)
                {
                    float t = CircleRoot(v0, motion, aq, ro, side > 0f);
                    if (t >= 0f && t <= 1f && t < best.T)
                    {
                        Vec2 x = v0 + motion * t;
                        int arc = SolidArcAt(ref c, SimMath.Atan2(x.Y, x.X));
                        if (arc != NoArc)
                        {
                            best = new SweepHit { T = t, Pos = o + x, N = FallbackNormal(x) * side, Feature = arc };
                            found = true;
                        }
                    }
                }
            }
            if (g1 < 0f && g0 < rt + ContactSlop)
            {
                // Overlapping at the start and crossing the centre line.
                float t = CircleRoot(v0, motion, aq, radius, side > 0f);
                if (t >= 0f && t <= 1f && t < best.T)
                {
                    Vec2 x = v0 + motion * t;
                    int arc = SolidArcAt(ref c, SimMath.Atan2(x.Y, x.X));
                    if (arc != NoArc)
                    {
                        Vec2 dir = FallbackNormal(x);
                        best = new SweepHit { T = t, Pos = o + dir * (radius + side * rt), N = dir * side, Feature = arc, Corrected = true };
                        found = true;
                    }
                }
            }
            else if (g1 < 0f)
            {
                // Started clear, crossed the centre line where the offset face was a gap but the centre line is solid
                // (grazing a gap edge diagonally): treat the centre-line crossing as the contact.
                float t = CircleRoot(v0, motion, aq, radius, side > 0f);
                if (t >= 0f && t <= 1f && t < best.T)
                {
                    Vec2 x = v0 + motion * t;
                    int arc = SolidArcAt(ref c, SimMath.Atan2(x.Y, x.X));
                    if (arc != NoArc)
                    {
                        Vec2 dir = FallbackNormal(x);
                        best = new SweepHit { T = t, Pos = o + dir * (radius + side * rt), N = dir * side, Feature = arc, Corrected = true };
                        found = true;
                    }
                }
            }
            if (!c.Full)
            {
                for (int k = 0; k < c.ArcCount; k++)
                {
                    int ai = c.ArcStart + k;
                    if (SweepCircle(q0, motion, o + _arcStartDir[ai] * radius, rt, k, ref best)) found = true;
                    if (SweepCircle(q0, motion, o + _arcEndDir[ai] * radius, rt, k, ref best)) found = true;
                }
            }
            return found;
        }

        /// <summary>Parameter where |v0 + t·motion| = radius: the entering (smaller) or leaving (larger) root; −1 if none.</summary>
        static float CircleRoot(Vec2 v0, Vec2 motion, float aq, float radius, bool entering)
        {
            float bq = Vec2.Dot(v0, motion);
            float cq = v0.LengthSquared - radius * radius;
            float disc = bq * bq - aq * cq;
            if (disc < 0f) return -1f;
            float sq = MathF.Sqrt(disc);
            return entering ? (-bq - sq) / aq : (-bq + sq) / aq;
        }

        // ------------------------------------------------------------------ spin platforms

        /// <summary>
        /// Visible solid pieces of a spin platform for the given spin, as pairs of sin(azimuth) in <see cref="_pieces"/>.
        /// Only the front half (world azimuth −π/2…π/2, facing the camera) is collidable.
        /// </summary>
        int BuildPlatformPieces(ref ColliderData c, float spin)
        {
            if (c.Full)
            {
                _pieces[0] = -1f;
                _pieces[1] = 1f;
                return 1;
            }
            int n = 0;
            for (int k = 0; k < c.ArcCount; k++)
            {
                int ai = c.ArcStart + k;
                float lo = SimMath.WrapPi(_arcStart[ai] + spin);
                float hi = lo + _arcSpan[ai];
                n = AddPiece(lo, hi, n);
                n = AddPiece(lo - SimMath.TwoPi, hi - SimMath.TwoPi, n);
            }
            return n;
        }

        int AddPiece(float lo, float hi, int n)
        {
            if (lo < -SimMath.HalfPi) lo = -SimMath.HalfPi;
            if (hi > SimMath.HalfPi) hi = SimMath.HalfPi;
            if (!(hi > lo)) return n;
            _pieces[2 * n] = SimMath.Sin(lo);
            _pieces[2 * n + 1] = SimMath.Sin(hi);
            return n + 1;
        }

        void PlatformPiece(ref ColliderData c, float s, int k, out Vec2 a, out Vec2 b)
        {
            float w = c.Radius * s;
            Vec2 o = c.A * s;
            a = new Vec2(o.X + w * _pieces[2 * k], o.Y);
            b = new Vec2(o.X + w * _pieces[2 * k + 1], o.Y);
        }

        // ------------------------------------------------------------------ response

        /// <summary>
        /// Applies one contact. <paramref name="qPos"/> / <paramref name="nLocal"/> are the ball centre and normal in q space.
        /// <para>
        /// With <paramref name="sweepT"/> ≥ 0 (time of impact as a fraction of the substep) the contact is placed with the body's
        /// pose interpolated to that time; with <paramref name="advance"/> the ball then travels the rest of the substep with its
        /// new velocity — so a ball bouncing off a rotating wall is not carried along by the wall's rotation (which would bias
        /// energy) — otherwise it stays at the contact. With −1 the current pose is used and the ball stays at the resolved
        /// position (overlap correction). <see cref="_contactPos"/> receives the world contact centre.
        /// </para>
        /// The impulse uses the wall's velocity at the contact point (linear + ω × r + scale rate + platform surface motion).
        /// Returns false if the ball is dead or the mode hook rejected the contact or killed the ball (nothing changed).
        /// </summary>
        bool ResolveContact(int bi, int ci, Vec2 qPos, Vec2 nLocal, int feature, float sweepT, float h, bool advance)
        {
            ref ColliderData c = ref _colliders[ci];
            ref BodyState body = ref _bodies[c.Body];
            ref BallState ball = ref _balls[bi];
            if (!ball.Alive) return false;
            float r = ball.Radius;
            float s = body.Scale;

            Vec2 bodyPos;
            float cos, sin, scaleNow;
            if (sweepT >= 0f)
            {
                PoseAt(c.Body, sweepT, out bodyPos, out cos, out sin, out scaleNow);
            }
            else
            {
                ref BodyFrame f = ref _frames[c.Body];
                bodyPos = body.Position;
                cos = f.Cos;
                sin = f.Sin;
                scaleNow = s;
            }
            Vec2 qWorldScale = scaleNow != s ? qPos * (scaleNow / s) : qPos;
            Vec2 n = Vec2.Rotate(nLocal, cos, sin);
            Vec2 pos = bodyPos + Vec2.Rotate(qWorldScale, cos, sin);
            Vec2 cp = pos - n * r;
            Vec2 cpLocal = qPos - nLocal * r;

            float localAngle = 0f;
            Vec2 surfaceLocal = Vec2.Zero;
            if (c.Kind == ColliderKind.Ring)
            {
                Vec2 rel = cpLocal - c.A * s;
                localAngle = SimMath.Wrap2Pi(SimMath.Atan2(rel.Y, rel.X));
            }
            else if (c.Kind == ColliderKind.SpinPlatform)
            {
                float w = c.Radius * s;
                float sinA = SimMath.Clamp((cpLocal.X - c.A.X * s) / w, -1f, 1f);
                float alpha = SimMath.Asin(sinA);
                localAngle = SimMath.Wrap2Pi(alpha - body.Spin);
                surfaceLocal = new Vec2(w * SimMath.Cos(alpha) * body.SpinVelocity, 0f);
            }

            Vec2 wallVelocity = Vec2.Zero;
            if (body.Motion == BodyMotion.Kinematic)
            {
                Vec2 arm = cp - bodyPos;
                wallVelocity = body.LinearVelocity + Vec2.Cross(body.AngularVelocity, arm);
                if (body.ScaleVelocity != 0f) wallVelocity = wallVelocity + arm * (body.ScaleVelocity / scaleNow);
                if (surfaceLocal.X != 0f) wallVelocity = wallVelocity + Vec2.Rotate(surfaceLocal, cos, sin);
            }

            Vec2 t = n.Perp;
            bool rotates = (ball.Flags & BallFlags.NoRotation) == 0;
            // Velocity of the ball's surface at the contact point: v + ω × (−r·n) = v − ω·r·t.
            Vec2 vb = rotates ? ball.Velocity - t * (ball.AngularVelocity * r) : ball.Velocity;
            Vec2 vrel = vb - wallVelocity;
            float vn = Vec2.Dot(vrel, n);

            ISimContactHook hook = ContactHook;
            if (hook != null)
            {
                var info = new ContactPoint { Point = cp, Normal = n, ApproachSpeed = -vn, LocalAngle = localAngle, Feature = feature };
                if (!hook.PreSolveHit(this, bi, ci, in info)) return false;
                // A hook that kills the ball (deadly sector) ends its contacts at once: no response, no Hit event.
                if (!ball.Alive) return false;
            }

            if (vn < 0f)
            {
                float e = ball.Restitution > c.Material.Restitution ? ball.Restitution : c.Material.Restitution;
                if (-vn < RestingThreshold(ball.GravityScale)) e = 0f;
                float jn = -(1f + e) * vn * ball.Mass;
                float jt = 0f;
                float mu = MathF.Sqrt(ball.Friction * c.Material.Friction);
                if (mu > 0f)
                {
                    float vt = Vec2.Dot(vrel, t);
                    float kt = rotates ? ball.InvMass + r * r * ball.InvInertia : ball.InvMass;
                    jt = -vt / kt;
                    float maxJt = mu * jn;
                    if (jt > maxJt) jt = maxJt;
                    else if (jt < -maxJt) jt = -maxJt;
                }
                ball.Velocity = ball.Velocity + (n * jn + t * jt) * ball.InvMass;
                if (rotates) ball.AngularVelocity = ball.AngularVelocity - r * jt * ball.InvInertia;
                RecordContact(SimEventKind.Hit, bi, ci, c.Body, cp, n, jn, -vn, localAngle, feature);
            }
            _contactPos = pos;
            ball.Position = sweepT >= 0f && advance ? pos + ball.Velocity * ((1f - sweepT) * h) : pos;
            return true;
        }
    }
}
