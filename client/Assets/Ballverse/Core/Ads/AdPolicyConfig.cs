using System;
using System.Collections.Generic;
using Ballverse.Core.Config;
using Newtonsoft.Json;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Remote-configurable ad rules (spec §5.3, §9.3). Defaults are the build-time values, so the policies work without
    /// network or a fetched config. This type does not parse the §9.3 Remote Config payload: the Config module
    /// (RemoteConfigParser / RemoteConfigValues) is the only §9.3 parser, and <c>Ballverse.Core.Integration.RemoteConfigAdapters</c>
    /// maps its typed values onto the properties here. The Default*/Min*/Max* constants of the remote keys are aliases of
    /// <see cref="RemoteConfigLimits"/> and the Config defaults (not copies), so the safety-net clamp in
    /// <see cref="Validated"/> can never disagree with the authoritative one. Only the quiet period after a rewarded ad
    /// (IN3) has no Remote Config key and is defined here. <see cref="FromJson"/> reads only this type's own CoreJson format
    /// (camelCase keys).
    /// </summary>
    public sealed class AdPolicyConfig
    {
        /// <summary>N in "interstitial after every N completed games" (spec §5.3.2 IN1, Р7).</summary>
        public const int DefaultInterstitialEveryNGames = RemoteConfigDefaults.InterstitialEveryNGames;
        public const int MinInterstitialEveryNGames = RemoteConfigLimits.InterstitialEveryNGamesMin;
        public const int MaxInterstitialEveryNGames = RemoteConfigLimits.InterstitialEveryNGamesMax;

        /// <summary>Minimum interval between two interstitials, seconds (spec §5.3.2 IN5).</summary>
        public const int DefaultInterstitialMinIntervalSec = RemoteConfigDefaults.InterstitialMinIntervalSec;
        public const int MinInterstitialMinIntervalSec = RemoteConfigLimits.InterstitialMinIntervalSecMin;
        public const int MaxInterstitialMinIntervalSec = RemoteConfigLimits.InterstitialMinIntervalSecMax;

        /// <summary>Completed games before interstitials start counting (spec §5.3.2 IN6, question В3).</summary>
        public const int DefaultInterstitialGraceGames = RemoteConfigDefaults.InterstitialGraceGames;
        public const int MinInterstitialGraceGames = RemoteConfigLimits.InterstitialGraceGamesMin;
        public const int MaxInterstitialGraceGames = RemoteConfigLimits.InterstitialGraceGamesMax;

        /// <summary>No interstitial within this many seconds after a rewarded ad (spec §5.3.2 IN3).</summary>
        public const int DefaultInterstitialQuietAfterRewardedSec = 30;
        public const int MinInterstitialQuietAfterRewardedSec = 0;
        public const int MaxInterstitialQuietAfterRewardedSec = 600;

        /// <summary>How long the export flow waits for a rewarded ad to load, seconds (spec §5.3.1 RW3).</summary>
        public const int DefaultRewardedWaitTimeoutSec = RemoteConfigDefaults.RewardedWaitTimeoutSec;
        public const int MinRewardedWaitTimeoutSec = RemoteConfigLimits.RewardedWaitTimeoutSecMin;
        public const int MaxRewardedWaitTimeoutSec = RemoteConfigLimits.RewardedWaitTimeoutSecMax;

        /// <summary>Retry window after a failed or cancelled ad-funded export, minutes (spec §5.3.1 RW4, §3.8, question В5).</summary>
        public const int DefaultRewardedRetryWindowMin = RemoteConfigDefaults.RewardedRetryWindowMin;
        public const int MinRewardedRetryWindowMin = RemoteConfigLimits.RewardedRetryWindowMinMin;
        public const int MaxRewardedRetryWindowMin = RemoteConfigLimits.RewardedRetryWindowMinMax;

        /// <summary>Remote Config <c>interstitial_every_n_games</c> (spec §5.3.2 IN1). Range 1–20.</summary>
        public int InterstitialEveryNGames { get; set; } = DefaultInterstitialEveryNGames;

        /// <summary>Remote Config <c>interstitial_min_interval_sec</c> (spec §5.3.2 IN5). Range 0–600.</summary>
        public int InterstitialMinIntervalSec { get; set; } = DefaultInterstitialMinIntervalSec;

        /// <summary>Remote Config <c>interstitial_grace_games</c> (spec §5.3.2 IN6). Range 0–50.</summary>
        public int InterstitialGraceGames { get; set; } = DefaultInterstitialGraceGames;

        /// <summary>Quiet period after a rewarded ad, seconds (spec §5.3.2 IN3, fixed at 30 by the spec). Range 0–600.</summary>
        public int InterstitialQuietAfterRewardedSec { get; set; } = DefaultInterstitialQuietAfterRewardedSec;

        /// <summary>Remote Config <c>rewarded_export_enabled</c>: kill switch of the whole export gate (spec §5.3.1 RW7).</summary>
        public bool RewardedExportEnabled { get; set; } = RemoteConfigDefaults.RewardedExportEnabled;

        /// <summary>Remote Config <c>rewarded_wait_timeout_sec</c> (spec §5.3.1 RW3, RW7). Range 1–30.</summary>
        public int RewardedWaitTimeoutSec { get; set; } = DefaultRewardedWaitTimeoutSec;

        /// <summary>Remote Config <c>rewarded_retry_window_min</c> (spec §5.3.1 RW4). Range 0–60; 0 disables retry credits.</summary>
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

        /// <summary>
        /// Parses this type's CoreJson format (camelCase keys, as written by <see cref="ToJson"/>). The §9.3 Remote Config
        /// keys (<c>interstitial_every_n_games</c>…) are not read here; the Config module owns them. Missing keys and values
        /// of the wrong type keep their defaults. Numbers may be JSON numbers or numeric strings and must be integral:
        /// a fraction, NaN or infinity keeps the default (as the Config module does). Integral values are clamped
        /// (<see cref="Validated"/>), including values beyond the <see cref="int"/> or <see cref="long"/> range.
        /// The JSON literal <c>null</c> yields the defaults.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="System.IO.InvalidDataException">
        /// The text is not JSON (including an integer literal longer than 380 characters, a Newtonsoft reader limit),
        /// or its root is not an object.
        /// </exception>
        public static AdPolicyConfig FromJson(string json)
        {
            var obj = AdJson.ParseObject(json, "ad policy config");
            var c = new AdPolicyConfig();
            if (obj == null) return c;
            c.InterstitialEveryNGames = ReadInt(obj, "interstitialEveryNGames", c.InterstitialEveryNGames);
            c.InterstitialMinIntervalSec = ReadInt(obj, "interstitialMinIntervalSec", c.InterstitialMinIntervalSec);
            c.InterstitialGraceGames = ReadInt(obj, "interstitialGraceGames", c.InterstitialGraceGames);
            c.InterstitialQuietAfterRewardedSec = ReadInt(obj, "interstitialQuietAfterRewardedSec", c.InterstitialQuietAfterRewardedSec);
            c.RewardedWaitTimeoutSec = ReadInt(obj, "rewardedWaitTimeoutSec", c.RewardedWaitTimeoutSec);
            c.RewardedRetryWindowMin = ReadInt(obj, "rewardedRetryWindowMin", c.RewardedRetryWindowMin);
            if (AdJson.TryGetBool(AdJson.Get(obj, "rewardedExportEnabled"), out bool enabled)) c.RewardedExportEnabled = enabled;
            return c.Validated();
        }

        static int ReadInt(Newtonsoft.Json.Linq.JObject obj, string key, int fallback)
        {
            return AdJson.TryGetIntegralInt(AdJson.Get(obj, key), out int v) ? v : fallback;
        }

        static void Check(List<string> problems, string name, int value, int min, int max)
        {
            if (value < min || value > max) problems.Add(name);
        }
    }
}
