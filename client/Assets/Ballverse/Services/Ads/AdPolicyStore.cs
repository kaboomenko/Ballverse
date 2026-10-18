using System;
using System.IO;
using Ballverse.Core.Ads;
using Ballverse.Core.Config;

namespace Ballverse.Services
{
    /// <summary>Maps the typed Remote Config (spec §9.3) to the Core ad policy config.</summary>
    public static class AdPolicyConfigMapper
    {
        /// <summary>
        /// The §9.3 ad keys; <c>InterstitialQuietAfterRewardedSec</c> (IN3, 30 s) has no Remote Config key and keeps its default.
        /// The result is clamped (<see cref="AdPolicyConfig.Validated"/>).
        /// </summary>
        public static AdPolicyConfig From(RemoteConfigValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var c = AdPolicyConfig.CreateDefault();
            c.InterstitialEveryNGames = values.InterstitialEveryNGames;
            c.InterstitialMinIntervalSec = values.InterstitialMinIntervalSec;
            c.InterstitialGraceGames = values.InterstitialGraceGames;
            c.RewardedExportEnabled = values.RewardedExportEnabled;
            c.RewardedWaitTimeoutSec = values.RewardedWaitTimeoutSec;
            c.RewardedRetryWindowMin = values.RewardedRetryWindowMin;
            return c.Validated();
        }
    }

    /// <summary>
    /// Holds the Core ad policies (<see cref="RewardedExportGate"/>, <see cref="InterstitialPolicy"/>) and persists their state
    /// JSON between sessions (IN1 counter, RW4 retry credits). Unreadable state starts fresh and is logged; it never throws.
    /// </summary>
    public sealed class AdPolicyStore
    {
        public const string RewardedGateKey = "ballverse.ads.rewarded_gate.v1";
        public const string InterstitialKey = "ballverse.ads.interstitial.v1";

        const string Tag = "AdPolicy";

        readonly IKeyValueStore _store;
        readonly IServiceLog _log;

        public AdPolicyStore(IKeyValueStore store, AdPolicyConfig config, IServiceLog log)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log ?? NullServiceLog.Instance;
            if (config == null) throw new ArgumentNullException(nameof(config));
            Gate = LoadGate(config);
            Interstitial = LoadInterstitial(config);
        }

        public RewardedExportGate Gate { get; }

        public InterstitialPolicy Interstitial { get; }

        /// <summary>A fresh Remote Config: both policies keep their state and take the new values (clamped by Core).</summary>
        public void ApplyConfig(AdPolicyConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            Gate.Config = config;
            Interstitial.Config = config;
        }

        public void SaveGate()
        {
            Write(RewardedGateKey, Gate.SerializeState());
        }

        public void SaveInterstitial()
        {
            Write(InterstitialKey, Interstitial.SerializeState());
        }

        void Write(string key, string json)
        {
            try
            {
                _store.SetString(key, json);
                _store.Save();
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Saving " + key + " failed.", e);
            }
        }

        RewardedExportGate LoadGate(AdPolicyConfig config)
        {
            string json = Read(RewardedGateKey);
            if (json != null)
            {
                try
                {
                    return RewardedExportGate.FromJson(config, json);
                }
                catch (Exception e) when (e is InvalidDataException || e is ArgumentException)
                {
                    _log.Warning(Tag, "Rewarded gate state unreadable; starting fresh (" + e.Message + ").");
                }
            }
            return new RewardedExportGate(config);
        }

        InterstitialPolicy LoadInterstitial(AdPolicyConfig config)
        {
            string json = Read(InterstitialKey);
            if (json != null)
            {
                try
                {
                    return InterstitialPolicy.FromJson(config, json);
                }
                catch (Exception e) when (e is InvalidDataException || e is ArgumentException)
                {
                    _log.Warning(Tag, "Interstitial state unreadable; starting fresh (" + e.Message + ").");
                }
            }
            return new InterstitialPolicy(config);
        }

        string Read(string key)
        {
            try
            {
                return _store.GetString(key);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading " + key + " failed.", e);
                return null;
            }
        }
    }
}
