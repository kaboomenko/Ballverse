namespace Ballverse.Core.Sim
{
    /// <summary>Event kinds in their sort order within a tick.</summary>
    public enum SimEventKind
    {
        /// <summary>Ball vs collider impact. A = ball, B = collider.</summary>
        Hit = 0,
        /// <summary>Ball vs ball impact. A &lt; B are ball ids.</summary>
        BallHit = 1,
        /// <summary>Ball started touching a sensor. A = ball, B = collider.</summary>
        SensorEnter = 2,
        /// <summary>Ball stopped touching a sensor. A = ball, B = collider.</summary>
        SensorExit = 3,
    }

    /// <summary>
    /// One event of the last tick. Contacts are aggregated per (ball, collider) or (ball, ball) pair per tick: impulses summed,
    /// point / normal / angle taken from the strongest substep contact. Events are sorted by (kind, A, B) (spec §7.4 rule 4).
    /// </summary>
    public struct SimEvent
    {
        public SimEventKind Kind;
        public int Tick;
        public int A;
        public int B;
        /// <summary>Body of collider B for Hit / sensor events, −1 for BallHit.</summary>
        public int Body;
        /// <summary>World contact point.</summary>
        public Vec2 Point;
        /// <summary>Contact normal: Hit — from the collider towards ball A; BallHit — from ball A towards ball B; sensor events — zero.</summary>
        public Vec2 Normal;
        /// <summary>Total normal impulse over the tick, N·s (drives volume, spec §2.7).</summary>
        public float Impulse;
        /// <summary>Strongest approach speed along the normal, m/s.</summary>
        public float Speed;
        /// <summary>Ring: body-local angle of the contact; SpinPlatform: azimuth in the platform frame (gaps convention). Otherwise 0.</summary>
        public float LocalAngle;
        /// <summary>Polyline: segment index; Ring: solid arc index or −1 for a full ring. Otherwise 0.</summary>
        public int Feature;
    }

    /// <summary>Contact details passed to <see cref="ISimContactHook"/> before a response is applied.</summary>
    public struct ContactPoint
    {
        public Vec2 Point;
        /// <summary>Normal from the collider (or ball A) towards the ball (or ball B).</summary>
        public Vec2 Normal;
        /// <summary>Approach speed along the normal (positive = approaching), m/s.</summary>
        public float ApproachSpeed;
        public float LocalAngle;
        public int Feature;
    }

    /// <summary>
    /// Mode hook called inside the solver before each contact response (spec: "hook for mode logic"). Return false to let the
    /// ball pass through for the rest of this substep (one-way walls, deadly sectors handled as pass-through, ghost balls).
    /// Must be deterministic and must not add or remove bodies, balls or colliders.
    /// </summary>
    public interface ISimContactHook
    {
        bool PreSolveHit(PhysicsWorld world, int ball, int collider, in ContactPoint contact);

        bool PreSolveBallHit(PhysicsWorld world, int ballA, int ballB, in ContactPoint contact);
    }
}
