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
    // Guarantee: the pass never moves the centre across a solid centre line. If the projection is infeasible (the ball does not
    // fit) the nearest candidate that keeps every centre on its legal side is used; if the projection's jump would cross the
    // centre line of some other primitive, the ball stays where it was. Velocity responses then run for the contacts still
    // touching at the final position (ascending collider id, primitive order).
    public sealed partial class PhysicsWorld
    {
        /// <summary>Overlapping primitives handled jointly per ball and substep; further ones are skipped for that substep.</summary>
        const int MaxOverlapContacts = 16;
        /// <summary>A face side is taken from the pass's start position only when the centre is farther than this from the line.</summary>
        const float SideEpsilon = 1e-6f;
        /// <summary>Constraint tolerance of the projection (world metres).</summary>
        const float FeasibleTolerance = 1e-5f;

        readonly ContactFrame[] _overlaps = new ContactFrame[MaxOverlapContacts];
        int _overlapCount;

        void ResolveOverlaps(int bi, int n, float h)
        {
            ref BallState ball = ref _balls[bi];
            Vec2 pPre = ball.Position;
            _overlapCount = 0;
            for (int k = 0; k < n; k++)
            {
                int c = _wallCandidates[k];
                if (_wallRejected[c] == _wallStamp) continue;
                int before = _overlapCount;
                if (!GatherOverlaps(bi, c, pPre, h))
                {
                    if (!ball.Alive) return;
                    // The hook let the ball pass through this collider for the rest of the substep: drop all its contacts.
                    _wallRejected[c] = _wallStamp;
                    _overlapCount = before;
                }
            }
            int m = _overlapCount;
            if (m == 0) return;

            Vec2 p = m == 1 ? Project(pPre, 0) : SolveOverlaps(pPre, m);
            // One push is at most one ball radius plus half a wall thickness along the normal of the only primitive within that
            // distance, so it cannot cross another centre line; joint solutions are checked.
            if (m > 1 && !JumpKeepsSides(bi, n, pPre, p)) p = pPre;

            bool moved = p.X != pPre.X || p.Y != pPre.Y;
            for (int i = 0; i < m; i++)
            {
                ref ContactFrame cf = ref _overlaps[i];
                // Constraints another push already cleared are no longer in contact.
                if (moved && Vec2.Dot(cf.N, p) > cf.Target + ContactSlop) continue;
                ApplyImpulse(bi, ref cf);
            }
            ball.Position = p;
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
                    // The centre must not enter the disc itself.
                    return AddOverlap(bi, ci, o + nl * rc, nl, 0, r, h);
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
            return AddOverlap(bi, ci, cp + n * rt, n, feature, face ? rt : -1f, h);
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
                return AddOverlap(bi, ci, o + dir * (radius + side * rt), dir * side, arc, rt, h);
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
            return AddOverlap(bi, ci, centre + n * rt, n, feature, -1f, h);
        }

        /// <summary>
        /// Prepares one overlap contact (hook included) and stores its constraint. <paramref name="hardDepth"/> ≥ 0: the centre
        /// must also stay that far behind the target plane's offset (the centre line); negative: no centre-line constraint.
        /// </summary>
        bool AddOverlap(int bi, int ci, Vec2 qPos, Vec2 nLocal, int feature, float hardDepth, float h)
        {
            if (!PrepareContact(bi, ci, qPos, nLocal, feature, -1f, h, out ContactFrame cf, out _)) return false;
            cf.Hard = hardDepth >= 0f ? cf.Target - hardDepth : float.NegativeInfinity;
            if (_overlapCount < MaxOverlapContacts) _overlaps[_overlapCount++] = cf;
            return true;
        }

        /// <summary>The start position projected onto constraint <paramref name="i"/> (unchanged if already satisfied).</summary>
        Vec2 Project(Vec2 p, int i)
        {
            ref ContactFrame cf = ref _overlaps[i];
            float violation = cf.Target - Vec2.Dot(cf.N, p);
            return violation > 0f ? p + cf.N * violation : p;
        }

        /// <summary>
        /// Nearest point to <paramref name="p0"/> satisfying every target constraint: the minimum lies on at most two active
        /// constraints, so the single projections and pairwise intersections are enumerated (fixed order, first minimum wins).
        /// If none is feasible, the nearest candidate satisfying the centre-line constraints; failing that, <paramref name="p0"/>.
        /// </summary>
        Vec2 SolveOverlaps(Vec2 p0, int m)
        {
            Vec2 best = p0, bestHard = p0;
            float bestD = float.MaxValue, bestHardD = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                Vec2 cand = Project(p0, i);
                Consider(cand, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
            }
            for (int i = 0; i < m; i++)
            {
                ref ContactFrame ci = ref _overlaps[i];
                for (int j = i + 1; j < m; j++)
                {
                    ref ContactFrame cj = ref _overlaps[j];
                    float det = ci.N.X * cj.N.Y - ci.N.Y * cj.N.X;
                    if (det > -1e-6f && det < 1e-6f) continue;
                    var cand = new Vec2((ci.Target * cj.N.Y - ci.N.Y * cj.Target) / det, (ci.N.X * cj.Target - ci.Target * cj.N.X) / det);
                    Consider(cand, p0, m, ref best, ref bestD, ref bestHard, ref bestHardD);
                }
            }
            if (bestD < float.MaxValue) return best;
            return bestHardD < float.MaxValue ? bestHard : p0;
        }

        void Consider(Vec2 cand, Vec2 p0, int m, ref Vec2 best, ref float bestD, ref Vec2 bestHard, ref float bestHardD)
        {
            float d = (cand - p0).LengthSquared;
            if (d >= bestD) return;
            bool feasible = true, hardOk = true;
            for (int k = 0; k < m; k++)
            {
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
