using System;

namespace Ballverse.Core.Sim
{
    // Bridges: openings narrower than the ball.
    //
    // A wall's free end (segment end, open polyline end, ring gap edge) and a disc collider are round "caps" whose core is a
    // single point, so unlike a face they have no centre line the sweep could guard. Two caps of the same body whose inflated
    // discs overlap (an opening narrower than the ball: M02 «Открытая грань», a ring gap narrower than the ball), or a cap
    // whose inflated disc overlaps another primitive's band (a wall end next to another wall), close the space between them:
    // the segment joining their nearest core points (the bridge) lies entirely inside the forbidden region, so a ball centre
    // that crosses it has passed through a wall. The sweep misses such a crossing when the ball starts the substep already in
    // contact with the caps (in-contact primitives are resolved discretely), and the overlap projection alone cannot tell the
    // legal side of a point. So every leg of the sweep also tests the bridges near its chord, in the body's q space like any
    // other wall: the earliest crossing stops the ball just on its start side, and the joint overlap projection (which itself
    // never jumps across a bridge) pushes it back out; a ball no position fits (crushed into the opening) stays on the bridge
    // and loses its velocity through it (HoldOnBridge).
    //
    // Bridges are formed only between primitives of the same body (they move rigidly together); openings between colliders of
    // different moving bodies are not closed by this rule.
    public sealed partial class PhysicsWorld
    {
        struct NearCap
        {
            public int Collider;
            public int Feature;
            public int Body;
            /// <summary>Cap centre (q space of <see cref="Body"/>, current pose).</summary>
            public Vec2 C;
            /// <summary>Inflated radius (ball radius + half thickness / disc radius).</summary>
            public float Rt;
            /// <summary>The leg's chord in the same q space.</summary>
            public Vec2 Q0, Q1;
        }

        NearCap[] _nearCaps = new NearCap[16];
        int _nearCapCount;
        /// <summary>Largest cap half-size (half thickness / disc radius) among the active colliders this substep; −1 when no collider has a free cap.</summary>
        float _capExtent = -1f;

        /// <summary>
        /// Distance from the ball's path within which a collider's caps may take part in a bridge the path crosses: the crossing
        /// point lies on the bridge, at most rt + (r + largest cap half-size) from either end. Just <paramref name="r"/> when no
        /// collider has free caps.
        /// </summary>
        float BridgeReach(float r)
        {
            return _capExtent >= 0f ? r + r + _capExtent + ContactSlop : r;
        }

        static bool HasFreeCaps(ref ColliderData c)
        {
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                case ColliderKind.Circle:
                    return true;
                case ColliderKind.Polyline:
                    return !c.Closed;
                case ColliderKind.Ring:
                    return !c.Full;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Tests the remaining path (from <paramref name="wStart"/> at <paramref name="tStart"/> to the current position) against
        /// the bridges of the candidate colliders; an earlier crossing than <paramref name="best"/> replaces it with a bridge hit.
        /// </summary>
        bool SweepBridges(int bi, int n, float tStart, Vec2 wStart, int impacts, ref SweepHit best, out int collider)
        {
            collider = -1;
            ref BallState ball = ref _balls[bi];
            float r = ball.Radius;
            // A cap can bridge to a partner at most rt + (r + largest cap half-size) away; the crossing point lies on the bridge.
            float partner = r + _capExtent + ContactSlop;
            _nearCapCount = 0;
            for (int k = 0; k < n; k++)
            {
                int ci = _wallCandidates[k];
                if (_wallRejected[ci] == _wallStamp) continue;
                ref ColliderData c = ref _colliders[ci];
                if (!HasFreeCaps(ref c)) continue;
                if (impacts > 0 && !MayTouch(ref _colliderBounds[ci], wStart, ball.Position, BridgeReach(r))) continue;
                float s = _bodies[c.Body].Scale;
                Vec2 q1 = ToBodySpace(c.Body, ball.Position);
                Vec2 q0 = ToBodySpaceAt(c.Body, wStart, tStart);
                float rt = r + c.HalfThickness;
                switch (c.Kind)
                {
                    case ColliderKind.Segment:
                        AddNearCap(ci, 0, c.Body, c.A * s, rt, q0, q1, partner);
                        if (c.B != c.A) AddNearCap(ci, 0, c.Body, c.B * s, rt, q0, q1, partner);
                        break;
                    case ColliderKind.Polyline:
                        AddNearCap(ci, 0, c.Body, _vertices[c.VertexStart] * s, rt, q0, q1, partner);
                        AddNearCap(ci, c.VertexCount - 2, c.Body, _vertices[c.VertexStart + c.VertexCount - 1] * s, rt, q0, q1, partner);
                        break;
                    case ColliderKind.Ring:
                    {
                        Vec2 o = c.A * s;
                        float radius = c.Radius * s;
                        for (int a = 0; a < c.ArcCount; a++)
                        {
                            int ai = c.ArcStart + a;
                            AddNearCap(ci, a, c.Body, o + _arcStartDir[ai] * radius, rt, q0, q1, partner);
                            AddNearCap(ci, a, c.Body, o + _arcEndDir[ai] * radius, rt, q0, q1, partner);
                        }
                        break;
                    }
                    case ColliderKind.Circle:
                        AddNearCap(ci, 0, c.Body, c.A * s, r + c.Radius * s, q0, q1, partner);
                        break;
                }
            }
            if (_nearCapCount == 0) return false;

            bool found = false;
            for (int i = 0; i < _nearCapCount; i++)
            {
                ref NearCap a = ref _nearCaps[i];
                // Cap–cap: the two ends of one segment are joined by its own centre line already.
                for (int j = i + 1; j < _nearCapCount; j++)
                {
                    ref NearCap b = ref _nearCaps[j];
                    if (b.Body != a.Body) continue;
                    if (b.Collider == a.Collider && _colliders[a.Collider].Kind == ColliderKind.Segment) continue;
                    if (!BridgeCloses(a.C, b.C, a.Rt + b.Rt)) continue;
                    if (BridgeCrossing(ref a, b.C, b.Collider, b.Feature, ref best))
                    {
                        collider = a.Collider;
                        found = true;
                    }
                }
                // Cap–face: the nearest point of another primitive's centre line, when it is not an end cap.
                for (int k = 0; k < n; k++)
                {
                    int ci = _wallCandidates[k];
                    if (_wallRejected[ci] == _wallStamp) continue;
                    ref ColliderData c = ref _colliders[ci];
                    if (c.Body != a.Body) continue;
                    float s = _bodies[c.Body].Scale;
                    float reach = a.Rt + r + c.HalfThickness;
                    Vec2 x;
                    switch (c.Kind)
                    {
                        case ColliderKind.Segment:
                            if (SegmentBridgePoint(a.C, c.A * s, c.B * s, out x) && BridgeCloses(a.C, x, reach) && BridgeCrossing(ref a, x, ci, 0, ref best))
                            {
                                collider = a.Collider;
                                found = true;
                            }
                            break;
                        case ColliderKind.Polyline:
                        {
                            int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                            for (int e = 0; e < edges; e++)
                            {
                                Vec2 va = _vertices[c.VertexStart + e] * s;
                                Vec2 vb = _vertices[c.VertexStart + (e + 1 == c.VertexCount ? 0 : e + 1)] * s;
                                if (SegmentBridgePoint(a.C, va, vb, out x) && BridgeCloses(a.C, x, reach) && BridgeCrossing(ref a, x, ci, e, ref best))
                                {
                                    collider = a.Collider;
                                    found = true;
                                }
                            }
                            break;
                        }
                        case ColliderKind.Ring:
                            if (ArcBridgePoint(ref c, a.C, c.A * s, c.Radius * s, reach, out x) && BridgeCloses(a.C, x, reach)
                                && BridgeCrossing(ref a, x, ci, SolidArcAt(ref c, SimMath.Atan2(x.Y - c.A.Y * s, x.X - c.A.X * s)), ref best))
                            {
                                collider = a.Collider;
                                found = true;
                            }
                            break;
                    }
                }
            }
            return found;
        }

        void AddNearCap(int ci, int feature, int body, Vec2 centre, float rt, Vec2 q0, Vec2 q1, float partner)
        {
            float reach = rt + partner;
            if (SegmentPointDistanceSq(q0, q1, centre) >= reach * reach) return;
            if (_nearCapCount == _nearCaps.Length) Array.Resize(ref _nearCaps, _nearCaps.Length * 2);
            _nearCaps[_nearCapCount++] = new NearCap { Collider = ci, Feature = feature, Body = body, C = centre, Rt = rt, Q0 = q0, Q1 = q1 };
        }

        /// <summary>Cores <paramref name="a"/> and <paramref name="b"/> do not touch but their inflated shapes (radii summing to <paramref name="reach"/>) overlap.</summary>
        static bool BridgeCloses(Vec2 a, Vec2 b, float reach)
        {
            float d2 = (b - a).LengthSquared;
            return d2 > BridgeEpsilon * BridgeEpsilon && d2 < reach * reach;
        }

        /// <summary>
        /// The cap's chord crosses the bridge from the cap to <paramref name="b"/> (on collider <paramref name="partner"/>)
        /// strictly from one side to the other, earlier than <paramref name="best"/>: the ball is held just off the bridge on its
        /// start side.
        /// </summary>
        static bool BridgeCrossing(ref NearCap cap, Vec2 b, int partner, int partnerFeature, ref SweepHit best)
        {
            Vec2 a = cap.C;
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            Vec2 ln = d.Perp;
            float sa = Vec2.Dot(cap.Q0 - a, ln);
            float sb = Vec2.Dot(cap.Q1 - a, ln);
            if (!((sa > 0f && sb < 0f) || (sa < 0f && sb > 0f))) return false;
            float t = sa / (sa - sb);
            if (t >= best.T) return false;
            Vec2 x = cap.Q0 + (cap.Q1 - cap.Q0) * t;
            float u = Vec2.Dot(x - a, d) / l2;
            if (u < 0f || u > 1f) return false;
            Vec2 nb = ln / MathF.Sqrt(l2);
            if (sa < 0f) nb = -nb;
            best = new SweepHit
            {
                T = t, Pos = x + nb * ContactSlop, N = nb, Feature = cap.Feature, Corrected = true, Bridge = true,
                Partner = partner, PartnerFeature = partnerFeature, EndA = a, EndB = b,
            };
            return true;
        }

        /// <summary>
        /// Asks the contact hook about a bridge hit, for both colliders (ascending id, each once) with the normal from each end
        /// towards the held centre. False if either was rejected (it is skipped for the rest of the substep) or the ball died.
        /// </summary>
        bool AcceptBridge(int bi, int ci, ref SweepHit hit, float h)
        {
            if (ContactHook == null) return true;
            int cb = hit.Partner;
            bool aFirst = ci <= cb;
            if (!AskBridgeHook(bi, aFirst ? ci : cb, hit.Pos, aFirst ? hit.EndA : hit.EndB, aFirst ? hit.Feature : hit.PartnerFeature, h)) return false;
            if (cb == ci) return true;
            return AskBridgeHook(bi, aFirst ? cb : ci, hit.Pos, aFirst ? hit.EndB : hit.EndA, aFirst ? hit.PartnerFeature : hit.Feature, h);
        }

        bool AskBridgeHook(int bi, int ci, Vec2 qPos, Vec2 end, int feature, float h)
        {
            if (PrepareContact(bi, ci, qPos, FallbackNormal(qPos - end), feature, -1f, h, out _, out _)) return true;
            if (_balls[bi].Alive) _wallRejected[ci] = _wallStamp;
            return false;
        }
    }
}
