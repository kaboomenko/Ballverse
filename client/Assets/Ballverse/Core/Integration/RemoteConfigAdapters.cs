using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Config;
using Ballverse.Core.Generation;
using Ballverse.Core.Layout;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Maps the typed Remote Config snapshot (<see cref="RemoteConfigValues"/>, spec §9.3) onto the types each consumer module
    /// works with. The Config module is the only owner of the §9.3 keys (parsing, validation, defaults, one JSON schema per
    /// key, see <c>Config/REMOTE_CONFIG_KEYS.md</c>); the consumer modules keep only their own persisted-state JSON and take
    /// typed values from here. Pure and deterministic; never touches the network or the clock.
    /// </summary>
    public static class RemoteConfigAdapters
    {
        // ---- Ads ----

        /// <summary>
        /// <c>interstitial_every_n_games</c>, <c>interstitial_min_interval_sec</c>, <c>interstitial_grace_games</c>,
        /// <c>rewarded_export_enabled</c>, <c>rewarded_wait_timeout_sec</c>, <c>rewarded_retry_window_min</c> → a new
        /// <see cref="AdPolicyConfig"/>. The quiet period after a rewarded ad (IN3) has no Remote Config key: it is taken from
        /// <paramref name="local"/> (default: the build default, 30 s). Config's ranges are the ones AdPolicyConfig clamps to
        /// (its constants alias <see cref="RemoteConfigLimits"/>), so <see cref="AdPolicyConfig.Validated"/> never changes a
        /// value from here.
        /// </summary>
        public static AdPolicyConfig ToAdPolicyConfig(RemoteConfigValues values, AdPolicyConfig local = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var c = (local ?? AdPolicyConfig.CreateDefault()).Clone();
            c.InterstitialEveryNGames = values.InterstitialEveryNGames;
            c.InterstitialMinIntervalSec = values.InterstitialMinIntervalSec;
            c.InterstitialGraceGames = values.InterstitialGraceGames;
            c.RewardedExportEnabled = values.RewardedExportEnabled;
            c.RewardedWaitTimeoutSec = values.RewardedWaitTimeoutSec;
            c.RewardedRetryWindowMin = values.RewardedRetryWindowMin;
            return c.Validated();
        }

        // ---- Generation ----

        /// <summary><c>generation_budget_by_tier</c> ({candidates, ms} per tier) → <see cref="GenerationBudgetTable"/>.</summary>
        public static GenerationBudgetTable ToGenerationBudgets(RemoteConfigValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var t = values.GenerationBudgetByTier;
            return GenerationBudgetTable.Default
                .With(DeviceTier.A, ToBudget(t.A))
                .With(DeviceTier.B, ToBudget(t.B))
                .With(DeviceTier.C, ToBudget(t.C));
        }

        static GenerationBudget ToBudget(GenerationBudgetConfig b)
        {
            return new GenerationBudget(b.Candidates, b.Milliseconds);
        }

        /// <summary>
        /// <c>generation_weights</c> merged over <paramref name="builtIn"/> (default <see cref="SpectacleProfileTable.Default"/>):
        /// a mode entry replaces the weights when it has them, and each of densityMin / densityMax / qualityThreshold it
        /// carries; absent fields keep the built-in value. An entry whose merged density range is invalid (e.g. a delivered
        /// densityMin above the built-in densityMax) keeps that mode's built-in profile and is reported.
        /// </summary>
        public static AdapterResult<SpectacleProfileTable> ToSpectacleProfiles(RemoteConfigValues values, SpectacleProfileTable builtIn = null)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var table = builtIn ?? SpectacleProfileTable.Default;
            var issues = new List<string>();
            foreach (var kv in values.GenerationWeights)
            {
                SpectacleProfile current = table.Get(kv.Key);
                GenerationWeights w = kv.Value;
                SpectacleWeights weights = w.HasWeights ? new SpectacleWeights(w.W1, w.W2, w.W3, w.W4, w.W5) : current.Weights;
                double min = w.DensityMin ?? current.Density.MinPerSecond;
                double max = w.DensityMax ?? current.Density.MaxPerSecond;
                if (!DensityRange.IsValid(min, max))
                {
                    issues.Add(kv.Key + ": density range " + min.ToString(CultureInfo.InvariantCulture) + ".."
                        + max.ToString(CultureInfo.InvariantCulture) + " is invalid with the built-in bound; built-in profile kept");
                    continue;
                }
                double threshold = w.QualityThreshold ?? current.QualityThreshold;
                table = table.With(kv.Key, new SpectacleProfile(weights, new DensityRange(min, max), threshold));
            }
            return new AdapterResult<SpectacleProfileTable>(table, issues);
        }

        // ---- Timeline ----

        /// <summary>
        /// <c>export_bitrates</c> (kbps per "720p30" … "1080p60") → <see cref="ExportBitrateTable"/> in bit/s. The audio bitrate
        /// has no Remote Config key and stays <see cref="ExportBitrateTable.DefaultAudioBps"/>.
        /// </summary>
        public static ExportBitrateTable ToExportBitrates(RemoteConfigValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var b = values.ExportBitrates;
            return new ExportBitrateTable(
                b.P1080Fps60Kbps * 1000L,
                b.P1080Fps30Kbps * 1000L,
                b.P720Fps60Kbps * 1000L,
                b.P720Fps30Kbps * 1000L,
                ExportBitrateTable.DefaultAudioBps);
        }

        /// <summary>
        /// Export sheet defaults for a tier (spec §3.8, §9.3): 1080p, fps from <c>export_default_fps_by_tier</c>, bitrates from
        /// <c>export_bitrates</c>.
        /// </summary>
        public static ExportSettings DefaultExportSettings(RemoteConfigValues values, DeviceTier tier)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return new ExportSettings(ExportResolution.P1080, values.ExportDefaultFpsByTier.Get(tier), ToExportBitrates(values));
        }

        // ---- Layout ----

        /// <summary>
        /// <c>platform_safe_zones</c> → <see cref="PlatformUiMasks"/>. Config has already merged delivered platforms over its
        /// offline defaults, so the result is built directly (not over <see cref="PlatformUiMasks.Default"/>); with the Config
        /// defaults it equals <see cref="PlatformUiMasks.Default"/> rect for rect. Config keeps platform ids unique under Layout's
        /// normalization too (<see cref="PlatformSafeZone.SameId"/>); as a second line of defence an id that would still collide
        /// in Layout (or is blank) is skipped, first one wins, so remote data can never make this throw.
        /// </summary>
        public static PlatformUiMasks ToPlatformUiMasks(RemoteConfigValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var platforms = new List<KeyValuePair<string, IEnumerable<FloatRect>>>(values.PlatformSafeZones.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var zone in values.PlatformSafeZones)
            {
                if (string.IsNullOrWhiteSpace(zone.Platform) || !ids.Add(PlatformSafeZone.CanonicalId(zone.Platform))) continue;
                var rects = new FloatRect[zone.Rects.Count];
                for (int i = 0; i < rects.Length; i++)
                {
                    NormalizedRect r = zone.Rects[i];
                    rects[i] = new FloatRect(r.X, r.Y, r.W, r.H);
                }
                platforms.Add(new KeyValuePair<string, IEnumerable<FloatRect>>(zone.Platform, rects));
            }
            return new PlatformUiMasks(platforms);
        }

        // ---- Params ----

        /// <summary>
        /// <c>remote_presets</c> validated against the mode schemas (spec §2.3, §7.6) with <see cref="RemotePresets.Load"/>.
        /// Presets of a mode that is not in this build's release are rejected as an unknown mode whatever
        /// <paramref name="schemaForMode"/> returns, so an unreleased mode never surfaces through a preset.
        /// </summary>
        /// <param name="schemaForMode">Complete schema of a mode id (e.g. <c>Merge(CommonParams.ForMode(info, common), layer)</c>), or null.</param>
        public static RemotePresetLoadResult LoadRemotePresets(RemoteConfigValues values, Func<string, ParamSchema> schemaForMode)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.RemotePresets.Count == 0) return RemotePresets.Load(null, schemaForMode);
            // Each element is a compact JSON object, so the array is their comma-joined list.
            string json = "[" + string.Join(",", values.RemotePresets) + "]";
            Func<string, ParamSchema> released = schemaForMode == null ? (Func<string, ParamSchema>)null : modeId =>
                ModeCatalog.TryGetById(modeId, out ModeInfo info) && !info.InRelease ? null : schemaForMode(modeId);
            return RemotePresets.Load(json, released);
        }

        // ---- Audio ----

        /// <summary>
        /// <c>remote_melodies</c> → Audio <see cref="Melody"/> objects, validated by Audio's own rules (<see cref="Melody.TryCreate"/>).
        /// Config applies the same rules, so those rejections only appear if the two rule sets ever drift apart.
        /// <para><b>Decision (built-in ids are reserved):</b> Remote Config only <i>adds</i> melodies (spec §2.7). An entry whose id
        /// (trimmed, ordinal) equals a built-in melody id (<see cref="CommonParams.Melodies"/>) is skipped and reported, so a
        /// project's <c>"melody"</c> always means the same notes whatever the console delivers; a new arrangement needs a new id.</para>
        /// </summary>
        public static AdapterResult<IReadOnlyList<Melody>> ToMelodies(RemoteConfigValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var melodies = new List<Melody>(values.RemoteMelodies.Count);
            var issues = new List<string>();
            var builtIn = new HashSet<string>(CommonParams.Melodies, StringComparer.Ordinal);
            foreach (var m in values.RemoteMelodies)
            {
                if (m.Id != null && builtIn.Contains(m.Id.Trim()))
                {
                    issues.Add(m.Id.Trim() + ": the id of a built-in melody is reserved; remote entry ignored");
                    continue;
                }
                var notes = new MelodyNote[m.Notes.Count];
                for (int i = 0; i < notes.Length; i++)
                {
                    RemoteMelodyNote n = m.Notes[i];
                    notes[i] = new MelodyNote(n.Pitch, n.DurWeight, n.Velocity);
                }
                if (Melody.TryCreate(m.Id, m.Name, notes, out Melody melody, out IReadOnlyList<string> errors)) melodies.Add(melody);
                else issues.Add(m.Id + ": " + string.Join("; ", errors));
            }
            return new AdapterResult<IReadOnlyList<Melody>>(melodies.AsReadOnly(), issues);
        }

        /// <summary>
        /// Melody option ids for <see cref="CommonParams.Build"/>: the built-in <see cref="CommonParams.Melodies"/> followed by the
        /// remote melodies not already listed, so presets naming a remote melody validate (spec §2.7, §7.6). Melodies from
        /// <see cref="ToMelodies"/> never reuse a built-in id; the duplicate check only guards hand-built lists.
        /// </summary>
        public static IReadOnlyList<string> MelodyOptionIds(IReadOnlyList<Melody> remoteMelodies)
        {
            var ids = new List<string>(CommonParams.Melodies);
            if (remoteMelodies != null)
            {
                foreach (var m in remoteMelodies)
                {
                    if (m != null && !ids.Contains(m.Id)) ids.Add(m.Id);
                }
            }
            return ids.AsReadOnly();
        }
    }
}
