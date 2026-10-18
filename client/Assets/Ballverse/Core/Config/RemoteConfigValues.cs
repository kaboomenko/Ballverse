using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Immutable typed snapshot of every Remote Config key of the spec §9.3 table.
    /// <see cref="Defaults"/> holds the built-in values, so the app works fully offline (spec §9.3, §8.4 SZ5).
    /// Create from Firebase strings with <see cref="RemoteConfigParser.Parse"/>, or with <see cref="RemoteConfigBuilder"/>.
    /// Collections are read-only and in a deterministic order (never dictionary order).
    /// </summary>
    public sealed class RemoteConfigValues
    {
        /// <summary>Built-in defaults of spec §9.3. Safe to use before (or without) any network fetch.</summary>
        public static readonly RemoteConfigValues Defaults = new RemoteConfigBuilder().Build();

        bool[] _free;
        bool[] _disabled;
        IReadOnlyList<ModeCode> _effectiveFree;

        internal RemoteConfigValues()
        {
        }

        /// <summary>
        /// <c>free_modes</c> (spec §5.1): modes usable without an entitlement, in config order. Default M01–M05.
        /// Config is meant to only extend this set; users who already played a mode for free keep it via
        /// Monetization's <c>grandfathered_modes</c>, which this snapshot does not know about.
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => FreeModesList;

        /// <summary><c>modes_disabled</c> (spec §7.6 emergency kill switch). Default empty.</summary>
        public IReadOnlyList<ModeCode> ModesDisabled => ModesDisabledList;

        /// <summary>
        /// <c>gallery_order</c> (spec §4.3): always a permutation of all 15 catalog modes. Default catalog order.
        /// The UI still groups free → bought → paid and applies this order inside each group.
        /// </summary>
        public IReadOnlyList<ModeCode> GalleryOrder => GalleryOrderList;

        /// <summary><c>interstitial_every_n_games</c> (spec §5.3.2 IN1, Р7). Default 3, range 1–20.</summary>
        public int InterstitialEveryNGames { get; internal set; }
        /// <summary><c>interstitial_min_interval_sec</c> (spec §5.3.2 IN5). Default 60, range 0–600.</summary>
        public int InterstitialMinIntervalSec { get; internal set; }
        /// <summary><c>interstitial_grace_games</c> (spec §5.3.2 IN6). Default 0, range 0–50.</summary>
        public int InterstitialGraceGames { get; internal set; }
        /// <summary><c>rewarded_export_enabled</c> (spec §5.3.1 RW7). Default true; false disables the export ad gate.</summary>
        public bool RewardedExportEnabled { get; internal set; }
        /// <summary><c>rewarded_wait_timeout_sec</c> (spec §5.3.1 RW3). Default 5, range 1–30.</summary>
        public int RewardedWaitTimeoutSec { get; internal set; }
        /// <summary><c>rewarded_retry_window_min</c> (spec §5.3.1 RW4). Default 10, range 0–60.</summary>
        public int RewardedRetryWindowMin { get; internal set; }
        /// <summary><c>paid_mode_trial_enabled</c> (spec §5.2). Default true.</summary>
        public bool PaidModeTrialEnabled { get; internal set; }
        /// <summary><c>paid_mode_trial_seconds</c> (spec §5.2). Default 30, range 10–120.</summary>
        public int PaidModeTrialSeconds { get; internal set; }
        /// <summary><c>export_bitrates</c> (spec §3.8 table). kbps, range 1000–50000.</summary>
        public ExportBitrates ExportBitrates { get; internal set; }
        /// <summary><c>export_default_fps_by_tier</c> (spec §9.3). Default {A:60, B:60, C:30}; values 30 or 60.</summary>
        public TierValues<int> ExportDefaultFpsByTier { get; internal set; }

        /// <summary>
        /// <c>generation_weights</c> (spec §2.4): per-mode overrides of w1…w5, ordered by mode code.
        /// Default empty: every mode uses its built-in weights.
        /// </summary>
        public IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> GenerationWeights => GenerationWeightsList;

        /// <summary><c>generation_budget_by_tier</c> (spec §2.4). Default A 24/1500 ms, B 16/2500 ms, C 8/3500 ms.</summary>
        public TierValues<GenerationBudget> GenerationBudgetByTier { get; internal set; }

        /// <summary><c>tier_overrides</c> (spec §8.1, R5): forced tiers per device model, ordered by model (ordinal, ignoring case). Default empty.</summary>
        public IReadOnlyList<TierOverride> TierOverrides => TierOverridesList;

        /// <summary><c>platform_safe_zones</c> (spec §3.3): UI masks per platform, ordered by platform id. Default tiktok/reels/shorts.</summary>
        public IReadOnlyList<PlatformSafeZone> PlatformSafeZones => PlatformSafeZonesList;

        /// <summary>
        /// <c>remote_presets</c> (spec §2.3): each element is one preset as a compact JSON object string, in config order.
        /// Only checked to be a JSON object here; schema validation belongs to the Params module. Default empty.
        /// </summary>
        public IReadOnlyList<string> RemotePresets => RemotePresetsList;

        /// <summary><c>remote_melodies</c> (spec §2.7): validated melodies in config order, unique ids. Default empty.</summary>
        public IReadOnlyList<RemoteMelody> RemoteMelodies => RemoteMelodiesList;

        /// <summary><c>text_templates</c> (spec §3.5): templates per language, ordered by language tag. Default "en" and "ru".</summary>
        public IReadOnlyList<TextTemplateSet> TextTemplates => TextTemplatesList;

        /// <summary><c>watermark_enabled</c> (spec §3.10, Р13). Default true; false removes the watermark for everyone.</summary>
        public bool WatermarkEnabled { get; internal set; }
        /// <summary><c>rate_prompt_after_exports</c> (spec §9.3). Default 2, range 1–20.</summary>
        public int RatePromptAfterExports { get; internal set; }

        internal IReadOnlyList<ModeCode> FreeModesList { get; set; }
        internal IReadOnlyList<ModeCode> ModesDisabledList { get; set; }
        internal IReadOnlyList<ModeCode> GalleryOrderList { get; set; }
        internal IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> GenerationWeightsList { get; set; }
        internal IReadOnlyList<TierOverride> TierOverridesList { get; set; }
        internal IReadOnlyList<PlatformSafeZone> PlatformSafeZonesList { get; set; }
        internal IReadOnlyList<string> RemotePresetsList { get; set; }
        internal IReadOnlyList<RemoteMelody> RemoteMelodiesList { get; set; }
        internal IReadOnlyList<TextTemplateSet> TextTemplatesList { get; set; }

        /// <summary>
        /// Effective free set: <see cref="FreeModes"/> minus <see cref="ModesDisabled"/>, in <c>free_modes</c> order (spec §5.1, §7.6).
        /// Disabling wins for availability: a disabled mode is unavailable even if listed as free.
        /// Grandfathered modes (spec §5.1) are added by Monetization, not here.
        /// </summary>
        public IReadOnlyList<ModeCode> EffectiveFreeModes => _effectiveFree;

        /// <summary>True when <paramref name="mode"/> is listed in <c>free_modes</c> (ignores <c>modes_disabled</c>).</summary>
        public bool IsListedFree(ModeCode mode)
        {
            int i = (int)mode;
            return i > 0 && i < _free.Length && _free[i];
        }

        /// <summary>True when <paramref name="mode"/> is in <c>modes_disabled</c> (spec §7.6).</summary>
        public bool IsModeDisabled(ModeCode mode)
        {
            int i = (int)mode;
            return i > 0 && i < _disabled.Length && _disabled[i];
        }

        /// <summary>True when the mode is free and not disabled (see <see cref="EffectiveFreeModes"/>).</summary>
        public bool IsEffectivelyFree(ModeCode mode)
        {
            return IsListedFree(mode) && !IsModeDisabled(mode);
        }

        /// <summary>Weight override of a mode, or false when the mode should use its built-in weights (spec §2.4).</summary>
        public bool TryGetGenerationWeights(ModeCode mode, out GenerationWeights weights)
        {
            foreach (var kv in GenerationWeightsList)
            {
                if (kv.Key == mode)
                {
                    weights = kv.Value;
                    return true;
                }
            }
            weights = null;
            return false;
        }

        /// <summary>Forced tier for a device model (case-insensitive, surrounding whitespace ignored), spec §8.1.</summary>
        public bool TryGetTierOverride(string deviceModel, out DeviceTier tier)
        {
            tier = default;
            if (deviceModel == null) return false;
            string key = deviceModel.Trim();
            foreach (var o in TierOverridesList)
            {
                if (string.Equals(o.DeviceModel, key, StringComparison.OrdinalIgnoreCase))
                {
                    tier = o.Tier;
                    return true;
                }
            }
            return false;
        }

        /// <summary>UI masks of a platform (case-insensitive id, e.g. "tiktok"), spec §3.3.</summary>
        public bool TryGetSafeZones(string platform, out IReadOnlyList<NormalizedRect> rects)
        {
            rects = null;
            if (platform == null) return false;
            string key = platform.Trim();
            foreach (var p in PlatformSafeZonesList)
            {
                if (string.Equals(p.Platform, key, StringComparison.OrdinalIgnoreCase))
                {
                    rects = p.Rects;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Text templates of a language (case-insensitive tag, e.g. "ru"), spec §3.5.</summary>
        public bool TryGetTextTemplates(string language, out IReadOnlyList<TextTemplate> templates)
        {
            templates = null;
            if (language == null) return false;
            string key = language.Trim();
            foreach (var set in TextTemplatesList)
            {
                if (string.Equals(set.Language, key, StringComparison.OrdinalIgnoreCase))
                {
                    templates = set.Templates;
                    return true;
                }
            }
            return false;
        }

        internal void Freeze()
        {
            int n = ModeCatalog.All.Count + 1;
            _free = new bool[n];
            _disabled = new bool[n];
            foreach (var c in FreeModesList) _free[(int)c] = true;
            foreach (var c in ModesDisabledList) _disabled[(int)c] = true;
            var effective = new List<ModeCode>();
            foreach (var c in FreeModesList)
            {
                if (!_disabled[(int)c]) effective.Add(c);
            }
            _effectiveFree = effective.AsReadOnly();
        }
    }
}
