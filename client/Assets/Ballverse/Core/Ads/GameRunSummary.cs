namespace Ballverse.Core.Ads
{
    /// <summary>Why a simulation run was started, for the <c>GameCompleted</c> rule (spec §2.1, §5.3.2).</summary>
    public enum GameRunKind
    {
        /// <summary>A normal run the user plays or watches in the play screen (free or owned mode).</summary>
        Regular = 0,
        /// <summary>Trial preview of a paid mode the user does not own (spec §5.2). Never counted.</summary>
        PaidModeTrialPreview = 1,
        /// <summary>Headless candidate run of the auto-generator (spec §2.4). Never counted.</summary>
        GenerationCandidate = 2,
        /// <summary>Replay playback inside the studio (spec §3). Never counted.</summary>
        StudioReplayPlayback = 3,
    }

    /// <summary>How a run ended (spec §2.1 run lifecycle).</summary>
    public enum GameRunEnd
    {
        /// <summary>The run reached the <c>Finished</c> state (victory, defeat or time cap).</summary>
        Finished = 0,
        /// <summary>«Ещё раз» / «🎲» pressed before the finale. Never counted (spec §2.1).</summary>
        RestartedBeforeFinale = 1,
        /// <summary>The user left the run (back, gallery, app killed) before <c>Finished</c>. Never counted.</summary>
        Abandoned = 2,
    }

    /// <summary>What the play loop reports about one run when it stops (input of <see cref="GameCompletedRule"/>).</summary>
    public readonly struct GameRunSummary
    {
        public GameRunKind Kind { get; }
        public GameRunEnd End { get; }

        /// <summary>
        /// Game time of the run in simulation ticks (1/60 s, <see cref="SimConstants.TickRate"/>), counted from the first
        /// recorded tick (Countdown or Running) to the transition into <c>Finished</c>. Independent of the time-speed setting.
        /// </summary>
        public int DurationTicks { get; }

        public GameRunSummary(GameRunKind kind, GameRunEnd end, int durationTicks)
        {
            Kind = kind;
            End = end;
            DurationTicks = durationTicks;
        }

        /// <summary>A regular run that reached <c>Finished</c> after <paramref name="durationTicks"/> ticks.</summary>
        public static GameRunSummary FinishedRegular(int durationTicks)
        {
            return new GameRunSummary(GameRunKind.Regular, GameRunEnd.Finished, durationTicks);
        }
    }
}
