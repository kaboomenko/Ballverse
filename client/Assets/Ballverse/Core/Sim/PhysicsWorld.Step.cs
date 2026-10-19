using System;

namespace Ballverse.Core.Sim
{
    public sealed partial class PhysicsWorld
    {
        bool _stepping;
        float _gravityLength;
        float _effectiveMaxSpeed;

        /// <summary>
        /// Speed clamp the next tick will use (the current one while stepping): <see cref="SimSettings.MaxSpeed"/>, raised to
        /// the fastest wall surface speed plus <see cref="SimSettings.WallSpeedMargin"/> so the clamp never makes a ball slower
        /// than the wall pushing it (spec §6.5: max(maxSpeed; v_wall,max + 5 m/s)).
        /// </summary>
        public float EffectiveMaxSpeed => _stepping ? _effectiveMaxSpeed : ComputeEffectiveMaxSpeed();

        /// <summary>True while <see cref="Step"/> runs (contact hooks): adding balls, bodies or colliders is refused then.</summary>
        public bool IsStepping => _stepping;

        /// <summary>
        /// Advances the world by one tick (1/60 s) in <see cref="SimSettings.Substeps"/> substeps: kinematic bodies move,
        /// balls integrate gravity, ball–ball contacts are solved (uniform grid; bottom-up along gravity, otherwise ascending
        /// id), then every ball (ascending id) is swept against all colliders at once and resolved (time of impact order, then
        /// one joint projection out of every wall it still overlaps), so walls win over ball pushes; then spin-platform
        /// transits are released. Afterwards <see cref="Events"/> holds this tick's sorted events.
        /// </summary>
        public void Step()
        {
            if (_stepping) throw new InvalidOperationException("Step() called from inside Step() (contact hook)");
            _stepping = true;
            try
            {
                StepCore();
            }
            finally
            {
                _stepping = false;
            }
        }

        void StepCore()
        {
            BeginTickEvents();
            EnsureColliderScratch();
            float dt = TickDuration;
            for (int i = 0; i < _bodyCount; i++)
            {
                ref BodyFrame f = ref _frames[i];
                if (!f.HasTarget) continue;
                ref PhysicsBodyState b = ref _bodies[i];
                b.LinearVelocity = (f.TargetPosition - b.Position) * SimConstants.TickRate;
                b.AngularVelocity = SimMath.WrapPi(f.TargetAngle - b.Angle) * SimConstants.TickRate;
            }
            _gravityLength = _settings.Gravity.Length;
            _effectiveMaxSpeed = ComputeEffectiveMaxSpeed();

            int substeps = _settings.Substeps;
            float h = dt / substeps;
            for (int s = 0; s < substeps; s++) Substep(h);

            for (int i = 0; i < _bodyCount; i++)
            {
                ref PhysicsBodyState b = ref _bodies[i];
                ref BodyFrame f = ref _frames[i];
                if (f.HasTarget)
                {
                    b.Position = f.TargetPosition;
                    b.Angle = f.TargetAngle;
                    b.LinearVelocity = Vec2.Zero;
                    b.AngularVelocity = 0f;
                    f.HasTarget = false;
                }
                // Keep angles small so float precision does not degrade over a 180 s run.
                b.Angle = SimMath.WrapPi(b.Angle);
                b.Spin = SimMath.WrapPi(b.Spin);
                SimMath.SinCos(b.Angle, out f.Sin, out f.Cos);
            }
            for (int i = 0; i < _ballCount; i++)
            {
                if (_balls[i].Alive) _balls[i].Angle = SimMath.WrapPi(_balls[i].Angle);
            }

            FinishTickEvents();
            _tick++;
        }

        void Substep(float h)
        {
            for (int i = 0; i < _bodyCount; i++)
            {
                ref PhysicsBodyState b = ref _bodies[i];
                ref BodyFrame f = ref _frames[i];
                f.PrevPosition = b.Position;
                f.PrevCos = f.Cos;
                f.PrevSin = f.Sin;
                f.PrevScale = b.Scale;
                if (b.Motion != BodyMotion.Kinematic || !b.Enabled) continue;
                b.Position = b.Position + b.LinearVelocity * h;
                if (b.AngularVelocity != 0f)
                {
                    b.Angle = b.Angle + b.AngularVelocity * h;
                    SimMath.SinCos(b.Angle, out f.Sin, out f.Cos);
                }
                if (b.ScaleVelocity != 0f)
                {
                    float sc = b.Scale + b.ScaleVelocity * h;
                    b.Scale = sc < 1e-4f ? 1e-4f : sc;
                }
                b.Spin = b.Spin + b.SpinVelocity * h;
            }

            Vec2 g = _settings.Gravity;
            float maxSpeed = _effectiveMaxSpeed;
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                if (!b.Alive) continue;
                _ballPrev[i] = b.Position;
                b.Velocity = b.Velocity + g * (b.GravityScale * h);
                ApplyLocksAndClamp(ref b, maxSpeed);
                b.Position = b.Position + b.Velocity * h;
                b.Angle = b.Angle + b.AngularVelocity * h;
            }

            if (_settings.BallCollisions) SolveBallPairs();

            UpdateColliderBounds(h);
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                if (!b.Alive || b.Mask == 0u) continue;
                Vec2 p0 = _ballPrev[i];
                CollideBallWithWalls(i, h);
                if (!b.Alive) continue;
                ApplyLocksAndClamp(ref b, maxSpeed);
                if ((b.Flags & BallFlags.LockX) != 0) b.Position.X = p0.X;
                if ((b.Flags & BallFlags.LockY) != 0) b.Position.Y = p0.Y;
            }

            if (_transitCount > 0) ReleaseTransits();
            if (_sensorCount > 0) DetectSensorTouches();
        }

        static void ApplyLocksAndClamp(ref BallState b, float maxSpeed)
        {
            if ((b.Flags & BallFlags.LockX) != 0) b.Velocity.X = 0f;
            if ((b.Flags & BallFlags.LockY) != 0) b.Velocity.Y = 0f;
            if ((b.Flags & BallFlags.NoRotation) != 0) b.AngularVelocity = 0f;
            float v2 = b.Velocity.LengthSquared;
            if (v2 > maxSpeed * maxSpeed)
            {
                b.Velocity = b.Velocity * (maxSpeed / MathF.Sqrt(v2));
            }
            // Surface speed of the spin cannot physically exceed contact speeds; clamp guards restitution > 1 feedback.
            float maxSpin = 2f * maxSpeed / b.Radius;
            if (b.AngularVelocity > maxSpin) b.AngularVelocity = maxSpin;
            else if (b.AngularVelocity < -maxSpin) b.AngularVelocity = -maxSpin;
        }

        /// <summary>
        /// max(MaxSpeed, v_wall,max + WallSpeedMargin): v_wall,max bounds the surface speed of every enabled kinematic collider
        /// over its whole extent (linear + |ω|·reach + scale rate·reach + platform spin), where reach is the farthest point of
        /// the collider from the body's rotation centre (plus half the wall thickness) — the "reachable zone" of spec §6.5
        /// (a regular polygon of circumradius R̂ at ω gives ω·R̂ + margin). Body velocities are constant within a tick, so this
        /// is evaluated once per tick.
        /// </summary>
        float ComputeEffectiveMaxSpeed()
        {
            float max = _settings.MaxSpeed;
            float margin = _settings.WallSpeedMargin;
            if (margin < 0f) return max;
            float wall = 0f;
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderData cd = ref _colliders[c];
                if (!cd.Enabled || cd.Material.IsSensor) continue;
                ref PhysicsBodyState b = ref _bodies[cd.Body];
                if (!b.Enabled || b.Motion != BodyMotion.Kinematic) continue;
                float reach = cd.Reach * b.Scale + cd.HalfThickness;
                float v = b.LinearVelocity.Length + MathF.Abs(b.AngularVelocity) * reach + MathF.Abs(b.ScaleVelocity) * reach / b.Scale;
                if (cd.Kind == ColliderKind.SpinPlatform) v += MathF.Abs(b.SpinVelocity) * cd.Radius * b.Scale;
                if (v > wall) wall = v;
            }
            // No moving wall: the clamp stays exactly MaxSpeed (even below the margin).
            if (wall <= 0f) return max;
            float limit = wall + margin;
            return limit > max ? limit : max;
        }

        /// <summary>
        /// Restitution threshold for a ball with the given gravity scale: <see cref="SimSettings.RestitutionThreshold"/> scaled by
        /// the ball's effective gravity relative to 1 g (capped at 1). Without gravity nothing has to come to rest, so slow
        /// elastic contacts keep their energy.
        /// </summary>
        float RestingThreshold(float gravityScale)
        {
            float g = _gravityLength * MathF.Abs(gravityScale);
            if (g >= SimSettings.StandardGravity) return _settings.RestitutionThreshold;
            return _settings.RestitutionThreshold * (g / SimSettings.StandardGravity);
        }

        void UpdateColliderBounds(float h)
        {
            _capExtent = -1f;
            for (int c = 0; c < _colliderCount; c++)
            {
                ref ColliderData cd = ref _colliders[c];
                ref ColliderBounds cb = ref _colliderBounds[c];
                ref PhysicsBodyState b = ref _bodies[cd.Body];
                cb.Active = cd.Enabled && b.Enabled;
                if (!cb.Active) continue;
                if (!cd.Material.IsSensor && HasFreeCaps(ref cd))
                {
                    float extent = cd.Kind == ColliderKind.Circle ? cd.Radius * b.Scale : cd.HalfThickness;
                    if (extent > _capExtent) _capExtent = extent;
                }
                ref BodyFrame f = ref _frames[cd.Body];
                float s = b.Scale;
                cb.Centre = b.Position + Vec2.Rotate(cd.BoundCentre * s, f.Cos, f.Sin);
                // Margin covers the body's own translation, rotation of an off-centre bound and scale change within the substep.
                float margin = (b.Position - f.PrevPosition).Length
                    + MathF.Abs(s - f.PrevScale) * (cd.BoundRadius + cd.BoundCentre.Length)
                    + cd.BoundCentre.Length * s * MathF.Abs(b.AngularVelocity) * h;
                float geom = cd.BoundRadius * s;
                if (cd.Kind == ColliderKind.Circle)
                {
                    cb.Outer = geom + margin;
                    cb.Inner = -1f;
                }
                else
                {
                    cb.Outer = geom + cd.HalfThickness + margin;
                    cb.Inner = cd.Kind == ColliderKind.Ring ? geom - cd.HalfThickness - margin : -1f;
                }
            }
        }

        /// <summary>Cheap reject: the ball's path in this substep against the collider's bounding circle (and a ring's empty interior).</summary>
        static bool MayTouch(ref ColliderBounds cb, Vec2 p0, Vec2 p1, float r)
        {
            float outer = cb.Outer + r;
            if (SegmentPointDistanceSq(p0, p1, cb.Centre) > outer * outer) return false;
            float inner = cb.Inner - r;
            if (inner > 0f)
            {
                float i2 = inner * inner;
                // The disc is convex: both ends strictly inside means the whole path is inside.
                if ((p0 - cb.Centre).LengthSquared < i2 && (p1 - cb.Centre).LengthSquared < i2) return false;
            }
            return true;
        }

        static float SegmentPointDistanceSq(Vec2 a, Vec2 b, Vec2 p)
        {
            Vec2 d = b - a;
            float l2 = d.LengthSquared;
            float t = 0f;
            if (l2 > 1e-14f)
            {
                t = Vec2.Dot(p - a, d) / l2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
            }
            Vec2 c = a + d * t;
            return (p - c).LengthSquared;
        }
    }
}
