using System;

namespace Ballverse.Core.Sim
{
    // Ball vs walls: continuous sweep against ALL colliders at once (anti-tunnelling), then a joint overlap projection.
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
    // the ball already overlaps a wall and its centre crosses the wall's centre line, it is put back just off the centre line
    // on the side it came from ("corrected" hit). Openings narrower than the ball are closed by bridges between the caps (and
    // faces) of one body, which the sweep treats like centre lines (PhysicsWorld.Bridges.cs). Invariant after the sweep: the
    // ball centre has not crossed any solid centre line or bridge during the substep. The overlap pass
    // (PhysicsWorld.Overlap.cs) keeps that invariant while pushing the ball out of every wall it still overlaps at once (acute
    // corners), and spin platforms decide pass / bounce once per entry (PhysicsWorld.Platform.cs).
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
            /// <summary>Spin platform entered with the ball's footprint inside a gap: a transit starts, no contact.</summary>
            public bool Transit;
            /// <summary>Centre would cross a bridge (PhysicsWorld.Bridges.cs): held at <see cref="Pos"/>, the overlap pass responds.</summary>
            public bool Bridge;
            /// <summary>Bridge: the other end's collider and feature (the hit's collider owns the cap at <see cref="EndA"/>).</summary>
            public int Partner;
            public int PartnerFeature;
            /// <summary>Bridge end points (q space).</summary>
            public Vec2 EndA, EndB;

            public static SweepHit None => new SweepHit { T = 2f };
        }

        /// <summary>
        /// Contact prepared for a response: world normal and contact point, wall surface velocity, event data and, for the
        /// overlap pass, the linear constraint <c>N·p ≥ Target</c> on the world ball centre (and <c>N·p ≥ Hard</c>: centre
        /// on the legal side of the wall's centre line).
        /// </summary>
        struct ContactFrame
        {
            public int Collider;
            public int Body;
            public int Feature;
            public Vec2 N;
            public Vec2 Cp;
            public Vec2 WallVelocity;
            public float LocalAngle;
            public float Target;
            public float Hard;
        }

        /// <summary>Distance from the centre line at which a corrected (centre-line crossing) hit leaves the ball.</summary>
        static float CorrectedOffset(float rt)
        {
            return rt > 2f * ContactSlop ? ContactSlop : rt * 0.5f;
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
                    if (SweepWall(bi, c, tStart, wStart, h, ref best)) bestC = c;
                }
                if (_capExtent >= 0f && SweepBridges(bi, n, tStart, wStart, impacts, ref best, out int bridgeC)) bestC = bridgeC;
                if (bestC < 0) break;
                if (best.Bridge)
                {
                    if (!AcceptBridge(bi, bestC, ref best, h))
                    {
                        if (!ball.Alive) return;
                        continue;
                    }
                    // Stopped on the start side of a closed opening, inside both primitives: the joint overlap projection pushes
                    // the ball back out and applies the responses of everything it touches.
                    ball.Position = FromBodySpace(_colliders[bestC].Body, best.Pos);
                    break;
                }
                if (best.Transit)
                {
                    // The path is unchanged; the platform no longer collides with this ball, so sweep the rest again.
                    AddTransit(bi, bestC);
                    continue;
                }
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
            ResolveOverlaps(bi, n, h);
        }

        /// <summary>
        /// Adds every eligible collider not yet listed whose bounds the path <paramref name="from"/>→<paramref name="to"/> may
        /// touch, keeping <see cref="_wallCandidates"/>[0..n) in ascending id order. Returns the new count. When some collider
        /// has free caps, colliders are gathered within the bridge reach (<see cref="BridgeReach"/>), so the partner of a bridge
        /// whose other end the ball touches is a candidate too.
        /// </summary>
        int GatherWallCandidates(int bi, Vec2 from, Vec2 to, int n)
        {
            ref BallState b = ref _balls[bi];
            float r = b.Radius;
            float rCap = BridgeReach(r);
            for (int c = 0; c < _colliderCount; c++)
            {
                if (_wallMark[c] == _wallStamp) continue;
                ref ColliderBounds cb = ref _colliderBounds[c];
                if (!cb.Active) continue;
                ref ColliderData cd = ref _colliders[c];
                if (cd.Material.IsSensor) continue;
                if ((b.Mask & cd.Material.Category) == 0u || (cd.Material.Mask & b.Category) == 0u) continue;
                if (!MayTouch(ref cb, from, to, rCap)) continue;
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
            ref PhysicsBodyState b = ref _bodies[body];
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

        /// <summary>World point → q space of <paramref name="body"/> at its current pose.</summary>
        Vec2 ToBodySpace(int body, Vec2 world)
        {
            ref BodyFrame f = ref _frames[body];
            return Vec2.InvRotate(world - _bodies[body].Position, f.Cos, f.Sin);
        }

        /// <summary>Sweeps the ball's remaining path (from <paramref name="wStart"/> at <paramref name="tStart"/> to its current position) against one collider.</summary>
        bool SweepWall(int bi, int ci, float tStart, Vec2 wStart, float h, ref SweepHit best)
        {
            ref ColliderData c = ref _colliders[ci];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            ref BallState ball = ref _balls[bi];
            Vec2 q1 = ToBodySpace(c.Body, ball.Position);
            Vec2 q0 = ToBodySpaceAt(c.Body, wStart, tStart);
            float r = ball.Radius;
            float s = body.Scale;
            float rt = r + c.HalfThickness;
            switch (c.Kind)
            {
                case ColliderKind.Segment:
                    return SweepCapsule(q0, q1, c.A * s, c.B * s, rt, 0, ref best);
                case ColliderKind.Polyline:
                {
                    bool found = false;
                    int edges = c.Closed ? c.VertexCount : c.VertexCount - 1;
                    for (int k = 0; k < edges; k++)
                    {
                        Vec2 a = _vertices[c.VertexStart + k] * s;
                        Vec2 b = _vertices[c.VertexStart + (k + 1 == c.VertexCount ? 0 : k + 1)] * s;
                        if (SweepCapsule(q0, q1, a, b, rt, k, ref best)) found = true;
                    }
                    return found;
                }
                case ColliderKind.Ring:
                    return SweepRing(ref c, q0, q1, s, rt, ref best);
                case ColliderKind.Circle:
                    return SweepCircle(q0, q1 - q0, c.A * s, r + c.Radius * s, 0, ref best);
                case ColliderKind.SpinPlatform:
                    return SweepPlatform(bi, ci, q0, q1, tStart, h, ref best);
            }
            return false;
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

        static Vec2 FallbackNormal(Vec2 preferred)
        {
            float l = preferred.Length;
            return l > NormalEpsilon ? preferred / l : new Vec2(0f, 1f);
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
                    // Already overlapping and the centre crosses the centre line: back to the entry side, just off the centre
                    // line (the overlap pass then pushes the ball out together with every other wall it touches).
                    float t = g0 / (g0 - g1);
                    if (t < best.T)
                    {
                        Vec2 x = q0 + motion * t;
                        float u = Vec2.Dot(x - a, d) / l2;
                        if (u >= 0f && u <= 1f)
                        {
                            best = new SweepHit { T = t, Pos = a + d * u + n * (side * CorrectedOffset(rt)), N = n * side, Feature = feature, Corrected = true };
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
            // Entry into the inflated band from clear. Inside the ring the free disc (radius R − rt) is convex, so the chord
            // reaches the band only if it ends there; outside, a chord can dip into the band (radius R + rt) and leave it again
            // within one substep, so the entering root is taken wherever the chord ends (QA: missed outer-face grazes).
            if (g0 >= rt + ContactSlop && (side > 0f || g1 < rt))
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
                        best = new SweepHit { T = t, Pos = o + dir * (radius + side * CorrectedOffset(rt)), N = dir * side, Feature = arc, Corrected = true };
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
                        best = new SweepHit { T = t, Pos = o + dir * (radius + side * CorrectedOffset(rt)), N = dir * side, Feature = arc, Corrected = true };
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

        // ------------------------------------------------------------------ response

        /// <summary>
        /// Applies one swept contact. <paramref name="qPos"/> / <paramref name="nLocal"/> are the ball centre and normal in q space.
        /// <para>
        /// With <paramref name="sweepT"/> ≥ 0 (time of impact as a fraction of the substep) the contact is placed with the body's
        /// pose interpolated to that time; with <paramref name="advance"/> the ball then travels the rest of the substep with its
        /// new velocity — so a ball bouncing off a rotating wall is not carried along by the wall's rotation (which would bias
        /// energy) — otherwise it stays at the contact. With −1 the current pose is used and the ball stays at the resolved
        /// position (corrected hit). <see cref="_contactPos"/> receives the world contact centre.
        /// </para>
        /// Returns false if the ball is dead or the mode hook rejected the contact or killed the ball (nothing changed).
        /// </summary>
        bool ResolveContact(int bi, int ci, Vec2 qPos, Vec2 nLocal, int feature, float sweepT, float h, bool advance)
        {
            ref BallState ball = ref _balls[bi];
            if (!PrepareContact(bi, ci, qPos, nLocal, feature, sweepT, h, out ContactFrame cf, out Vec2 pos)) return false;
            ApplyImpulse(bi, ref cf);
            _contactPos = pos;
            ball.Position = sweepT >= 0f && advance ? pos + ball.Velocity * ((1f - sweepT) * h) : pos;
            return true;
        }

        /// <summary>
        /// Maps a contact from q space to the world (pose at <paramref name="sweepT"/>, or the current pose for −1), computes the
        /// wall's velocity at the contact point (linear + ω × r + scale rate + platform surface motion) and the event data, and
        /// asks the mode hook. <paramref name="pos"/> is the world ball centre that resolves this contact alone;
        /// <see cref="ContactFrame.Target"/> is set from it. False if the ball is dead, or the hook rejected the contact or
        /// killed the ball.
        /// </summary>
        bool PrepareContact(int bi, int ci, Vec2 qPos, Vec2 nLocal, int feature, float sweepT, float h, out ContactFrame cf, out Vec2 pos)
        {
            ref ColliderData c = ref _colliders[ci];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            ref BallState ball = ref _balls[bi];
            cf = default;
            pos = ball.Position;
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
            pos = bodyPos + Vec2.Rotate(qWorldScale, cos, sin);
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
                float sinA = SimMath.Clamp((qPos.X - c.A.X * s) / w, -1f, 1f);
                float alpha = SimMath.Asin(sinA);
                float spin = sweepT >= 0f ? body.Spin - body.SpinVelocity * h * (1f - sweepT) : body.Spin;
                localAngle = SimMath.Wrap2Pi(alpha - spin);
                surfaceLocal = new Vec2(w * SimMath.Cos(alpha) * body.SpinVelocity, 0f);
            }

            Vec2 wallVelocity = WallVelocityAt(ref body, bodyPos, scaleNow, cp, surfaceLocal, cos, sin);

            cf.Collider = ci;
            cf.Body = c.Body;
            cf.Feature = feature;
            cf.N = n;
            cf.Cp = cp;
            cf.WallVelocity = wallVelocity;
            cf.LocalAngle = localAngle;
            cf.Target = Vec2.Dot(n, pos);
            cf.Hard = float.NegativeInfinity;

            ISimContactHook hook = ContactHook;
            if (hook != null)
            {
                float vn = Vec2.Dot(BallSurfaceVelocity(ref ball, n) - wallVelocity, n);
                var info = new ContactPoint { Point = cp, Normal = n, ApproachSpeed = -vn, LocalAngle = localAngle, Feature = feature };
                if (!hook.PreSolveHit(this, bi, ci, in info)) return false;
                // A hook that kills the ball (deadly sector) ends its contacts at once: no response, no Hit event.
                if (!ball.Alive) return false;
            }
            return true;
        }

        /// <summary>
        /// Velocity of a body's surface at world point <paramref name="cp"/> (body at <paramref name="bodyPos"/>, scale
        /// <paramref name="scaleNow"/>, rotation cos/sin): linear + ω × r + scale rate + platform surface motion; zero for static bodies.
        /// </summary>
        static Vec2 WallVelocityAt(ref PhysicsBodyState body, Vec2 bodyPos, float scaleNow, Vec2 cp, Vec2 surfaceLocal, float cos, float sin)
        {
            if (body.Motion != BodyMotion.Kinematic) return Vec2.Zero;
            Vec2 arm = cp - bodyPos;
            Vec2 wallVelocity = body.LinearVelocity + Vec2.Cross(body.AngularVelocity, arm);
            if (body.ScaleVelocity != 0f) wallVelocity = wallVelocity + arm * (body.ScaleVelocity / scaleNow);
            if (surfaceLocal.X != 0f) wallVelocity = wallVelocity + Vec2.Rotate(surfaceLocal, cos, sin);
            return wallVelocity;
        }

        /// <summary>Velocity of the ball's surface at the contact with normal <paramref name="n"/>: v + ω × (−r·n) = v − ω·r·t.</summary>
        static Vec2 BallSurfaceVelocity(ref BallState ball, Vec2 n)
        {
            if ((ball.Flags & BallFlags.NoRotation) != 0) return ball.Velocity;
            return ball.Velocity - n.Perp * (ball.AngularVelocity * ball.Radius);
        }

        /// <summary>
        /// Normal impulse with restitution (max of ball and wall, 0 below the resting threshold) and Coulomb friction against the
        /// wall's surface velocity, from the ball's current velocity; records the Hit event when the ball approaches.
        /// </summary>
        void ApplyImpulse(int bi, ref ContactFrame cf)
        {
            ref BallState ball = ref _balls[bi];
            ref ColliderData c = ref _colliders[cf.Collider];
            Vec2 n = cf.N;
            Vec2 t = n.Perp;
            float r = ball.Radius;
            bool rotates = (ball.Flags & BallFlags.NoRotation) == 0;
            Vec2 vrel = BallSurfaceVelocity(ref ball, n) - cf.WallVelocity;
            float vn = Vec2.Dot(vrel, n);
            if (vn >= 0f) return;
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
            RecordContact(SimEventKind.Hit, bi, cf.Collider, cf.Body, cf.Cp, n, jn, -vn, cf.LocalAngle, cf.Feature);
        }
    }
}
