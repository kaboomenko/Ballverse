using System;

namespace Ballverse.Core.Sim
{
    [Flags]
    public enum BallFlags
    {
        None = 0,
        /// <summary>Horizontal velocity is held at zero (e.g. the Helix Drop ball that only moves vertically).</summary>
        LockX = 1,
        /// <summary>Vertical velocity is held at zero.</summary>
        LockY = 2,
        /// <summary>Spin is held at zero (friction still acts on linear velocity as if the ball could not roll).</summary>
        NoRotation = 4,
    }

    /// <summary>Description of a dynamic circle to add with <see cref="PhysicsWorld.AddBall"/>.</summary>
    public struct BallDef
    {
        public Vec2 Position;
        public Vec2 Velocity;
        public float Angle;
        public float AngularVelocity;
        /// <summary>Radius in metres, &gt; 0.</summary>
        public float Radius;
        /// <summary>Mass in kg; 0 means density 1 kg/m² × πr².</summary>
        public float Mass;
        /// <summary>Restitution 0–1.2 (spec §2.2); values &gt; 1 gain energy and rely on the max-speed clamp.</summary>
        public float Restitution;
        /// <summary>Coulomb friction coefficient 0–1.</summary>
        public float Friction;
        /// <summary>Multiplier of world gravity (1 = normal, 0 = floats).</summary>
        public float GravityScale;
        /// <summary>Mode-defined tag (team, colour index, participant id…). Not used by the solver.</summary>
        public int Tag;
        /// <summary>Collision category bits of this ball.</summary>
        public uint Category;
        /// <summary>Categories this ball collides with. 0 makes it a pure ballistic particle (e.g. ring fragments).</summary>
        public uint Mask;
        public BallFlags Flags;

        public static BallDef Create(Vec2 position, float radius)
        {
            return new BallDef
            {
                Position = position,
                Radius = radius,
                Restitution = 0.8f,
                Friction = 0.2f,
                GravityScale = 1f,
                Category = 1u,
                Mask = uint.MaxValue,
            };
        }
    }

    /// <summary>Current state of one ball. Read through <see cref="PhysicsWorld.GetBall"/>; change through the world's setters.</summary>
    public struct BallState
    {
        public Vec2 Position;
        public Vec2 Velocity;
        public float Angle;
        public float AngularVelocity;
        public float Radius;
        public float Mass;
        public float InvMass;
        public float InvInertia;
        public float Restitution;
        public float Friction;
        public float GravityScale;
        public int Tag;
        public uint Category;
        public uint Mask;
        public BallFlags Flags;
        public bool Alive;
    }
}
