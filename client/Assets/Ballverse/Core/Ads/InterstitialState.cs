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
        /// Parses and normalizes: negative counters become 0, a counter above <see cref="int.MaxValue"/> saturates, the
        /// lifetime total is raised to at least the counter, negative timestamps become null, a missing or older version
        /// becomes <see cref="CurrentVersion"/>. A field of the wrong type keeps its default. The JSON literal <c>null</c>
        /// yields a fresh state.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="System.IO.InvalidDataException">The text is not JSON, or its root is not an object.</exception>
        public static InterstitialState FromJson(string json)
        {
            var obj = AdJson.ParseObject(json, "interstitial state");
            var state = new InterstitialState();
            if (obj != null)
            {
                if (AdJson.TryGetInt(AdJson.Get(obj, "version"), out int version)) state.Version = version;
                if (AdJson.TryGetInt(AdJson.Get(obj, "completedGames"), out int completed)) state.CompletedGames = completed;
                if (AdJson.TryGetLong(AdJson.Get(obj, "totalCompletedGames"), out long total)) state.TotalCompletedGames = total;
                if (AdJson.TryGetLong(AdJson.Get(obj, "lastInterstitialShownMs"), out long shown)) state.LastInterstitialShownMs = shown;
                if (AdJson.TryGetLong(AdJson.Get(obj, "lastRewardedShownMs"), out long rewarded)) state.LastRewardedShownMs = rewarded;
            }
            state.Normalize();
            return state;
        }

        internal void Normalize()
        {
            if (Version < CurrentVersion) Version = CurrentVersion;
            if (CompletedGames < 0) CompletedGames = 0;
            if (TotalCompletedGames < 0) TotalCompletedGames = 0;
            // Every game in the counter is also a lifetime game; a partial state must not look like a grace period (IN6).
            if (TotalCompletedGames < CompletedGames) TotalCompletedGames = CompletedGames;
            if (LastInterstitialShownMs.HasValue && LastInterstitialShownMs.Value < 0) LastInterstitialShownMs = null;
            if (LastRewardedShownMs.HasValue && LastRewardedShownMs.Value < 0) LastRewardedShownMs = null;
        }
    }
}
