using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Remote-configurable ad rules (spec §5.3, §9.3). Defaults are the build-time values, so the policies work without
    /// network or a fetched config. Mapping from Remote Config keys (<c>interstitial_every_n_games</c>…) is done by the
    /// services layer; this class only holds, validates and clamps the values.
    /// </summary>
    public sealed class AdPolicyConfig
    {
        /// <summary>N in "interstitial after every N completed games" (spec §5.3.2 IN1, Р7).</summary>
        public const int DefaultInterstitialEveryNGames = 3;
        public const int MinInterstitialEveryNGames = 1;
        public const int MaxInterstitialEveryNGames = 100;

        /// <summary>Minimum interval between two interstitials, seconds (spec §5.3.2 IN5).</summary>
        public const int DefaultInterstitialMinIntervalSec = 60;
        public const int MinInterstitialMinIntervalSec = 0;
        public const int MaxInterstitialMinIntervalSec = 3600;

        /// <summary>Completed games before interstitials start counting (spec §5.3.2 IN6, question В3).</summary>
        public const int DefaultInterstitialGraceGames = 0;
        public const int MinInterstitialGraceGames = 0;
        public const int MaxInterstitialGraceGames = 1000;

        /// <summary>No interstitial within this many seconds after a rewarded ad (spec §5.3.2 IN3).</summary>
        public const int DefaultInterstitialQuietAfterRewardedSec = 30;
        public const int MinInterstitialQuietAfterRewardedSec = 0;
        public const int MaxInterstitialQuietAfterRewardedSec = 600;

        /// <summary>How long the export flow waits for a rewarded ad to load, seconds (spec §5.3.1 RW3).</summary>
        public const int DefaultRewardedWaitTimeoutSec = 5;
        public const int MinRewardedWaitTimeoutSec = 1;
        public const int MaxRewardedWaitTimeoutSec = 30;

        /// <summary>Retry window after a failed or cancelled ad-funded export, minutes (spec §5.3.1 RW4, §3.8, question В5).</summary>
        public const int DefaultRewardedRetryWindowMin = 10;
        public const int MinRewardedRetryWindowMin = 0;
        public const int MaxRewardedRetryWindowMin = 1440;

        /// <summary>Remote Config <c>interstitial_every_n_games</c> (spec §5.3.2 IN1). Range 1–100.</summary>
        public int InterstitialEveryNGames { get; set; } = DefaultInterstitialEveryNGames;

        /// <summary>Remote Config <c>interstitial_min_interval_sec</c> (spec §5.3.2 IN5). Range 0–3600.</summary>
        public int InterstitialMinIntervalSec { get; set; } = DefaultInterstitialMinIntervalSec;

        /// <summary>Remote Config <c>interstitial_grace_games</c> (spec §5.3.2 IN6). Range 0–1000.</summary>
        public int InterstitialGraceGames { get; set; } = DefaultInterstitialGraceGames;

        /// <summary>Quiet period after a rewarded ad, seconds (spec §5.3.2 IN3, fixed at 30 by the spec). Range 0–600.</summary>
        public int InterstitialQuietAfterRewardedSec { get; set; } = DefaultInterstitialQuietAfterRewardedSec;

        /// <summary>Remote Config <c>rewarded_export_enabled</c>: kill switch of the whole export gate (spec §5.3.1 RW7).</summary>
        public bool RewardedExportEnabled { get; set; } = true;

        /// <summary>Remote Config <c>rewarded_wait_timeout_sec</c> (spec §5.3.1 RW3, RW7). Range 1–30.</summary>
        public int RewardedWaitTimeoutSec { get; set; } = DefaultRewardedWaitTimeoutSec;

        /// <summary>Remote Config <c>rewarded_retry_window_min</c> (spec §5.3.1 RW4). Range 0–1440; 0 disables retry credits.</summary>
        public int RewardedRetryWindowMin { get; set; } = DefaultRewardedRetryWindowMin;

        /// <summary><see cref="InterstitialMinIntervalSec"/> in milliseconds.</summary>
        [JsonIgnore]
        public long InterstitialMinIntervalMs => InterstitialMinIntervalSec * 1000L;

        /// <summary><see cref="InterstitialQuietAfterRewardedSec"/> in milliseconds.</summary>
        [JsonIgnore]
        public long InterstitialQuietAfterRewardedMs => InterstitialQuietAfterRewardedSec * 1000L;

        /// <summary><see cref="RewardedWaitTimeoutSec"/> in milliseconds.</summary>
        [JsonIgnore]
        public long RewardedWaitTimeoutMs => RewardedWaitTimeoutSec * 1000L;

        /// <summary><see cref="RewardedRetryWindowMin"/> in milliseconds.</summary>
        [JsonIgnore]
        public long RewardedRetryWindowMs => RewardedRetryWindowMin * 60_000L;

        /// <summary>A fresh config with the build defaults (spec §9.3).</summary>
        public static AdPolicyConfig CreateDefault()
        {
            return new AdPolicyConfig();
        }

        public AdPolicyConfig Clone()
        {
            return (AdPolicyConfig)MemberwiseClone();
        }

        /// <summary>
        /// Copy with every value clamped into its allowed range. A malformed remote value never disables
        /// the user-protection rules (e.g. N = 0 becomes 1, a negative interval becomes 0).
        /// </summary>
        public AdPolicyConfig Validated()
        {
            var c = Clone();
            c.InterstitialEveryNGames = Math.Clamp(c.InterstitialEveryNGames, MinInterstitialEveryNGames, MaxInterstitialEveryNGames);
            c.InterstitialMinIntervalSec = Math.Clamp(c.InterstitialMinIntervalSec, MinInterstitialMinIntervalSec, MaxInterstitialMinIntervalSec);
            c.InterstitialGraceGames = Math.Clamp(c.InterstitialGraceGames, MinInterstitialGraceGames, MaxInterstitialGraceGames);
            c.InterstitialQuietAfterRewardedSec = Math.Clamp(c.InterstitialQuietAfterRewardedSec, MinInterstitialQuietAfterRewardedSec, MaxInterstitialQuietAfterRewardedSec);
            c.RewardedWaitTimeoutSec = Math.Clamp(c.RewardedWaitTimeoutSec, MinRewardedWaitTimeoutSec, MaxRewardedWaitTimeoutSec);
            c.RewardedRetryWindowMin = Math.Clamp(c.RewardedRetryWindowMin, MinRewardedRetryWindowMin, MaxRewardedRetryWindowMin);
            return c;
        }

        /// <summary>Names of out-of-range values (camelCase JSON keys) in declaration order; empty when valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var problems = new List<string>();
            Check(problems, "interstitialEveryNGames", InterstitialEveryNGames, MinInterstitialEveryNGames, MaxInterstitialEveryNGames);
            Check(problems, "interstitialMinIntervalSec", InterstitialMinIntervalSec, MinInterstitialMinIntervalSec, MaxInterstitialMinIntervalSec);
            Check(problems, "interstitialGraceGames", InterstitialGraceGames, MinInterstitialGraceGames, MaxInterstitialGraceGames);
            Check(problems, "interstitialQuietAfterRewardedSec", InterstitialQuietAfterRewardedSec, MinInterstitialQuietAfterRewardedSec, MaxInterstitialQuietAfterRewardedSec);
            Check(problems, "rewardedWaitTimeoutSec", RewardedWaitTimeoutSec, MinRewardedWaitTimeoutSec, MaxRewardedWaitTimeoutSec);
            Check(problems, "rewardedRetryWindowMin", RewardedRetryWindowMin, MinRewardedRetryWindowMin, MaxRewardedRetryWindowMin);
            return problems;
        }

        /// <summary>True when <see cref="Validate"/> reports nothing.</summary>
        [JsonIgnore]
        public bool IsValid => Validate().Count == 0;

        /// <summary>Serializes with <see cref="CoreJson"/>.</summary>
        public string ToJson(bool indented = true)
        {
            return CoreJson.Serialize(this, indented);
        }

        /// <summary>Parses with <see cref="CoreJson"/>; missing keys keep their defaults, values are clamped (<see cref="Validated"/>).</summary>
        public static AdPolicyConfig FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var parsed = CoreJson.Deserialize<AdPolicyConfig>(json) ?? new AdPolicyConfig();
            return parsed.Validated();
        }

        static void Check(List<string> problems, string name, int value, int min, int max)
        {
            if (value < min || value > max) problems.Add(name);
        }
    }
}
