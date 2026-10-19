namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Shared HUD keys of render frames (spec §7.4 "состояние HUD", §2.9 HUD lines). Every mode writes the keys it has; the
    /// view decides what to show from the look parameters (<c>hud</c>, <c>hudTimer</c>, <c>hudCounter</c>, …), so frames always
    /// carry the values. Keys are persisted in render streams and must not change.
    /// </summary>
    public static class HudKeys
    {
        /// <summary>Game time in ticks since the run started (Countdown included).</summary>
        public const int TimeTicks = 0;
        /// <summary>Whole seconds left in the countdown (3, 2, 1), 0 when none (spec §2.1 Countdown).</summary>
        public const int Countdown = 1;
        /// <summary>Progress counter: rings passed (M01), floors passed (M05), balls (M02).</summary>
        public const int Progress = 2;
        /// <summary>Total of the progress counter (rings, floors), 0 when open-ended.</summary>
        public const int ProgressTotal = 3;
        /// <summary>Score (player modes) or the main percentage ×100.</summary>
        public const int Score = 4;
        /// <summary>Streak / combo counter.</summary>
        public const int Streak = 5;
        /// <summary><see cref="Sim.SimPhase"/> as an int.</summary>
        public const int Phase = 6;
        /// <summary>Winning participant index once decided, −1 before.</summary>
        public const int Winner = 7;
        /// <summary>Round number (1-based) for modes that start new rounds.</summary>
        public const int Round = 8;
        /// <summary>Participant p's score is at <c>ParticipantScoreBase + p</c> (p &lt; 64).</summary>
        public const int ParticipantScoreBase = 100;
        /// <summary>First key a mode may use for its own values.</summary>
        public const int ModeSpecificBase = 1000;
    }
}
