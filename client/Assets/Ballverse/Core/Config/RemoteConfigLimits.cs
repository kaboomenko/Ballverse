namespace Ballverse.Core.Config
{
    /// <summary>
    /// Accepted ranges of numeric Remote Config values (spec §9.3, §5.3, §3.8, §2.4).
    /// Out-of-range numbers are clamped into these bounds by <see cref="RemoteConfigParser"/>.
    /// </summary>
    public static class RemoteConfigLimits
    {
        public const int InterstitialEveryNGamesMin = 1;
        public const int InterstitialEveryNGamesMax = 20;
        public const int InterstitialMinIntervalSecMin = 0;
        public const int InterstitialMinIntervalSecMax = 600;
        public const int InterstitialGraceGamesMin = 0;
        public const int InterstitialGraceGamesMax = 50;
        public const int RewardedWaitTimeoutSecMin = 1;
        public const int RewardedWaitTimeoutSecMax = 30;
        public const int RewardedRetryWindowMinMin = 0;
        public const int RewardedRetryWindowMinMax = 60;
        public const int PaidModeTrialSecondsMin = 10;
        public const int PaidModeTrialSecondsMax = 120;
        public const int BitrateKbpsMin = 1000;
        public const int BitrateKbpsMax = 50000;
        public const int BudgetCandidatesMin = 1;
        public const int BudgetCandidatesMax = 64;
        public const int BudgetMsMin = 100;
        public const int BudgetMsMax = 10000;
        /// <summary>Largest spectacle weight w1…w5 (<c>generation_weights</c>); guards the score against a broken remote value.</summary>
        public const double WeightMax = 100.0;
        /// <summary>Largest |qualityThreshold| (<c>generation_weights</c>).</summary>
        public const double QualityThresholdAbsMax = 1000.0;
        public const int RatePromptAfterExportsMin = 1;
        public const int RatePromptAfterExportsMax = 20;

        /// <summary>The only export frame rates (spec §3.8: CFR 30 or 60).</summary>
        public static bool IsValidFps(int fps)
        {
            return fps == 30 || fps == 60;
        }

        /// <summary>MIDI pitch range of a melody note (spec §2.7).</summary>
        public const int PitchMin = 0;
        public const int PitchMax = 127;
    }
}
