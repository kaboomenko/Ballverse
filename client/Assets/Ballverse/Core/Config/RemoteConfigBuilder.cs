using System;
using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Mutable staging area for a <see cref="RemoteConfigValues"/> snapshot (spec §9.3). Starts from the built-in defaults
    /// or from an existing snapshot. Used by <see cref="RemoteConfigParser"/>, by service fakes and by tests.
    /// <see cref="Build"/> is strict (throws on out-of-range numbers and duplicates); the lenient path is the parser.
    /// </summary>
    public sealed class RemoteConfigBuilder
    {
        public List<ModeCode> FreeModes { get; set; }
        public List<ModeCode> ModesDisabled { get; set; }
        /// <summary>May be partial; <see cref="Build"/> appends missing modes in catalog order.</summary>
        public List<ModeCode> GalleryOrder { get; set; }
        public int InterstitialEveryNGames { get; set; }
        public int InterstitialMinIntervalSec { get; set; }
        public int InterstitialGraceGames { get; set; }
        public bool RewardedExportEnabled { get; set; }
        public int RewardedWaitTimeoutSec { get; set; }
        public int RewardedRetryWindowMin { get; set; }
        public bool PaidModeTrialEnabled { get; set; }
        public int PaidModeTrialSeconds { get; set; }
        public ExportBitrates ExportBitrates { get; set; }
        /// <summary>Each value must be 30 or 60.</summary>
        public TierValues<int> ExportDefaultFpsByTier { get; set; }
        /// <summary>Per-mode weight overrides; modes absent here use the mode's built-in weights.</summary>
        public Dictionary<ModeCode, GenerationWeights> GenerationWeights { get; set; }
        public TierValues<GenerationBudget> GenerationBudgetByTier { get; set; }
        public List<TierOverride> TierOverrides { get; set; }
        public List<PlatformSafeZone> PlatformSafeZones { get; set; }
        /// <summary>Raw JSON objects, one per preset; validated by the Params module later.</summary>
        public List<string> RemotePresets { get; set; }
        public List<RemoteMelody> RemoteMelodies { get; set; }
        public List<TextTemplateSet> TextTemplates { get; set; }
        public bool WatermarkEnabled { get; set; }
        public int RatePromptAfterExports { get; set; }

        /// <summary>Builder holding the spec §9.3 defaults.</summary>
        public RemoteConfigBuilder()
        {
            FreeModes = RemoteConfigDefaults.FreeModes();
            ModesDisabled = new List<ModeCode>();
            GalleryOrder = RemoteConfigDefaults.GalleryOrder();
            InterstitialEveryNGames = RemoteConfigDefaults.InterstitialEveryNGames;
            InterstitialMinIntervalSec = RemoteConfigDefaults.InterstitialMinIntervalSec;
            InterstitialGraceGames = RemoteConfigDefaults.InterstitialGraceGames;
            RewardedExportEnabled = RemoteConfigDefaults.RewardedExportEnabled;
            RewardedWaitTimeoutSec = RemoteConfigDefaults.RewardedWaitTimeoutSec;
            RewardedRetryWindowMin = RemoteConfigDefaults.RewardedRetryWindowMin;
            PaidModeTrialEnabled = RemoteConfigDefaults.PaidModeTrialEnabled;
            PaidModeTrialSeconds = RemoteConfigDefaults.PaidModeTrialSeconds;
            ExportBitrates = ExportBitrates.Defaults;
            ExportDefaultFpsByTier = RemoteConfigDefaults.ExportDefaultFpsByTier();
            GenerationWeights = new Dictionary<ModeCode, GenerationWeights>();
            GenerationBudgetByTier = RemoteConfigDefaults.GenerationBudgetByTier();
            TierOverrides = new List<TierOverride>();
            PlatformSafeZones = RemoteConfigDefaults.PlatformSafeZones();
            RemotePresets = new List<string>();
            RemoteMelodies = new List<RemoteMelody>();
            TextTemplates = RemoteConfigDefaults.TextTemplates();
            WatermarkEnabled = RemoteConfigDefaults.WatermarkEnabled;
            RatePromptAfterExports = RemoteConfigDefaults.RatePromptAfterExports;
        }

        /// <summary>Builder holding a copy of <paramref name="source"/>.</summary>
        public RemoteConfigBuilder(RemoteConfigValues source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            FreeModes = new List<ModeCode>(source.FreeModes);
            ModesDisabled = new List<ModeCode>(source.ModesDisabled);
            GalleryOrder = new List<ModeCode>(source.GalleryOrder);
            InterstitialEveryNGames = source.InterstitialEveryNGames;
            InterstitialMinIntervalSec = source.InterstitialMinIntervalSec;
            InterstitialGraceGames = source.InterstitialGraceGames;
            RewardedExportEnabled = source.RewardedExportEnabled;
            RewardedWaitTimeoutSec = source.RewardedWaitTimeoutSec;
            RewardedRetryWindowMin = source.RewardedRetryWindowMin;
            PaidModeTrialEnabled = source.PaidModeTrialEnabled;
            PaidModeTrialSeconds = source.PaidModeTrialSeconds;
            ExportBitrates = source.ExportBitrates;
            ExportDefaultFpsByTier = source.ExportDefaultFpsByTier;
            GenerationWeights = new Dictionary<ModeCode, GenerationWeights>();
            foreach (var kv in source.GenerationWeights) GenerationWeights.Add(kv.Key, kv.Value);
            GenerationBudgetByTier = source.GenerationBudgetByTier;
            TierOverrides = new List<TierOverride>(source.TierOverrides);
            PlatformSafeZones = new List<PlatformSafeZone>(source.PlatformSafeZones);
            RemotePresets = new List<string>(source.RemotePresets);
            RemoteMelodies = new List<RemoteMelody>(source.RemoteMelodies);
            TextTemplates = new List<TextTemplateSet>(source.TextTemplates);
            WatermarkEnabled = source.WatermarkEnabled;
            RatePromptAfterExports = source.RatePromptAfterExports;
        }

        /// <summary>
        /// Validates and freezes the values. Normalization: mode lists de-duplicated (first wins); gallery order completed to a
        /// permutation of all 15 modes; map-like lists sorted by key (ordinal, ignoring case) so the snapshot never depends on
        /// insertion order; presets re-serialized as compact JSON.
        /// </summary>
        /// <exception cref="ArgumentException">A value is out of range, null, duplicated or malformed.</exception>
        public RemoteConfigValues Build()
        {
            var v = new RemoteConfigValues();
            v.FreeModesList = Array.AsReadOnly(DistinctModes(FreeModes, nameof(FreeModes)).ToArray());
            v.ModesDisabledList = Array.AsReadOnly(DistinctModes(ModesDisabled, nameof(ModesDisabled)).ToArray());
            v.GalleryOrderList = Array.AsReadOnly(CompleteGalleryOrder(DistinctModes(GalleryOrder, nameof(GalleryOrder))).ToArray());

            v.InterstitialEveryNGames = Range(InterstitialEveryNGames, RemoteConfigLimits.InterstitialEveryNGamesMin, RemoteConfigLimits.InterstitialEveryNGamesMax, nameof(InterstitialEveryNGames));
            v.InterstitialMinIntervalSec = Range(InterstitialMinIntervalSec, RemoteConfigLimits.InterstitialMinIntervalSecMin, RemoteConfigLimits.InterstitialMinIntervalSecMax, nameof(InterstitialMinIntervalSec));
            v.InterstitialGraceGames = Range(InterstitialGraceGames, RemoteConfigLimits.InterstitialGraceGamesMin, RemoteConfigLimits.InterstitialGraceGamesMax, nameof(InterstitialGraceGames));
            v.RewardedExportEnabled = RewardedExportEnabled;
            v.RewardedWaitTimeoutSec = Range(RewardedWaitTimeoutSec, RemoteConfigLimits.RewardedWaitTimeoutSecMin, RemoteConfigLimits.RewardedWaitTimeoutSecMax, nameof(RewardedWaitTimeoutSec));
            v.RewardedRetryWindowMin = Range(RewardedRetryWindowMin, RemoteConfigLimits.RewardedRetryWindowMinMin, RemoteConfigLimits.RewardedRetryWindowMinMax, nameof(RewardedRetryWindowMin));
            v.PaidModeTrialEnabled = PaidModeTrialEnabled;
            v.PaidModeTrialSeconds = Range(PaidModeTrialSeconds, RemoteConfigLimits.PaidModeTrialSecondsMin, RemoteConfigLimits.PaidModeTrialSecondsMax, nameof(PaidModeTrialSeconds));
            v.ExportBitrates = ExportBitrates ?? throw new ArgumentNullException(nameof(ExportBitrates));

            var fps = ExportDefaultFpsByTier ?? throw new ArgumentNullException(nameof(ExportDefaultFpsByTier));
            if (!RemoteConfigLimits.IsValidFps(fps.A) || !RemoteConfigLimits.IsValidFps(fps.B) || !RemoteConfigLimits.IsValidFps(fps.C))
                throw new ArgumentException("export fps must be 30 or 60", nameof(ExportDefaultFpsByTier));
            v.ExportDefaultFpsByTier = fps;

            v.GenerationWeightsList = BuildWeights();
            var budget = GenerationBudgetByTier ?? throw new ArgumentNullException(nameof(GenerationBudgetByTier));
            if (budget.A == null || budget.B == null || budget.C == null)
                throw new ArgumentException("budget per tier must be set", nameof(GenerationBudgetByTier));
            v.GenerationBudgetByTier = budget;

            v.TierOverridesList = SortedUnique(TierOverrides, t => t.DeviceModel, nameof(TierOverrides));
            v.PlatformSafeZonesList = SortedUnique(PlatformSafeZones, p => p.Platform, nameof(PlatformSafeZones));
            v.RemotePresetsList = BuildPresets();
            v.RemoteMelodiesList = BuildMelodies();
            v.TextTemplatesList = SortedUnique(TextTemplates, t => t.Language, nameof(TextTemplates));
            v.WatermarkEnabled = WatermarkEnabled;
            v.RatePromptAfterExports = Range(RatePromptAfterExports, RemoteConfigLimits.RatePromptAfterExportsMin, RemoteConfigLimits.RatePromptAfterExportsMax, nameof(RatePromptAfterExports));
            v.Freeze();
            return v;
        }

        /// <summary>Gallery order completed to a permutation of all catalog modes: given order first, missing modes appended in catalog order.</summary>
        internal static List<ModeCode> CompleteGalleryOrder(List<ModeCode> distinct)
        {
            var result = new List<ModeCode>(distinct);
            var seen = new bool[ModeCatalog.All.Count + 1];
            foreach (var c in distinct) seen[(int)c] = true;
            foreach (var m in ModeCatalog.All)
            {
                if (!seen[(int)m.Code]) result.Add(m.Code);
            }
            return result;
        }

        static List<ModeCode> DistinctModes(List<ModeCode> modes, string name)
        {
            if (modes == null) throw new ArgumentNullException(name);
            var seen = new bool[ModeCatalog.All.Count + 1];
            var result = new List<ModeCode>(modes.Count);
            foreach (var c in modes)
            {
                int i = (int)c;
                if (i < 1 || i > ModeCatalog.All.Count) throw new ArgumentException("unknown mode code " + i, name);
                if (seen[i]) continue;
                seen[i] = true;
                result.Add(c);
            }
            return result;
        }

        static int Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name, value, "must be " + min + "–" + max);
            return value;
        }

        IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> BuildWeights()
        {
            if (GenerationWeights == null) throw new ArgumentNullException(nameof(GenerationWeights));
            var list = new List<KeyValuePair<ModeCode, GenerationWeights>>();
            foreach (var m in ModeCatalog.All)
            {
                if (GenerationWeights.TryGetValue(m.Code, out var w))
                {
                    if (w == null) throw new ArgumentException("null weights for " + m.Code, nameof(GenerationWeights));
                    list.Add(new KeyValuePair<ModeCode, GenerationWeights>(m.Code, w));
                }
            }
            if (list.Count != GenerationWeights.Count) throw new ArgumentException("unknown mode code in weights", nameof(GenerationWeights));
            return list.AsReadOnly();
        }

        IReadOnlyList<string> BuildPresets()
        {
            if (RemotePresets == null) throw new ArgumentNullException(nameof(RemotePresets));
            var list = new List<string>(RemotePresets.Count);
            foreach (var p in RemotePresets)
            {
                if (!RemoteConfigJson.TryParse(p, out var token, out var error) || token.Type != Newtonsoft.Json.Linq.JTokenType.Object)
                    throw new ArgumentException("preset must be a JSON object: " + (error ?? RemoteConfigJson.Excerpt(p)), nameof(RemotePresets));
                list.Add(RemoteConfigJson.Compact(token));
            }
            return list.AsReadOnly();
        }

        IReadOnlyList<RemoteMelody> BuildMelodies()
        {
            if (RemoteMelodies == null) throw new ArgumentNullException(nameof(RemoteMelodies));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in RemoteMelodies)
            {
                if (m == null) throw new ArgumentException("null melody", nameof(RemoteMelodies));
                if (!ids.Add(m.Id)) throw new ArgumentException("duplicate melody id " + m.Id, nameof(RemoteMelodies));
            }
            return new List<RemoteMelody>(RemoteMelodies).AsReadOnly();
        }

        static IReadOnlyList<T> SortedUnique<T>(List<T> items, Func<T, string> key, string name) where T : class
        {
            if (items == null) throw new ArgumentNullException(name);
            var list = new List<T>(items.Count);
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("null entry", name);
                list.Add(item);
            }
            // Stable, culture-independent order. Keys are unique ignoring case, so the comparison is total.
            list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(key(a), key(b)));
            for (int i = 1; i < list.Count; i++)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(key(list[i - 1]), key(list[i])))
                    throw new ArgumentException("duplicate key " + key(list[i]), name);
            }
            return list.AsReadOnly();
        }
    }
}
