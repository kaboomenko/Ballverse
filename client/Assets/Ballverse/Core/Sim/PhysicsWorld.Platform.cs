using System;

namespace Ballverse.Core.Sim
{
    /// <summary>A ball passing through the thickness of a spin platform (spec §2.9 M05 "транзит"): no contacts for this pair.</summary>
    public struct SpinTransit
    {
        public int Ball;
        public int Collider;

        public SpinTransit(int ball, int collider)
        {
            Ball = ball;
            Collider = collider;
        }
    }

    // Helix Drop platforms (ColliderKind.SpinPlatform), spec §2.9 M05 and §6.5.
    //
    // In q space the platform is a slab: x within ±w of its centre (w = half-width = R_path), y between bottom = y_c − T/2 and
    // top = y_c + T/2. A ball of radius r can touch it only while its centre is in the band (bottom − r, top + r) and within
    // w + r horizontally. The pass-or-bounce decision is made ONCE, when the centre enters the band (crossing top + r going
    // down, or bottom − r going up): the ball's footprint in platform azimuth is [a − δ, a + δ], a = asin(x / w) − Spin,
    // δ = asin(r / w). If the footprint lies strictly inside one gap (gap edges count as solid) a transit starts; otherwise
    // the ball hits the flat face (normal ±Y; no round end caps, no side faces). While the (ball, platform) transit exists the
    // pair makes no contact at all — sweep, overlap or hook — however far the platform turns; it ends when the ball leaves the
    // band (y + r < bottom: passed, or y − r > top). A ball found inside the band without a transit (spawned, teleported,
    // pushed there) is decided at its current position. Transits are part of the world state: hashed, in the snapshot.
    public sealed partial class PhysicsWorld
    {
        SpinTransit[] _transits = new SpinTransit[4];
        int _transitCount;

        /// <summary>Number of active (ball, platform) transits, sorted by ball then collider.</summary>
        public int TransitCount => _transitCount;

        public SpinTransit GetTransit(int index)
        {
            if ((uint)index >= (uint)_transitCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _transits[index];
        }

        /// <summary>True while the ball passes through the platform's thickness (render it in front of that platform).</summary>
        public bool IsInTransit(int ball, int collider)
        {
            CheckBall(ball);
            CheckCollider(collider);
            return FindTransit(ball, collider) >= 0;
        }

        /// <summary>Index of the pair in <see cref="_transits"/>, or the bitwise complement of its insertion point.</summary>
        int FindTransit(int ball, int collider)
        {
            int lo = 0, hi = _transitCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref SpinTransit t = ref _transits[mid];
                int cmp = t.Ball != ball ? (t.Ball < ball ? -1 : 1) : (t.Collider < collider ? -1 : (t.Collider > collider ? 1 : 0));
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return ~lo;
        }

        void AddTransit(int ball, int collider)
        {
            int idx = FindTransit(ball, collider);
            if (idx >= 0) return;
            idx = ~idx;
            // Grows straight to one entry per ball, so a pile falling through a platform settles the buffer at once.
            if (_transitCount == _transits.Length) Array.Resize(ref _transits, Math.Max(_transits.Length * 2, _ballCount));
            Array.Copy(_transits, idx, _transits, idx + 1, _transitCount - idx);
            _transits[idx] = new SpinTransit(ball, collider);
            _transitCount++;
        }

        void RemoveTransitAt(int idx)
        {
            _transitCount--;
            Array.Copy(_transits, idx + 1, _transits, idx, _transitCount - idx);
        }

        void RemoveTransitsOfBall(int ball)
        {
            for (int i = _transitCount - 1; i >= 0; i--)
            {
                if (_transits[i].Ball == ball) RemoveTransitAt(i);
            }
        }

        /// <summary>End of every substep: ends the transits of dead balls and of balls that left the platform's band.</summary>
        void ReleaseTransits()
        {
            for (int i = _transitCount - 1; i >= 0; i--)
            {
                ref SpinTransit t = ref _transits[i];
                ref BallState ball = ref _balls[t.Ball];
                if (!ball.Alive)
                {
                    RemoveTransitAt(i);
                    continue;
                }
                ref ColliderData c = ref _colliders[t.Collider];
                float y = ToBodySpace(c.Body, ball.Position).Y;
                float yc = c.A.Y * _bodies[c.Body].Scale;
                float reach = c.HalfThickness + ball.Radius;
                if (y < yc - reach || y > yc + reach) RemoveTransitAt(i);
            }
        }

        /// <summary>
        /// The footprint [a − δ, a + δ] of a ball at horizontal offset <paramref name="dx"/> from the platform centre lies
        /// strictly inside one gap (spec §2.9 M05: "Кромка просвета считается сплошной").
        /// </summary>
        bool FootprintInGap(ref ColliderData c, float dx, float w, float r, float spin)
        {
            if (c.Full) return false;
            float a = SimMath.Asin(SimMath.Clamp(dx / w, -1f, 1f)) - spin;
            float delta = r >= w ? SimMath.HalfPi : SimMath.Asin(r / w);
            float width = 2f * delta;
            for (int g = 0; g < c.GapCount; g++)
            {
                float u = SimMath.Wrap2Pi(a - delta - _gapStart[c.GapStart + g]);
                if (u > 0f && u + width < _gapSpan[c.GapStart + g]) return true;
            }
            return false;
        }

        /// <summary>Sweep against a spin platform: entry into its band (one decision per entry), or a crossing straight through it.</summary>
        bool SweepPlatform(int bi, int ci, Vec2 q0, Vec2 q1, float tStart, float h, ref SweepHit best)
        {
            if (FindTransit(bi, ci) >= 0) return false;
            ref ColliderData c = ref _colliders[ci];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            float r = _balls[bi].Radius;
            float s = body.Scale;
            float ox = c.A.X * s, oy = c.A.Y * s;
            float w = c.Radius * s;
            float lTop = oy + c.HalfThickness + r;
            float lBot = oy - c.HalfThickness - r;
            float reach = w + r;
            float t;
            int face;
            bool corrected = false;
            if (q0.Y >= lTop && q1.Y < lTop)
            {
                t = (q0.Y - lTop) / (q0.Y - q1.Y);
                face = 0;
            }
            else if (q0.Y <= lBot && q1.Y > lBot)
            {
                t = (lBot - q0.Y) / (q1.Y - q0.Y);
                face = 1;
            }
            else if (q0.Y < lTop && q0.Y > lBot)
            {
                // Inside the band without a transit and leaving it through the far side this substep: decide now.
                face = q0.Y >= oy ? 0 : 1;
                if (face == 0 ? q1.Y > lBot : q1.Y < lTop) return false;
                t = 0f;
                corrected = true;
            }
            else
            {
                return false;
            }
            if (t >= best.T) return false;
            float x = q0.X + (q1.X - q0.X) * t;
            if (MathF.Abs(x - ox) >= reach) return false;
            float tAbs = tStart + (1f - tStart) * t;
            float spin = body.Spin - body.SpinVelocity * h * (1f - tAbs);
            if (FootprintInGap(ref c, x - ox, w, r, spin))
            {
                best = new SweepHit { T = t, Transit = true };
                return true;
            }
            float ly = face == 0 ? lTop : lBot;
            Vec2 n = new Vec2(0f, face == 0 ? 1f : -1f);
            best = corrected
                ? new SweepHit { T = t, Pos = new Vec2(q1.X, ly), N = n, Feature = face, Corrected = true }
                : new SweepHit { T = t, Pos = new Vec2(x, ly), N = n, Feature = face };
            return true;
        }

        /// <summary>Overlap pass for a spin platform: a ball in its band without a transit is decided at its current position.</summary>
        bool GatherPlatform(int bi, int ci, Vec2 q, Vec2 qStart, float h)
        {
            if (FindTransit(bi, ci) >= 0) return true;
            ref ColliderData c = ref _colliders[ci];
            ref PhysicsBodyState body = ref _bodies[c.Body];
            float r = _balls[bi].Radius;
            float s = body.Scale;
            float ox = c.A.X * s, oy = c.A.Y * s;
            float w = c.Radius * s;
            float lTop = oy + c.HalfThickness + r;
            float lBot = oy - c.HalfThickness - r;
            if (!(q.Y < lTop && q.Y > lBot) || MathF.Abs(q.X - ox) >= w + r) return true;
            if (FootprintInGap(ref c, q.X - ox, w, r, body.Spin))
            {
                AddTransit(bi, ci);
                return true;
            }
            int face;
            if (qStart.Y >= lTop - ContactSlop) face = 0;
            else if (qStart.Y <= lBot + ContactSlop) face = 1;
            else face = q.Y >= oy ? 0 : 1;
            var shape = new OverlapShape { Radius = c.HalfThickness + r, HardRadius = -1f, Core = CoreNone };
            return AddOverlap(bi, ci, new Vec2(q.X, face == 0 ? lTop : lBot), new Vec2(0f, face == 0 ? 1f : -1f), face, -1f, h, in shape);
        }

        /// <summary>Sensor test for a spin platform: overlaps the band with a solid footprint, or (sweep) crosses its centre line on solid.</summary>
        bool PlatformSensorTouch(ref ColliderData c, ref PhysicsBodyState body, Vec2 q0, Vec2 q1, float r, bool sweep)
        {
            float s = body.Scale;
            float ox = c.A.X * s, oy = c.A.Y * s;
            float w = c.Radius * s;
            float band = c.HalfThickness + r;
            if (q1.Y < oy + band && q1.Y > oy - band && MathF.Abs(q1.X - ox) < w + r && !FootprintInGap(ref c, q1.X - ox, w, r, body.Spin)) return true;
            if (!sweep) return false;
            float s0 = q0.Y - oy, s1 = q1.Y - oy;
            if (!((s0 > 0f && s1 < 0f) || (s0 < 0f && s1 > 0f))) return false;
            float x = q0.X + (q1.X - q0.X) * (s0 / (s0 - s1));
            return MathF.Abs(x - ox) < w + r && !FootprintInGap(ref c, x - ox, w, r, body.Spin);
        }
    }
}
