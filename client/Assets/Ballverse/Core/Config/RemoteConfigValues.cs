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

        readonly bool[] _free;
        readonly bool[] _disabled;
        readonly IReadOnlyList<ModeCode> _effectiveFree;
        readonly IReadOnlyList<ModeCode> _freeModes;
        readonly IReadOnlyList<ModeCode> _modesDisabled;
        readonly IReadOnlyList<ModeCode> _galleryOrder;
        readonly IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> _generationWeights;
        readonly IReadOnlyList<TierOverride> _tierOverrides;
        readonly IReadOnlyList<PlatformSafeZone> _platformSafeZones;
        readonly IReadOnlyList<string> _remotePresets;
        readonly IReadOnlyList<RemoteMelody> _remoteMelodies;
        readonly IReadOnlyList<TextTemplateSet> _textTemplates;

        /// <summary>
        /// Validates and copies the builder's values (see <see cref="RemoteConfigBuilder.Build"/>). Every field is read-only and
        /// every collection is a private read-only copy, so a snapshot (including <see cref="Defaults"/>) cannot be changed later.
        /// </summary>
        internal RemoteConfigValues(RemoteConfigBuilder b)
        {
            _freeModes = Array.AsReadOnly(RemoteConfigBuilder.CompleteFreeModes(RemoteConfigBuilder.DistinctModes(b.FreeModes, nameof(b.FreeModes))).ToArray());
            _modesDisabled = Array.AsReadOnly(RemoteConfigBuilder.DistinctModes(b.ModesDisabled, nameof(b.ModesDisabled)).ToArray());
            _galleryOrder = Array.AsReadOnly(RemoteConfigBuilder.CompleteGalleryOrder(RemoteConfigBuilder.DistinctModes(b.GalleryOrder, nameof(b.GalleryOrder))).ToArray());

            InterstitialEveryNGames = RemoteConfigBuilder.Range(b.InterstitialEveryNGames, RemoteConfigLimits.InterstitialEveryNGamesMin, RemoteConfigLimits.InterstitialEveryNGamesMax, nameof(b.InterstitialEveryNGames));
            InterstitialMinIntervalSec = RemoteConfigBuilder.Range(b.InterstitialMinIntervalSec, RemoteConfigLimits.InterstitialMinIntervalSecMin, RemoteConfigLimits.InterstitialMinIntervalSecMax, nameof(b.InterstitialMinIntervalSec));
            InterstitialGraceGames = RemoteConfigBuilder.Range(b.InterstitialGraceGames, RemoteConfigLimits.InterstitialGraceGamesMin, RemoteConfigLimits.InterstitialGraceGamesMax, nameof(b.InterstitialGraceGames));
            RewardedExportEnabled = b.RewardedExportEnabled;
            RewardedWaitTimeoutSec = RemoteConfigBuilder.Range(b.RewardedWaitTimeoutSec, RemoteConfigLimits.RewardedWaitTimeoutSecMin, RemoteConfigLimits.RewardedWaitTimeoutSecMax, nameof(b.RewardedWaitTimeoutSec));
            RewardedRetryWindowMin = RemoteConfigBuilder.Range(b.RewardedRetryWindowMin, RemoteConfigLimits.RewardedRetryWindowMinMin, RemoteConfigLimits.RewardedRetryWindowMinMax, nameof(b.RewardedRetryWindowMin));
            PaidModeTrialEnabled = b.PaidModeTrialEnabled;
            PaidModeTrialSeconds = RemoteConfigBuilder.Range(b.PaidModeTrialSeconds, RemoteConfigLimits.PaidModeTrialSecondsMin, RemoteConfigLimits.PaidModeTrialSecondsMax, nameof(b.PaidModeTrialSeconds));
            ExportBitrates = b.ExportBitrates ?? throw new ArgumentNullException(nameof(b.ExportBitrates));

            var fps = b.ExportDefaultFpsByTier ?? throw new ArgumentNullException(nameof(b.ExportDefaultFpsByTier));
            if (!RemoteConfigLimits.IsValidFps(fps.A) || !RemoteConfigLimits.IsValidFps(fps.B) || !RemoteConfigLimits.IsValidFps(fps.C))
                throw new ArgumentException("export fps must be 30 or 60", nameof(b.ExportDefaultFpsByTier));
            ExportDefaultFpsByTier = fps;

            _generationWeights = RemoteConfigBuilder.BuildWeights(b.GenerationWeights);
            var budget = b.GenerationBudgetByTier ?? throw new ArgumentNullException(nameof(b.GenerationBudgetByTier));
            if (budget.A == null || budget.B == null || budget.C == null)
                throw new ArgumentException("budget per tier must be set", nameof(b.GenerationBudgetByTier));
            GenerationBudgetByTier = budget;

            _tierOverrides = RemoteConfigBuilder.SortedUnique(b.TierOverrides, t => t.DeviceModel, nameof(b.TierOverrides));
            _platformSafeZones = RemoteConfigBuilder.SortedUnique(b.PlatformSafeZones, p => p.Platform, nameof(b.PlatformSafeZones));
            _remotePresets = RemoteConfigBuilder.BuildPresets(b.RemotePresets);
            _remoteMelodies = RemoteConfigBuilder.BuildMelodies(b.RemoteMelodies);
            _textTemplates = RemoteConfigBuilder.SortedUnique(b.TextTemplates, t => t.Language, nameof(b.TextTemplates));
            WatermarkEnabled = b.WatermarkEnabled;
            RatePromptAfterExports = RemoteConfigBuilder.Range(b.RatePromptAfterExports, RemoteConfigLimits.RatePromptAfterExportsMin, RemoteConfigLimits.RatePromptAfterExportsMax, nameof(b.RatePromptAfterExports));

            // Lookup caches are derived once from the final read-only lists, so they can never disagree with them.
            int n = ModeCatalog.All.Count + 1;
            _free = new bool[n];
            _disabled = new bool[n];
            foreach (var c in _freeModes) _free[(int)c] = true;
            foreach (var c in _modesDisabled) _disabled[(int)c] = true;
            var effective = new List<ModeCode>();
            foreach (var c in _freeModes)
            {
                if (!_disabled[(int)c]) effective.Add(c);
            }
            _effectiveFree = effective.AsReadOnly();
        }

        /// <summary>
        /// <c>free_modes</c> (spec §5.1): released modes usable without an entitlement, in config order. Default (release 1, Р18):
        /// M01, M02, M05 — every released mode. Extension only (spec §9.3 "только расширение", §5.1): the catalog's FreeByDefault
        /// modes are always part of this list; a remote list that omits them gets them appended in catalog order (Warning
        /// <see cref="RemoteConfigIssueKind.NarrowedFreeModes"/>). Modes not in this build's release are ignored (Info
        /// <see cref="RemoteConfigIssueKind.UnreleasedMode"/>), so a future mode can never become available through config alone.
        /// An A/B test that swaps a free mode for a paid one (old spec §9.6 "замена M04 на M07") is therefore not expressible;
        /// the release-1 spec rewrote that test as "+ one paid mode" (extension). Grandfathering is Monetization's concern.
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => _freeModes;

        /// <summary><c>modes_disabled</c> (spec §7.6 emergency kill switch). Default empty.</summary>
        public IReadOnlyList<ModeCode> ModesDisabled => _modesDisabled;

        /// <summary>
        /// <c>gallery_order</c> (spec §4.3): always a permutation of the released modes (<see cref="ModeCatalog.Released"/>);
        /// unreleased modes never appear. Default: released modes in catalog order (release 1: M01, M02, M05).
        /// The UI still groups free → bought → paid and applies this order inside each group.
        /// </summary>
        public IReadOnlyList<ModeCode> GalleryOrder => _galleryOrder;

        /// <summary><c>interstitial_every_n_games</c> (spec §5.3.2 IN1, Р7). Default 3, range 1–20.</summary>
        public int InterstitialEveryNGames { get; }
        /// <summary><c>interstitial_min_interval_sec</c> (spec §5.3.2 IN5). Default 60, range 0–600.</summary>
        public int InterstitialMinIntervalSec { get; }
        /// <summary><c>interstitial_grace_games</c> (spec §5.3.2 IN6). Default 0, range 0–50.</summary>
        public int InterstitialGraceGames { get; }
        /// <summary><c>rewarded_export_enabled</c> (spec §5.3.1 RW7). Default true; false disables the export ad gate.</summary>
        public bool RewardedExportEnabled { get; }
        /// <summary><c>rewarded_wait_timeout_sec</c> (spec §5.3.1 RW3). Default 5, range 1–30.</summary>
        public int RewardedWaitTimeoutSec { get; }
        /// <summary><c>rewarded_retry_window_min</c> (spec §5.3.1 RW4). Default 10, range 0–60.</summary>
        public int RewardedRetryWindowMin { get; }
        /// <summary><c>paid_mode_trial_enabled</c> (spec §5.2). Default true.</summary>
        public bool PaidModeTrialEnabled { get; }
        /// <summary><c>paid_mode_trial_seconds</c> (spec §5.2). Default 30, range 10–120.</summary>
        public int PaidModeTrialSeconds { get; }
        /// <summary><c>export_bitrates</c> (spec §3.8 table). kbps, range 1000–50000.</summary>
        public ExportBitrates ExportBitrates { get; }
        /// <summary><c>export_default_fps_by_tier</c> (spec §9.3). Default {A:60, B:60, C:30}; values 30 or 60.</summary>
        public TierValues<int> ExportDefaultFpsByTier { get; }

        /// <summary>
        /// <c>generation_weights</c> (spec §2.4): per-mode overrides of w1…w5 and / or density range and quality threshold
        /// (<see cref="Config.GenerationWeights"/>), ordered by mode code. Default empty: every mode uses its built-in profile.
        /// </summary>
        public IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> GenerationWeights => _generationWeights;

        /// <summary><c>generation_budget_by_tier</c> (spec §2.4). Default A 24/1500 ms, B 16/2500 ms, C 8/3500 ms.</summary>
        public TierValues<GenerationBudgetConfig> GenerationBudgetByTier { get; }

        /// <summary><c>tier_overrides</c> (spec §8.1, R5): forced tiers per device model, ordered by model (ordinal, ignoring case). Default empty.</summary>
        public IReadOnlyList<TierOverride> TierOverrides => _tierOverrides;

        /// <summary><c>platform_safe_zones</c> (spec §3.3): UI masks per platform, ordered by platform id. Default tiktok/reels/shorts.</summary>
        public IReadOnlyList<PlatformSafeZone> PlatformSafeZones => _platformSafeZones;

        /// <summary>
        /// <c>remote_presets</c> (spec §2.3): each element is one preset as a compact JSON object string, in config order. Numbers
        /// keep double precision, exactly as Params reads them (integers keep every digit; presets with a number beyond the double
        /// range are dropped).
        /// Only checked to be a JSON object here; schema validation belongs to the Params module. Default empty.
        /// </summary>
        public IReadOnlyList<string> RemotePresets => _remotePresets;

        /// <summary><c>remote_melodies</c> (spec §2.7): validated melodies in config order, unique ids. Default empty.</summary>
        public IReadOnlyList<RemoteMelody> RemoteMelodies => _remoteMelodies;

        /// <summary><c>text_templates</c> (spec §3.5): templates per language, ordered by language tag. Default "en" and "ru".</summary>
        public IReadOnlyList<TextTemplateSet> TextTemplates => _textTemplates;

        /// <summary><c>watermark_enabled</c> (spec §3.10, Р13). Default true; false removes the watermark for everyone.</summary>
        public bool WatermarkEnabled { get; }
        /// <summary><c>rate_prompt_after_exports</c> (spec §9.3). Default 2, range 1–20.</summary>
        public int RatePromptAfterExports { get; }


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
            foreach (var kv in _generationWeights)
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
            foreach (var o in _tierOverrides)
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
            foreach (var p in _platformSafeZones)
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
            foreach (var set in _textTemplates)
            {
                if (string.Equals(set.Language, key, StringComparison.OrdinalIgnoreCase))
                {
                    templates = set.Templates;
                    return true;
                }
            }
            return false;
        }
    }
}
