using System;

namespace Ballverse.Core.Sim
{
    /// <summary>
    /// Deterministic 2D world for the release-1 modes (Ring Escape, Geometry Bounce, Helix Drop): dynamic circles ("balls")
    /// against colliders carried by static or kinematic bodies, plus ball–ball contacts.
    /// <para>
    /// One <see cref="Step"/> is one tick of 1/60 s (<see cref="SimConstants.TickRate"/>) split into
    /// <see cref="SimSettings.Substeps"/> substeps. Single-threaded, float math in a fixed order, stable id order everywhere and
    /// no allocations in steady state, so the same inputs give the same <see cref="ComputeStateHash"/> on the same binary
    /// (spec §7.4). Ids of balls, bodies and colliders are dense indices in creation order and are never reused.
    /// </para>
    /// <para>
    /// Not thread-safe; independent worlds may run on different threads (headless generation candidates, spec §7.11).
    /// </para>
    /// </summary>
    public sealed partial class PhysicsWorld
    {
        public const int MaxBalls = 1 << 20;
        public const int MaxColliders = 1 << 20;
        public const int MaxBodies = 1 << 20;
        /// <summary>Events kept per tick; more are dropped and counted in <see cref="DroppedEvents"/>.</summary>
        public const int MaxEventsPerTick = 1 << 20;

        /// <summary>Duration of one tick in seconds.</summary>
        public const float TickDuration = 1f / SimConstants.TickRate;

        SimSettings _settings;

        // Balls.
        BallState[] _balls = new BallState[16];
        Vec2[] _ballPrev = new Vec2[16];
        int[] _ballEventHead = new int[16];
        int _ballCount;
        int _aliveBalls;

        // Bodies.
        BodyState[] _bodies = new BodyState[4];
        BodyFrame[] _frames = new BodyFrame[4];
        int _bodyCount;

        // Colliders and their geometry pools.
        ColliderData[] _colliders = new ColliderData[8];
        ColliderBounds[] _colliderBounds = new ColliderBounds[8];
        int _colliderCount;
        int _sensorCount;
        Vec2[] _vertices = new Vec2[16];
        int _vertexCount;
        float[] _arcStart = new float[8];
        float[] _arcSpan = new float[8];
        Vec2[] _arcStartDir = new Vec2[8];
        Vec2[] _arcEndDir = new Vec2[8];
        int _arcCount;
        float[] _gapStart = new float[8];
        float[] _gapSpan = new float[8];
        int _gapPoolCount;
        float[] _pieces = new float[8];

        int _tick;

        /// <summary>Gravity, substeps, speed clamp and contact thresholds (validated on set).</summary>
        public SimSettings Settings
        {
            get => _settings;
            set
            {
                CheckNotStepping();
                value.Validate();
                _settings = value;
            }
        }

        /// <summary>Optional mode hook called before each contact response.</summary>
        public ISimContactHook ContactHook { get; set; }

        /// <summary>Number of completed ticks.</summary>
        public int Tick => _tick;

        public int BallCount => _ballCount;

        public int AliveBallCount => _aliveBalls;

        public int BodyCount => _bodyCount;

        public int ColliderCount => _colliderCount;

        public PhysicsWorld() : this(SimSettings.Default)
        {
        }

        public PhysicsWorld(SimSettings settings)
        {
            settings.Validate();
            _settings = settings;
        }

        // ------------------------------------------------------------------ balls

        /// <summary>Pre-sizes ball storage so adding up to <paramref name="capacity"/> balls later does not allocate.</summary>
        public void ReserveBalls(int capacity)
        {
            if (capacity < 0 || capacity > MaxBalls) throw new ArgumentOutOfRangeException(nameof(capacity));
            CheckNotStepping();
            EnsureBallCapacity(capacity);
        }

        /// <summary>Adds a ball and returns its id.</summary>
        public int AddBall(in BallDef def)
        {
            CheckNotStepping();
            if (_ballCount >= MaxBalls) throw new InvalidOperationException("Ball limit " + MaxBalls + " reached");
            if (!SimMath.IsFinite(def.Position) || !SimMath.IsFinite(def.Velocity)) throw new ArgumentException("Ball position and velocity must be finite");
            if (!SimMath.IsFinite(def.Angle) || !SimMath.IsFinite(def.AngularVelocity)) throw new ArgumentException("Ball angle and angular velocity must be finite");
            ValidateRadius(def.Radius);
            if (!SimMath.IsFinite(def.Mass) || def.Mass < 0f) throw new ArgumentException("Ball mass must be >= 0 (0 = from density)");
            ValidateMaterial(def.Restitution, def.Friction);
            if (!SimMath.IsFinite(def.GravityScale)) throw new ArgumentException("GravityScale must be finite");

            EnsureBallCapacity(_ballCount + 1);
            int id = _ballCount++;
            ref BallState b = ref _balls[id];
            b = default;
            b.Position = def.Position;
            b.Velocity = def.Velocity;
            b.Angle = def.Angle;
            b.AngularVelocity = def.AngularVelocity;
            b.Restitution = def.Restitution;
            b.Friction = def.Friction;
            b.GravityScale = def.GravityScale;
            b.Tag = def.Tag;
            b.Category = def.Category;
            b.Mask = def.Mask;
            b.Flags = def.Flags;
            b.Alive = true;
            b.Radius = def.Radius;
            SetMass(ref b, def.Mass > 0f ? def.Mass : SimMath.Pi * def.Radius * def.Radius);
            _ballPrev[id] = def.Position;
            _ballEventHead[id] = -1;
            _aliveBalls++;
            _radiusVersion++;
            return id;
        }

        /// <summary>Current state of a ball (by reference, read-only).</summary>
        public ref readonly BallState GetBall(int id)
        {
            CheckBall(id);
            return ref _balls[id];
        }

        /// <summary>Teleports a ball (no sweep from the old position).</summary>
        public void SetBallPosition(int id, Vec2 position)
        {
            CheckBall(id);
            if (!SimMath.IsFinite(position)) throw new ArgumentException("Position must be finite");
            _balls[id].Position = position;
            _ballPrev[id] = position;
        }

        public void SetBallVelocity(int id, Vec2 velocity)
        {
            CheckBall(id);
            if (!SimMath.IsFinite(velocity)) throw new ArgumentException("Velocity must be finite");
            _balls[id].Velocity = velocity;
        }

        public void SetBallAngularVelocity(int id, float angularVelocity)
        {
            CheckBall(id);
            if (!SimMath.IsFinite(angularVelocity)) throw new ArgumentException("Angular velocity must be finite");
            _balls[id].AngularVelocity = angularVelocity;
        }

        /// <summary>Changes the radius (growth variants). Mass follows the area when <paramref name="scaleMass"/> is true.</summary>
        public void SetBallRadius(int id, float radius, bool scaleMass = true)
        {
            CheckBall(id);
            ValidateRadius(radius);
            ref BallState b = ref _balls[id];
            float mass = scaleMass ? b.Mass * (radius * radius) / (b.Radius * b.Radius) : b.Mass;
            b.Radius = radius;
            SetMass(ref b, mass);
            _radiusVersion++;
        }

        public void SetBallMaterial(int id, float restitution, float friction)
        {
            CheckBall(id);
            ValidateMaterial(restitution, friction);
            _balls[id].Restitution = restitution;
            _balls[id].Friction = friction;
        }

        public void SetBallFilter(int id, uint category, uint mask)
        {
            CheckBall(id);
            _balls[id].Category = category;
            _balls[id].Mask = mask;
            _radiusVersion++;
        }

        public void SetBallGravityScale(int id, float gravityScale)
        {
            CheckBall(id);
            if (!SimMath.IsFinite(gravityScale)) throw new ArgumentException("GravityScale must be finite");
            _balls[id].GravityScale = gravityScale;
        }

        public void SetBallFlags(int id, BallFlags flags)
        {
            CheckBall(id);
            _balls[id].Flags = flags;
        }

        public void SetBallTag(int id, int tag)
        {
            CheckBall(id);
            _balls[id].Tag = tag;
        }

        /// <summary>
        /// Removes a ball from the simulation. Its id stays reserved; state is kept for inspection. Allowed inside a contact
        /// hook (deadly sector): the ball's remaining contacts of that step are skipped and produce no events.
        /// </summary>
        public void KillBall(int id)
        {
            CheckBall(id);
            if (!_balls[id].Alive) return;
            _balls[id].Alive = false;
            _aliveBalls--;
            _radiusVersion++;
        }

        // ------------------------------------------------------------------ bodies

        public int AddBody(in BodyDef def)
        {
            CheckNotStepping();
            if (_bodyCount >= MaxBodies) throw new InvalidOperationException("Body limit reached");
            if (!SimMath.IsFinite(def.Position) || !SimMath.IsFinite(def.LinearVelocity)) throw new ArgumentException("Body position and velocity must be finite");
            if (!SimMath.IsFinite(def.Angle) || !SimMath.IsFinite(def.AngularVelocity)) throw new ArgumentException("Body angle and angular velocity must be finite");
            if (!SimMath.IsFinite(def.Spin) || !SimMath.IsFinite(def.SpinVelocity)) throw new ArgumentException("Body spin must be finite");
            if (!SimMath.IsFinite(def.Scale) || def.Scale < 0f) throw new ArgumentException("Body scale must be >= 0 (0 = 1)");
            if (!SimMath.IsFinite(def.ScaleVelocity)) throw new ArgumentException("Body scale velocity must be finite");
            if (_bodyCount == _bodies.Length)
            {
                Array.Resize(ref _bodies, _bodies.Length * 2);
                Array.Resize(ref _frames, _frames.Length * 2);
            }
            int id = _bodyCount++;
            bool kinematic = def.Motion == BodyMotion.Kinematic;
            _bodies[id] = new BodyState
            {
                Motion = def.Motion,
                Position = def.Position,
                Angle = def.Angle,
                LinearVelocity = kinematic ? def.LinearVelocity : Vec2.Zero,
                AngularVelocity = kinematic ? def.AngularVelocity : 0f,
                Scale = def.Scale > 0f ? def.Scale : 1f,
                ScaleVelocity = kinematic ? def.ScaleVelocity : 0f,
                Spin = def.Spin,
                SpinVelocity = kinematic ? def.SpinVelocity : 0f,
                Tag = def.Tag,
                Enabled = true,
            };
            _frames[id] = default;
            RefreshFrame(id);
            return id;
        }

        public ref readonly BodyState GetBody(int id)
        {
            CheckBody(id);
            return ref _bodies[id];
        }

        /// <summary>Sets the velocities of a kinematic body (ignored for static bodies).</summary>
        public void SetBodyVelocity(int id, Vec2 linear, float angular)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(linear) || !SimMath.IsFinite(angular)) throw new ArgumentException("Velocities must be finite");
            ref BodyState b = ref _bodies[id];
            if (b.Motion != BodyMotion.Kinematic) return;
            b.LinearVelocity = linear;
            b.AngularVelocity = angular;
            _frames[id].HasTarget = false;
        }

        public void SetBodyAngularVelocity(int id, float angular)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(angular)) throw new ArgumentException("Angular velocity must be finite");
            if (_bodies[id].Motion != BodyMotion.Kinematic) return;
            _bodies[id].AngularVelocity = angular;
            _frames[id].HasTarget = false;
        }

        /// <summary>Spin (tower azimuth) rate of a kinematic body, rad/s — Helix Drop swipe input.</summary>
        public void SetBodySpinVelocity(int id, float spinVelocity)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(spinVelocity)) throw new ArgumentException("Spin velocity must be finite");
            if (_bodies[id].Motion != BodyMotion.Kinematic) return;
            _bodies[id].SpinVelocity = spinVelocity;
        }

        /// <summary>Uniform scale rate of a kinematic body (shrinking walls), 1/s.</summary>
        public void SetBodyScaleVelocity(int id, float scaleVelocity)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(scaleVelocity)) throw new ArgumentException("Scale velocity must be finite");
            if (_bodies[id].Motion != BodyMotion.Kinematic) return;
            _bodies[id].ScaleVelocity = scaleVelocity;
        }

        /// <summary>
        /// Scripted motion: the kinematic body moves to the target pose over the next tick with the matching velocities
        /// (so it pushes balls correctly) and lands exactly on it; afterwards its linear and angular velocities are zero
        /// unless set again. The angle takes the shortest way.
        /// </summary>
        public void MoveBodyTo(int id, Vec2 position, float angle)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(position) || !SimMath.IsFinite(angle)) throw new ArgumentException("Target pose must be finite");
            if (_bodies[id].Motion != BodyMotion.Kinematic) return;
            ref BodyFrame f = ref _frames[id];
            f.HasTarget = true;
            f.TargetPosition = position;
            f.TargetAngle = angle;
        }

        /// <summary>Teleports a body (no velocity, balls are not pushed by the jump).</summary>
        public void SetBodyPose(int id, Vec2 position, float angle)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(position) || !SimMath.IsFinite(angle)) throw new ArgumentException("Pose must be finite");
            _bodies[id].Position = position;
            _bodies[id].Angle = angle;
            _frames[id].HasTarget = false;
            RefreshFrame(id);
        }

        public void SetBodySpin(int id, float spin)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(spin)) throw new ArgumentException("Spin must be finite");
            _bodies[id].Spin = spin;
            RefreshFrame(id);
        }

        public void SetBodyScale(int id, float scale)
        {
            CheckBody(id);
            if (!SimMath.IsFinite(scale) || scale <= 0f) throw new ArgumentException("Scale must be > 0");
            _bodies[id].Scale = scale;
            RefreshFrame(id);
        }

        /// <summary>Disables or enables a body with all its colliders.</summary>
        public void SetBodyEnabled(int id, bool enabled)
        {
            CheckBody(id);
            _bodies[id].Enabled = enabled;
        }

        // ------------------------------------------------------------------ colliders

        /// <summary>Capsule from <paramref name="a"/> to <paramref name="b"/> (body-local) with a full thickness.</summary>
        public int AddSegment(int body, Vec2 a, Vec2 b, float thickness, in ColliderMaterial material)
        {
            CheckBody(body);
            if (!SimMath.IsFinite(a) || !SimMath.IsFinite(b)) throw new ArgumentException("Segment points must be finite");
            ValidateThickness(thickness);
            var c = NewCollider(ColliderKind.Segment, body, thickness, material);
            c.A = a;
            c.B = b;
            Vec2 mid = (a + b) * 0.5f;
            return Register(ref c, mid, (b - a).Length * 0.5f);
        }

        /// <summary>
        /// Chain of capsules through <paramref name="points"/> (body-local). <paramref name="closed"/> adds the last→first edge
        /// (polygon walls; at least 3 points). Both faces collide; events report the segment index in <see cref="SimEvent.Feature"/>.
        /// </summary>
        public int AddPolyline(int body, ReadOnlySpan<Vec2> points, bool closed, float thickness, in ColliderMaterial material)
        {
            CheckBody(body);
            ValidateThickness(thickness);
            if (points.Length < (closed ? 3 : 2)) throw new ArgumentException(closed ? "A closed polyline needs at least 3 points" : "A polyline needs at least 2 points");
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                Vec2 p = points[i];
                if (!SimMath.IsFinite(p)) throw new ArgumentException("Polyline points must be finite");
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            var c = NewCollider(ColliderKind.Polyline, body, thickness, material);
            c.Closed = closed;
            c.VertexStart = _vertexCount;
            c.VertexCount = points.Length;
            if (_vertexCount + points.Length > _vertices.Length) Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + points.Length));
            for (int i = 0; i < points.Length; i++) _vertices[_vertexCount++] = points[i];
            Vec2 centre = new Vec2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            float r = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                float d = (points[i] - centre).Length;
                if (d > r) r = d;
            }
            return Register(ref c, centre, r);
        }

        /// <summary>
        /// Analytic ring: centre-line <paramref name="radius"/>, full <paramref name="thickness"/>, optional gaps in body-local
        /// angles. Gaps may overlap or wrap; they must not cover the whole circle.
        /// </summary>
        public int AddRing(int body, Vec2 centre, float radius, float thickness, ReadOnlySpan<AngularGap> gaps, in ColliderMaterial material)
        {
            CheckBody(body);
            if (!SimMath.IsFinite(centre)) throw new ArgumentException("Ring centre must be finite");
            if (!SimMath.IsFinite(radius) || radius <= 0f) throw new ArgumentException("Ring radius must be > 0");
            ValidateThickness(thickness);
            var c = NewCollider(ColliderKind.Ring, body, thickness, material);
            c.A = centre;
            c.Radius = radius;
            BuildArcs(ref c, gaps);
            return Register(ref c, centre, radius);
        }

        /// <summary>Solid disc (body-local centre).</summary>
        public int AddCircle(int body, Vec2 centre, float radius, in ColliderMaterial material)
        {
            CheckBody(body);
            if (!SimMath.IsFinite(centre)) throw new ArgumentException("Circle centre must be finite");
            if (!SimMath.IsFinite(radius) || radius <= 0f) throw new ArgumentException("Circle radius must be > 0");
            var c = NewCollider(ColliderKind.Circle, body, 0f, material);
            c.A = centre;
            c.Radius = radius;
            return Register(ref c, centre, radius);
        }

        /// <summary>
        /// Helix Drop platform seen edge-on (see <see cref="ColliderKind.SpinPlatform"/>): centred at <paramref name="centre"/>,
        /// spanning ±<paramref name="halfWidth"/> along body-local X, gaps in platform azimuth (0 = facing the camera when Spin = 0).
        /// </summary>
        public int AddSpinPlatform(int body, Vec2 centre, float halfWidth, float thickness, ReadOnlySpan<AngularGap> gaps, in ColliderMaterial material)
        {
            CheckBody(body);
            if (!SimMath.IsFinite(centre)) throw new ArgumentException("Platform centre must be finite");
            if (!SimMath.IsFinite(halfWidth) || halfWidth <= 0f) throw new ArgumentException("Platform half-width must be > 0");
            ValidateThickness(thickness);
            var c = NewCollider(ColliderKind.SpinPlatform, body, thickness, material);
            c.A = centre;
            c.Radius = halfWidth;
            BuildArcs(ref c, gaps);
            return Register(ref c, centre, halfWidth);
        }

        public void SetColliderEnabled(int id, bool enabled)
        {
            CheckCollider(id);
            _colliders[id].Enabled = enabled;
        }

        public bool IsColliderEnabled(int id)
        {
            CheckCollider(id);
            return _colliders[id].Enabled;
        }

        public void SetColliderMaterial(int id, float restitution, float friction)
        {
            CheckCollider(id);
            ValidateMaterial(restitution, friction);
            _colliders[id].Material.Restitution = restitution;
            _colliders[id].Material.Friction = friction;
        }

        public ColliderInfo GetColliderInfo(int id)
        {
            CheckCollider(id);
            ref ColliderData c = ref _colliders[id];
            return new ColliderInfo
            {
                Id = id,
                Kind = c.Kind,
                Body = c.Body,
                Enabled = c.Enabled,
                Material = c.Material,
                A = c.A,
                B = c.B,
                Radius = c.Radius,
                Thickness = c.HalfThickness * 2f,
                Closed = c.Closed,
                VertexCount = c.VertexCount,
                GapCount = c.GapCount,
            };
        }

        public Vec2 GetPolylineVertex(int id, int index)
        {
            CheckCollider(id);
            ref ColliderData c = ref _colliders[id];
            if ((uint)index >= (uint)c.VertexCount) throw new ArgumentOutOfRangeException(nameof(index));
            return _vertices[c.VertexStart + index];
        }

        /// <summary>Gap <paramref name="index"/> of a ring / platform after normalisation (merged, sorted by start in [0, 2π)).</summary>
        public AngularGap GetGap(int id, int index)
        {
            CheckCollider(id);
            ref ColliderData c = ref _colliders[id];
            if ((uint)index >= (uint)c.GapCount) throw new ArgumentOutOfRangeException(nameof(index));
            float s = _gapStart[c.GapStart + index];
            return new AngularGap(s, s + _gapSpan[c.GapStart + index]);
        }

        // ------------------------------------------------------------------ internals

        struct BodyFrame
        {
            public float Cos, Sin;
            public Vec2 PrevPosition;
            public float PrevCos, PrevSin, PrevScale;
            public bool HasTarget;
            public Vec2 TargetPosition;
            public float TargetAngle;
        }

        struct ColliderData
        {
            public ColliderKind Kind;
            public int Body;
            public bool Enabled;
            public ColliderMaterial Material;
            public Vec2 A, B;
            public float Radius;
            public float HalfThickness;
            public bool Closed;
            public int VertexStart, VertexCount;
            public int GapStart, GapCount;
            public int ArcStart, ArcCount;
            /// <summary>Ring / platform without gaps.</summary>
            public bool Full;
            /// <summary>Body-local bounding circle of the centre-line geometry (scaled with the body; thickness added separately).</summary>
            public Vec2 BoundCentre;
            public float BoundRadius;
        }

        /// <summary>Per-substep world-space bounds used for the cheap ball reject.</summary>
        struct ColliderBounds
        {
            public Vec2 Centre;
            public float Outer;
            /// <summary>Rings: radius of the empty disc inside the wall (−1 otherwise).</summary>
            public float Inner;
            public bool Active;
        }

        ColliderData NewCollider(ColliderKind kind, int body, float thickness, in ColliderMaterial m)
        {
            CheckNotStepping();
            ValidateMaterial(m.Restitution, m.Friction);
            if (_colliderCount >= MaxColliders) throw new InvalidOperationException("Collider limit reached");
            return new ColliderData { Kind = kind, Body = body, Enabled = true, Material = m, HalfThickness = thickness * 0.5f };
        }

        int Register(ref ColliderData c, Vec2 boundCentre, float boundRadius)
        {
            c.BoundCentre = boundCentre;
            c.BoundRadius = boundRadius;
            if (_colliderCount == _colliders.Length)
            {
                Array.Resize(ref _colliders, _colliders.Length * 2);
                Array.Resize(ref _colliderBounds, _colliderBounds.Length * 2);
            }
            int id = _colliderCount++;
            _colliders[id] = c;
            if (c.Material.IsSensor) _sensorCount++;
            int pieces = (c.ArcCount + 1) * 4;
            if (_pieces.Length < pieces) _pieces = new float[pieces];
            return id;
        }

        /// <summary>Normalises gaps (wrap, sort, merge) and stores them plus the complementary solid arcs.</summary>
        void BuildArcs(ref ColliderData c, ReadOnlySpan<AngularGap> gaps)
        {
            int n = gaps.Length;
            c.GapStart = _gapPoolCount;
            c.ArcStart = _arcCount;
            if (n == 0)
            {
                c.Full = true;
                return;
            }
            var s = new float[n];
            var e = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!SimMath.IsFinite(gaps[i].Start) || !SimMath.IsFinite(gaps[i].End)) throw new ArgumentException("Gap angles must be finite");
                float span = gaps[i].Span;
                if (!(span > 1e-6f)) throw new ArgumentException("Gap " + i + " has no width (start == end is ambiguous)");
                s[i] = SimMath.Wrap2Pi(gaps[i].Start);
                e[i] = s[i] + span;
            }
            // Insertion sort by start (setup time only).
            for (int i = 1; i < n; i++)
            {
                float vs = s[i], ve = e[i];
                int j = i - 1;
                while (j >= 0 && s[j] > vs)
                {
                    s[j + 1] = s[j];
                    e[j + 1] = e[j];
                    j--;
                }
                s[j + 1] = vs;
                e[j + 1] = ve;
            }
            int m = 0;
            for (int i = 1; i < n; i++)
            {
                if (s[i] <= e[m])
                {
                    if (e[i] > e[m]) e[m] = e[i];
                }
                else
                {
                    m++;
                    s[m] = s[i];
                    e[m] = e[i];
                }
            }
            m++;
            // Merge gaps that wrap past 2π into the first ones.
            while (m > 1 && e[m - 1] - SimMath.TwoPi >= s[0])
            {
                float wrappedEnd = e[0] + SimMath.TwoPi;
                if (wrappedEnd > e[m - 1]) e[m - 1] = wrappedEnd;
                for (int i = 1; i < m; i++)
                {
                    s[i - 1] = s[i];
                    e[i - 1] = e[i];
                }
                m--;
            }
            for (int i = 0; i < m; i++)
            {
                if (e[i] - s[i] >= SimMath.TwoPi - 1e-6f) throw new ArgumentException("Gaps cover the whole circle");
            }
            EnsureArcCapacity(m);
            for (int i = 0; i < m; i++)
            {
                _gapStart[_gapPoolCount] = s[i];
                _gapSpan[_gapPoolCount] = e[i] - s[i];
                _gapPoolCount++;
            }
            c.GapCount = m;
            for (int i = 0; i < m; i++)
            {
                float next = i + 1 < m ? s[i + 1] : s[0] + SimMath.TwoPi;
                float span = next - e[i];
                if (!(span > 1e-6f)) throw new ArgumentException("Gaps leave a solid arc of zero width");
                _arcStart[_arcCount] = SimMath.Wrap2Pi(e[i]);
                _arcSpan[_arcCount] = span;
                _arcStartDir[_arcCount] = Vec2.FromAngle(e[i]);
                _arcEndDir[_arcCount] = Vec2.FromAngle(next);
                _arcCount++;
            }
            c.ArcCount = m;
        }

        void EnsureArcCapacity(int extra)
        {
            if (_arcCount + extra > _arcStart.Length)
            {
                int size = Math.Max(_arcStart.Length * 2, _arcCount + extra);
                Array.Resize(ref _arcStart, size);
                Array.Resize(ref _arcSpan, size);
                Array.Resize(ref _arcStartDir, size);
                Array.Resize(ref _arcEndDir, size);
            }
            if (_gapPoolCount + extra > _gapStart.Length)
            {
                int size = Math.Max(_gapStart.Length * 2, _gapPoolCount + extra);
                Array.Resize(ref _gapStart, size);
                Array.Resize(ref _gapSpan, size);
            }
        }

        void EnsureBallCapacity(int capacity)
        {
            if (capacity <= _balls.Length) return;
            int size = Math.Max(capacity, _balls.Length * 2);
            Array.Resize(ref _balls, size);
            Array.Resize(ref _ballPrev, size);
            Array.Resize(ref _ballEventHead, size);
            ResizeBallScratch(size);
        }

        void RefreshFrame(int id)
        {
            ref BodyState b = ref _bodies[id];
            ref BodyFrame f = ref _frames[id];
            SimMath.SinCos(b.Angle, out f.Sin, out f.Cos);
            f.PrevCos = f.Cos;
            f.PrevSin = f.Sin;
            f.PrevPosition = b.Position;
            f.PrevScale = b.Scale;
        }

        static void SetMass(ref BallState b, float mass)
        {
            if (!(mass > 0f) || !SimMath.IsFinite(mass)) throw new ArgumentException("Ball mass must be > 0");
            b.Mass = mass;
            b.InvMass = 1f / mass;
            // Solid disc: I = m r² / 2.
            b.InvInertia = 2f / (mass * b.Radius * b.Radius);
        }

        static void ValidateRadius(float r)
        {
            if (!SimMath.IsFinite(r) || r <= 0f) throw new ArgumentException("Radius must be > 0");
        }

        static void ValidateThickness(float t)
        {
            if (!SimMath.IsFinite(t) || t < 0f) throw new ArgumentException("Thickness must be >= 0");
        }

        static void ValidateMaterial(float restitution, float friction)
        {
            if (!SimMath.IsFinite(restitution) || restitution < 0f || restitution > 2f) throw new ArgumentException("Restitution must be in [0, 2]");
            if (!SimMath.IsFinite(friction) || friction < 0f) throw new ArgumentException("Friction must be >= 0");
        }

        /// <summary>Structural changes inside a contact hook would invalidate the solver's buffers mid-step.</summary>
        void CheckNotStepping()
        {
            if (_stepping) throw new InvalidOperationException("Balls, bodies, colliders and settings cannot be added or changed while Step() runs (contact hook); do it in IModeRules.Tick / AfterPhysics");
        }

        void CheckBall(int id)
        {
            if ((uint)id >= (uint)_ballCount) throw new ArgumentOutOfRangeException(nameof(id), "Unknown ball " + id);
        }

        void CheckBody(int id)
        {
            if ((uint)id >= (uint)_bodyCount) throw new ArgumentOutOfRangeException(nameof(id), "Unknown body " + id);
        }

        void CheckCollider(int id)
        {
            if ((uint)id >= (uint)_colliderCount) throw new ArgumentOutOfRangeException(nameof(id), "Unknown collider " + id);
        }
    }
}
