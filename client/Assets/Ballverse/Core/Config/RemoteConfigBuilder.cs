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
        /// <summary>May omit M01–M05; <see cref="Build"/> appends them (spec §9.3: the free set can only be extended).</summary>
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
        public TierValues<GenerationBudgetConfig> GenerationBudgetByTier { get; set; }
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
        /// Validates and freezes the values. Normalization: mode lists de-duplicated (first wins); free modes completed with the
        /// default free modes M01–M05 (spec §9.3 extension only, §5.1); gallery order completed to a
        /// permutation of all 15 modes; map-like lists sorted by key (ordinal, ignoring case) so the snapshot never depends on
        /// insertion order; presets re-serialized as compact JSON.
        /// </summary>
        /// <exception cref="ArgumentException">A value is out of range, null, duplicated or malformed.</exception>
        public RemoteConfigValues Build()
        {
            return new RemoteConfigValues(this);
        }

        /// <summary>
        /// Free modes with the catalog's FreeByDefault modes (M01–M05) guaranteed: given order first, missing defaults appended in
        /// catalog order. Spec §9.3 "только расширение", §5.1 "Бесплатные режимы M01–M05 доступны всегда".
        /// </summary>
        internal static List<ModeCode> CompleteFreeModes(List<ModeCode> distinct)
        {
            var result = new List<ModeCode>(distinct);
            var seen = new bool[ModeCatalog.All.Count + 1];
            foreach (var c in distinct) seen[(int)c] = true;
            foreach (var m in ModeCatalog.All)
            {
                if (m.FreeByDefault && !seen[(int)m.Code]) result.Add(m.Code);
            }
            return result;
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

        internal static List<ModeCode> DistinctModes(List<ModeCode> modes, string name)
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

        internal static int Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) throw new ArgumentOutOfRangeException(name, value, "must be " + min + "–" + max);
            return value;
        }

        internal static IReadOnlyList<KeyValuePair<ModeCode, GenerationWeights>> BuildWeights(Dictionary<ModeCode, GenerationWeights> weights)
        {
            if (weights == null) throw new ArgumentNullException(nameof(GenerationWeights));
            var list = new List<KeyValuePair<ModeCode, GenerationWeights>>();
            foreach (var m in ModeCatalog.All)
            {
                if (weights.TryGetValue(m.Code, out var w))
                {
                    if (w == null) throw new ArgumentException("null weights for " + m.Code, nameof(GenerationWeights));
                    list.Add(new KeyValuePair<ModeCode, GenerationWeights>(m.Code, w));
                }
            }
            if (list.Count != weights.Count) throw new ArgumentException("unknown mode code in weights", nameof(GenerationWeights));
            return list.AsReadOnly();
        }

        internal static IReadOnlyList<string> BuildPresets(List<string> presets)
        {
            if (presets == null) throw new ArgumentNullException(nameof(RemotePresets));
            var list = new List<string>(presets.Count);
            foreach (var p in presets)
            {
                if (!RemoteConfigJson.TryParsePreservingNumbers(p, out var token, out var error) || token.Type != Newtonsoft.Json.Linq.JTokenType.Object)
                    throw new ArgumentException("preset must be a JSON object: " + (error ?? RemoteConfigJson.Excerpt(p)), nameof(RemotePresets));
                if (RemoteConfigJson.ContainsNonFiniteNumber(token))
                    throw new ArgumentException("preset contains a number outside the double range", nameof(RemotePresets));
                list.Add(RemoteConfigJson.Compact(token));
            }
            return list.AsReadOnly();
        }

        internal static IReadOnlyList<RemoteMelody> BuildMelodies(List<RemoteMelody> melodies)
        {
            if (melodies == null) throw new ArgumentNullException(nameof(RemoteMelodies));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in melodies)
            {
                if (m == null) throw new ArgumentException("null melody", nameof(RemoteMelodies));
                if (!ids.Add(m.Id)) throw new ArgumentException("duplicate melody id " + m.Id, nameof(RemoteMelodies));
            }
            return new List<RemoteMelody>(melodies).AsReadOnly();
        }

        internal static IReadOnlyList<T> SortedUnique<T>(List<T> items, Func<T, string> key, string name) where T : class
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
