using System;

namespace Ballverse.Core.Sim
{
    // Overlap pass: after the sweep, every wall primitive the ball still overlaps becomes a linear constraint on the ball centre
    // (N·p ≥ Target), all gathered at the same position, and the ball moves to the nearest point satisfying all of them at
    // once — an exact projection onto the intersection of half-planes, solved by enumerating the active sets (one or two
    // constraints in 2D).
    //
    // Why jointly: resolving walls one after another picks each push from the position the previous push left, and in a
    // corner under 90° (triangle, star tips) pushing out of one edge moves the centre towards the next edge — far enough to
    // carry it across that edge's centre line, which then pushes the ball out to its outer face. Here a face's push side is
    // fixed by the position at the start of the pass, which the sweep guarantees is on the legal side of every centre line
    // (the start-of-substep side breaks a tie when the centre lies on the line), and the projection lands in the corner pocket.
    //
    // Round features (end caps, ring gap caps, discs) are not linearised: their constraint is |p − C| ≥ rt itself, and the
    // candidates include the exact circle–line and circle–circle intersections, so a ball pressed between two caps lands where
    // it touches both instead of on the far apex of two nearly opposite tangent half-planes.
    //
    // Bridges: two primitives of the same body whose cores do not touch but whose inflated shapes overlap (two caps across an
    // opening narrower than the ball, a cap next to a face) close the gap between them; the segment joining their nearest core
    // points (the bridge) lies inside the forbidden region, so it is a solid centre line too. The sweep stops a ball centre on
    // its start side of every such bridge (PhysicsWorld.Bridges.cs) and no candidate here may jump across one.
    //
    // Guarantee: the pass never moves the centre across a solid centre line or bridge. If the projection is infeasible (the
    // ball does not fit) the nearest candidate that keeps every centre on its legal side is used; if the projection's jump
    // would cross the centre line of some other primitive, the ball stays where it was. Velocity responses then run for the
    // contacts still touching at the final position (ascending collider id, primitive order); round contacts of a joint
    // solution take their normal at the final position. A ball left approaching a wedge (touching contacts with opposing
    // normals: an opening slightly narrower than the ball, an acute corner) gets one simultaneous response (ResolveWedge), so
    // the velocity of a held ball follows the walls instead of accumulating what the sequential responses cannot remove.
    public sealed partial class PhysicsWorld
    {
        /// <summary>Overlapping primitives handled jointly per ball and substep; further ones are skipped for that substep.</summary>
        const int MaxOverlapContacts = 16;
        /// <summary>A face side is taken from the pass's start position only when the centre is farther than this from the line.</summary>
        const float SideEpsilon = 1e-6f;
        /// <summary>Constraint tolerance of the projection (world metres).</summary>
        const float FeasibleTolerance = 1e-5f;

        /// <summary>Bridges between the overlapping primitives of one ball and substep; further ones are ignored.</summary>
        const int MaxOverlapBridges = 16;
        /// <summary>Cores closer than this touch (shared polygon corners): no bridge between them.</summary>
        const float BridgeEpsilon = 1e-5f;

        const byte CoreNone = 0;
        const byte CorePoint = 1;
        const byte CoreSegment = 2;
        const byte CoreArc = 3;

        /// <summary>Exact shape of one overlap constraint and its core (world space, current pose), for round projections and bridges.</summary>
        struct OverlapShape
        {
            /// <summary>Constraint |p − Centre| ≥ Radius instead of the linear target.</summary>
            public bool Round;
            public Vec2 Centre;
            /// <summary>Inflated radius: ball radius + half thickness (or disc radius).</summary>
            public float Radius;
            /// <summary>Round: the centre must stay at least this far from <see cref="Centre"/> (disc colliders); −1 none.</summary>
            public float HardRadius;
            public byte Core;
            /// <summary>Point core / segment start / ring centre.</summary>
            public Vec2 CoreA;
            /// <summary>Segment end.</summary>
            public Vec2 CoreB;
            /// <summary>Ring centre-line radius.</summary>
            public float CoreR;
        }

        readonly ContactFrame[] _overlaps = new ContactFrame[MaxOverlapContacts];
        readonly OverlapShape[] _shapes = new OverlapShape[MaxOverlapContacts];
        int _overlapCount;
        readonly Vec2[] _bridgeA = new Vec2[MaxOverlapBridges];
        readonly Vec2[] _bridgeB = new Vec2[MaxOverlapBridges];
        int _bridgeCount;

        /// <summary>Gather–solve rounds per ball and substep (the later ones add primitives the push reached).</summary>
        const int MaxOverlapRounds = 3;

        void ResolveOverlaps(int bi, int n, float h)
        {
            ref BallState ball = ref _balls[bi];
            Vec2 pPre = ball.Position;
            _overlapCount = 0;
            if (!GatherAllOverlaps(bi, n, pPre, h)) return;
            int m = _overlapCount;
            if (m == 0) return;

            Vec2 p;
            for (int round = 0; ; round++)
            {
                p = SolveOverlapSet(bi, n, pPre, m);
                // A push out of one primitive can end inside a neighbour the start position did not touch — along a closed
                // opening, out of one cap into the cap at its other end. Where walls have free caps, gather again at the result
                // (primitives already listed are kept as they are) and solve the union from the start position again.
                if (round == MaxOverlapRounds - 1 || _capExtent < 0f || p == pPre) break;
                if (!GatherAllOverlaps(bi, n, p, h)) return;
                if (_overlapCount == m) break;
                m = _overlapCount;
            }

            float r = ball.Radius;
            Vec2 v0 = ball.Velocity;
            int touching = 0;
            for (int i = 0; i < m; i++)
            {
                ref ContactFrame cf = ref _overlaps[i];
                ref OverlapShape sh = ref _shapes[i];
                // Only contacts still touching at the final position respond (a push may clear some of them).
                if (m > 1 && sh.Round)
                {
                    float reach = sh.Radius + ContactSlop;
                    if ((p - sh.Centre).LengthSquared > reach * reach) continue;
                    RefreshRoundContact(ref cf, sh.Centre, p, r);
                }
                else if (Vec2.Dot(cf.N, p) > cf.Target + ContactSlop) continue;
                touching |= 1 << i;
                ApplyImpulse(bi, ref cf);
            }
            if ((touching & (touching - 1)) != 0) ResolveWedge(bi, touching, m, v0);
            ball.Position = p;
        }

        /// <summary>Velocity constraint tolerance of the wedge solve (m/s).</summary>
        const float WedgeVelocityTolerance = 1e-5f;
        /// <summary>Per-contact velocity targets of the wedge solve (N·v ≥ target).</summary>
        readonly float[] _wedgeTarget = new float[MaxOverlapContacts];

        /// <summary>
        /// Wedge: the sequential responses above are exact for one contact and for contacts whose normals do not oppose each other,
        /// but a ball pressed between two contacts with opposing normals (an opening slightly narrower than the ball: the caps of
        /// a closed opening, a ring gap; an acute corner) loses only a sliver of its velocity into the wedge per pass — nearly
        /// opposite normals hand the push back and forth. The position stays held by the projection while the velocity keeps
        /// what gravity or a pusher adds every substep (phantom velocity), so the ball sticks or shoots off when the push reverses.
        /// When, after the responses, the ball still approaches one of at least two touching contacts with opposing normals, it
        /// gets one simultaneous response of a point mass (one or two active contacts in 2D):
        /// <list type="number">
        /// <item>compression: the nearest velocity leaving no touching contact behind, N·v ≥ N·v_wall (projection onto the
        /// feasible set); if there is none (a pusher crushing the ball into the opening) the sequential result stays;</item>
        /// <item>restitution on the joint impulse (Poisson): v' = v_c + e·(v_c − v), e the largest restitution of the touching
        /// contacts (as in <see cref="ApplyImpulse"/>, from their approach before the responses; zero below the resting
        /// threshold, so a held ball only follows the walls); a v' still approaching a contact is projected again.</item>
        /// </list>
        /// Both steps are projections or a reflection of the removed part, so against static walls the speed never grows for
        /// e ≤ 1 (v_c ⊥ v − v_c): per-contact restitution targets on nearly opposite normals would intersect far out along the
        /// opening's axis (QA: a 9.5 m/s hit left at 12.1 m/s with e = 0.9). The velocity change is added to the contacts' Hit
        /// events (<see cref="RecordWedgeImpulse"/>).
        /// </summary>
        void ResolveWedge(int bi, int touching, int m, Vec2 v0)
        {
            ref BallState ball = ref _balls[bi];
            Vec2 v = ball.Velocity;
            bool opposing = false, approaching = false;
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                ref ContactFrame ci = ref _overlaps[i];
                if (Vec2.Dot(ci.N, v - ci.WallVelocity) < -WedgeVelocityTolerance) approaching = true;
                for (int j = i + 1; j < m; j++)
                {
                    if ((touching & (1 << j)) != 0 && Vec2.Dot(ci.N, _overlaps[j].N) < 0f) opposing = true;
                }
            }
            if (!opposing || !approaching) return;

            float e = 0f;
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                ref ContactFrame cf = ref _overlaps[i];
                float wn = Vec2.Dot(cf.N, cf.WallVelocity);
                _wedgeTarget[i] = wn;
                float approach = wn - Vec2.Dot(cf.N, v0);
                if (approach > 0f)
                {
                    float ei = RestitutionOf(ref ball, cf.Collider, approach);
                    if (ei > e) e = ei;
                }
            }
            if (!NearestLeavingVelocity(v, touching, m, out Vec2 solved)) return;
            if (e > 0f)
            {
                Vec2 bounced = solved + (solved - v) * e;
                if (LeavesAll(bounced, touching, m)) solved = bounced;
                else if (NearestLeavingVelocity(bounced, touching, m, out Vec2 projected)) solved = projected;
            }
            ball.Velocity = solved;
            RecordWedgeImpulse(bi, touching, m, v, solved - v);
        }

        /// <summary>True if <paramref name="cand"/> meets every touching contact's target (within the tolerance).</summary>
        bool LeavesAll(Vec2 cand, int touching, int m)
        {
            for (int k = 0; k < m; k++)
            {
                if ((touching & (1 << k)) == 0) continue;
                if (Vec2.Dot(_overlaps[k].N, cand) < _wedgeTarget[k] - WedgeVelocityTolerance) return false;
            }
            return true;
        }

        /// <summary>
        /// Adds the wedge response's momentum change m·|Δv| to the Hit events of the touching contacts (the sound / haptics
        /// intensity), split in proportion to each contact's share N·Δv &gt; 0; the speed is the contact's approach before the
        /// wedge response. <see cref="RecordContact"/> merges into the contact's event of this tick.
        /// </summary>
        void RecordWedgeImpulse(int bi, int touching, int m, Vec2 v, Vec2 dv)
        {
            float sum = 0f;
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                float s = Vec2.Dot(_overlaps[i].N, dv);
                if (s > 0f) sum += s;
            }
            if (sum <= 0f) return;
            ref BallState ball = ref _balls[bi];
            float total = ball.Mass * dv.Length;
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                ref ContactFrame cf = ref _overlaps[i];
                float s = Vec2.Dot(cf.N, dv);
                if (s <= 0f) continue;
                float approach = Vec2.Dot(cf.N, cf.WallVelocity - v);
                RecordContact(SimEventKind.Hit, bi, cf.Collider, cf.Body, cf.Cp, cf.N, total * (s / sum), approach > 0f ? approach : 0f, cf.LocalAngle, cf.Feature);
            }
        }

        /// <summary>
        /// Nearest velocity to <paramref name="v"/> with N·v ≥ target for every touching contact: the single projections and the
        /// pairwise intersections of the constraint lines (fixed order, first minimum wins). False if none is feasible.
        /// </summary>
        bool NearestLeavingVelocity(Vec2 v, int touching, int m, out Vec2 best)
        {
            best = v;
            float bestD = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                ref ContactFrame ci = ref _overlaps[i];
                float violation = _wedgeTarget[i] - Vec2.Dot(ci.N, v);
                if (violation > 0f) ConsiderVelocity(v + ci.N * violation, v, touching, m, ref best, ref bestD);
            }
            for (int i = 0; i < m; i++)
            {
                if ((touching & (1 << i)) == 0) continue;
                ref ContactFrame ci = ref _overlaps[i];
                for (int j = i + 1; j < m; j++)
                {
                    if ((touching & (1 << j)) == 0) continue;
                    ref ContactFrame cj = ref _overlaps[j];
                    float det = ci.N.X * cj.N.Y - ci.N.Y * cj.N.X;
                    if (det > -1e-6f && det < 1e-6f) continue;
                    float ti = _wedgeTarget[i], tj = _wedgeTarget[j];
                    var cand = new Vec2((ti * cj.N.Y - ci.N.Y * tj) / det, (ci.N.X * tj - ti * cj.N.X) / det);
                    ConsiderVelocity(cand, v, touching, m, ref best, ref bestD);
                }
            }
            return bestD < float.MaxValue;
        }

        void ConsiderVelocity(Vec2 cand, Vec2 v, int touching, int m, ref Vec2 best, ref float bestD)
        {
            float d = (cand - v).LengthSquared;
            if (d >= bestD || !LeavesAll(cand, touching, m)) return;
            best = cand;
            bestD = d;
        }

        /// <summary>Adds the overlaps of every candidate at <paramref name="pWorld"/> not listed yet. False if the ball died.</summary>
        bool GatherAllOverlaps(int bi, int n, Vec2 pWorld, float h)
        {
            ref BallState ball = ref _balls[bi];
            for (int k = 0; k < n; k++)
            {
                int c = _wallCandidates[k];
                if (_wallRejected[c] == _wallStamp) continue;
                int before = _overlapCount;
                if (!GatherOverlaps(bi, c, pWorld, h))
                {
                    if (!ball.Alive) return false;
                    // The hook let the ball pass through this collider for the rest of the substep: drop its new contacts.
                    _wallRejected[c] = _wallStamp;
                    _overlapCount = before;
                }
            }
            return true;
        }

        /// <summary>Joint projection of the start position out of the first <paramref name="m"/> overlap constraints.</summary>
        Vec2 SolveOverlapSet(int bi, int n, Vec2 pPre, int m)
        {
            // One push is at most one ball radius plus half a wall thickness along the normal of the only primitive within that
            // distance (radial for a round one), so it cannot cross another centre line; joint solutions are checked.
            if (m == 1) return Project(pPre, 0);
            BuildBridges(m);
            Vec2 p = SolveOverlaps(pPre, m);
            return JumpKeepsSides(bi, n, pPre, p) ? p : pPre;
        }

        /// <summary>Gathers the overlapping primitives of one collider at <paramref name="pWorld"/>. False if the hook rejected one or killed the ball.</summary>
        bool GatherOverlaps(int bi, int ci, Vec2 pWorld, float h)
        {
            ref ColliderData c = ref _colliders[ci];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            ref BallState ball = ref _balls[bi];
            float s = body.Scale;
            Vec2 q = ToBodySpace(c.Body, pWorld);
            // Start-of-substep position: tells which side a ball exactly on a centre line came from.
            Vec2 qStart = ToBodySpaceAt(c.Body, _ballPrev[bi], 0f);
            float r = ball.Radius;
            float rt = r + c.HalfThickness;
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                    return GatherCapsule(bi, ci, q, qStart, c.A * s, c.B * s, rt, 0, h);
                case ColliderKind.Polyline:
                {
                    int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                    for (int k = 0; k < edges; k++)
                    {
                        Vec2 a = _vertices[c.VertexStart + k] * s;
                        Vec2 b = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                        if (!GatherCapsule(bi, ci, q, qStart, a, b, rt, k, h)) return false;
                    }
                    return true;
                }
                case ColliderKind.Ring:
                    return GatherRing(bi, ci, ref c, q, qStart, s, rt, h);
                case ColliderKind.Circle:
                {
                    Vec2 o = c.A * s;
                    float rc = r + c.Radius * s;
                    Vec2 v = q - o;
                    float d2 = v.LengthSquared;
                    if (d2 >= rc * rc) return true;
                    float d = MathF.Sqrt(d2);
                    Vec2 nl = d > NormalEpsilon ? v / d : FallbackNormal(qStart - o);
                    Vec2 ow = FromBodySpace(c.Body, o);
                    // The centre must not enter the disc itself.
                    var shape = new OverlapShape { Round = true, Centre = ow, Radius = rc, HardRadius = c.Radius * s, Core = CorePoint, CoreA = ow };
                    return AddOverlap(bi, ci, o + nl * rc, nl, 0, r, h, in shape);
                }
                case ColliderKind.SpinPlatform:
                    return GatherPlatform(bi, ci, q, qStart, h);
            }
            return true;
        }

        bool GatherCapsule(int bi, int ci, Vec2 q, Vec2 qStart, Vec2 a, Vec2 b, float rt, int feature, float h)
        {
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            float u = 0f;
            if (l2 > 1e-14f) u = Vec2.Dot(q - a, d) / l2;
            float uc = u < 0f ? 0f : (u > 1f ? 1f : u);
            Vec2 cp = a + d * uc;
            Vec2 v = q - cp;
            float dist2 = v.LengthSquared;
            if (dist2 >= rt * rt) return true;
            Vec2 n;
            bool face = l2 > 1e-14f && u > 0f && u < 1f;
            if (l2 > 1e-14f && (face || dist2 <= NormalEpsilon * NormalEpsilon))
            {
                // Face: the side the centre is on now (legal after the sweep); exactly on the line — the side it came from.
                Vec2 ln = d.Perp / MathF.Sqrt(l2);
                float side = Vec2.Dot(q - a, ln);
                if (side > -SideEpsilon && side < SideEpsilon) side = Vec2.Dot(qStart - a, ln);
                n = side >= 0f ? ln : -ln;
            }
            else
            {
                float dist = MathF.Sqrt(dist2);
                n = dist > NormalEpsilon ? v / dist : FallbackNormal(qStart - a);
            }
            // Faces keep the centre on their side of the centre line; a round end cap is a point.
            int body = _colliders[ci].Body;
            var shape = new OverlapShape { Radius = rt, HardRadius = -1f };
            if (face)
            {
                shape.Core = CoreSegment;
                shape.CoreA = FromBodySpace(body, a);
                shape.CoreB = FromBodySpace(body, b);
            }
            else
            {
                shape.Round = true;
                shape.Core = CorePoint;
                shape.Centre = shape.CoreA = FromBodySpace(body, cp);
            }
            return AddOverlap(bi, ci, cp + n * rt, n, feature, face ? rt : -1f, h, in shape);
        }

        bool GatherRing(int bi, int ci, ref ColliderData c, Vec2 q, Vec2 qStart, float s, float rt, float h)
        {
            Vec2 o = c.A * s;
            float radius = c.Radius * s;
            Vec2 v = q - o;
            float d = v.Length;
            float gap = d - radius;
            if (gap >= rt || gap <= -rt) return true;
            float theta = SimMath.Atan2(v.Y, v.X);
            int arc = SolidArcAt(ref c, theta);
            if (arc != NoArc)
            {
                if (d <= NormalEpsilon) return true;
                float side;
                if (gap > SideEpsilon) side = 1f;
                else if (gap < -SideEpsilon) side = -1f;
                else side = (qStart - o).Length >= radius ? 1f : -1f;
                Vec2 dir = v / d;
                var shape = new OverlapShape { Radius = rt, HardRadius = -1f, Core = CoreArc, CoreA = FromBodySpace(c.Body, o), CoreR = radius };
                return AddOverlap(bi, ci, o + dir * (radius + side * rt), dir * side, arc, rt, h, in shape);
            }
            // Inside a gap: only the round end caps of the neighbouring solid arcs can touch.
            for (int k = 0; k < c.ArcCount; k++)
            {
                int ai = c.ArcStart + k;
                if (!GatherCap(bi, ci, q, qStart, o + _arcStartDir[ai] * radius, rt, k, h)) return false;
                if (!GatherCap(bi, ci, q, qStart, o + _arcEndDir[ai] * radius, rt, k, h)) return false;
            }
            return true;
        }

        bool GatherCap(int bi, int ci, Vec2 q, Vec2 qStart, Vec2 centre, float rt, int feature, float h)
        {
            Vec2 v = q - centre;
            float d2 = v.LengthSquared;
            if (d2 >= rt * rt) return true;
            float d = MathF.Sqrt(d2);
            Vec2 n = d > NormalEpsilon ? v / d : FallbackNormal(qStart - centre);
            Vec2 cw = FromBodySpace(_colliders[ci].Body, centre);
            var shape = new OverlapShape { Round = true, Centre = cw, Radius = rt, HardRadius = -1f, Core = CorePoint, CoreA = cw };
            return AddOverlap(bi, ci, centre + n * rt, n, feature, -1f, h, in shape);
        }

        /// <summary>
        /// Prepares one overlap contact (hook included) and stores its constraint. <paramref name="hardDepth"/> ≥ 0: the centre
        /// must also stay that far behind the target plane's offset (the centre line); negative: no centre-line constraint.
        /// </summary>
        bool AddOverlap(int bi, int ci, Vec2 qPos, Vec2 nLocal, int feature, float hardDepth, float h, in OverlapShape shape)
        {
            // Gathering again after a push: a primitive already listed keeps its first constraint (and its hook call).
            for (int k = 0; k < _overlapCount; k++)
            {
                ref ContactFrame o = ref _overlaps[k];
                ref OverlapShape os = ref _shapes[k];
                if (o.Collider == ci && o.Feature == feature && os.Core == shape.Core && os.CoreA == shape.CoreA && os.CoreB == shape.CoreB) return true;
            }
            if (!PrepareContact(bi, ci, qPos, nLocal, feature, -1f, h, out ContactFrame cf, out _)) return false;
            cf.Hard = hardDepth >= 0f ? cf.Target - hardDepth : float.NegativeInfinity;
            if (_overlapCount < MaxOverlapContacts)
            {
                _shapes[_overlapCount] = shape;
                _overlaps[_overlapCount++] = cf;
            }
            return true;
        }

        /// <summary>World point of q space of <paramref name="body"/> at its current pose (inverse of <see cref="ToBodySpace"/>).</summary>
        Vec2 FromBodySpace(int body, Vec2 q)
        {
            ref BodyFrame f = ref _frames[body];
            return _bodies[body].Position + Vec2.Rotate(q, f.Cos, f.Sin);
        }

        /// <summary>Normal, contact point, wall velocity and ring angle of a round contact re-evaluated at the final centre <paramref name="p"/>.</summary>
        void RefreshRoundContact(ref ContactFrame cf, Vec2 centre, Vec2 p, float r)
        {
            Vec2 d = p - centre;
            float l = d.Length;
            if (l <= NormalEpsilon) return;
            Vec2 n = d / l;
            ref ColliderData c = ref _colliders[cf.Collider];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            ref BodyFrame f = ref _frames[c.Body];
            cf.N = n;
            cf.Cp = p - n * r;
            cf.Target = Vec2.Dot(n, p);
            cf.WallVelocity = WallVelocityAt(ref body, body.Position, body.Scale, cf.Cp, Vec2.Zero, f.Cos, f.Sin);
            if (c.Kind == ColliderKind.Ring)
            {
                Vec2 rel = ToBodySpace(c.Body, cf.Cp) - c.A * body.Scale;
                cf.LocalAngle = SimMath.Wrap2Pi(SimMath.Atan2(rel.Y, rel.X));
            }
        }

        /// <summary>
        /// Collects the bridges between pairs of overlapping primitives of the same body: a point core (cap, disc) and another
        /// point core, a face's centre line or a solid ring arc, with non-touching cores and overlapping inflated shapes.
        /// </summary>
        void BuildBridges(int m)
        {
            _bridgeCount = 0;
            for (int i = 0; i < m; i++)
            {
                for (int j = i + 1; j < m; j++)
                {
                    if (_overlaps[i].Body != _overlaps[j].Body) continue;
                    ref OverlapShape si = ref _shapes[i];
                    ref OverlapShape sj = ref _shapes[j];
                    if (si.Core == CorePoint) TryOverlapBridge(ref si, ref sj, _overlaps[j].Collider);
                    else if (sj.Core == CorePoint) TryOverlapBridge(ref sj, ref si, _overlaps[i].Collider);
                }
            }
        }

        void TryOverlapBridge(ref OverlapShape point, ref OverlapShape other, int otherCollider)
        {
            if (_bridgeCount >= MaxOverlapBridges) return;
            Vec2 p = point.CoreA;
            float reach = point.Radius + other.Radius;
            Vec2 x;
            switch (other.Core)
            {
                case CorePoint:
                    x = other.CoreA;
                    break;
                case CoreSegment:
                    if (!SegmentBridgePoint(p, other.CoreA, other.CoreB, out x)) return;
                    break;
                case CoreArc:
                {
                    ref ColliderData c = ref _colliders[otherCollider];
                    Vec2 o = c.A * _bodies[c.Body].Scale;
                    if (!ArcBridgePoint(ref c, ToBodySpace(c.Body, p), o, other.CoreR, reach, out Vec2 xq)) return;
                    x = FromBodySpace(c.Body, xq);
                    break;
                }
                default:
                    return;
            }
            float d2 = (x - p).LengthSquared;
            if (d2 <= BridgeEpsilon * BridgeEpsilon || d2 >= reach * reach) return;
            _bridgeA[_bridgeCount] = p;
            _bridgeB[_bridgeCount] = x;
            _bridgeCount++;
        }

        /// <summary>Nearest point of segment a–b to <paramref name="p"/> when it lies strictly inside the segment (otherwise an end cap is nearer).</summary>
        static bool SegmentBridgePoint(Vec2 p, Vec2 a, Vec2 b, out Vec2 x)
        {
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            x = a;
            if (l2 <= 1e-14f) return false;
            float u = Vec2.Dot(p - a, d) / l2;
            if (u <= 0f || u >= 1f) return false;
            x = a + d * u;
            return true;
        }

        /// <summary>
        /// Nearest point of a ring's centre line (q space, centre <paramref name="o"/>) to <paramref name="q"/>, if it is closer than
        /// <paramref name="reach"/> and lies on a solid arc.
        /// </summary>
        bool ArcBridgePoint(ref ColliderData c, Vec2 q, Vec2 o, float radius, float reach, out Vec2 x)
        {
            Vec2 v = q - o;
            float l = v.Length;
            x = o;
            float gap = l - radius;
            if (l <= NormalEpsilon || gap >= reach || gap <= -reach) return false;
            Vec2 dir = v / l;
            if (SolidArcAt(ref c, SimMath.Atan2(dir.Y, dir.X)) == NoArc) return false;
            x = o + dir * radius;
            return true;
        }

        /// <summary>The start position projected onto constraint <paramref name="i"/> linearised at it (unchanged if already satisfied).</summary>
        Vec2 Project(Vec2 p, int i)
        {
            ref ContactFrame cf = ref _overlaps[i];
            float violation = cf.Target - Vec2.Dot(cf.N, p);
            return violation > 0f ? p + cf.N * violation : p;
        }

        /// <summary>Nearest point to <paramref name="p"/> on constraint <paramref name="i"/>'s own boundary side: radial for a round one.</summary>
        Vec2 ProjectExact(Vec2 p, int i)
        {
            ref OverlapShape sh = ref _shapes[i];
            if (!sh.Round) return Project(p, i);
            Vec2 d = p - sh.Centre;
            float l2 = d.LengthSquared;
            if (l2 >= sh.Radius * sh.Radius) return p;
            float l = MathF.Sqrt(l2);
            Vec2 dir = l > NormalEpsilon ? d / l : _overlaps[i].N;
            return sh.Centre + dir * sh.Radius;
        }

        /// <summary>
        /// Nearest point to <paramref name="p0"/> satisfying every constraint (half-planes and circle exteriors) without jumping
        /// across a bridge: the minimum lies on at most two active constraint boundaries, so the single projections (radial for
        /// round ones) and the pairwise line–line, line–circle and circle–circle intersections are enumerated (fixed order,
        /// first minimum wins). If none is feasible, the nearest candidate satisfying the centre-line constraints; failing that,
        /// <paramref name="p0"/>.
        /// </summary>
        Vec2 SolveOverlaps(Vec2 p0, int m)
        {
            Vec2 best = p0, bestHard = p0;
            float bestD = float.MaxValue, bestHardD = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                Vec2 cand = ProjectExact(p0, i);
                Consider(cand, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
            }
            for (int i = 0; i < m; i++)
            {
                ref ContactFrame ci = ref _overlaps[i];
                bool ri = _shapes[i].Round;
                for (int j = i + 1; j < m; j++)
                {
                    ref ContactFrame cj = ref _overlaps[j];
                    bool rj = _shapes[j].Round;
                    if (ri || rj)
                    {
                        Vec2 c0, c1;
                        bool two;
                        if (ri && rj) two = CircleCircle(_shapes[i].Centre, _shapes[i].Radius, _shapes[j].Centre, _shapes[j].Radius, out c0, out c1);
                        else if (ri) two = LineCircle(cj.N, cj.Target, _shapes[i].Centre, _shapes[i].Radius, out c0, out c1);
                        else two = LineCircle(ci.N, ci.Target, _shapes[j].Centre, _shapes[j].Radius, out c0, out c1);
                        if (!two) continue;
                        Consider(c0, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
                        Consider(c1, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
                        continue;
                    }
                    float det = ci.N.X * cj.N.Y - ci.N.Y * cj.N.X;
                    if (det > -1e-6f && det < 1e-6f) continue;
                    var cand = new Vec2((ci.Target * cj.N.Y - ci.N.Y * cj.Target) / det, (ci.N.X * cj.Target - ci.Target * cj.N.X) / det);
                    Consider(cand, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
                }
            }
            if (bestD < float.MaxValue) return best;
            return bestHardD < float.MaxValue ? bestHard : p0;
        }

        /// <summary>Intersections of two circles (false if they do not cross).</summary>
        static bool CircleCircle(Vec2 a, float ra, Vec2 b, float rb, out Vec2 x0, out Vec2 x1)
        {
            x0 = x1 = a;
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            if (l2 <= 1e-14f) return false;
            float l = MathF.Sqrt(l2);
            if (l >= ra + rb || l <= MathF.Abs(ra - rb)) return false;
            float along = (ra * ra - rb * rb + l2) / (2f * l);
            float h2 = ra * ra - along * along;
            if (h2 < 0f) h2 = 0f;
            Vec2 u = d / l;
            Vec2 mid = a + u * along;
            Vec2 off = u.Perp * MathF.Sqrt(h2);
            x0 = mid + off;
            x1 = mid - off;
            return true;
        }

        /// <summary>Intersections of the line N·p = target with a circle (false if they do not meet).</summary>
        static bool LineCircle(Vec2 n, float target, Vec2 c, float radius, out Vec2 x0, out Vec2 x1)
        {
            float s = target - Vec2.Dot(n, c);
            float h2 = radius * radius - s * s;
            x0 = x1 = c;
            if (h2 < 0f) return false;
            Vec2 foot = c + n * s;
            Vec2 off = n.Perp * MathF.Sqrt(h2);
            x0 = foot + off;
            x1 = foot - off;
            return true;
        }

        void Consider(Vec2 cand, Vec2 p0, int m, ref Vec2 best, ref float bestD, ref Vec2 bestHard, ref float bestHardD)
        {
            float d = (cand - p0).LengthSquared;
            if (d >= bestD) return;
            for (int k = 0; k < _bridgeCount; k++)
            {
                if (CrossesSegment(p0, cand, _bridgeA[k], _bridgeB[k])) return;
            }
            bool feasible = true, hardOk = true;
            for (int k = 0; k < m; k++)
            {
                ref OverlapShape sh = ref _shapes[k];
                if (sh.Round)
                {
                    float l2 = (cand - sh.Centre).LengthSquared;
                    float lim = sh.Radius - FeasibleTolerance;
                    if (l2 < lim * lim) feasible = false;
                    if (sh.HardRadius >= 0f && l2 < sh.HardRadius * sh.HardRadius) hardOk = false;
                    continue;
                }
                ref ContactFrame cf = ref _overlaps[k];
                float dot = Vec2.Dot(cf.N, cand);
                if (dot < cf.Target - FeasibleTolerance) feasible = false;
                if (dot < cf.Hard) hardOk = false;
            }
            if (feasible)
            {
                best = cand;
                bestD = d;
            }
            else if (hardOk && d < bestHardD)
            {
                bestHard = cand;
                bestHardD = d;
            }
        }

        /// <summary>
        /// True if moving the ball centre from <paramref name="from"/> to <paramref name="to"/> (current poses) crosses no solid
        /// centre line of the candidate colliders: segment / polyline edges, ring arcs, disc interiors and spin platforms the
        /// ball is not in transit through.
        /// </summary>
        bool JumpKeepsSides(int bi, int n, Vec2 from, Vec2 to)
        {
            for (int k = 0; k < n; k++)
            {
                int ci = _wallCandidates[k];
                if (_wallRejected[ci] == _wallStamp) continue;
                ref ColliderData c = ref _colliders[ci];
                float s = _bodies[c.Body].Scale;
                Vec2 qa = ToBodySpace(c.Body, from);
                Vec2 qb = ToBodySpace(c.Body, to);
                switch (c.Kind)
                {
                    case ColliderKind.Segment:
                        if (CrossesSegment(qa, qb, c.A * s, c.B * s)) return false;
                        break;
                    case ColliderKind.Polyline:
                    {
                        int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                        for (int e = 0; e < edges; e++)
                        {
                            Vec2 a = _vertices[c.VertexStart + e] * s;
                            Vec2 b = _vertices[c.VertexStart + (e + 1 == c.VertexCount ? 0 : e + 1)] * s;
                            if (CrossesSegment(qa, qb, a, b)) return false;
                        }
                        break;
                    }
                    case ColliderKind.Ring:
                    {
                        Vec2 o = c.A * s;
                        float radius = c.Radius * s;
                        Vec2 va = qa - o, vb = qb - o;
                        float da = va.Length - radius, db = vb.Length - radius;
                        if ((da > 0f && db < 0f) || (da < 0f && db > 0f))
                        {
                            Vec2 motion = vb - va;
                            float t = CircleRoot(va, motion, motion.LengthSquared, radius, da > 0f);
                            if (t >= 0f && t <= 1f)
                            {
                                Vec2 x = va + motion * t;
                                if (SolidArcAt(ref c, SimMath.Atan2(x.Y, x.X)) != NoArc) return false;
                            }
                        }
                        break;
                    }
                    case ColliderKind.Circle:
                    {
                        Vec2 o = c.A * s;
                        float rc = c.Radius * s;
                        if ((qb - o).LengthSquared < rc * rc && (qa - o).LengthSquared >= rc * rc) return false;
                        break;
                    }
                    case ColliderKind.SpinPlatform:
                        if (FindTransit(bi, ci) < 0 && CrossesSegment(qa, qb, new Vec2((c.A.X - c.Radius) * s, c.A.Y * s), new Vec2((c.A.X + c.Radius) * s, c.A.Y * s))) return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>The move <paramref name="qa"/>→<paramref name="qb"/> goes from one side of segment a–b to the other through the segment.</summary>
        static bool CrossesSegment(Vec2 qa, Vec2 qb, Vec2 a, Vec2 b)
        {
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            if (l2 <= 1e-14f) return false;
            Vec2 ln = d.Perp;
            float sa = Vec2.Dot(qa - a, ln);
            float sb = Vec2.Dot(qb - a, ln);
            if (!((sa > 0f && sb < 0f) || (sa < 0f && sb > 0f))) return false;
            float t = sa / (sa - sb);
            Vec2 x = qa + (qb - qa) * t;
            float u = Vec2.Dot(x - a, d) / l2;
            return u >= 0f && u <= 1f;
        }
    }
}
