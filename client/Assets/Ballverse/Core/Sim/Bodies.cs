namespace Ballverse.Core.Sim
{
    public enum BodyKind
    {
        /// <summary>Never moves; velocities are ignored.</summary>
        Static = 0,
        /// <summary>Moved by velocities or scripted targets; infinite mass, pushes balls and imparts its surface velocity.</summary>
        Kinematic = 1,
    }

    /// <summary>Description of a static or kinematic body that carries colliders.</summary>
    public struct BodyDef
    {
        public BodyKind Kind;
        public Vec2 Position;
        /// <summary>Rotation in radians (counter-clockwise).</summary>
        public float Angle;
        public Vec2 LinearVelocity;
        /// <summary>rad/s, counter-clockwise positive.</summary>
        public float AngularVelocity;
        /// <summary>Uniform scale of the collider geometry (thickness is not scaled). 0 means 1.</summary>
        public float Scale;
        /// <summary>Scale change per second (shrinking rings / walls); contact velocity includes the radial motion.</summary>
        public float ScaleVelocity;
        /// <summary>Azimuth of the tower axis for <see cref="ColliderKind.SpinPlatform"/> colliders (radians).</summary>
        public float Spin;
        /// <summary>Azimuth rate (rad/s) for spin platforms.</summary>
        public float SpinVelocity;
        public int Tag;

        public static BodyDef Static(Vec2 position, float angle = 0f)
        {
            return new BodyDef { Kind = BodyKind.Static, Position = position, Angle = angle, Scale = 1f };
        }

        public static BodyDef Kinematic(Vec2 position, float angle = 0f, float angularVelocity = 0f)
        {
            return new BodyDef { Kind = BodyKind.Kinematic, Position = position, Angle = angle, AngularVelocity = angularVelocity, Scale = 1f };
        }
    }

    /// <summary>Pose and velocities of one body. Read through <see cref="PhysicsWorld.GetBody"/>.</summary>
    public struct BodyState
    {
        public BodyKind Kind;
        public Vec2 Position;
        public float Angle;
        public Vec2 LinearVelocity;
        public float AngularVelocity;
        public float Scale;
        public float ScaleVelocity;
        public float Spin;
        public float SpinVelocity;
        public int Tag;
        public bool Enabled;
    }
}
