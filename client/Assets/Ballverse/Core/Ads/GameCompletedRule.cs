namespace Ballverse.Core.Ads
{
    /// <summary>
    /// The <c>GameCompleted</c> definition (spec §2.1, §5.3.2): a run counts only if it reached <c>Finished</c> and lasted
    /// at least 5 s of game time. Victory and defeat in interactive modes both count. Trial previews of paid modes,
    /// generation candidates, studio replay playback and restarts before the finale never count.
    /// </summary>
    public static class GameCompletedRule
    {
        /// <summary>Minimum game time of a completed game, seconds (spec §2.1).</summary>
        public const int MinDurationSeconds = 5;

        /// <summary>Minimum game time in ticks: 5 s × 60 Hz = 300. Exactly 300 ticks counts.</summary>
        public const int MinDurationTicks = MinDurationSeconds * SimConstants.TickRate;

        /// <summary>True if the run is a <c>GameCompleted</c> event. Invalid input (negative ticks, unknown enum values) never counts.</summary>
        public static bool IsGameCompleted(GameRunSummary run)
        {
            return run.Kind == GameRunKind.Regular
                && run.End == GameRunEnd.Finished
                && run.DurationTicks >= MinDurationTicks;
        }
    }
}
