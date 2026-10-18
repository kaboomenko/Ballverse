namespace Ballverse.UI.Layout
{
    /// <summary>Simulation life cycle as the UI sees it (spec §2.1).</summary>
    public enum SimPhaseView
    {
        Setup = 0,
        Countdown = 1,
        Running = 2,
        Finale = 3,
        Finished = 4,
    }

    public readonly struct ChromeState
    {
        /// <summary>Opacity of the floating top/bottom glass bars.</summary>
        public float Opacity { get; }
        /// <summary>False: bars are hidden and do not take touches.</summary>
        public bool Interactive { get; }

        public ChromeState(float opacity, bool interactive)
        {
            Opacity = opacity;
            Interactive = interactive;
        }
    }

    /// <summary>
    /// Floating bars of the simulation screen (spec §4.3): during <c>Running</c> they hide after 2 s in interactive modes
    /// and fade to 40% in automatic modes; any touch outside the arena brings them back. Paused, setup, finished or with
    /// a sheet open they are fully visible. Pure.
    /// </summary>
    public static class ChromeVisibilityPolicy
    {
        public const float IdleSeconds = 2f;
        public const float AutoModeOpacity = 0.4f;

        public static ChromeState Evaluate(SimPhaseView phase, bool playing, bool interactive, float secondsSinceInteraction,
            bool overlayOpen)
        {
            bool running = phase == SimPhaseView.Running && playing;
            if (!running || overlayOpen || secondsSinceInteraction < IdleSeconds) return new ChromeState(1f, true);
            return interactive ? new ChromeState(0f, false) : new ChromeState(AutoModeOpacity, true);
        }
    }
}
