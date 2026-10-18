using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Remote-configurable ad rules (spec §5.3, §9.3). Defaults are the build-time values, so the policies work without
    /// network or a fetched config. <see cref="FromRemoteValues"/> maps the §9.3 Remote Config keys
    /// (<c>interstitial_every_n_games</c>…); <see cref="FromJson"/> reads the CoreJson file format (camelCase keys) and
    /// also accepts the §9.3 keys. Every entry point clamps the values.
    /// </summary>
    public sealed class AdPolicyConfig
    {
        /// <summary>Remote Config key of <see cref="InterstitialEveryNGames"/> (spec §9.3).</summary>
        public const string RemoteKeyInterstitialEveryNGames = "interstitial_every_n_games";
        /// <summary>Remote Config key of <see cref="InterstitialMinIntervalSec"/> (spec §9.3).</summary>
        public const string RemoteKeyInterstitialMinIntervalSec = "interstitial_min_interval_sec";
        /// <summary>Remote Config key of <see cref="InterstitialGraceGames"/> (spec §9.3).</summary>
        public const string RemoteKeyInterstitialGraceGames = "interstitial_grace_games";
        /// <summary>Remote Config key of <see cref="RewardedExportEnabled"/>, the RW7 kill switch (spec §9.3).</summary>
        public const string RemoteKeyRewardedExportEnabled = "rewarded_export_enabled";
        /// <summary>Remote Config key of <see cref="RewardedWaitTimeoutSec"/> (spec §9.3).</summary>
        public const string RemoteKeyRewardedWaitTimeoutSec = "rewarded_wait_timeout_sec";
        /// <summary>Remote Config key of <see cref="RewardedRetryWindowMin"/> (spec §9.3).</summary>
        public const string RemoteKeyRewardedRetryWindowMin = "rewarded_retry_window_min";

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

        /// <summary>
        /// Parses the CoreJson format (camelCase keys, as written by <see cref="ToJson"/>); each value may instead use its
        /// §9.3 Remote Config key (the camelCase key wins when both are present). Missing keys and values of the wrong type
        /// keep their defaults; numbers may be JSON numbers or numeric strings, fractions are truncated toward zero, and
        /// every value is clamped (<see cref="Validated"/>), including values beyond the <see cref="int"/> range.
        /// The JSON literal <c>null</c> yields the defaults.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="System.IO.InvalidDataException">The text is not JSON, or its root is not an object.</exception>
        public static AdPolicyConfig FromJson(string json)
        {
            var obj = AdJson.ParseObject(json, "ad policy config");
            var c = new AdPolicyConfig();
            if (obj == null) return c;
            c.InterstitialEveryNGames = ReadInt(obj, "interstitialEveryNGames", RemoteKeyInterstitialEveryNGames, c.InterstitialEveryNGames);
            c.InterstitialMinIntervalSec = ReadInt(obj, "interstitialMinIntervalSec", RemoteKeyInterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            c.InterstitialGraceGames = ReadInt(obj, "interstitialGraceGames", RemoteKeyInterstitialGraceGames, c.InterstitialGraceGames);
            c.InterstitialQuietAfterRewardedSec = ReadInt(obj, "interstitialQuietAfterRewardedSec", null, c.InterstitialQuietAfterRewardedSec);
            c.RewardedWaitTimeoutSec = ReadInt(obj, "rewardedWaitTimeoutSec", RemoteKeyRewardedWaitTimeoutSec, c.RewardedWaitTimeoutSec);
            c.RewardedRetryWindowMin = ReadInt(obj, "rewardedRetryWindowMin", RemoteKeyRewardedRetryWindowMin, c.RewardedRetryWindowMin);
            if (AdJson.TryGetBool(AdJson.Get(obj, "rewardedExportEnabled"), out bool enabled)
                || AdJson.TryGetBool(AdJson.Get(obj, RemoteKeyRewardedExportEnabled), out enabled))
            {
                c.RewardedExportEnabled = enabled;
            }
            return c.Validated();
        }

        /// <summary>
        /// Builds a config from Remote Config string values keyed by the §9.3 names (the <c>RemoteKey…</c> constants,
        /// exact ordinal match). Numbers parse with the invariant culture (fractions truncate toward zero); booleans
        /// accept true/false/1/0. Missing, null or unparsable values keep the build defaults; every value is clamped.
        /// The IN3 quiet period has no remote key and stays at its default.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
        public static AdPolicyConfig FromRemoteValues(IReadOnlyDictionary<string, string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var c = new AdPolicyConfig();
            c.InterstitialEveryNGames = ReadRemoteInt(values, RemoteKeyInterstitialEveryNGames, c.InterstitialEveryNGames);
            c.InterstitialMinIntervalSec = ReadRemoteInt(values, RemoteKeyInterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            c.InterstitialGraceGames = ReadRemoteInt(values, RemoteKeyInterstitialGraceGames, c.InterstitialGraceGames);
            c.RewardedWaitTimeoutSec = ReadRemoteInt(values, RemoteKeyRewardedWaitTimeoutSec, c.RewardedWaitTimeoutSec);
            c.RewardedRetryWindowMin = ReadRemoteInt(values, RemoteKeyRewardedRetryWindowMin, c.RewardedRetryWindowMin);
            if (values.TryGetValue(RemoteKeyRewardedExportEnabled, out string s) && AdJson.TryParseBool(s, out bool enabled))
                c.RewardedExportEnabled = enabled;
            return c.Validated();
        }

        static int ReadInt(Newtonsoft.Json.Linq.JObject obj, string key, string remoteKey, int fallback)
        {
            if (AdJson.TryGetInt(AdJson.Get(obj, key), out int v)) return v;
            if (remoteKey != null && AdJson.TryGetInt(AdJson.Get(obj, remoteKey), out v)) return v;
            return fallback;
        }

        static int ReadRemoteInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
        {
            return values.TryGetValue(key, out string s) && AdJson.TryParseLong(s, out long v) ? AdJson.SaturateToInt(v) : fallback;
        }

        static void Check(List<string> problems, string name, int value, int min, int max)
        {
            if (value < min || value > max) problems.Add(name);
        }
    }
}
