using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Tests.Sim
{
    internal static class SimTestUtil
    {
        public static SimSettings NoGravity(int substeps = SimSettings.DefaultSubsteps, float maxSpeed = 30f)
        {
            var s = SimSettings.Default;
            s.Gravity = Vec2.Zero;
            s.Substeps = substeps;
            s.MaxSpeed = maxSpeed;
            return s;
        }

        public static ColliderMaterial Wall(float restitution = 0f, float friction = 1f)
        {
            var m = ColliderMaterial.Default;
            m.Restitution = restitution;
            m.Friction = friction;
            return m;
        }

        /// <summary>Kinematic regular polygon (closed polyline) centred at the origin. Returns (body, collider).</summary>
        public static (int body, int collider) AddPolygonArena(PhysicsWorld w, int sides, float circumradius, float thickness, float angularVelocity,
            float restitution = 0f, float friction = 1f)
        {
            int body = w.AddBody(BodyDef.Kinematic(Vec2.Zero, 0f, angularVelocity));
            int col = w.AddPolyline(body, ShapeBuilder.RegularPolygon(sides, circumradius), true, thickness, Wall(restitution, friction));
            return (body, col);
        }

        public static BallDef Ball(Vec2 p, Vec2 v, float radius, float restitution = 1f, float friction = 0f)
        {
            var d = BallDef.Create(p, radius);
            d.Velocity = v;
            d.Restitution = restitution;
            d.Friction = friction;
            return d;
        }

        /// <summary>
        /// 500 balls (r = 0.1) in a rotating hexagon (R = 4, 90°/s) under gravity — the performance / determinism reference scene
        /// (Geometry Bounce "new ball" variant at its tier A budget order).
        /// </summary>
        public static PhysicsWorld HexagonScene(int balls = 500, ulong seed = 42UL, int substeps = SimSettings.DefaultSubsteps)
        {
            var s = SimSettings.Default;
            s.Substeps = substeps;
            var w = new PhysicsWorld(s);
            AddPolygonArena(w, 6, 4f, 0.1f, SimMath.HalfPi, 0.5f, 0.5f);
            w.ReserveBalls(balls);
            var rng = Rng.FromSeed(seed, 7);
            float inr = ShapeBuilder.Inradius(6, 4f) - 0.3f;
            for (int i = 0; i < balls; i++)
            {
                // Rejection-sample inside the inscribed circle.
                Vec2 p;
                do
                {
                    p = new Vec2(rng.NextFloat(-inr, inr), rng.NextFloat(-inr, inr));
                } while (p.LengthSquared > inr * inr);
                var d = BallDef.Create(p, 0.1f);
                d.Velocity = new Vec2(rng.NextFloat(-5f, 5f), rng.NextFloat(-5f, 5f));
                d.Restitution = 0.9f;
                d.Friction = 0.2f;
                d.Tag = i % 4;
                w.AddBall(d);
            }
            return w;
        }

        public static bool InsideConvexPolygon(Vec2 localPoint, int sides, float circumradius, float tolerance)
        {
            float inr = ShapeBuilder.Inradius(sides, circumradius);
            for (int i = 0; i < sides; i++)
            {
                // Edge i joins vertices i and i+1; its outward normal points at the mid-angle.
                float a = SimMath.TwoPi * (i + 0.5f) / sides;
                Vec2 n = Vec2.FromAngle(a);
                if (Vec2.Dot(localPoint, n) > inr + tolerance) return false;
            }
            return true;
        }

        public static Vec2 ToLocal(PhysicsWorld w, int body, Vec2 world)
        {
            ref readonly PhysicsBodyState b = ref w.GetBody(body);
            return Vec2.InvRotate(world - b.Position, MathF.Cos(b.Angle), MathF.Sin(b.Angle));
        }

        public static List<SimEvent> CopyEvents(PhysicsWorld w)
        {
            var list = new List<SimEvent>();
            foreach (var e in w.Events) list.Add(e);
            return list;
        }
    }

    /// <summary>Configurable rules for runner tests.</summary>
    internal sealed class ScriptedRules : IModeRules
    {
        public SimPhase PhaseValue = SimPhase.Running;
        public SimResult ResultValue = SimResult.None;
        public int StartCalls;
        public int TickCalls;
        public int AfterCalls;
        public int TimeLimitCalls;
        public int TimeLimitAtTick = -1;
        public readonly List<(int tick, SimInput input)> Applied = new List<(int, SimInput)>();
        public Action<SimRunner, int> OnTick;
        public Action<SimRunner> OnAfter;
        public bool RecordInputs = true;
        public long Counter;

        public void Start(SimRunner runner)
        {
            StartCalls++;
        }

        public void Tick(SimRunner runner, ReadOnlySpan<SimInput> inputs)
        {
            TickCalls++;
            if (RecordInputs)
            {
                for (int i = 0; i < inputs.Length; i++) Applied.Add((runner.Tick, inputs[i]));
            }
            else
            {
                for (int i = 0; i < inputs.Length; i++) Counter += inputs[i].A;
            }
            OnTick?.Invoke(runner, inputs.Length);
        }

        public void AfterPhysics(SimRunner runner)
        {
            AfterCalls++;
            OnAfter?.Invoke(runner);
        }

        public void OnTimeLimit(SimRunner runner)
        {
            TimeLimitCalls++;
            TimeLimitAtTick = runner.Tick;
            PhaseValue = SimPhase.Finale;
            ResultValue = new SimResult { Outcome = SimOutcome.TimeLimit, Winner = -1, Tick = runner.Tick };
        }

        public SimPhase Phase => PhaseValue;

        public SimResult Result => ResultValue;

        public void AppendStateHash(XxHash64 hash)
        {
            hash.AppendULong((ulong)Counter);
            hash.AppendInt((int)PhaseValue);
        }
    }
}
