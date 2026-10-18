using System;

namespace Ballverse.Core.Sim
{
    public sealed class SimRunnerOptions
    {
        /// <summary>Hard cap of a run in ticks (spec §2.1: 180 s).</summary>
        public int MaxTicks = SimConstants.MaxDurationTicks;
        /// <summary>The time-limit notice comes this many ticks before <see cref="MaxTicks"/> so a finale fits (default 3 s).</summary>
        public int FinaleReserveTicks = 3 * SimConstants.TickRate;
        /// <summary>Most physics ticks one display frame may run (catch-up cap after a hitch).</summary>
        public int MaxStepsPerDisplayFrame = 12;
        /// <summary>Keep stepping after the rules report Finished ("доигрывать" background, spec §2.1), up to <see cref="MaxTicks"/>.</summary>
        public bool ContinueAfterFinish;

        internal void Validate()
        {
            if (MaxTicks < 1 || MaxTicks > SimConstants.MaxDurationTicks)
                throw new ArgumentException("MaxTicks must be in [1, " + SimConstants.MaxDurationTicks + "]");
            if (FinaleReserveTicks < 0 || FinaleReserveTicks >= MaxTicks) throw new ArgumentException("FinaleReserveTicks must be in [0, MaxTicks)");
            if (MaxStepsPerDisplayFrame < 1) throw new ArgumentException("MaxStepsPerDisplayFrame must be >= 1");
        }
    }

    /// <summary>
    /// Fixed-tick driver of one run: owns the <see cref="PhysicsWorld"/> and the mode's <see cref="IModeRules"/>, applies queued
    /// inputs at tick boundaries, enforces the 180 s cap, paces real-time playback by the time scale (steps per display frame,
    /// spec §2.1) and fast-forwards headless runs for generation candidates. Deterministic: results depend only on the scene,
    /// the rules and the inputs, never on display pacing.
    /// </summary>
    public sealed class SimRunner
    {
        public const float MinTimeScale = 0.25f;
        public const float MaxTimeScale = 3f;

        readonly SimRunnerOptions _options;
        readonly XxHash64 _hasher = new XxHash64();
        SimInput[] _queue = new SimInput[16];
        int _queueHead;
        int _queueEnd;
        int _consumedThrough = -1;
        bool _started;
        bool _inRulesTick;
        bool _hardStopped;
        bool _timeLimitSignalled;
        SimPhase _lastPhase = SimPhase.Running;
        int _timeScaleMilli = 1000;
        long _accumulator;

        public SimRunner(PhysicsWorld world, IModeRules rules, SimRunnerOptions options = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _options = options ?? new SimRunnerOptions();
            _options.Validate();
        }

        public PhysicsWorld World { get; }

        public IModeRules Rules { get; }

        /// <summary>Completed ticks (equals <see cref="PhysicsWorld.Tick"/>).</summary>
        public int Tick => World.Tick;

        public float ElapsedSeconds => World.Tick / (float)SimConstants.TickRate;

        public SimPhase Phase => Rules.Phase;

        public SimResult Result => Rules.Result;

        /// <summary>Tick at which the rules entered Finale, −1 if not yet.</summary>
        public int FinaleTick { get; private set; } = -1;

        /// <summary>Tick at which the rules reported Finished, −1 if not yet.</summary>
        public int FinishedTick { get; private set; } = -1;

        /// <summary>The time-limit notice was given to the rules.</summary>
        public bool TimeLimitReached => _timeLimitSignalled;

        /// <summary>No more ticks will run: the rules finished (and continuing is off) or the hard cap was hit.</summary>
        public bool IsFinished => _hardStopped || (Rules.Phase == SimPhase.Finished && !_options.ContinueAfterFinish);

        public bool Paused { get; set; }

        /// <summary>Playback speed 0.25×–3× (spec §2.2 timeScale): changes steps per display frame, never the step size.</summary>
        public float TimeScale
        {
            get => _timeScaleMilli / 1000f;
            set
            {
                if (!SimMath.IsFinite(value)) throw new ArgumentException("Time scale must be finite");
                float v = SimMath.Clamp(value, MinTimeScale, MaxTimeScale);
                _timeScaleMilli = (int)MathF.Round(v * 1000f);
            }
        }

        /// <summary>Queued inputs not yet applied.</summary>
        public int PendingInputCount => _queueEnd - _queueHead;

        /// <summary>Calls <see cref="IModeRules.Start"/> if it has not run yet (otherwise done by the first step).</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            Rules.Start(this);
            _lastPhase = Rules.Phase;
        }

        /// <summary>
        /// Queues an input for its tick (replay playback, autopilot). The tick must not have been consumed yet; inputs of one
        /// tick keep their queue order.
        /// </summary>
        public void EnqueueInput(in SimInput input)
        {
            if (input.Tick <= _consumedThrough)
                throw new ArgumentException("Input for tick " + input.Tick + " arrives after that tick was consumed (next is " + (_consumedThrough + 1) + ")");
            if (_queueEnd == _queue.Length)
            {
                // Never compact while the rules hold the current tick's input span.
                if (_queueHead > 0 && !_inRulesTick) CompactQueue();
                else Array.Resize(ref _queue, _queue.Length * 2);
            }
            int k = _queueEnd;
            while (k > _queueHead && _queue[k - 1].Tick > input.Tick)
            {
                _queue[k] = _queue[k - 1];
                k--;
            }
            _queue[k] = input;
            _queueEnd++;
        }

        /// <summary>Queues a live input for the next tick to run; returns that tick (to be written to the replay).</summary>
        public int EnqueueInputNow(byte type, int a = 0, int b = 0, int c = 0, int d = 0)
        {
            int tick = Math.Max(World.Tick, _consumedThrough + 1);
            EnqueueInput(new SimInput(tick, type, a, b, c, d));
            return tick;
        }

        /// <summary>Runs one tick. Returns false (and does nothing) when the run is over.</summary>
        public bool Step()
        {
            Start();
            if (IsFinished) return false;
            int tick = World.Tick;
            int first = TakeInputs(tick, out int count);
            _inRulesTick = true;
            try
            {
                Rules.Tick(this, new ReadOnlySpan<SimInput>(_queue, first, count));
            }
            finally
            {
                _inRulesTick = false;
            }
            World.Step();
            Rules.AfterPhysics(this);
            TrackPhase();
            if (!_timeLimitSignalled && Rules.Phase == SimPhase.Running && World.Tick >= _options.MaxTicks - _options.FinaleReserveTicks)
            {
                _timeLimitSignalled = true;
                Rules.OnTimeLimit(this);
                TrackPhase();
            }
            if (World.Tick >= _options.MaxTicks) _hardStopped = true;
            return true;
        }

        /// <summary>
        /// Real-time pacing: runs the ticks due for one display frame at <paramref name="displayFrameRate"/> Hz and the current
        /// time scale (integer accumulator, e.g. 0.25× at 60 Hz = one tick every 4 frames). Returns the ticks run.
        /// </summary>
        public int AdvanceDisplayFrame(int displayFrameRate = SimConstants.TickRate)
        {
            if (displayFrameRate <= 0) throw new ArgumentOutOfRangeException(nameof(displayFrameRate));
            if (Paused || IsFinished) return 0;
            long perTick = 1000L * displayFrameRate;
            _accumulator += (long)_timeScaleMilli * SimConstants.TickRate;
            int steps = 0;
            while (_accumulator >= perTick)
            {
                if (steps >= _options.MaxStepsPerDisplayFrame)
                {
                    // Drop the backlog instead of spiralling after a hitch.
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

        /// <summary>
        /// Headless fast-forward (generation candidates, render-stream rebuild): runs up to <paramref name="maxTicks"/> ticks or
        /// until the run is over, calling <paramref name="observer"/> after each tick. Returns the ticks run.
        /// </summary>
        public int RunHeadless(int maxTicks, ISimObserver observer = null)
        {
            if (maxTicks < 0) throw new ArgumentOutOfRangeException(nameof(maxTicks));
            int n = 0;
            while (n < maxTicks && Step())
            {
                n++;
                if (observer != null) observer.OnTick(this);
            }
            return n;
        }

        /// <summary>Hash of world + rule state after the last tick (spec §7.4 HASH; call every tick or every 60 ticks).</summary>
        public ulong ComputeStateHash()
        {
            _hasher.Reset();
            World.AppendStateHash(_hasher);
            Rules.AppendStateHash(_hasher);
            return _hasher.Digest();
        }

        int TakeInputs(int tick, out int count)
        {
            if (_queueHead == _queueEnd)
            {
                _queueHead = 0;
                _queueEnd = 0;
            }
            else if (_queueHead >= 64 && _queueHead * 2 >= _queue.Length)
            {
                CompactQueue();
            }
            int first = _queueHead;
            int k = first;
            while (k < _queueEnd && _queue[k].Tick == tick) k++;
            count = k - first;
            _queueHead = k;
            _consumedThrough = tick;
            return first;
        }

        void CompactQueue()
        {
            int n = _queueEnd - _queueHead;
            Array.Copy(_queue, _queueHead, _queue, 0, n);
            _queueHead = 0;
            _queueEnd = n;
        }

        void TrackPhase()
        {
            SimPhase p = Rules.Phase;
            if (p == _lastPhase) return;
            if (p < _lastPhase) throw new InvalidOperationException("Mode phase went back from " + _lastPhase + " to " + p);
            if (p >= SimPhase.Finale && FinaleTick < 0) FinaleTick = World.Tick;
            if (p == SimPhase.Finished && FinishedTick < 0) FinishedTick = World.Tick;
            _lastPhase = p;
        }
    }
}
