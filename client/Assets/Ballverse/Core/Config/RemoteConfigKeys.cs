using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>Remote Config key names, exactly as in the spec §9.3 table.</summary>
    public static class RemoteConfigKeys
    {
        /// <summary>Free mode set (spec §5.1). JSON array of mode codes.</summary>
        public const string FreeModes = "free_modes";
        /// <summary>Emergency mode kill switch (spec §7.6). JSON array of mode codes.</summary>
        public const string ModesDisabled = "modes_disabled";
        /// <summary>Gallery card order (spec §4.3). JSON array of mode codes.</summary>
        public const string GalleryOrder = "gallery_order";
        /// <summary>Interstitial cadence N (spec §5.3.2 IN1, Р7).</summary>
        public const string InterstitialEveryNGames = "interstitial_every_n_games";
        /// <summary>Minimum seconds between interstitials (spec §5.3.2 IN5).</summary>
        public const string InterstitialMinIntervalSec = "interstitial_min_interval_sec";
        /// <summary>Newcomer grace games (spec §5.3.2 IN6).</summary>
        public const string InterstitialGraceGames = "interstitial_grace_games";
        /// <summary>Rewarded export gate kill switch (spec §5.3.1 RW7).</summary>
        public const string RewardedExportEnabled = "rewarded_export_enabled";
        /// <summary>Seconds to wait for a rewarded ad before exporting without it (spec §5.3.1 RW3).</summary>
        public const string RewardedWaitTimeoutSec = "rewarded_wait_timeout_sec";
        /// <summary>Minutes a failed export may be retried without a new ad (spec §5.3.1 RW4).</summary>
        public const string RewardedRetryWindowMin = "rewarded_retry_window_min";
        /// <summary>Paid mode trial preview switch (spec §5.2).</summary>
        public const string PaidModeTrialEnabled = "paid_mode_trial_enabled";
        /// <summary>Paid mode trial preview length (spec §5.2).</summary>
        public const string PaidModeTrialSeconds = "paid_mode_trial_seconds";
        /// <summary>Export bitrates per resolution and FPS (spec §3.8).</summary>
        public const string ExportBitrates = "export_bitrates";
        /// <summary>Default export FPS per device tier (spec §9.3).</summary>
        public const string ExportDefaultFpsByTier = "export_default_fps_by_tier";
        /// <summary>Per-mode spectacle score weights w1..w5 (spec §2.4).</summary>
        public const string GenerationWeights = "generation_weights";
        /// <summary>Candidate count and time budget per tier (spec §2.4).</summary>
        public const string GenerationBudgetByTier = "generation_budget_by_tier";
        /// <summary>Forced tier per device model (spec §8.1, R5).</summary>
        public const string TierOverrides = "tier_overrides";
        /// <summary>TikTok/Reels/Shorts UI masks (spec §3.3).</summary>
        public const string PlatformSafeZones = "platform_safe_zones";
        /// <summary>Additional mode presets as raw JSON (spec §2.3).</summary>
        public const string RemotePresets = "remote_presets";
        /// <summary>Additional melodies for the Melody sound pack (spec §2.7).</summary>
        public const string RemoteMelodies = "remote_melodies";
        /// <summary>Text templates per language (spec §3.5).</summary>
        public const string TextTemplates = "text_templates";
        /// <summary>Watermark kill switch for everyone (spec §3.10, Р13).</summary>
        public const string WatermarkEnabled = "watermark_enabled";
        /// <summary>Ask for a store rating after the N-th successful export (spec §9.3).</summary>
        public const string RatePromptAfterExports = "rate_prompt_after_exports";

        static readonly string[] s_all =
        {
            FreeModes, ModesDisabled, GalleryOrder,
            InterstitialEveryNGames, InterstitialMinIntervalSec, InterstitialGraceGames,
            RewardedExportEnabled, RewardedWaitTimeoutSec, RewardedRetryWindowMin,
            PaidModeTrialEnabled, PaidModeTrialSeconds,
            ExportBitrates, ExportDefaultFpsByTier,
            GenerationWeights, GenerationBudgetByTier,
            TierOverrides, PlatformSafeZones,
            RemotePresets, RemoteMelodies, TextTemplates,
            WatermarkEnabled, RatePromptAfterExports,
        };

        /// <summary>All keys in spec §9.3 table order. Parsing and serialization follow this order.</summary>
        public static IReadOnlyList<string> All => s_all;
    }
}
