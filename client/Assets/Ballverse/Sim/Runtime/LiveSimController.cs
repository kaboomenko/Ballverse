using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Sim;

namespace Ballverse.Sim
{
    /// <summary>
    /// Where a live run is recorded (the App adapts Studio's <c>RunRecording</c>, spec §3.1 "последние 5 запусков"): the
    /// runner writes the replay, render stream and render events to these absolute paths, then commits or abandons.
    /// </summary>
    public interface ILiveRunRecording
    {
        string ReplayPath { get; }
        string RenderStreamPath { get; }
        string EventsPath { get; }

        /// <summary>The run joins the ring of recent runs; returns its id.</summary>
        string Commit(string modeId, int durationTicks, string winnerId);

        void Abandon();
    }

    /// <summary>Receives the sound events of a live run (live audio through Core <c>EventMixer</c>).</summary>
    public interface ILiveSoundSink
    {
        /// <summary>A new run starts: its events start at run frame 0.</summary>
        void BeginRun();

        /// <summary>
        /// A sound event of the tick just simulated. <paramref name="runFrame"/> is its time since the run started on the
        /// real-time output timeline (48 kHz frames, the time scale applied).
        /// </summary>
        void OnSound(in SoundEvent e, long runFrame);
    }

    /// <summary>Settings of a <see cref="LiveSimController"/>.</summary>
    public sealed class LiveSimOptions
    {
        /// <summary>Tier live edits are sanitized for.</summary>
        public DeviceTier Tier = DeviceTier.B;

        /// <summary>Written to the replay header.</summary>
        public string AppVersion = "";

        /// <summary>Past frames kept for trails and camera smoothing.</summary>
        public int FrameHistory = 240;

        /// <summary>Catch-up cap: ticks run in one display frame at most (the rest is dropped, the run slows down).</summary>
        public int MaxStepsPerDisplayFrame = 12;

        /// <summary>
        /// A run that did not reach its finale is kept in the recent-runs ring only from this length on (a finished run, or an
        /// explicit "keep" on end, is always kept).
        /// </summary>
        public int MinKeptTicks = 3 * SimConstants.TickRate;

        /// <summary>
        /// Mode-owned gesture → <c>SimInput</c> mappings (CONTRACT.md §3 rule 5). A mode without a mapper receives no player
        /// input. Default: the release-1 mappers (M05 Helix Drop).
        /// </summary>
        public ArenaInputMapperRegistry InputMappers = ArenaInputMapperRegistry.CreateRelease1();
    }

    /// <summary>What a live run produced.</summary>
    public sealed class LiveRunOutcome
    {
        internal LiveRunOutcome(string modeId, ParamSet values, ulong seed, ModeSessionOutcome session, string runId, bool finished)
        {
            ModeId = modeId;
            Values = values;
            Seed = seed;
            Session = session;
            RunId = runId;
            Finished = finished;
        }

        public string ModeId { get; }

        /// <summary>Parameters at the end (start values plus live edits).</summary>
        public ParamSet Values { get; }

        public ulong Seed { get; }

        public ModeSessionOutcome Session { get; }

        /// <summary>Id in the recent-runs ring, or null when the run was not kept (too short, recording failed).</summary>
        public string RunId { get; }

        /// <summary>The rules finished (or the 180 s cap was hit).</summary>
        public bool Finished { get; }

        public int Ticks => Session != null ? Session.Ticks : 0;

        public GameRunSummary Run => Session != null ? Session.Run : default;

        public SimResult Result => Session != null ? Session.Result : SimResult.None;
    }

    /// <summary>
    /// Real-time driver of one Core <see cref="ModeSession"/> at a time (spec §2.1, §7.11 main thread): steps the session per
    /// display frame with the time scale as the number of ticks per display frame (never the step size), feeds arena gestures as
    /// inputs at tick boundaries, keeps a ring of recent frames for the renderer (trails, camera), forwards sound events to the
    /// live audio and haptics, records the run (replay + render stream + render events) and commits it to the recent-runs ring.
    /// <para>Steady state allocates nothing beyond what the session and its writers do (sound list and frame ring are reused).
    /// Main thread only.</para>
    /// </summary>
    public sealed class LiveSimController : IDisposable
    {
        readonly LiveSimOptions _options;
        readonly ILiveSoundSink _sound;
        readonly ILiveSoundSink _haptics;
        readonly List<SoundEvent> _soundScratch = new List<SoundEvent>(256);
        readonly RenderEventLog _events = new RenderEventLog(1024);
        readonly ArenaGestureTracker _gestures = new ArenaGestureTracker();
        readonly ShapeTableView _shapes;
        readonly Func<int, string> _labels;
        RenderFrameRing _ring;
        ModeSession _session;
        ILiveRunRecording _recording;
        Stream _replayStream;
        Stream _streamStream;
        long _accumulator;
        double _runFrame;
        bool _finishRaised;
        bool _completed;
        bool _interactive;
        IArenaInputMapper _mapper;
        LiveRunOutcome _outcome;
        GameRunKind _kind;

        public LiveSimController(LiveSimOptions options = null, ILiveSoundSink sound = null, ILiveSoundSink haptics = null)
        {
            _options = options ?? new LiveSimOptions();
            _sound = sound;
            _haptics = haptics;
            _shapes = new ShapeTableView(this);
            _labels = LookupLabel;
            _ring = new RenderFrameRing(Math.Max(2, _options.FrameHistory));
        }

        /// <summary>The rules finished; the run is already completed and committed (raised once per run, main thread).</summary>
        public event Action<LiveRunOutcome> RunFinished;

        /// <summary>A recoverable error (recording could not be written, …): context and exception.</summary>
        public event Action<string, Exception> Error;

        public LiveSimOptions Options => _options;

        public bool HasRun => _session != null;

        /// <summary>Incremented by every <see cref="Begin"/> (the view rebinds its stream caches).</summary>
        public int RunGeneration { get; private set; }

        public IModeDefinition Mode => _session?.Mode;

        public ModeSession Session => _session;

        /// <summary>Current parameters (start values with applied live edits).</summary>
        public ParamSet Values => _session?.Values;

        public ulong Seed => _session != null ? _session.Seed : 0UL;

        public int Tick => _session != null ? _session.Tick : 0;

        public SimPhase Phase => _session != null ? _session.Phase : SimPhase.Running;

        public bool IsFinished => _session != null && (_session.IsFinished || _completed);

        public bool IsCompleted => _completed;

        /// <summary>Stops stepping (display keeps the last frame); does not end the run.</summary>
        public bool Paused { get; set; }

        /// <summary>The run accepts arena gestures (interactive mode with an input mapper that accepts the run's parameters).</summary>
        public bool AcceptsInput => _session != null && _interactive && !_completed;

        /// <summary>Displayed arena width in points (iOS pt / Android dp) used to turn finger speed into mode units.</summary>
        public float PointsPerArenaWidth { get; set; } = 390f;

        /// <summary>Whether a run of <paramref name="mode"/> with <paramref name="values"/> takes player input (spec §2.6).</summary>
        public bool TakesPlayerInput(IModeDefinition mode, ParamSet values)
        {
            if (mode == null || !mode.Info.Interactive) return false;
            IArenaInputMapper mapper = _options.InputMappers?.Find(mode.Info.Id);
            return mapper != null && mapper.AcceptsInput(values);
        }

        /// <summary>Playback speed 0.25×–3× (ticks per display frame).</summary>
        public float TimeScale => _session != null ? _session.TimeScale : 1f;

        /// <summary>Frame of the last simulated tick (reused instance).</summary>
        public RenderFrame Frame => _session?.Frame;

        public RenderFrameRing FrameRing => _ring;

        /// <summary>Render events of the run (VFX); cleared when the next run begins.</summary>
        public RenderEventLog Events => _events;

        /// <summary>Live view of the run's shape table (grows when the scene defines shapes).</summary>
        public IReadOnlyList<ShapeDefinition> Shapes => _shapes;

        /// <summary>Label lookup of the run (cached delegate).</summary>
        public Func<int, string> Labels => _labels;

        public RenderQuantization Quantization => _session != null ? _session.Scene.Quantization : RenderQuantization.Default;

        /// <summary>Outcome of the current run once it was completed, else null.</summary>
        public LiveRunOutcome Outcome => _outcome;

        /// <summary>
        /// Starts a run (the previous one must have been ended with <see cref="End"/>, otherwise it is abandoned and not kept):
        /// builds the scene, opens the recording streams (a failure is reported and the run continues unrecorded) and writes
        /// frame 0.
        /// </summary>
        /// <exception cref="ArgumentException">The values are not runnable for this mode.</exception>
        public void Begin(IModeDefinition mode, ParamSet values, ulong seed, ILiveRunRecording recording, GameRunKind kind = GameRunKind.Regular)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (_session != null && !_completed) End(GameRunEnd.Abandoned, false);
            ResetRunState();

            _recording = recording;
            if (_recording != null && !OpenRecording())
            {
                SafeAbandon();
                _recording = null;
            }
            var options = new ModeSessionOptions
            {
                RenderStream = _streamStream,
                Replay = _replayStream,
                RenderEvents = _events,
                SoundEvents = _soundScratch,
                AppVersion = _options.AppVersion ?? "",
                RunKind = kind,
                Tier = _options.Tier,
                BuildFrames = true,
                Runner = new SimRunnerOptions { MaxStepsPerDisplayFrame = Math.Max(1, _options.MaxStepsPerDisplayFrame) },
            };
            try
            {
                _session = new ModeSession(mode, values, seed, options);
                _session.Start();
            }
            catch
            {
                _session = null;
                CloseStreams();
                SafeAbandon();
                _recording = null;
                throw;
            }
            _kind = kind;
            _mapper = _options.InputMappers?.Find(mode.Info.Id);
            _interactive = mode.Info.Interactive && _mapper != null && _mapper.AcceptsInput(values);
            if (_interactive) _mapper.BeginRun();
            RunGeneration++;
            _sound?.BeginRun();
            _haptics?.BeginRun();
            _ring.Push(_session.Tick, _session.Frame);
            ForwardSounds();
        }

        /// <summary>
        /// Runs the ticks due for one display frame at <paramref name="displayFrameRate"/> Hz: pending gestures are enqueued at
        /// the tick boundary first; each tick's frame goes to the ring and its sounds to the sinks. When the rules finish the run
        /// is completed, committed and <see cref="RunFinished"/> is raised. Returns the ticks run.
        /// <para>This reimplements Core <c>ModeSession.AdvanceDisplayFrame</c>'s integer accumulator (time scale × tick rate per
        /// display frame, same catch-up cap) instead of calling it, because the live runtime needs a hook after every single
        /// tick — the frame ring for trails / camera, and the sound events placed on the audio timeline at that tick — which the
        /// Core method does not expose. Keep the two rules identical (a pacing test compares them). Pass the measured display
        /// rate (<see cref="DisplayRateEstimator"/>), never the target rate.</para>
        /// </summary>
        public int AdvanceDisplayFrame(int displayFrameRate, double nowSeconds)
        {
            if (displayFrameRate <= 0) throw new ArgumentOutOfRangeException(nameof(displayFrameRate));
            if (_session == null || _completed || Paused) return 0;
            FlushInputs(nowSeconds);
            long perTick = 1000L * displayFrameRate;
            int scaleMilli = (int)Math.Round(_session.TimeScale * 1000f);
            _accumulator += (long)scaleMilli * SimConstants.TickRate;
            int steps = 0;
            int max = Math.Max(1, _options.MaxStepsPerDisplayFrame);
            while (_accumulator >= perTick)
            {
                if (steps >= max)
                {
                    _accumulator = 0;
                    break;
                }
                if (!_session.Step())
                {
                    _accumulator = 0;
                    break;
                }
                _accumulator -= perTick;
                steps++;
                _runFrame += AudioConstants.FramesPerTick / Math.Max(0.25, (double)_session.TimeScale);
                _ring.Push(_session.Tick, _session.Frame);
                ForwardSounds();
            }
            if (_session.IsFinished && !_finishRaised)
            {
                _finishRaised = true;
                LiveRunOutcome outcome = CompleteRun(null, true);
                RunFinished?.Invoke(outcome);
            }
            return steps;
        }

        /// <summary>
        /// A live parameter edit (spec §2.2): sanitized, clamped to the mode's dynamic range, applied at the next tick boundary
        /// and recorded (PARM); <c>timeScale</c> changes the pacing at once. Returns the value as applied.
        /// </summary>
        /// <exception cref="InvalidOperationException">No running run.</exception>
        /// <exception cref="ArgumentException">Unknown or structural parameter.</exception>
        public ParamValue SetLiveParam(string id, ParamValue value)
        {
            if (_session == null || _completed) throw new InvalidOperationException("No running run");
            return _session.SetLiveParam(id, value);
        }

        /// <summary>Changes the pacing only (no PARM record); for modes whose schema has no live <c>timeScale</c>.</summary>
        public void SetPacing(float timeScale)
        {
            if (_session != null) _session.TimeScale = timeScale;
        }

        /// <summary>An arena touch (normalized over the render target, origin bottom-left).</summary>
        public void OnTouch(ArenaTouchPhase phase, float u, float v, int pointerId, double timeSeconds)
        {
            if (!AcceptsInput) return;
            _gestures.OnTouch(phase, u, v, pointerId, timeSeconds);
        }

        /// <summary>
        /// Ends the current run (idempotent): completes the session with <paramref name="end"/> (null = Finished when the rules
        /// finished, else Abandoned), closes the recording and keeps the run in the recent-runs ring when it finished, when
        /// <paramref name="keep"/> is set (e.g. "В видео" before the finale) or when it lasted at least
        /// <see cref="LiveSimOptions.MinKeptTicks"/>. Returns the outcome, or null when there is no run.
        /// </summary>
        public LiveRunOutcome End(GameRunEnd? end, bool keep)
        {
            if (_session == null) return null;
            if (_completed) return _outcome;
            return CompleteRun(end, keep);
        }

        /// <summary>
        /// The touch is lost (screen hidden, app paused): drops pending gestures and, when a finger was down, sends a zero-speed
        /// release at the next tick boundary (<see cref="ArenaGestureTracker.CancelTouch"/>).
        /// </summary>
        public void ResetInput()
        {
            _gestures.CancelTouch();
        }

        public void Dispose()
        {
            if (_session != null && !_completed) End(GameRunEnd.Abandoned, false);
            CloseStreams();
        }

        // ------------------------------------------------------------------ internals

        void ResetRunState()
        {
            _session = null;
            _outcome = null;
            _completed = false;
            _finishRaised = false;
            _accumulator = 0;
            _runFrame = 0;
            _soundScratch.Clear();
            _events.Clear();
            _ring.Clear();
            _gestures.Reset();
            Paused = false;
        }

        void FlushInputs(double nowSeconds)
        {
            if (!_interactive) return;
            _gestures.Update(nowSeconds);
            float points = PointsPerArenaWidth > 1f ? PointsPerArenaWidth : 390f;
            while (_gestures.TryDequeue(out ArenaGesture g))
            {
                if (!_mapper.TryMap(in g, _session.Values, points, out ArenaCommand c)) continue;
                try
                {
                    _session.EnqueueInput(c.Type, c.A, c.B, c.C, c.D);
                }
                catch (ArgumentException e)
                {
                    // The runner refused the input (its tick was already consumed): drop it, keep playing.
                    Error?.Invoke("sim.input", e);
                    break;
                }
            }
        }

        void ForwardSounds()
        {
            int n = _soundScratch.Count;
            if (n == 0) return;
            long frame = (long)_runFrame;
            for (int i = 0; i < n; i++)
            {
                SoundEvent e = _soundScratch[i];
                if (_sound != null) _sound.OnSound(in e, frame);
                if (_haptics != null) _haptics.OnSound(in e, frame);
            }
            _soundScratch.Clear();
        }

        LiveRunOutcome CompleteRun(GameRunEnd? end, bool keep)
        {
            ModeSession session = _session;
            _completed = true;
            _gestures.Reset();
            ModeSessionOutcome result = null;
            try
            {
                result = session.Complete(end);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is NotSupportedException)
            {
                Error?.Invoke("sim.complete", e);
            }
            CloseStreams();

            bool finished = session.Runner.FinishedTick >= 0 || session.IsFinished;
            string runId = null;
            ILiveRunRecording recording = _recording;
            _recording = null;
            if (recording != null)
            {
                bool kept = result != null && (finished || keep || session.Tick >= _options.MinKeptTicks) && session.Tick > 0;
                if (kept && WriteEvents(recording.EventsPath))
                {
                    try
                    {
                        string winner = result.ReplayResult != null && result.ReplayResult.WinnerId.Length > 0 ? result.ReplayResult.WinnerId : null;
                        runId = recording.Commit(session.Mode.Info.Id, session.Tick, winner);
                    }
                    catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException || e is ArgumentException)
                    {
                        Error?.Invoke("sim.commit", e);
                        runId = null;
                    }
                }
                if (runId == null) SafeAbandon(recording);
            }
            _outcome = new LiveRunOutcome(session.Mode.Info.Id, session.Values, session.Seed, result, runId, finished);
            return _outcome;
        }

        bool WriteEvents(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
                {
                    _events.WriteTo(fs);
                }
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Error?.Invoke("sim.events", e);
                return false;
            }
        }

        bool OpenRecording()
        {
            try
            {
                _replayStream = new FileStream(_recording.ReplayPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 16 * 1024);
                _streamStream = new FileStream(_recording.RenderStreamPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 64 * 1024);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException)
            {
                CloseStreams();
                Error?.Invoke("sim.record", e);
                return false;
            }
        }

        void CloseStreams()
        {
            try
            {
                _replayStream?.Dispose();
                _streamStream?.Dispose();
            }
            catch (IOException e)
            {
                Error?.Invoke("sim.close", e);
            }
            _replayStream = null;
            _streamStream = null;
        }

        void SafeAbandon(ILiveRunRecording recording = null)
        {
            recording = recording ?? _recording;
            if (recording == null) return;
            try
            {
                recording.Abandon();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                Error?.Invoke("sim.abandon", e);
            }
        }

        string LookupLabel(int id)
        {
            ModeSession s = _session;
            if (s == null || id < 1 || id > s.Visuals.LabelCount) return null;
            return s.Visuals.GetLabel(id);
        }

        /// <summary>Allocation-free read-only list over the current run's <see cref="ModeVisualTable"/> shapes.</summary>
        sealed class ShapeTableView : IReadOnlyList<ShapeDefinition>
        {
            readonly LiveSimController _owner;

            public ShapeTableView(LiveSimController owner)
            {
                _owner = owner;
            }

            public int Count => _owner._session != null ? _owner._session.Visuals.ShapeCount : 0;

            public ShapeDefinition this[int index]
            {
                get
                {
                    ModeSession s = _owner._session;
                    if (s == null) throw new ArgumentOutOfRangeException(nameof(index));
                    return s.Visuals.GetShape(index);
                }
            }

            public IEnumerator<ShapeDefinition> GetEnumerator()
            {
                int n = Count;
                for (int i = 0; i < n; i++) yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
