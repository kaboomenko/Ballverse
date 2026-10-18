using System;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Persistent state of <see cref="InterstitialPolicy"/> (spec §5.3.2 IN1: the counter survives sessions).
    /// Stored as JSON via <see cref="CoreJson"/>; all times are unix milliseconds supplied by the caller.
    /// </summary>
    public sealed class InterstitialState
    {
        public const int CurrentVersion = 1;

        /// <summary>Format version for future migrations.</summary>
        public int Version { get; set; } = CurrentVersion;

        /// <summary>
        /// <c>completedGames</c> of IN1: completed games since the last interstitial (or since the last reset of IN4).
        /// Never negative; saturates at <see cref="int.MaxValue"/>.
        /// </summary>
        public int CompletedGames { get; set; }

        /// <summary>Lifetime completed games, used by the grace period (IN6). Never negative.</summary>
        public long TotalCompletedGames { get; set; }

        /// <summary>When the last interstitial was shown (IN5), or null if never.</summary>
        public long? LastInterstitialShownMs { get; set; }

        /// <summary>When the last rewarded ad was shown (IN3 quiet period), or null if never.</summary>
        public long? LastRewardedShownMs { get; set; }

        public InterstitialState Clone()
        {
            return (InterstitialState)MemberwiseClone();
        }

        /// <summary>Serializes with <see cref="CoreJson"/> (compact by default).</summary>
        public string ToJson(bool indented = false)
        {
            return CoreJson.Serialize(this, indented);
        }

        /// <summary>
        /// Parses with <see cref="CoreJson"/> and normalizes: negative counters become 0, negative timestamps become null,
        /// a missing or older version becomes <see cref="CurrentVersion"/>. The JSON literal <c>null</c> yields a fresh state.
        /// </summary>
        public static InterstitialState FromJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var state = CoreJson.Deserialize<InterstitialState>(json) ?? new InterstitialState();
            state.Normalize();
            return state;
        }

        internal void Normalize()
        {
            if (Version < CurrentVersion) Version = CurrentVersion;
            if (CompletedGames < 0) CompletedGames = 0;
            if (TotalCompletedGames < 0) TotalCompletedGames = 0;
            if (LastInterstitialShownMs.HasValue && LastInterstitialShownMs.Value < 0) LastInterstitialShownMs = null;
            if (LastRewardedShownMs.HasValue && LastRewardedShownMs.Value < 0) LastRewardedShownMs = null;
        }
    }
}
