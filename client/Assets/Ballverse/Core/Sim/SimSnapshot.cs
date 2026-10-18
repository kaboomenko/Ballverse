using System;

namespace Ballverse.Core.Sim
{
    /// <summary>Pose of one ball for rendering.</summary>
    public struct BallPose
    {
        public Vec2 Position;
        /// <summary>For motion blur / trails / squash; not needed to draw the frame.</summary>
        public Vec2 Velocity;
        public float Angle;
        public float Radius;
        public int Tag;
        public bool Alive;
    }

    /// <summary>Pose of one body for rendering (collider geometry is static in body space, see <see cref="PhysicsWorld.GetColliderInfo"/>).</summary>
    public struct BodyPose
    {
        public Vec2 Position;
        public float Angle;
        public float Scale;
        /// <summary>Tower azimuth for spin platforms (pseudo-3D drawing of the sectors).</summary>
        public float Spin;
        public int Tag;
        public bool Enabled;
    }

    /// <summary>
    /// Reusable per-tick view of the world for rendering and recording. Independent of the RenderStream module: an adapter
    /// maps it to render frames (ids are indices, so the mapping is direct). Filled by <see cref="PhysicsWorld.WriteSnapshot"/>.
    /// </summary>
    public sealed class SimSnapshot
    {
        public int Tick;
        public int BallCount;
        public BallPose[] Balls = new BallPose[0];
        public int BodyCount;
        public BodyPose[] Bodies = new BodyPose[0];
        public int ColliderCount;
        /// <summary>Collider and its body enabled (destroyed rings disappear).</summary>
        public bool[] ColliderEnabled = new bool[0];
        public int TransitCount;
        /// <summary>
        /// Balls passing through a spin platform's thickness (sorted by ball, then collider): draw the ball in front of that
        /// platform while the pair is listed (spec §2.9 M05).
        /// </summary>
        public SpinTransit[] Transits = new SpinTransit[0];

        internal void EnsureTransitCapacity(int transits)
        {
            if (Transits.Length < transits) Transits = new SpinTransit[Math.Max(transits, Transits.Length * 2)];
        }

        internal void EnsureCapacity(int balls, int bodies, int colliders)
        {
            if (Balls.Length < balls) Balls = new BallPose[Math.Max(balls, Balls.Length * 2)];
            if (Bodies.Length < bodies) Bodies = new BodyPose[Math.Max(bodies, Bodies.Length * 2)];
            if (ColliderEnabled.Length < colliders) ColliderEnabled = new bool[Math.Max(colliders, ColliderEnabled.Length * 2)];
        }
    }
}
