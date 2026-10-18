using System;

namespace Ballverse.Core.Sim
{
    /// <summary>
    /// World-wide solver settings (spec §2.1, §2.2 physics group). A value type: the world keeps its own copy and
    /// validates it in <see cref="PhysicsWorld.Settings"/>. "Live" values (gravity, max speed) may change between ticks.
    /// </summary>
    public struct SimSettings
    {
        public const int MinSubsteps = 1;
        public const int MaxSubsteps = 64;
        public const int DefaultSubsteps = 8;
        public const float StandardGravity = 9.81f;

        /// <summary>Gravity acceleration, m/s². Spec default: 1 g straight down.</summary>
        public Vec2 Gravity;
        /// <summary>Substeps per 1/60 s tick (default 8). More substeps: stiffer stacks, less tunnelling risk.</summary>
        public int Substeps;
        /// <summary>
        /// Speed clamp in m/s applied to every ball after each substep (spec §2.2 maxSpeed 5–60, default 30). The solver raises
        /// it to the fastest kinematic wall surface speed + <see cref="WallSpeedMargin"/> (see
        /// <see cref="PhysicsWorld.EffectiveMaxSpeed"/>).
        /// </summary>
        public float MaxSpeed;
        /// <summary>
        /// Spec §6.5 effective limit max(maxSpeed; v_wall,max + margin), m/s (default 5). Negative disables the rule: the clamp
        /// is exactly <see cref="MaxSpeed"/> even below a pushing wall's speed.
        /// </summary>
        public float WallSpeedMargin;
        /// <summary>
        /// Approach speeds below this bounce with restitution 0 so resting contacts settle (m/s, at 1 g). It is scaled by each
        /// ball's effective gravity |Gravity·GravityScale| / 1 g (capped at 1): without gravity no contact needs to come to rest,
        /// so zero-gravity elastic scenes («Невесомость», «Медитация») keep their energy at any speed.
        /// </summary>
        public float RestitutionThreshold;
        /// <summary>Hit/BallHit events whose strongest approach speed in the tick is below this are not reported (m/s).</summary>
        public float HitEventMinSpeed;
        /// <summary>Global switch for ball–ball collisions (per-ball filtering uses category/mask).</summary>
        public bool BallCollisions;
        /// <summary>
        /// Gauss–Seidel passes over ball–ball pairs per substep (1–8, default 2). One pass is enough for sparse scenes;
        /// resting piles need two so they fall silent (no residual contact events) instead of creeping.
        /// </summary>
        public int BallIterations;
        /// <summary>Ball–ball penetration allowed without positional correction (m).</summary>
        public float PositionSlop;
        /// <summary>Fraction (0–1] of ball–ball penetration beyond the slop removed per pass.</summary>
        public float PositionCorrection;

        public static SimSettings Default => new SimSettings
        {
            Gravity = new Vec2(0f, -StandardGravity),
            Substeps = DefaultSubsteps,
            MaxSpeed = 30f,
            WallSpeedMargin = 5f,
            RestitutionThreshold = 0.25f,
            HitEventMinSpeed = 0.05f,
            BallCollisions = true,
            BallIterations = 2,
            PositionSlop = 0.002f,
            PositionCorrection = 0.8f,
        };

        /// <summary>Gravity vector from the spec parameters: <paramref name="g"/> in units of 9.81 m/s² and a direction in degrees (270 = down).</summary>
        public static Vec2 GravityFrom(float g, float angleDegrees)
        {
            float a = angleDegrees * SimMath.DegToRad;
            float m = g * StandardGravity;
            return Vec2.FromAngle(a) * m;
        }

        internal void Validate()
        {
            if (!SimMath.IsFinite(Gravity)) throw new ArgumentException("Gravity must be finite");
            if (Substeps < MinSubsteps || Substeps > MaxSubsteps)
                throw new ArgumentException("Substeps must be in [" + MinSubsteps + ", " + MaxSubsteps + "], got " + Substeps);
            if (!SimMath.IsFinite(MaxSpeed) || MaxSpeed <= 0f) throw new ArgumentException("MaxSpeed must be > 0");
            if (!SimMath.IsFinite(WallSpeedMargin)) throw new ArgumentException("WallSpeedMargin must be finite");
            if (!SimMath.IsFinite(RestitutionThreshold) || RestitutionThreshold < 0f) throw new ArgumentException("RestitutionThreshold must be >= 0");
            if (!SimMath.IsFinite(HitEventMinSpeed) || HitEventMinSpeed < 0f) throw new ArgumentException("HitEventMinSpeed must be >= 0");
            if (BallIterations < 1 || BallIterations > 8) throw new ArgumentException("BallIterations must be in [1, 8]");
            if (!SimMath.IsFinite(PositionSlop) || PositionSlop < 0f) throw new ArgumentException("PositionSlop must be >= 0");
            if (!SimMath.IsFinite(PositionCorrection) || PositionCorrection <= 0f || PositionCorrection > 1f)
                throw new ArgumentException("PositionCorrection must be in (0, 1]");
        }
    }
}
