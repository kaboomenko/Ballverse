using System;
using System.IO;
using Ballverse.Core.Ads;

namespace Ballverse.Services
{
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
