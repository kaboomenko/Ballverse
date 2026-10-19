using System;
using Ballverse.Core.Audio;
using Ballverse.Core.Generation;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// One run of M01 «Побег из колец» (spec §2.9 M01): concentric kinematic rings with gaps rotate around the arena centre,
    /// balls start inside ring 0, a ring is passed when a ball is entirely outside it, passed rings break (or go dark), the
    /// run ends with the escape from the last ring (or by score at <c>targetDuration × 1.3</c> / the 180 s cap).
    /// <para>
    /// Every ring collider has the reference radius <see cref="ReferenceRadius"/>; the body's uniform scale gives the actual
    /// radius (thickness is not scaled), so «Сжатие» shifts are a kinematic scale velocity the solver includes in the contact
    /// (radial wall speed ≤ 3 m/s) and the render stream draws the same ring shape at that scale. Gap k of a ring lies at local
    /// angles [k·360°/n, k·360°/n + gap_i]; the gap placement law sets the body angle.
    /// </para>
    /// </summary>
    public sealed class RingEscapeScene : IModeScene
    {
        /// <summary>Collider radius of every ring; body scale = actual radius / this.</summary>
        public const float ReferenceRadius = 4f;
        /// <summary>Render body id of ring j in round r: <c>RingIdBase + r · 128 + j</c> (balls use their solver ids, &lt; RingIdBase).</summary>
        public const int RingIdBase = 1000000;
        /// <summary>Hits slower than this make no sound / VFX / replay event and do not count (resting contact noise), m/s.</summary>
        public const float MinEventSpeed = 0.5f;
        /// <summary>Approach speed of a "loud" hit (render intensity 1), m/s.</summary>
        public const float LoudSpeed = 12f;
        /// <summary>Countdown 3-2-1 length (spec §2.1).</summary>
        public const int CountdownTicks = 3 * SimConstants.TickRate;
        /// <summary>Finale effect length unless finaleStyle = stop (spec §2.1: 0–3 s).</summary>
        public const int FinaleTicks = 2 * SimConstants.TickRate;
        /// <summary>«Почти побег»: a ring hit closer than this to a gap edge is a significant event (card «Генератор»), degrees.</summary>
        public const float NearEscapeDeg = 10f;
        /// <summary>Card «Отбраковка»: no ring may stay unbroken longer than max(8 s, (2 + ln n) · T̄).</summary>
        public const int MaxBreakGapSeconds = 8;

        /// <summary>
        /// Longest allowed stretch without a break for a run of <paramref name="ringCount"/> rings with expected time per ring
        /// <paramref name="perRingTicks"/>: max(8 s, (2 + ln n) · T̄). Per-ring times are right-skewed, so the longest of n rings
        /// grows like ln n · T̄; a fixed multiple would reject most long runs.
        /// </summary>
        public static int MaxBreakGapTicksFor(int perRingTicks, int ringCount)
        {
            double factor = 2.0 + Math.Log(Math.Max(1, ringCount));
            return Math.Max(MaxBreakGapSeconds * SimConstants.TickRate, (int)(factor * perRingTicks));
        }
        /// <summary>Impulse given to break / escape sound events (no contact impulse exists for them), N·s.</summary>
        public const float BreakSoundImpulse = 2f;

        const ulong LayoutStream = 0x4D30315F4C41594FUL; // "M01_LAYO"
        const ulong RulesStream = 0x4D30315F52554C45UL;  // "M01_RULE"
        const int MaxRingsPerRound = 128;
        /// <summary>«Сжатие»: rings join the solver and the stream at r ≤ 12 m (float tolerance for innerRadius + k · spacing).</summary>
        const float InSolverRadius = RingEscapeGeometry.MaxRingRadius + 1e-4f;

        readonly PhysicsWorld _world;
        readonly ModeEventBuffer _events;
        readonly ModeVisualTable _visuals;
        readonly RingEscapeConfig _cfg;
        readonly RenderQuantization _q = new RenderQuantization(16f, 16f);
        readonly ModeMetrics _metrics;
        /// <summary>Priority of breaks and the escape chord over hits within the mixer's 40/s (RingEscapeSound caps).</summary>
        readonly ModeSoundBudget _soundBudget = new ModeSoundBudget();
        readonly int _teams;
        readonly bool _competition;
        readonly int _timeLimitTicks;
        readonly int _countdownTicks;
        readonly int _finaleTicks;
        readonly int _circleShape;
        readonly int[] _numberLabels;
        readonly Vec2[] _layout = new Vec2[RingEscapeParams.MaxBalls + 1];

        // Rings (index j = ring number, 0 = innermost).
        readonly int _ringCount;
        readonly int[] _ringBody;
        readonly int[] _ringShape;
        readonly float[] _ringBaseRadius;
        readonly float[] _ringRadius;
        readonly float[] _ringGapDeg;
        readonly float[] _ringMultiplier;
        readonly float[] _ringDirection;
        readonly bool[] _ringPassed;
        readonly bool[] _ringDestroyed;
        readonly bool[] _ringInSolver;
        readonly bool[] _ringHitOnce;
        readonly float[] _growLimit;
        int[] _colliderToRing;

        // Balls (indexed by solver id).
        int[] _ballTeam = new int[32];
        int[] _ballSlot = new int[32];
        bool[] _ballEscaped = new bool[32];
        int[] _ballCombo = new int[32];
        int _roundFirstBall;
        int _roundBallCount;

        // Rule state.
        Rng _rng;
        long _rngDraws;
        SimPhase _phase = SimPhase.Running;
        SimResult _result = SimResult.None;
        int _finaleEndTick = -1;
        int _round;
        int _current;
        int _ringsPassedRound;
        int _ringsPassedTotal;
        int _escapes;
        int _escapeTick = -1;
        int _winnerTeam = -1;
        int _decisionTick = -1;
        int _lastBreakTick;
        int _maxBreakGap;
        int _maxCombo;
        bool _launched;
        bool _pendingRound;
        readonly int[] _teamScore;
        readonly int[] _teamReachTick;
        float _rotationSpeedDeg;
        string _ringPalette;
        string _labels;
        string _trail;
        int[] _nameLabels = new int[0];

        internal RingEscapeScene(PhysicsWorld world, ModeSceneContext ctx)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (world.BallCount != 0 || world.BodyCount != 0 || world.Tick != 0) throw new ArgumentException("The world must be empty", nameof(world));
            _events = ctx.Events;
            _visuals = ctx.Visuals;
            _cfg = RingEscapeConfig.From(ctx.Values);
            _teams = _cfg.TeamCount;
            _competition = _cfg.CompetitionActive;
            _metrics = new ModeMetrics(_competition ? _teams : 0);
            _teamScore = new int[_teams];
            _teamReachTick = new int[_teams];
            _timeLimitTicks = _cfg.TargetDurationTicks.HasValue ? GenerationSession.RunLimitTicks(_cfg.TargetDurationTicks) : -1;
            _countdownTicks = _cfg.Countdown ? CountdownTicks : 0;
            _finaleTicks = string.Equals(_cfg.FinaleStyle, "stop", StringComparison.Ordinal) ? 0 : FinaleTicks;
            _rotationSpeedDeg = _cfg.RotationSpeedDeg;
            _ringPalette = _cfg.RingPalette;
            _labels = _cfg.Labels;
            _trail = _cfg.Trail;
            _rng = Rng.FromSeed(ctx.Seed, RulesStream);
            Rng layout = Rng.FromSeed(ctx.Seed, LayoutStream);

            var settings = SimSettings.Default;
            settings.Gravity = SimSettings.GravityFrom(_cfg.GravityG, _cfg.GravityAngleDeg);
            settings.MaxSpeed = _cfg.MaxSpeed;
            settings.BallCollisions = true;
            world.Settings = settings;
            world.ReserveBalls(64);

            _ringCount = _cfg.RingCount;
            _ringBody = new int[_ringCount];
            _ringShape = new int[_ringCount];
            _ringBaseRadius = new float[_ringCount];
            _ringRadius = new float[_ringCount];
            _ringGapDeg = new float[_ringCount];
            _ringMultiplier = new float[_ringCount];
            _ringDirection = new float[_ringCount];
            _ringPassed = new bool[_ringCount];
            _ringDestroyed = new bool[_ringCount];
            _ringInSolver = new bool[_ringCount];
            _ringHitOnce = new bool[_ringCount];
            _growLimit = new float[_ringCount + 1];

            _circleShape = _visuals.DefineShape(ShapeDefinition.Circle);
            _numberLabels = new int[RingEscapeParams.MaxBalls * 4];
            for (int i = 0; i < _numberLabels.Length; i++) _numberLabels[i] = _visuals.DefineLabel((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            DefineNameLabels(_cfg.LabelNames);

            BuildRings(ref layout);
            for (int j = 0; j <= _ringCount; j++) _growLimit[j] = RingEscapeGeometry.GrowLimit(_cfg, j);
            SpawnBalls(_cfg.BallCount, _countdownTicks == 0, 0);
            _launched = _countdownTicks == 0;
        }

        // ------------------------------------------------------------------ IModeScene

        public RenderQuantization Quantization => _q;

        public int ParticipantCount => _competition ? _teams : 0;

        public SimPhase Phase => _phase;

        public SimResult Result => _result;

        /// <summary>Rings passed in total (all rounds).</summary>
        public int RingsPassed => _ringsPassedTotal;

        /// <summary>Tick of the first escape, −1 before.</summary>
        public int EscapeTick => _escapeTick;

        /// <summary>Escapes so far (rounds variants count each).</summary>
        public int Escapes => _escapes;

        /// <summary>Lowest ring not yet passed in the current round (= ring count once escaped).</summary>
        public int CurrentRing => _current;

        public int Round => _round;

        public RingEscapeConfig Config => _cfg;

        /// <summary>Longest stretch without a ring break so far (from the start / previous break), ticks.</summary>
        public int MaxBreakGapTicks => _maxBreakGap;

        /// <summary>Current rotation speed (live parameter), °/s.</summary>
        public float RotationSpeedDeg => _rotationSpeedDeg;

        public bool IsRingPassed(int ring) => _ringPassed[ring];

        public bool IsRingDestroyed(int ring) => _ringDestroyed[ring];

        public bool IsRingInSolver(int ring) => _ringInSolver[ring];

        /// <summary>Current (or, outside the solver, virtual) radius of ring j's centre line, m.</summary>
        public float RingRadius(int ring) => _ringRadius[ring];

        public int RingBody(int ring) => _ringBody[ring];

        /// <summary>Rule 4 diameter limit with ring <paramref name="current"/> current.</summary>
        public float GrowLimit(int current) => _growLimit[Math.Min(Math.Max(current, 0), _ringCount)];

        /// <summary>
        /// Rule 4 as applied now: d_grow over the rings a ball can still meet as walls — from the current ring outwards when
        /// passed rings are destroyed, over every ring when they stay as walls («Статично» / «Отдаление камеры» without
        /// destroyPassed: a ball can fall back inside a passed ring and must still fit through its gap).
        /// </summary>
        public float EffectiveGrowLimit => GrowLimit(_cfg.EffectiveDestroyPassed ? _current : 0);

        public bool IsBallEscaped(int ball) => ball < _ballEscaped.Length && _ballEscaped[ball];

        public int BallTeam(int ball) => _ballTeam[ball];

        /// <summary>Solver ids of the current round's balls: [first, first + count).</summary>
        public int RoundFirstBall => _roundFirstBall;

        public int RoundBallCount => _roundBallCount;

        public void Start(SimRunner runner)
        {
        }

        public void Tick(SimRunner runner, ReadOnlySpan<SimInput> inputs)
        {
            int t = runner.Tick;
            if (!_launched)
            {
                if (t >= _countdownTicks)
                {
                    LaunchRoundBalls();
                    _launched = true;
                }
                else
                {
                    HoldRoundBalls();
                }
            }
            if (_cfg.Progression == RingProgression.Compress) UpdateCompression();
            UpdateRotation(t);
        }

        public void AfterPhysics(SimRunner runner)
        {
            int tick = runner.Tick;
            ProcessContacts(tick);
            if (_phase == SimPhase.Running) DetectPasses(tick);
            if (_pendingRound)
            {
                _pendingRound = false;
                StartNewRound(tick);
            }
            RemoveFarBalls();
            if (_phase == SimPhase.Running && _timeLimitTicks > 0 && tick >= _timeLimitTicks) EnterFinale(tick, false);
            if (_phase == SimPhase.Finale && tick >= _finaleEndTick) _phase = SimPhase.Finished;
            if (_competition) SampleStandings(tick);
        }

        public void OnTimeLimit(SimRunner runner)
        {
            if (_phase == SimPhase.Running) EnterFinale(runner.Tick, false);
        }

        public void ApplyLiveParam(string paramId, ParamValue value)
        {
            if (value == null) return;
            switch (paramId)
            {
                case RingEscapeParams.RotationSpeed: _rotationSpeedDeg = (float)value.AsFloat; break;
                case RingEscapeParams.RingPalette: _ringPalette = value.AsString; break;
                case CommonParams.Labels: _labels = value.AsString; break;
                case CommonParams.LabelNames: DefineNameLabels(value.AsStrings); break;
                case CommonParams.Trail: _trail = value.AsString; break;
            }
        }

        public void AppendStateHash(XxHash64 hash)
        {
            hash.AppendInt((int)_phase);
            hash.AppendInt(_finaleEndTick);
            hash.AppendInt(_round);
            hash.AppendInt(_current);
            hash.AppendInt(_ringsPassedRound);
            hash.AppendInt(_ringsPassedTotal);
            hash.AppendInt(_escapes);
            hash.AppendInt(_escapeTick);
            hash.AppendInt(_winnerTeam);
            hash.AppendInt(_decisionTick);
            hash.AppendInt(_lastBreakTick);
            hash.AppendInt(_maxBreakGap);
            hash.AppendInt(_launched ? 1 : 0);
            hash.AppendULong((ulong)_rngDraws);
            hash.AppendULong((ulong)PhysicsWorld.QuantizeForHash(_rotationSpeedDeg));
            for (int j = 0; j < _ringCount; j++)
            {
                hash.AppendInt((_ringPassed[j] ? 1 : 0) | (_ringDestroyed[j] ? 2 : 0) | (_ringInSolver[j] ? 4 : 0) | (_ringHitOnce[j] ? 8 : 0));
                hash.AppendULong((ulong)PhysicsWorld.QuantizeForHash(_ringRadius[j]));
            }
            for (int k = 0; k < _teams; k++)
            {
                hash.AppendInt(_teamScore[k]);
                hash.AppendInt(_teamReachTick[k]);
            }
            int balls = _world.BallCount;
            for (int b = 0; b < balls; b++) hash.AppendInt((_ballEscaped[b] ? 1 : 0) | (_ballCombo[b] << 1));
        }

        public void WriteFrame(RenderFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            frame.Clear();
            for (int j = 0; j < _ringCount; j++)
            {
                if (!_ringInSolver[j] || _ringDestroyed[j]) continue;
                ref readonly PhysicsBodyState b = ref _world.GetBody(_ringBody[j]);
                BodyFlags flags = BodyFlags.Visible;
                if (_cfg.Progression == RingProgression.Compress) flags |= RenderBodyFlags.FadeOutsideSafeZone;
                if (_cfg.Progression == RingProgression.CameraZoom && j >= _current && j < _current + 4) flags |= RenderBodyFlags.CameraTarget;
                if (_ringPassed[j]) flags |= BodyFlags.Dead;
                frame.AddBody(_q.QuantizeBody(RingIdBase + _round * MaxRingsPerRound + j, _ringShape[j], 0f, 0f, b.Angle, b.Scale,
                    RingColor(j), flags));
            }
            float maxDist = _q.PositionRange - 1f;
            bool trail = !string.Equals(_trail, "off", StringComparison.Ordinal);
            int balls = _world.BallCount;
            for (int id = _roundFirstBall; id < balls; id++)
            {
                ref readonly BallState s = ref _world.GetBall(id);
                if (!s.Alive) continue;
                if (s.Position.Length + s.Radius > maxDist) continue;
                BodyFlags flags = BodyFlags.Visible;
                if (trail) flags |= BodyFlags.Trail;
                if (_cfg.Progression == RingProgression.CameraZoom && !_ballEscaped[id]) flags |= RenderBodyFlags.CameraTarget;
                if (_winnerTeam >= 0 && _ballTeam[id] == _winnerTeam && _competition) flags |= BodyFlags.Highlight;
                frame.AddBody(_q.QuantizeBody(id, _circleShape, s.Position.X, s.Position.Y, s.Angle, s.Radius, (byte)_ballTeam[id], flags,
                    LabelFor(_ballSlot[id])));
            }
            frame.AddHud(HudKeys.TimeTicks, _world.Tick);
            int countdown = _world.Tick < _countdownTicks ? (_countdownTicks - _world.Tick + SimConstants.TickRate - 1) / SimConstants.TickRate : 0;
            frame.AddHud(HudKeys.Countdown, countdown);
            frame.AddHud(HudKeys.Progress, _ringsPassedRound);
            frame.AddHud(HudKeys.ProgressTotal, _ringCount);
            frame.AddHud(HudKeys.Streak, _maxCombo);
            frame.AddHud(HudKeys.Phase, (int)_phase);
            frame.AddHud(HudKeys.Winner, _winnerTeam);
            frame.AddHud(HudKeys.Round, _round + 1);
            if (_competition)
            {
                for (int k = 0; k < _teams; k++) frame.AddHud(HudKeys.ParticipantScoreBase + k, _teamScore[k]);
            }
        }

        public CandidateRunSummary BuildSummary(SimRunner runner, bool endedByLimit)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            bool escaped = _escapeTick >= 0;
            int? target = _cfg.TargetDurationTicks;
            double tension;
            if (_competition)
            {
                int best = -1, second = -1;
                for (int k = 0; k < _teams; k++)
                {
                    int s = _teamScore[k];
                    if (s > best)
                    {
                        second = best;
                        best = s;
                    }
                    else if (s > second)
                    {
                        second = s;
                    }
                }
                double gap = best - Math.Max(0, second);
                tension = 1.0 - Math.Min(1.0, gap / Math.Max(1.0, 0.25 * _ringCount));
            }
            else if (!escaped)
            {
                tension = 0.0;
            }
            else if (target.HasValue)
            {
                tension = 1.0 - Math.Abs(_escapeTick - target.Value) / (0.3 * target.Value);
            }
            else
            {
                tension = 1.0;
            }
            int hit = 0;
            for (int j = 0; j < _ringCount; j++) if (_ringHitOnce[j]) hit++;
            double coverage = _ringCount == 0 ? 0.0 : hit / (double)_ringCount;

            // Card «Отбраковка»: no escape before the run ended; escape before 50 % of the target; a ring unbroken for longer
            // than max(8 s, (2 + ln n)·T̄) (T̄ = target / ring count, see MaxBreakGapTicksFor).
            bool rejected = !escaped;
            if (escaped && target.HasValue && _escapeTick * 2 < target.Value) rejected = true;
            if (escaped)
            {
                int perRing = (target ?? _escapeTick) / Math.Max(1, _cfg.RingCount);
                int maxGap = MaxBreakGapTicksFor(perRing, _cfg.RingCount);
                if (_maxBreakGap > maxGap) rejected = true;
            }
            return _metrics.ToSummary(runner, endedByLimit, coverage, tension, _competition ? _winnerTeam : -1, rejected);
        }

        public ReplayResult BuildReplayResult(SimRunner runner)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            int[] scores;
            if (_competition)
            {
                scores = new int[_teams];
                Array.Copy(_teamScore, scores, _teams);
            }
            else
            {
                scores = new[] { _ringsPassedTotal };
            }
            int finish = _decisionTick >= 0 ? _decisionTick : runner.Tick;
            if (finish > runner.Tick) finish = runner.Tick;
            string winnerId = _competition && _winnerTeam >= 0 ? "color" + _winnerTeam.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            return new ReplayResult(winnerId, _competition ? _winnerTeam : -1, scores, (uint)finish);
        }

        // ------------------------------------------------------------------ building

        void BuildRings(ref Rng layout)
        {
            Span<AngularGap> gaps = stackalloc AngularGap[4];
            Span<RingGap> qgaps = stackalloc RingGap[4];
            int gapCount = _cfg.GapCount;
            var material = ColliderMaterial.Default;
            material.Restitution = 0f;
            material.Friction = _cfg.Friction;
            for (int j = 0; j < _ringCount; j++)
            {
                float gap = RingEscapeGeometry.GapDeg(_cfg, j);
                _ringGapDeg[j] = gap;
                float radius = RingEscapeGeometry.RingRadius(_cfg, j);
                _ringBaseRadius[j] = radius;
                _ringRadius[j] = radius;
                _ringMultiplier[j] = RingEscapeGeometry.SpeedMultiplier(_cfg.SpeedStep, j);
                switch (_cfg.Direction)
                {
                    case RingDirection.Clockwise: _ringDirection[j] = -1f; break;
                    case RingDirection.CounterClockwise: _ringDirection[j] = 1f; break;
                    case RingDirection.Random: _ringDirection[j] = layout.NextBool() ? 1f : -1f; break;
                    default: _ringDirection[j] = (j & 1) == 0 ? 1f : -1f; break;
                }
                float w = gap * SimMath.DegToRad;
                for (int k = 0; k < gapCount; k++)
                {
                    float start = SimMath.TwoPi * k / gapCount;
                    gaps[k] = new AngularGap(start, start + w);
                    qgaps[k] = RingGap.FromRadians(start, start + w);
                }
                int body = _world.AddBody(new BodyDef
                {
                    Motion = BodyMotion.Kinematic,
                    Position = Vec2.Zero,
                    Angle = 0f,
                    Scale = radius / ReferenceRadius,
                    Tag = j,
                });
                material.Tag = j;
                _world.AddRing(body, Vec2.Zero, ReferenceRadius, _cfg.RingThickness, gaps.Slice(0, gapCount), material);
                _ringBody[j] = body;
                _ringShape[j] = _visuals.DefineShape(ShapeDefinition.Ring(_q.QuantizeScale(ReferenceRadius), _q.QuantizeScale(_cfg.RingThickness),
                    qgaps.Slice(0, gapCount), true));
                bool inSolver = _cfg.Progression != RingProgression.Compress || radius <= InSolverRadius;
                _ringInSolver[j] = inSolver;
                if (!inSolver) _world.SetBodyEnabled(body, false);
            }
            PlaceGaps(ref layout, false);
            _colliderToRing = new int[_world.ColliderCount];
            for (int c = 0; c < _colliderToRing.Length; c++) _colliderToRing[c] = _world.GetColliderInfo(c).Material.Tag;
        }

        /// <summary>Sets every ring's body angle from the gap placement law (gap 0 centre at the placement angle).</summary>
        void PlaceGaps(ref Rng rng, bool rules)
        {
            float baseDeg = 0f;
            if (_cfg.Placement == GapPlacementKind.Aligned || _cfg.Placement == GapPlacementKind.Spiral) baseDeg = Draw(ref rng, rules) * 360f;
            for (int j = 0; j < _ringCount; j++)
            {
                float centreDeg;
                switch (_cfg.Placement)
                {
                    case GapPlacementKind.Aligned: centreDeg = baseDeg; break;
                    case GapPlacementKind.Spiral: centreDeg = baseDeg + j * _cfg.GapSpiralStepDeg; break;
                    case GapPlacementKind.Fixed: centreDeg = _cfg.GapAngleDeg; break;
                    default: centreDeg = Draw(ref rng, rules) * 360f; break;
                }
                float angle = SimMath.WrapPi((centreDeg - _ringGapDeg[j] * 0.5f) * SimMath.DegToRad);
                _world.SetBodyPose(_ringBody[j], Vec2.Zero, angle);
            }
        }

        float Draw(ref Rng rng, bool rules)
        {
            if (rules) _rngDraws++;
            return rng.NextFloat();
        }

        void SpawnBalls(int count, bool launch, int tick)
        {
            int placed = RingEscapeGeometry.Layout(_cfg.BallSize, _cfg.InnerRadius, _cfg.RingThickness, count, _layout);
            _roundFirstBall = _world.BallCount;
            _roundBallCount = placed;
            for (int k = 0; k < placed; k++)
            {
                var def = BallDef.Create(_layout[k], _cfg.BallSize * 0.5f);
                def.Restitution = _cfg.Restitution;
                def.Friction = _cfg.Friction;
                def.Tag = k % _teams;
                def.GravityScale = launch ? 1f : 0f;
                if (launch) def.Velocity = LaunchVelocity();
                int id = _world.AddBall(def);
                EnsureBallArrays(id + 1);
                _ballTeam[id] = k % _teams;
                _ballSlot[id] = k;
                _ballEscaped[id] = false;
                _ballCombo[id] = 0;
                if (_events.WantsRender)
                {
                    _events.AddRender(RenderEvent.Spawn(tick, 0, id, _layout[k].X, _layout[k].Y, def.Radius, (byte)_ballTeam[id]));
                }
            }
        }

        Vec2 LaunchVelocity()
        {
            float angle = _cfg.LaunchAngleRandom ? NextRule() * SimMath.TwoPi : _cfg.LaunchAngleDeg * SimMath.DegToRad;
            return Vec2.FromAngle(angle) * _cfg.LaunchSpeed;
        }

        float NextRule()
        {
            _rngDraws++;
            return _rng.NextFloat();
        }

        void LaunchRoundBalls()
        {
            for (int id = _roundFirstBall; id < _roundFirstBall + _roundBallCount; id++)
            {
                if (!_world.GetBall(id).Alive) continue;
                _world.SetBallGravityScale(id, 1f);
                _world.SetBallVelocity(id, LaunchVelocity());
            }
        }

        void HoldRoundBalls()
        {
            for (int id = _roundFirstBall; id < _roundFirstBall + _roundBallCount; id++)
            {
                if (!_world.GetBall(id).Alive) continue;
                _world.SetBallVelocity(id, Vec2.Zero);
                _world.SetBallAngularVelocity(id, 0f);
            }
        }

        void EnsureBallArrays(int n)
        {
            if (n <= _ballTeam.Length) return;
            int size = Math.Max(n, _ballTeam.Length * 2);
            Array.Resize(ref _ballTeam, size);
            Array.Resize(ref _ballSlot, size);
            Array.Resize(ref _ballEscaped, size);
            Array.Resize(ref _ballCombo, size);
        }

        void DefineNameLabels(System.Collections.Generic.IReadOnlyList<string> names)
        {
            int n = names == null ? 0 : names.Count;
            var ids = new int[n];
            // Labels are never released and the table holds RenderStreamLimits.MaxLabels for the whole run: live edits (or a
            // replay with many PARM records) must not overflow it. A name that no longer fits is shown without a label.
            for (int i = 0; i < n; i++) _visuals.TryDefineLabel(names[i], out ids[i]);
            _nameLabels = ids;
        }

        int LabelFor(int slot)
        {
            switch (_labels)
            {
                case "numbers": return slot < _numberLabels.Length ? _numberLabels[slot] : BodyState.NoLabel;
                case "names": return _nameLabels.Length == 0 ? BodyState.NoLabel : _nameLabels[slot % _nameLabels.Length];
                default: return BodyState.NoLabel;
            }
        }

        byte RingColor(int j)
        {
            switch (_ringPalette)
            {
                case RingEscapeParams.PaletteArena: return PaletteSlots.Arena;
                case RingEscapeParams.PaletteRainbow: return (byte)(PaletteSlots.ThemeDecorFirst + j % 16);
                default: return (j & 1) == 0 ? PaletteSlots.Arena : PaletteSlots.ArenaAlt;
            }
        }

        // ------------------------------------------------------------------ per tick

        /// <summary>«Сжатие»: every ring moves towards innerRadius + slot · spacing (slot = j − rings destroyed below j) at ≤ spacing / 0.4 s.</summary>
        void UpdateCompression()
        {
            float step = _cfg.RingSpacing / RingEscapeGeometry.ShiftSeconds / SimConstants.TickRate;
            int destroyedBelow = 0;
            for (int j = 0; j < _ringCount; j++)
            {
                if (_ringDestroyed[j])
                {
                    destroyedBelow++;
                    continue;
                }
                float target = _cfg.InnerRadius + (j - destroyedBelow) * _cfg.RingSpacing;
                float r = _ringRadius[j];
                if (_ringInSolver[j]) r = _world.GetBody(_ringBody[j]).Scale * ReferenceRadius;
                float next = r - target > step ? r - step : target;
                if (next > r) next = r;
                if (_ringInSolver[j])
                {
                    float scale = next / ReferenceRadius;
                    float current = _world.GetBody(_ringBody[j]).Scale;
                    if (next == target && MathF.Abs(r - target) < 1e-4f)
                    {
                        if (current != scale) _world.SetBodyScale(_ringBody[j], scale);
                        _world.SetBodyScaleVelocity(_ringBody[j], 0f);
                    }
                    else
                    {
                        _world.SetBodyScaleVelocity(_ringBody[j], (scale - current) * SimConstants.TickRate);
                    }
                    _ringRadius[j] = next;
                }
                else
                {
                    _ringRadius[j] = next;
                    if (next <= InSolverRadius) EnterSolver(j);
                }
            }
        }

        /// <summary>A far «Сжатие» ring reaches 12 m: it joins the solver and the stream (or, if a ball already overlaps it, counts as passed).</summary>
        void EnterSolver(int j)
        {
            _ringInSolver[j] = true;
            int body = _ringBody[j];
            _world.SetBodyScale(body, _ringRadius[j] / ReferenceRadius);
            _world.SetBodyScaleVelocity(body, 0f);
            _world.SetBodyEnabled(body, true);
            float inner = _ringRadius[j] - _cfg.RingThickness * 0.5f;
            int balls = _world.BallCount;
            for (int id = _roundFirstBall; id < balls; id++)
            {
                ref readonly BallState s = ref _world.GetBall(id);
                if (!s.Alive || _ballEscaped[id]) continue;
                if (s.Position.Length + s.Radius > inner && !_ringPassed[j])
                {
                    // The ball is already at or beyond the ring's wall: it cannot be inside it, so the ring counts as passed.
                    PassRing(j, id, _world.Tick + 1);
                    return;
                }
            }
        }

        void UpdateRotation(int tick)
        {
            float rot = _rotationSpeedDeg * SimMath.DegToRad;
            float law = 1f;
            if (_cfg.SpeedLaw == RingSpeedLaw.Pulse)
            {
                float phase = SimMath.TwoPi * (tick % (int)(RingEscapeGeometry.PulsePeriod * SimConstants.TickRate))
                    / (RingEscapeGeometry.PulsePeriod * SimConstants.TickRate);
                law = 1f + RingEscapeGeometry.PulseAmplitude * SimMath.Sin(phase);
            }
            else if (_cfg.SpeedLaw == RingSpeedLaw.Reverse)
            {
                int period = Math.Max(1, (int)MathF.Round(_cfg.ReversePeriod * SimConstants.TickRate));
                law = ((tick / period) & 1) == 0 ? 1f : -1f;
            }
            float halfT = _cfg.RingThickness * 0.5f;
            for (int j = 0; j < _ringCount; j++)
            {
                if (!_ringInSolver[j] || _ringDestroyed[j]) continue;
                float omega = rot * _ringMultiplier[j] * _ringDirection[j] * law;
                float r = _world.GetBody(_ringBody[j]).Scale * ReferenceRadius;
                float limit = RingEscapeGeometry.OmegaLimitRad(r + halfT);
                if (omega > limit) omega = limit;
                else if (omega < -limit) omega = -limit;
                _world.SetBodyAngularVelocity(_ringBody[j], omega);
            }
        }

        void ProcessContacts(int tick)
        {
            ReadOnlySpan<SimEvent> events = _world.Events;
            for (int i = 0; i < events.Length; i++)
            {
                ref readonly SimEvent e = ref events[i];
                if (e.Speed < MinEventSpeed) continue;
                if (e.Kind == SimEventKind.Hit)
                {
                    if ((uint)e.B >= (uint)_colliderToRing.Length) continue;
                    int ring = _colliderToRing[e.B];
                    OnRingHit(tick, e, ring);
                }
                else if (e.Kind == SimEventKind.BallHit)
                {
                    OnBallHit(e);
                }
            }
        }

        void OnRingHit(int tick, in SimEvent e, int ring)
        {
            _metrics.AddCollisions();
            _ringHitOnce[ring] = true;
            int ball = e.A;
            _ballCombo[ball] = 0;
            if (_phase == SimPhase.Running && NearGapEdge(ring, e.LocalAngle)) _metrics.AddSignificant(Math.Min(tick, SimConstants.MaxDurationTicks));
            ref readonly BallState s = ref _world.GetBall(ball);
            if (_cfg.GrowOnHit > 0f && s.Alive && !_ballEscaped[ball]) Grow(ball, s.Radius);
            byte color = (byte)_ballTeam[ball];
            if (_events.WantsRender)
            {
                float intensity = SimMath.Clamp(e.Speed / LoudSpeed, 0f, 1f);
                _events.AddRender(RenderEvent.Hit(tick, 0, e.Point.X, e.Point.Y, e.Normal.X, e.Normal.Y, color, intensity, ball,
                    s.Radius, RingIdBase + _round * MaxRingsPerRound + ring, e.Feature, e.LocalAngle));
            }
            // Card «Звук»: the note rises with the ring number (pitch size, see RingEscapeSound).
            if (!AudibleSound(RingEscapeSound.HitSoundCap)) return;
            if (_events.WantsSound)
            {
                SoundEvent se = ModeSound.ForBall(SoundEventType.Hit, ball, e.Impulse, _q, s.Radius, s.Position.X);
                se.BodySize = RingEscapeSound.HitPitchSize(ring);
                _events.AddSound(se);
            }
            if (_events.WantsReplay) _events.AddReplay(ReplayEventType.Hit, ball, ReplayQuantization.QuantizeImpulse(e.Impulse), ring);
        }

        void OnBallHit(in SimEvent e)
        {
            _metrics.AddCollisions();
            ref readonly BallState s = ref _world.GetBall(e.A);
            if (_events.WantsRender)
            {
                float intensity = SimMath.Clamp(e.Speed / LoudSpeed, 0f, 1f);
                _events.AddRender(RenderEvent.Hit(0, 0, e.Point.X, e.Point.Y, e.Normal.X, e.Normal.Y, (byte)_ballTeam[e.A], intensity, e.A,
                    s.Radius, -1, -1, 0f));
            }
            if (!AudibleSound(RingEscapeSound.HitSoundCap)) return;
            if (_events.WantsSound) _events.AddSound(ModeSound.ForBall(SoundEventType.Hit, e.A, e.Impulse, _q, s.Radius, s.Position.X));
            if (_events.WantsReplay) _events.AddReplay(ReplayEventType.Hit, e.A, ReplayQuantization.QuantizeImpulse(e.Impulse), -1 - (long)e.B);
        }

        /// <summary>
        /// Sound priority (<see cref="ModeSoundBudget"/>): true when a sound of this priority cap fits the window. The same
        /// decision gates the sound event and the EVNT record, and it is taken whenever either sink is attached, so the live
        /// list and the export agree.
        /// </summary>
        bool AudibleSound(int cap)
        {
            if (!_events.WantsSound && !_events.WantsReplay) return false;
            return _soundBudget.TryTake(_events.Tick, cap);
        }

        /// <summary>Rule 4: grow by growOnHit, clipped to d_grow of the rings that are still walls (<see cref="EffectiveGrowLimit"/>).</summary>
        void Grow(int ball, float radius)
        {
            float limit = 0.5f * EffectiveGrowLimit;
            float next = radius * (1f + _cfg.GrowOnHit);
            if (next > limit) next = limit;
            if (next <= radius) return;
            _world.SetBallRadius(ball, next);
            if (_events.WantsRender)
            {
                ref readonly BallState s = ref _world.GetBall(ball);
                _events.AddRender(RenderEvent.Grow(0, 0, ball, s.Position.X, s.Position.Y, next, (byte)_ballTeam[ball]));
            }
        }

        bool NearGapEdge(int ring, float localAngle)
        {
            float half = _ringGapDeg[ring] * 0.5f * SimMath.DegToRad;
            float pitch = SimMath.TwoPi / _cfg.GapCount;
            float a = SimMath.Wrap2Pi(localAngle - half);
            float rel = a - pitch * MathF.Floor(a / pitch + 0.5f);
            float edgeDistance = MathF.Abs(MathF.Abs(rel) - half);
            return edgeDistance <= NearEscapeDeg * SimMath.DegToRad;
        }

        void DetectPasses(int tick)
        {
            float halfT = _cfg.RingThickness * 0.5f;
            int balls = _world.BallCount;
            for (int id = _roundFirstBall; id < balls && _phase == SimPhase.Running && !_pendingRound; id++)
            {
                ref readonly BallState s = ref _world.GetBall(id);
                if (!s.Alive || _ballEscaped[id]) continue;
                float out_ = s.Position.Length - s.Radius;
                bool outsideAll = out_ > RingEscapeGeometry.MaxRingRadius + halfT;
                for (int j = _current; j < _ringCount; j++)
                {
                    if (_ringPassed[j]) continue;
                    if (!outsideAll && out_ <= _ringRadius[j] + halfT) break;
                    PassRing(j, id, tick);
                    if (_ballEscaped[id] || _phase != SimPhase.Running || _pendingRound) break;
                }
            }
        }

        void PassRing(int j, int ball, int tick)
        {
            if (_ringPassed[j]) return;
            _ringPassed[j] = true;
            int team = _ballTeam[ball];
            _teamScore[team]++;
            _teamReachTick[team] = tick;
            _ringsPassedRound++;
            _ringsPassedTotal++;
            _ballCombo[ball]++;
            if (_ballCombo[ball] > _maxCombo) _maxCombo = _ballCombo[ball];
            int gap = tick - _lastBreakTick;
            if (gap > _maxBreakGap) _maxBreakGap = gap;
            _lastBreakTick = tick;
            _metrics.AddCollisions();
            _metrics.AddSignificant(Math.Min(tick, SimConstants.MaxDurationTicks));
            while (_current < _ringCount && _ringPassed[_current]) _current++;

            bool visible = _ringInSolver[j] && !_ringDestroyed[j];
            int ringRenderId = RingIdBase + _round * MaxRingsPerRound + j;
            if (_cfg.EffectiveDestroyPassed && _ringInSolver[j])
            {
                ref readonly PhysicsBodyState b = ref _world.GetBody(_ringBody[j]);
                if (visible && _events.WantsRender)
                {
                    _events.AddRender(RenderEvent.Break(tick, 0, ringRenderId, _ringShape[j], 0f, 0f, b.Angle, b.Scale,
                        RingColor(j), 1f));
                }
                _ringDestroyed[j] = true;
                _world.SetBodyEnabled(_ringBody[j], false);
                _world.SetBodyAngularVelocity(_ringBody[j], 0f);
                _world.SetBodyScaleVelocity(_ringBody[j], 0f);
            }
            else if (_cfg.EffectiveDestroyPassed)
            {
                _ringDestroyed[j] = true;
            }
            // Card «Звук» / «Модель»: the break rises with every ring and climbs further in a combo (pitch size, RingEscapeSound).
            // The emitter is the ring (its render id), not the ball: the break often falls in the tick of the ball's ring hit or
            // 1–2 ticks after the previous break of a combo, and the mixer's per-body 50 ms limit (spec §2.7) would drop it.
            // A ring breaks once, so its id never repeats. Breaks take priority over hits within the mixer's 40/s.
            if (AudibleSound(RingEscapeSound.BreakSoundCap))
            {
                if (_events.WantsSound)
                    _events.AddSound(RingEscapeSound.BreakSound(ringRenderId, j, _ballCombo[ball], BreakSoundImpulse));
                if (_events.WantsReplay)
                    _events.AddReplay(ReplayEventType.Break, ringRenderId, ReplayQuantization.QuantizeImpulse(BreakSoundImpulse), RingEscapeSound.BreakExtra(j, _ballCombo[ball]));
            }

            if (j == _ringCount - 1) Escape(ball, tick);
        }

        void Escape(int ball, int tick)
        {
            _ballEscaped[ball] = true;
            _escapes++;
            bool first = _escapeTick < 0;
            if (first) _escapeTick = tick;
            if (_competition && first) _winnerTeam = _ballTeam[ball];
            ref readonly BallState s = ref _world.GetBall(ball);
            if (_events.WantsRender)
            {
                RenderEvent e = RenderEvent.Create(tick, RenderEventType.Escape, 0, s.Position.X, s.Position.Y, (byte)_ballTeam[ball], 1f);
                e.Size = 6f;
                e.BodyId = ball;
                _events.AddRender(e);
            }
            // Escape chord: bodiless (the ball has just hit / broken a ring in this tick, the per-body limit would drop it).
            if (AudibleSound(RingEscapeSound.FinishSoundCap))
            {
                if (_events.WantsSound) _events.AddSound(RingEscapeSound.FinishSound(BreakSoundImpulse));
                if (_events.WantsReplay)
                    _events.AddReplay(ReplayEventType.Finish, ModeEventBuffer.BodilessReplayBodyId, ReplayQuantization.QuantizeImpulse(BreakSoundImpulse), _ballTeam[ball]);
            }
            if (_cfg.AfterEscape == AfterEscapeKind.Finale) EnterFinale(tick, true);
            else _pendingRound = true;
        }

        void EnterFinale(int tick, bool escaped)
        {
            if (_phase != SimPhase.Running) return;
            _decisionTick = tick;
            if (_competition && _winnerTeam < 0) _winnerTeam = LeaderByScore();
            SimOutcome outcome = escaped && _cfg.AfterEscape == AfterEscapeKind.Finale
                ? (_competition ? SimOutcome.Win : SimOutcome.Completed)
                : SimOutcome.TimeLimit;
            _result = new SimResult
            {
                Outcome = outcome,
                Winner = _competition ? _winnerTeam : -1,
                Score = _ringsPassedTotal,
                Detail = _cfg.AfterEscape == AfterEscapeKind.Finale ? _ringCount : _escapes,
                Tick = tick,
            };
            if (_finaleTicks == 0)
            {
                _phase = SimPhase.Finished;
            }
            else
            {
                _phase = SimPhase.Finale;
                _finaleEndTick = tick + _finaleTicks;
            }
            if (!escaped && AudibleSound(RingEscapeSound.FinishSoundCap))
            {
                if (_events.WantsReplay)
                    _events.AddReplay(ReplayEventType.Finish, ModeEventBuffer.BodilessReplayBodyId, ReplayQuantization.QuantizeImpulse(BreakSoundImpulse), _winnerTeam);
                if (_events.WantsSound) _events.AddSound(RingEscapeSound.FinishSound(BreakSoundImpulse));
            }
        }

        /// <summary>Team with the most rings; ties go to the team that reached that count first; −1 when nobody passed a ring.</summary>
        int LeaderByScore()
        {
            int best = -1;
            for (int k = 0; k < _teams; k++)
            {
                if (_teamScore[k] == 0) continue;
                if (best < 0 || _teamScore[k] > _teamScore[best] || (_teamScore[k] == _teamScore[best] && _teamReachTick[k] < _teamReachTick[best])) best = k;
            }
            return best;
        }

        void SampleStandings(int tick)
        {
            if (tick % SimConstants.TickRate != 0) return;
            int leader = -1, last = -1;
            int max = int.MinValue, min = int.MaxValue;
            int maxCount = 0, minCount = 0;
            for (int k = 0; k < _teams; k++)
            {
                int s = _teamScore[k];
                if (s > max)
                {
                    max = s;
                    leader = k;
                    maxCount = 1;
                }
                else if (s == max)
                {
                    maxCount++;
                }
                if (s < min)
                {
                    min = s;
                    last = k;
                    minCount = 1;
                }
                else if (s == min)
                {
                    minCount++;
                }
            }
            _metrics.SampleStandings(tick, maxCount == 1 ? leader : -1, minCount == 1 ? last : -1);
        }

        /// <summary>
        /// Escaped balls — and, once the run is decided, every ball — are removed once farther than 12 m from the centre (card
        /// «Поток рендера»), so nothing falls on outside the stream's range.
        /// </summary>
        void RemoveFarBalls()
        {
            int balls = _world.BallCount;
            float far = RingEscapeGeometry.MaxRingRadius;
            bool decided = _phase != SimPhase.Running;
            for (int id = _roundFirstBall; id < balls; id++)
            {
                ref readonly BallState s = ref _world.GetBall(id);
                if (!s.Alive || !(decided || _ballEscaped[id])) continue;
                if (s.Position.Length - s.Radius > far) _world.KillBall(id);
            }
        }

        /// <summary>«Новый раунд» / «Новый шар»: fresh gap angles from the rule stream, rings back at their start radii, balls relaunched.</summary>
        void StartNewRound(int tick)
        {
            _round++;
            _current = 0;
            _ringsPassedRound = 0;
            _lastBreakTick = tick;
            for (int j = 0; j < _ringCount; j++)
            {
                _ringPassed[j] = false;
                _ringDestroyed[j] = false;
                _ringRadius[j] = _ringBaseRadius[j];
                int body = _ringBody[j];
                _world.SetBodyScale(body, _ringBaseRadius[j] / ReferenceRadius);
                _world.SetBodyScaleVelocity(body, 0f);
                bool inSolver = _cfg.Progression != RingProgression.Compress || _ringBaseRadius[j] <= InSolverRadius;
                _ringInSolver[j] = inSolver;
                _world.SetBodyEnabled(body, inSolver);
            }
            PlaceGaps(ref _rng, true);
            int count = _cfg.BallCount;
            if (_cfg.AfterEscape == AfterEscapeKind.NewBall)
            {
                int capacity = RingEscapeGeometry.StartCapacity(_cfg.BallSize, _cfg.InnerRadius, _cfg.RingThickness);
                // Constrain keeps maxRoundBalls ≥ ballCount; the guard covers sets that bypassed it.
                int max = Math.Max(_cfg.MaxRoundBalls, _cfg.BallCount);
                count = Math.Min(Math.Min(_cfg.BallCount + _round, max), capacity);
            }
            RespawnBalls(count, tick);
        }

        /// <summary>
        /// Puts the round's balls back on the start layout and relaunches them, reusing the existing ball ids (rounds never kill
        /// balls: the new round starts in the tick of the escape, before the escaped ball is 12 m out) and adding balls only for
        /// «Новый шар».
        /// </summary>
        void RespawnBalls(int count, int tick)
        {
            int placed = RingEscapeGeometry.Layout(_cfg.BallSize, _cfg.InnerRadius, _cfg.RingThickness, count, _layout);
            int existing = _world.BallCount - _roundFirstBall;
            bool reuse = true;
            for (int k = 0; k < Math.Min(existing, placed); k++) reuse &= _world.GetBall(_roundFirstBall + k).Alive;
            if (!reuse)
            {
                // A ball of the round is gone: start a fresh contiguous block of balls.
                for (int id = _roundFirstBall; id < _world.BallCount; id++)
                {
                    if (_world.GetBall(id).Alive) _world.KillBall(id);
                }
                _roundFirstBall = _world.BallCount;
                existing = 0;
            }
            for (int k = 0; k < placed; k++)
            {
                int id;
                if (k < existing)
                {
                    id = _roundFirstBall + k;
                    _world.SetBallPosition(id, _layout[k]);
                    _world.SetBallRadius(id, _cfg.BallSize * 0.5f);
                    _world.SetBallVelocity(id, LaunchVelocity());
                    _world.SetBallAngularVelocity(id, 0f);
                    _world.SetBallGravityScale(id, 1f);
                }
                else
                {
                    var def = BallDef.Create(_layout[k], _cfg.BallSize * 0.5f);
                    def.Restitution = _cfg.Restitution;
                    def.Friction = _cfg.Friction;
                    def.Tag = k % _teams;
                    def.Velocity = LaunchVelocity();
                    id = _world.AddBall(def);
                    EnsureBallArrays(id + 1);
                }
                _ballTeam[id] = k % _teams;
                _ballSlot[id] = k;
                _ballEscaped[id] = false;
                _ballCombo[id] = 0;
                if (_events.WantsRender)
                {
                    _events.AddRender(RenderEvent.Spawn(tick, 0, id, _layout[k].X, _layout[k].Y, _cfg.BallSize * 0.5f, (byte)_ballTeam[id]));
                }
            }
            // Balls beyond the layout (none in practice) leave the round.
            for (int id = _roundFirstBall + placed; id < _world.BallCount; id++)
            {
                if (_world.GetBall(id).Alive) _world.KillBall(id);
            }
            _roundBallCount = placed;
        }
    }
}
