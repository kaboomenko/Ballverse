using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Tests.Modes
{
    /// <summary>
    /// Minimal test mode exercising every framework path independently of M01: one ball in a static box, a live parameter
    /// ("kick" strength) and a player input (type 1 = kick by (A, B) mm/s scaled by the live parameter); Finale after
    /// <c>lifetime</c> ticks, Finished 30 ticks later. Uses the M05 catalog entry (an interactive mode) for its info.
    /// </summary>
    public sealed class FakeBoxMode : IModeDefinition
    {
        public const string Kick = "kick";
        public const string Lifetime = "lifetime";
        public const byte KickInput = 1;

        public static readonly FakeBoxMode Instance = new FakeBoxMode();

        static readonly ParamSchema s_schema = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M05)), new ParamSchemaBuilder()
            .Add(new FloatParam(Kick, 0.0, 4.0, 0.1, 1.0).AsLive())
            .Add(new IntParam(Lifetime, 60, 3000, 1, 600))
            .BuildLayer());

        public ModeInfo Info => ModeCatalog.Get(ModeCode.M05);
        public ushort SimVersion => 7;
        public ParamSchema Schema => s_schema;
        public IReadOnlyList<Preset> BuiltInPresets => Array.Empty<Preset>();
        public ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues) => sanitized;

        public bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max)
        {
            if (paramId == Kick)
            {
                // Pretend rule: kick ≥ 0.5 (exercises the session's live clamp).
                min = 0.5;
                max = 4.0;
                return true;
            }
            min = max = 0;
            return false;
        }

        public int GetParticipantCount(ParamSet values) => 0;

        public ParamSet GenerateCandidate(in ModeCandidateRequest request)
        {
            var rng = Rng.FromSeed(request.Seed, 5);
            return request.BaseValues.With(Lifetime, ParamValue.Int(rng.NextInt(300, 900)));
        }

        public IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context) => new Scene(world, context);

        sealed class Scene : IModeScene
        {
            readonly PhysicsWorld _world;
            readonly ModeSceneContext _ctx;
            readonly int _lifetime;
            readonly int _boxShape;
            readonly int _ballShape;
            readonly ModeMetrics _metrics = new ModeMetrics(0);
            float _kick;
            SimPhase _phase = SimPhase.Running;
            int _finaleEnd = -1;
            int _hits;
            public readonly RenderQuantization Q = RenderQuantization.Default;

            public Scene(PhysicsWorld world, ModeSceneContext ctx)
            {
                _world = world;
                _ctx = ctx;
                _lifetime = ctx.Values.GetInt(Lifetime);
                _kick = ctx.Values.GetFloat(Kick);
                int body = world.AddBody(BodyDef.Static(Vec2.Zero));
                var box = ShapeBuilder.Rectangle(6f, 6f);
                world.AddPolyline(body, box, true, 0.1f, ColliderMaterial.Default);
                var rng = Rng.FromSeed(ctx.Seed, 1);
                var def = BallDef.Create(new Vec2(rng.NextFloat(-1f, 1f), 1f), 0.3f);
                def.Velocity = new Vec2(rng.NextFloat(-4f, 4f), rng.NextFloat(-4f, 4f));
                def.Restitution = 0.9f;
                world.AddBall(def);
                var pts = new ShapePoint[box.Length];
                for (int i = 0; i < box.Length; i++) pts[i] = Q.QuantizePoint(box[i].X, box[i].Y);
                _boxShape = ctx.Visuals.DefineShape(ShapeDefinition.Polyline(pts, true, Q.QuantizeScale(0.1f)));
                _ballShape = ctx.Visuals.DefineShape(ShapeDefinition.Circle);
            }

            public RenderQuantization Quantization => Q;
            public int ParticipantCount => 0;
            public SimPhase Phase => _phase;
            public SimResult Result => new SimResult { Outcome = _phase == SimPhase.Running ? SimOutcome.None : SimOutcome.Completed, Winner = -1, Score = _hits, Tick = _finaleEnd };

            public void Start(SimRunner runner)
            {
            }

            public void Tick(SimRunner runner, ReadOnlySpan<SimInput> inputs)
            {
                foreach (SimInput input in inputs)
                {
                    if (input.Type != KickInput) continue;
                    ref readonly BallState b = ref _world.GetBall(0);
                    _world.SetBallVelocity(0, b.Velocity + new Vec2(input.A, input.B) * (0.001f * _kick));
                }
            }

            public void AfterPhysics(SimRunner runner)
            {
                foreach (SimEvent e in _world.Events)
                {
                    if (e.Kind != SimEventKind.Hit || e.Speed < 0.5f) continue;
                    _hits++;
                    _metrics.AddCollisions();
                    ref readonly BallState b = ref _world.GetBall(e.A);
                    _ctx.Events.AddRender(RenderEvent.Hit(0, 0, e.Point.X, e.Point.Y, e.Normal.X, e.Normal.Y, 0, 0.5f, e.A, b.Radius, 1000, e.Feature, 0f));
                    _ctx.Events.AddSound(ModeSound.ForBall(SoundEventType.Hit, e.A, e.Impulse, Q, b.Radius, b.Position.X));
                    _ctx.Events.AddReplay(ReplayEventType.Hit, e.A, ReplayQuantization.QuantizeImpulse(e.Impulse), e.Feature);
                }
                if (_phase == SimPhase.Running && runner.Tick >= _lifetime)
                {
                    _phase = SimPhase.Finale;
                    _finaleEnd = runner.Tick + 30;
                    _metrics.AddSignificant(runner.Tick);
                }
                else if (_phase == SimPhase.Finale && runner.Tick >= _finaleEnd)
                {
                    _phase = SimPhase.Finished;
                }
            }

            public void OnTimeLimit(SimRunner runner)
            {
                if (_phase == SimPhase.Running) _phase = SimPhase.Finished;
            }

            public void AppendStateHash(XxHash64 hash)
            {
                hash.AppendInt((int)_phase);
                hash.AppendInt(_finaleEnd);
                hash.AppendInt(_hits);
                hash.AppendULong((ulong)PhysicsWorld.QuantizeForHash(_kick));
            }

            public void ApplyLiveParam(string paramId, ParamValue value)
            {
                if (paramId == Kick) _kick = (float)value.AsFloat;
            }

            public void WriteFrame(RenderFrame frame)
            {
                frame.Clear();
                frame.AddBody(Q.QuantizeBody(1000, _boxShape, 0f, 0f, 0f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
                ref readonly BallState b = ref _world.GetBall(0);
                frame.AddBody(Q.QuantizeBody(0, _ballShape, b.Position.X, b.Position.Y, b.Angle, b.Radius, 0, BodyFlags.Visible));
                frame.AddHud(HudKeys.TimeTicks, _world.Tick);
                frame.AddHud(HudKeys.Score, _hits);
            }

            public CandidateRunSummary BuildSummary(SimRunner runner, bool endedByLimit)
            {
                return _metrics.ToSummary(runner, endedByLimit, 0.5, 0.5, -1, false);
            }

            public ReplayResult BuildReplayResult(SimRunner runner)
            {
                return new ReplayResult("", -1, new[] { _hits }, (uint)Math.Min(runner.Tick, Math.Max(0, _finaleEnd)));
            }
        }
    }
}
