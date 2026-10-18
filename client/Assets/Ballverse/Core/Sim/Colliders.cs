using System;

namespace Ballverse.Core.Sim
{
    public enum ColliderKind
    {
        /// <summary>Line segment with thickness (a capsule): <see cref="ColliderInfo.A"/>–<see cref="ColliderInfo.B"/>.</summary>
        Segment = 0,
        /// <summary>Open or closed chain of capsule segments (polygon walls, arenas). Both faces collide.</summary>
        Polyline = 1,
        /// <summary>
        /// Analytic circular ring (centre, radius, thickness) with angular gaps in body-local angles. Collides on the inside
        /// and the outside; gap edges are round caps. Ring Escape rings, Geometry Bounce circle arena.
        /// </summary>
        Ring = 2,
        /// <summary>Solid disc (pegs, hubs). For sensors the whole disc area counts as inside.</summary>
        Circle = 3,
        /// <summary>
        /// Helix Drop platform: a horizontal disc of the tower seen edge-on from the front (spec §2.9 M05 side view), a flat slab
        /// along body-local X from −HalfWidth to +HalfWidth (HalfWidth = R_path) with the given thickness centred on the
        /// centre's Y (top = Y + T/2); the body's <see cref="PhysicsBodyState.Spin"/> rotates it about the vertical axis. A ball
        /// at local x sits at azimuth a = asin(x / HalfWidth) − Spin and covers [a − δ, a + δ], δ = asin(r / HalfWidth). When the
        /// ball reaches the top (or the bottom) the solver decides once: footprint strictly inside a gap — the ball passes and
        /// the pair is in transit (<see cref="PhysicsWorld.IsInTransit"/>, no contacts) until the ball leaves the slab's height
        /// band; otherwise a hit on the flat face. Hit events report a in <see cref="SimEvent.LocalAngle"/> (so rules can test
        /// deadly sectors against [a − δ, a + δ]) and the face in <see cref="SimEvent.Feature"/> (0 top, 1 bottom).
        /// </summary>
        SpinPlatform = 4,
    }

    /// <summary>Surface material and filtering of a collider.</summary>
    public struct ColliderMaterial
    {
        /// <summary>Combined with the ball's restitution by max().</summary>
        public float Restitution;
        /// <summary>Combined with the ball's friction by sqrt(a·b).</summary>
        public float Friction;
        public uint Category;
        public uint Mask;
        /// <summary>Mode-defined tag (ring index, wall kind…).</summary>
        public int Tag;
        /// <summary>Sensor: no collision response, only SensorEnter/SensorExit events.</summary>
        public bool IsSensor;

        public static ColliderMaterial Default => new ColliderMaterial
        {
            Restitution = 0f,
            Friction = 1f,
            Category = 1u,
            Mask = uint.MaxValue,
        };

        public static ColliderMaterial Sensor(int tag = 0)
        {
            var m = Default;
            m.IsSensor = true;
            m.Tag = tag;
            return m;
        }
    }

    /// <summary>
    /// Angular gap of a ring or spin platform in radians, counter-clockwise from <see cref="Start"/> to <see cref="End"/>
    /// (0 = local +X; may wrap through 0). Same convention as the render stream's RingGap, in float radians.
    /// </summary>
    public struct AngularGap
    {
        public float Start;
        public float End;

        public AngularGap(float start, float end)
        {
            Start = start;
            End = end;
        }

        /// <summary>Gap of a width centred on an angle.</summary>
        public static AngularGap Centered(float center, float width)
        {
            return new AngularGap(center - width * 0.5f, center + width * 0.5f);
        }

        /// <summary>Counter-clockwise extent in [0, 2π).</summary>
        public float Span => SimMath.Wrap2Pi(End - Start);
    }

    /// <summary>Read-only description of a collider (for rendering adapters and mode logic).</summary>
    public struct ColliderInfo
    {
        public int Id;
        public ColliderKind Kind;
        public int Body;
        public bool Enabled;
        public ColliderMaterial Material;
        /// <summary>Segment start; ring / circle / platform centre (body-local, unscaled).</summary>
        public Vec2 A;
        /// <summary>Segment end.</summary>
        public Vec2 B;
        /// <summary>Ring centre-line radius, circle radius or platform half-width (body-local, unscaled).</summary>
        public float Radius;
        /// <summary>Full wall thickness in metres (not scaled by the body).</summary>
        public float Thickness;
        public bool Closed;
        public int VertexCount;
        public int GapCount;
    }
}
