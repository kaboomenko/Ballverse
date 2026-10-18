namespace Ballverse.Core.Sim
{
    /// <summary>Run phase as seen by the runner (spec §2.1 life cycle; Setup / Countdown / Result live outside the physics run).</summary>
    public enum SimPhase
    {
        Running = 0,
        /// <summary>Finale effect (slow motion, zoom, confetti); physics keeps stepping.</summary>
        Finale = 1,
        Finished = 2,
    }

    public enum SimOutcome
    {
        /// <summary>No result yet.</summary>
        None = 0,
        /// <summary>The mode's goal was reached (all rings broken, arena filled, bottom reached…).</summary>
        Completed = 1,
        /// <summary>A participant won (see <see cref="SimResult.Winner"/>).</summary>
        Win = 2,
        /// <summary>The player lost (Helix Drop deadly sector).</summary>
        Lose = 3,
        /// <summary>Ended by the time limit (by score where applicable).</summary>
        TimeLimit = 4,
    }

    /// <summary>Generic run result; modes put their specifics in <see cref="Score"/> / <see cref="Detail"/>.</summary>
    public struct SimResult
    {
        public SimOutcome Outcome;
        /// <summary>Winning participant / ball tag, −1 if none.</summary>
        public int Winner;
        public int Score;
        /// <summary>Mode-defined extra value (rings broken, floors passed, fill percent ×100…).</summary>
        public int Detail;
        /// <summary>Tick at which the result was decided, −1 if undecided.</summary>
        public int Tick;

        public static SimResult None => new SimResult { Outcome = SimOutcome.None, Winner = -1, Tick = -1 };
    }

    /// <summary>
    /// Quantized input applied at a tick boundary (spec §2.6, §7.4 rule 6). Same shape as a replay INPT record (tick, type,
    /// small quantized payload) so the replay adapter maps it one to one.
    /// </summary>
    public struct SimInput
    {
        /// <summary>Tick before whose physics step the input is applied.</summary>
        public int Tick;
        /// <summary>Mode-defined input type (tap, swipe, gravity…).</summary>
        public byte Type;
        public int A;
        public int B;
        public int C;
        public int D;

        public SimInput(int tick, byte type, int a = 0, int b = 0, int c = 0, int d = 0)
        {
            Tick = tick;
            Type = type;
            A = a;
            B = b;
            C = c;
            D = d;
        }
    }

    /// <summary>
    /// Pluggable rules of one mode on top of the physics world (spec §7.6 IModeRuntime, physics part). Must be deterministic:
    /// only <see cref="Rng"/> streams derived from the seed, no wall-clock time, stable iteration order (spec §7.4).
    /// </summary>
    public interface IModeRules
    {
        /// <summary>Called once before the first tick; the scene is already built in <see cref="SimRunner.World"/>.</summary>
        void Start(SimRunner runner);

        /// <summary>
        /// Logic before the physics step of tick <see cref="SimRunner.Tick"/>: apply this tick's inputs, drive kinematic bodies
        /// (angular speed laws, swipes), spawn or grow balls.
        /// </summary>
        void Tick(SimRunner runner, System.ReadOnlySpan<SimInput> inputs);

        /// <summary>After the physics step: read <see cref="PhysicsWorld.Events"/>, break rings, score, change <see cref="Phase"/>.</summary>
        void AfterPhysics(SimRunner runner);

        /// <summary>
        /// Running reached the time limit (180 s minus the finale reserve, spec §2.1): switch to Finale with a by-score result,
        /// or straight to Finished. The runner stops hard at the limit either way.
        /// </summary>
        void OnTimeLimit(SimRunner runner);

        SimPhase Phase { get; }

        SimResult Result { get; }

        /// <summary>Adds rule state (scores, counters, rule RNG state) to the run's state hash.</summary>
        void AppendStateHash(XxHash64 hash);
    }

    /// <summary>Per-tick callback for headless runs (generation candidates: event density, coverage, stagnation).</summary>
    public interface ISimObserver
    {
        void OnTick(SimRunner runner);
    }
}
