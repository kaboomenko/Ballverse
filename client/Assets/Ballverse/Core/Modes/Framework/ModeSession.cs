using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>Sinks and settings of a <see cref="ModeSession"/>. Every sink is optional.</summary>
    public sealed class ModeSessionOptions
    {
        /// <summary>Destination of the <c>.bvs</c> render stream (spec §7.4), written frame by frame; null = none.</summary>
        public Stream RenderStream;
        /// <summary>Leave <see cref="RenderStream"/> open after <see cref="ModeSession.Complete"/> (default true).</summary>
        public bool LeaveRenderStreamOpen = true;
        /// <summary>Destination of the <c>.bvr</c> replay (never closed by the session); null = none.</summary>
        public Stream Replay;
        /// <summary>Render events for VFX (clip.bve, RenderEventContract.md); null = none.</summary>
        public RenderEventLog RenderEvents;
        /// <summary>Sound events for the mixer (spec §2.7); null = none.</summary>
        public List<SoundEvent> SoundEvents;
        /// <summary>Every state hash computed (every 60 ticks), with or without a replay writer; null = none.</summary>
        public List<ReplayStateHash> HashLog;
        /// <summary>Application version written to the replay header (printable ASCII ≤ 64).</summary>
        public string AppVersion = "";
        /// <summary>Kind of run for the GameCompleted rule (spec §2.1, §5.3.2).</summary>
        public GameRunKind RunKind = GameRunKind.Regular;
        /// <summary>Tier live edits are sanitized for (the user's device).</summary>
        public DeviceTier Tier = DeviceTier.A;
        /// <summary>
        /// Runner limits (180 s cap, finale reserve, catch-up cap, continue after finish); null = defaults.
        /// <para><see cref="SimRunnerOptions.MaxTicks"/> and <see cref="SimRunnerOptions.FinaleReserveTicks"/> change the
        /// simulation and are not stored in a <c>.bvr</c>, so a session with a <see cref="Replay"/> sink requires their defaults
        /// (the constructor throws otherwise). With <see cref="SimRunnerOptions.ContinueAfterFinish"/> the background keeps
        /// playing for the live view (<see cref="ModeSession.Frame"/>), but every recording sink (replay, render stream,
        /// render-event log, sound list, hash log) stops at the tick the rules finished (spec §2.1: Finished closes the
        /// replay), so a re-simulation reproduces exactly what was recorded.</para>
        /// </summary>
        public SimRunnerOptions Runner;
        /// <summary>
        /// Write a <see cref="RenderFrame"/> after every tick (live preview). Forced on when <see cref="RenderStream"/> is set;
        /// headless runs without a stream may turn it off.
        /// </summary>
        public bool BuildFrames = true;
    }

    /// <summary>What <see cref="ModeSession.Complete"/> returns.</summary>
    public sealed class ModeSessionOutcome
    {
        internal ModeSessionOutcome(ReplayHeader replayHeader, GameRunSummary run, SimResult result, ReplayResult replayResult, int ticks)
        {
            ReplayHeader = replayHeader;
            Run = run;
            Result = result;
            ReplayResult = replayResult;
            Ticks = ticks;
        }

        /// <summary>Final replay header (duration patched), or null without a replay sink.</summary>
        public ReplayHeader ReplayHeader { get; }

        /// <summary>Summary for the ads <c>GameCompleted</c> rule.</summary>
        public GameRunSummary Run { get; }

        public bool IsGameCompleted => GameCompletedRule.IsGameCompleted(Run);

        public SimResult Result { get; }

        /// <summary>The RSLT record as written (null when the run ended before any result, e.g. abandoned while running).</summary>
        public ReplayResult ReplayResult { get; }

        /// <summary>Ticks simulated.</summary>
        public int Ticks { get; }
    }

    /// <summary>
    /// Runs one mode definition with sanitized parameters and a seed through <see cref="SimRunner"/> and feeds the optional
    /// sinks (spec §2.1 life cycle, §7.4 recording): render stream (shape table from the scene's visual table, labels, a frame
    /// per tick), render-event log, sound events, replay (header with canonical params JSON / hash, PhysicsBackend.CoreSim,
    /// INPT, PARM, HASH every 60 ticks, EVNT, RSLT) and the ads <see cref="GameRunSummary"/>.
    /// <para>Live mode: <see cref="AdvanceDisplayFrame"/> once per display frame (steps per frame follow the time scale, never
    /// the step size, spec §2.1). Headless: <see cref="Step"/> / <see cref="RunHeadless"/>. Both give identical results.</para>
    /// <para>Single-threaded. Without stream sinks, steady-state ticks do not allocate (sink lists grow amortized).</para>
    /// </summary>
    public sealed class ModeSession
    {
        readonly ModeSessionOptions _options;
        readonly SimRunnerOptions _runnerOptions;
        readonly ModeEventBuffer _events;
        readonly RenderStreamWriter _renderWriter;
        readonly ReplayWriter _replayWriter;
        readonly int[] _inputScratch = new int[4];
        readonly List<KeyValuePair<string, ParamValue>> _pendingParams = new List<KeyValuePair<string, ParamValue>>();
        readonly bool _buildFrames;
        bool _started;
        bool _completed;
        int _timeScaleMilli = 1000;
        long _accumulator;
        ModeSessionOutcome _outcome;
        // Result as of the tick the rules finished (ContinueAfterFinish keeps simulating the background after it).
        ReplayResult _finishedReplayResult;
        SimResult _finishedResult;
        bool _finishedCaptured;

        /// <summary>Builds the scene (nothing is simulated yet; frame 0 is written by <see cref="Start"/> or the first step).</summary>
        /// <exception cref="ArgumentException">The values are not runnable (<see cref="ModeParams.IsRunnable"/>).</exception>
        public ModeSession(IModeDefinition mode, ParamSet values, ulong seed, ModeSessionOptions options = null)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (!ModeParams.IsRunnable(mode, values, out string problem)) throw new ArgumentException("Parameters are " + problem, nameof(values));
            _options = options ?? new ModeSessionOptions();
            _runnerOptions = _options.Runner ?? new SimRunnerOptions();
            if (_options.Replay != null)
            {
                var defaults = new SimRunnerOptions();
                if (_runnerOptions.MaxTicks != defaults.MaxTicks || _runnerOptions.FinaleReserveTicks != defaults.FinaleReserveTicks)
                    throw new ArgumentException("Runner MaxTicks / FinaleReserveTicks are not stored in a replay: a session with a Replay sink must use their defaults",
                        nameof(options));
            }
            Values = values;
            Seed = seed;
            _events = new ModeEventBuffer
            {
                WantsRender = _options.RenderEvents != null,
                WantsSound = _options.SoundEvents != null,
                WantsReplay = _options.Replay != null,
            };
            Visuals = new ModeVisualTable();
            World = new PhysicsWorld();
            Scene = mode.BuildScene(World, new ModeSceneContext(mode, values, seed, Visuals, _events));
            if (Scene == null) throw new InvalidOperationException("Mode " + mode.Info.Id + " built no scene");
            Runner = new SimRunner(World, Scene, _runnerOptions);
            _buildFrames = _options.BuildFrames || _options.RenderStream != null;
            Frame = new RenderFrame();
            if (values.TryGet(CommonParams.TimeScale, out ParamValue ts)) TimeScale = (float)ts.AsFloat;

            if (_options.Replay != null)
            {
                ReplayHeader header = ReplayParams.CreateHeader(mode.Info.Id, mode.SimVersion, _options.AppVersion ?? "",
                    PhysicsBackend.CoreSim, seed, values, mode.Schema);
                _replayWriter = new ReplayWriter(_options.Replay, header);
            }
            if (_options.RenderStream != null)
            {
                var header = new RenderStreamHeader(mode.Info.Id, SimConstants.TickRate, RenderStreamHeader.DefaultBlockTicks, Scene.Quantization);
                _renderWriter = new RenderStreamWriter(_options.RenderStream, header, _options.LeaveRenderStreamOpen);
            }
        }

        public IModeDefinition Mode { get; }

        public IModeScene Scene { get; }

        public PhysicsWorld World { get; }

        public SimRunner Runner { get; }

        public ModeVisualTable Visuals { get; }

        /// <summary>Current parameters: the start values with every applied live change.</summary>
        public ParamSet Values { get; private set; }

        public ulong Seed { get; }

        /// <summary>Frame of the last completed tick (<see cref="Tick"/>), rewritten every tick when frames are built.</summary>
        public RenderFrame Frame { get; }

        public int Tick => World.Tick;

        public SimPhase Phase => Runner.Phase;

        /// <summary>No more ticks will run (the rules finished or the 180 s cap was hit).</summary>
        public bool IsFinished => Runner.IsFinished;

        public bool IsCompleted => _completed;

        /// <summary>
        /// The recording sinks still receive data: false after the tick the rules finished (only reachable with
        /// <see cref="SimRunnerOptions.ContinueAfterFinish"/>, whose background play-out is live-only).
        /// </summary>
        public bool IsRecording => Runner.FinishedTick < 0 || World.Tick <= Runner.FinishedTick;

        public bool Paused { get; set; }

        /// <summary>Last state hash computed (every 60 ticks), and its tick (−1 before the first).</summary>
        public ulong LastHash { get; private set; }

        public int LastHashTick { get; private set; } = -1;

        /// <summary>Playback speed 0.25×–3× (live parameter <c>timeScale</c>): ticks per display frame, never the step size.</summary>
        public float TimeScale
        {
            get => _timeScaleMilli / 1000f;
            set
            {
                if (!SimMath.IsFinite(value)) throw new ArgumentException("Time scale must be finite");
                float v = SimMath.Clamp(value, SimRunner.MinTimeScale, SimRunner.MaxTimeScale);
                _timeScaleMilli = (int)MathF.Round(v * 1000f);
            }
        }

        /// <summary>Runs <see cref="IModeRules.Start"/> and records frame 0 (idempotent; the first step does it otherwise).</summary>
        public void Start()
        {
            if (_started) return;
            ThrowIfCompleted();
            _started = true;
            _events.Tick = 0;
            Runner.Start();
            Flush();
        }

        /// <summary>Runs one tick (live changes due now are applied first). False when the run is over.</summary>
        public bool Step()
        {
            Start();
            ThrowIfCompleted();
            if (Runner.IsFinished) return false;
            ApplyPendingParams();
            _events.Tick = World.Tick + 1;
            if (!Runner.Step()) return false;
            if (!_finishedCaptured && Runner.FinishedTick >= 0)
            {
                // Spec §2.1: Finished closes the replay; RSLT and the result are those of this tick, not of the play-out.
                _finishedCaptured = true;
                _finishedReplayResult = Scene.BuildReplayResult(Runner);
                _finishedResult = Runner.Result;
            }
            Flush();
            return true;
        }

        /// <summary>
        /// Live pacing: runs the ticks due for one display frame at <paramref name="displayFrameRate"/> Hz and the current time
        /// scale (integer accumulator, the same rule as <see cref="SimRunner.AdvanceDisplayFrame"/>). Returns the ticks run.
        /// </summary>
        public int AdvanceDisplayFrame(int displayFrameRate = SimConstants.TickRate)
        {
            if (displayFrameRate <= 0) throw new ArgumentOutOfRangeException(nameof(displayFrameRate));
            Start();
            if (Paused || Runner.IsFinished || _completed) return 0;
            long perTick = 1000L * displayFrameRate;
            _accumulator += (long)_timeScaleMilli * SimConstants.TickRate;
            int steps = 0;
            while (_accumulator >= perTick)
            {
                if (steps >= _runnerOptions.MaxStepsPerDisplayFrame)
                {
                    _accumulator = 0;
                    break;
                }
                if (!Step())
                {
                    _accumulator = 0;
                    break;
                }
                _accumulator -= perTick;
                steps++;
            }
            return steps;
        }

        /// <summary>Headless: up to <paramref name="maxTicks"/> ticks or until the run is over. Returns the ticks run.</summary>
        public int RunHeadless(int maxTicks)
        {
            if (maxTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxTicks));
            int n = 0;
            while (n < maxTicks && Step()) n++;
            return n;
        }

        /// <summary>Runs until the rules finish or the 180 s cap. Returns the ticks run.</summary>
        public int RunToEnd()
        {
            return RunHeadless(int.MaxValue);
        }

        /// <summary>
        /// Live edit while running (spec §2.2 panel rules): sanitized for the session tier, clamped to the mode's dynamic range,
        /// applied at the next tick boundary and recorded as PARM. Returns the value as it will be applied.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown or structural parameter (a structural change restarts the run).</exception>
        public ParamValue SetLiveParam(string paramId, ParamValue value)
        {
            ThrowIfCompleted();
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (paramId == null || !Mode.Schema.TryGet(paramId, out ParamDef def)) throw new ArgumentException("Unknown parameter " + paramId, nameof(paramId));
            if (!def.Live) throw new ArgumentException("Parameter " + paramId + " is structural; changing it restarts the run (spec §2.2)", nameof(paramId));
            ParamValue v = Mode.Schema.SanitizeValue(paramId, value, _options.Tier);
            if (v.Kind == ParamValueKind.Float || v.Kind == ParamValueKind.Int)
            {
                ParamSet probe = PendingValues().With(paramId, v);
                if (Mode.TryGetDynamicRange(paramId, probe, _options.Tier, out double min, out double max))
                {
                    double x = v.Kind == ParamValueKind.Float ? v.AsFloat : v.AsInt;
                    if (x < min || x > max)
                    {
                        double c = x < min ? min : max;
                        ParamValue clamped = v.Kind == ParamValueKind.Float ? ParamValue.Float(c) : ParamValue.Int((long)Math.Round(c));
                        v = Mode.Schema.SanitizeValue(paramId, clamped, _options.Tier);
                    }
                }
            }
            // Pacing follows the time scale at once (it never changes the simulation); the PARM record lands at the next tick.
            if (string.Equals(paramId, CommonParams.TimeScale, StringComparison.Ordinal)) TimeScale = (float)v.AsFloat;
            QueueParam(paramId, v);
            return v;
        }

        /// <summary>Queues a player input for the next tick and records it (INPT). Returns that tick.</summary>
        public int EnqueueInput(byte type, int a = 0, int b = 0, int c = 0, int d = 0)
        {
            ThrowIfCompleted();
            int tick = Runner.EnqueueInputNow(type, a, b, c, d);
            RecordInput(tick, type, a, b, c, d);
            return tick;
        }

        /// <summary>
        /// Ends the session: writes RSLT (when the run reached its finale or the cap) and finishes the replay and render stream.
        /// <paramref name="end"/> overrides how the run ended for the ads rule (e.g. <see cref="GameRunEnd.RestartedBeforeFinale"/>);
        /// by default Finished when the rules finished or the cap was hit, otherwise Abandoned. Idempotent.
        /// </summary>
        public ModeSessionOutcome Complete(GameRunEnd? end = null)
        {
            if (_completed) return _outcome;
            Start();
            _completed = true;
            bool finished = Runner.FinishedTick >= 0 || Runner.IsFinished;
            int duration = Runner.FinishedTick >= 0 ? Runner.FinishedTick : World.Tick;
            GameRunEnd e = end ?? (finished ? GameRunEnd.Finished : GameRunEnd.Abandoned);
            var run = new GameRunSummary(_options.RunKind, e, duration);

            ReplayResult rslt = null;
            if (_finishedCaptured) rslt = _finishedReplayResult;
            else if (Runner.FinaleTick >= 0 || Runner.IsFinished) rslt = Scene.BuildReplayResult(Runner);
            SimResult result = _finishedCaptured ? _finishedResult : Runner.Result;
            ReplayHeader header = null;
            if (_replayWriter != null)
            {
                if (rslt != null) _replayWriter.SetResult(rslt);
                header = _replayWriter.Finish((uint)duration);
            }
            if (_renderWriter != null)
            {
                _renderWriter.Finish();
                _renderWriter.Dispose();
            }
            _outcome = new ModeSessionOutcome(header, run, result, rslt, World.Tick);
            return _outcome;
        }

        // ------------------------------------------------------------------ replay re-simulation hooks

        /// <summary>Ends a session that failed on stored data: releases the render-stream writer without finishing it.</summary>
        internal void Abort()
        {
            if (_completed) return;
            _completed = true;
            if (_renderWriter != null) _renderWriter.Dispose();
        }

        /// <summary>Queues a recorded input at its own tick (replay playback); recorded again when a replay sink is attached.</summary>
        internal void EnqueueRecordedInput(in SimInput input)
        {
            Runner.EnqueueInput(input);
            RecordInput(input.Tick, input.Type, input.A, input.B, input.C, input.D);
        }

        /// <summary>Applies a recorded PARM value at the next tick boundary exactly as stored (no tier caps, no clamping).</summary>
        internal void QueueRecordedParam(string paramId, ParamValue value)
        {
            QueueParam(paramId, value);
        }

        // ------------------------------------------------------------------ internals

        void QueueParam(string paramId, ParamValue value)
        {
            for (int i = 0; i < _pendingParams.Count; i++)
            {
                if (string.Equals(_pendingParams[i].Key, paramId, StringComparison.Ordinal))
                {
                    _pendingParams[i] = new KeyValuePair<string, ParamValue>(paramId, value);
                    return;
                }
            }
            _pendingParams.Add(new KeyValuePair<string, ParamValue>(paramId, value));
        }

        ParamSet PendingValues()
        {
            ParamSet v = Values;
            for (int i = 0; i < _pendingParams.Count; i++) v = v.With(_pendingParams[i].Key, _pendingParams[i].Value);
            return v;
        }

        void ApplyPendingParams()
        {
            if (_pendingParams.Count == 0) return;
            uint tick = (uint)World.Tick;
            for (int i = 0; i < _pendingParams.Count; i++)
            {
                string id = _pendingParams[i].Key;
                ParamValue v = _pendingParams[i].Value;
                Values = Values.With(id, v);
                if (string.Equals(id, CommonParams.TimeScale, StringComparison.Ordinal)) TimeScale = (float)v.AsFloat;
                Scene.ApplyLiveParam(id, v);
                if (_replayWriter != null && Runner.FinishedTick < 0) _replayWriter.AddParamChange(ReplayParams.ParamChange(tick, id, v, Mode.Schema));
            }
            _pendingParams.Clear();
        }

        void RecordInput(int tick, byte type, int a, int b, int c, int d)
        {
            if (_replayWriter == null) return;
            if (Runner.FinishedTick >= 0 && tick > Runner.FinishedTick) return;
            _inputScratch[0] = a;
            _inputScratch[1] = b;
            _inputScratch[2] = c;
            _inputScratch[3] = d;
            _replayWriter.AddInput((uint)tick, type, _inputScratch);
        }

        void Flush()
        {
            int tick = World.Tick;
            if (!IsRecording)
            {
                // Background play-out after Finished (ContinueAfterFinish): live view only, nothing is recorded.
                _events.Clear();
                if (_buildFrames) Scene.WriteFrame(Frame);
                return;
            }
            if (_options.RenderEvents != null)
            {
                ReadOnlySpan<RenderEvent> re = _events.RenderEvents;
                for (int i = 0; i < re.Length; i++) _options.RenderEvents.Add(re[i]);
            }
            if (_options.SoundEvents != null)
            {
                ReadOnlySpan<SoundEvent> se = _events.SoundEvents;
                for (int i = 0; i < se.Length; i++) _options.SoundEvents.Add(se[i]);
            }
            if (_replayWriter != null)
            {
                ReadOnlySpan<ReplayEvent> ev = _events.ReplayEvents;
                for (int i = 0; i < ev.Length; i++) _replayWriter.AddEvent(ev[i]);
            }
            _events.Clear();

            if (_buildFrames)
            {
                Scene.WriteFrame(Frame);
                if (_renderWriter != null)
                {
                    SyncVisuals();
                    _renderWriter.AppendFrame(tick, Frame);
                }
            }

            if (tick > 0 && tick % SimConstants.HashIntervalTicks == 0)
            {
                ulong h = Runner.ComputeStateHash();
                LastHash = h;
                LastHashTick = tick;
                if (_options.HashLog != null) _options.HashLog.Add(new ReplayStateHash((uint)tick, h));
                if (_replayWriter != null) _replayWriter.AddHash((uint)tick, h);
            }
        }

        void SyncVisuals()
        {
            while (_renderWriter.ShapeCount < Visuals.ShapeCount)
            {
                int expected = _renderWriter.ShapeCount;
                int id = _renderWriter.DefineShape(Visuals.GetShape(expected));
                if (id != expected) throw new InvalidOperationException("Shape table out of sync: writer id " + id + ", table id " + expected);
            }
            while (_renderWriter.LabelCount < Visuals.LabelCount)
            {
                int expected = _renderWriter.LabelCount + 1;
                int id = _renderWriter.DefineLabel(Visuals.GetLabel(expected));
                if (id != expected) throw new InvalidOperationException("Label table out of sync: writer id " + id + ", table id " + expected);
            }
        }

        void ThrowIfCompleted()
        {
            if (_completed) throw new InvalidOperationException("The session is completed");
        }
    }
}
