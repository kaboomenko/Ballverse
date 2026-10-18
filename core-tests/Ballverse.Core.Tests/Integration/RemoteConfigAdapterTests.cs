using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Config;
using Ballverse.Core.Generation;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Tests.Params;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Integration
{
    /// <summary>
    /// RemoteConfigValues → consumer modules. Each adapter is checked with the Config defaults (must reproduce the consumer's
    /// built-in defaults exactly) and with edge payloads parsed by the Config module (the single owner of the §9.3 keys).
    /// </summary>
    public class RemoteConfigAdapterTests
    {
        static readonly RemoteConfigValues D = RemoteConfigValues.Defaults;

        static RemoteConfigParseResult Parse(params (string key, string value)[] pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in pairs) d[k] = v;
            return RemoteConfigParser.Parse(d);
        }

        // ---- Ads ----

        [Fact]
        public void AdPolicyFromDefaultsEqualsBuildDefaults()
        {
            AdPolicyConfig c = RemoteConfigAdapters.ToAdPolicyConfig(D);
            Assert.Equal(AdPolicyConfig.CreateDefault().ToJson(), c.ToJson());
            Assert.True(c.IsValid);
        }

        [Fact]
        public void AdPolicyConstantsAliasTheConfigLimits()
        {
            // QA residual (ads): the constants used to be copies, so a change in RemoteConfigLimits went unnoticed.
            Assert.Equal(RemoteConfigLimits.InterstitialEveryNGamesMin, AdPolicyConfig.MinInterstitialEveryNGames);
            Assert.Equal(RemoteConfigLimits.InterstitialEveryNGamesMax, AdPolicyConfig.MaxInterstitialEveryNGames);
            Assert.Equal(RemoteConfigLimits.InterstitialMinIntervalSecMin, AdPolicyConfig.MinInterstitialMinIntervalSec);
            Assert.Equal(RemoteConfigLimits.InterstitialMinIntervalSecMax, AdPolicyConfig.MaxInterstitialMinIntervalSec);
            Assert.Equal(RemoteConfigLimits.InterstitialGraceGamesMin, AdPolicyConfig.MinInterstitialGraceGames);
            Assert.Equal(RemoteConfigLimits.InterstitialGraceGamesMax, AdPolicyConfig.MaxInterstitialGraceGames);
            Assert.Equal(RemoteConfigLimits.RewardedWaitTimeoutSecMin, AdPolicyConfig.MinRewardedWaitTimeoutSec);
            Assert.Equal(RemoteConfigLimits.RewardedWaitTimeoutSecMax, AdPolicyConfig.MaxRewardedWaitTimeoutSec);
            Assert.Equal(RemoteConfigLimits.RewardedRetryWindowMinMin, AdPolicyConfig.MinRewardedRetryWindowMin);
            Assert.Equal(RemoteConfigLimits.RewardedRetryWindowMinMax, AdPolicyConfig.MaxRewardedRetryWindowMin);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialEveryNGames, D.InterstitialEveryNGames);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialMinIntervalSec, D.InterstitialMinIntervalSec);
            Assert.Equal(AdPolicyConfig.DefaultInterstitialGraceGames, D.InterstitialGraceGames);
            Assert.Equal(AdPolicyConfig.DefaultRewardedWaitTimeoutSec, D.RewardedWaitTimeoutSec);
            Assert.Equal(AdPolicyConfig.DefaultRewardedRetryWindowMin, D.RewardedRetryWindowMin);
        }

        [Theory]
        [InlineData("0", "-5", "-1", "0", "-3")]
        [InlineData("999", "99999", "1000", "1000", "1000")]
        [InlineData("7", "120", "4", "12", "30")]
        public void AdPolicyMapsEveryKeyAndValidatedChangesNothing(string n, string interval, string grace, string wait, string retry)
        {
            var r = Parse((RemoteConfigKeys.InterstitialEveryNGames, n), (RemoteConfigKeys.InterstitialMinIntervalSec, interval),
                (RemoteConfigKeys.InterstitialGraceGames, grace), (RemoteConfigKeys.RewardedWaitTimeoutSec, wait),
                (RemoteConfigKeys.RewardedRetryWindowMin, retry), (RemoteConfigKeys.RewardedExportEnabled, "false"));
            var v = r.Values;
            var local = AdPolicyConfig.CreateDefault();
            local.InterstitialQuietAfterRewardedSec = 45;
            AdPolicyConfig c = RemoteConfigAdapters.ToAdPolicyConfig(v, local);
            Assert.Equal(v.InterstitialEveryNGames, c.InterstitialEveryNGames);
            Assert.Equal(v.InterstitialMinIntervalSec, c.InterstitialMinIntervalSec);
            Assert.Equal(v.InterstitialGraceGames, c.InterstitialGraceGames);
            Assert.Equal(v.RewardedWaitTimeoutSec, c.RewardedWaitTimeoutSec);
            Assert.Equal(v.RewardedRetryWindowMin, c.RewardedRetryWindowMin);
            Assert.False(c.RewardedExportEnabled);
            Assert.Equal(45, c.InterstitialQuietAfterRewardedSec); // no Remote Config key: kept from the local config
            Assert.Equal(45, local.InterstitialQuietAfterRewardedSec);
            Assert.True(local.RewardedExportEnabled); // the local config is not mutated
            Assert.Equal(c.ToJson(), c.Validated().ToJson());
            Assert.Empty(c.Validate());
        }

        // ---- Generation budgets ----

        [Fact]
        public void GenerationBudgetsFromDefaultsEqualTheSpecTable()
        {
            GenerationBudgetTable t = RemoteConfigAdapters.ToGenerationBudgets(D);
            foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                Assert.Equal(GenerationBudgetTable.Default.Get(tier), t.Get(tier));
        }

        [Fact]
        public void GenerationBudgetLimitsOfConfigFitTheGenerationModule()
        {
            Assert.True(GenerationBudget.IsValidConfigured(RemoteConfigLimits.BudgetCandidatesMax, RemoteConfigLimits.BudgetMsMax));
            Assert.True(GenerationBudget.IsValidConfigured(RemoteConfigLimits.BudgetCandidatesMin, RemoteConfigLimits.BudgetMsMin));
            Assert.True(RemoteConfigLimits.BudgetCandidatesMax <= GenerationSessionOptions.MaxBudgetCandidates);
            Assert.True(RemoteConfigLimits.BudgetMsMax <= GenerationSessionOptions.MaxBudgetTimeMs);
        }

        [Fact]
        public void GenerationBudgetPayloadAndEdgeValues()
        {
            // One schema for generation_budget_by_tier: {"A":{"candidates":n,"ms":n}} (the Generation module's "timeMs" form is gone).
            var r = Parse((RemoteConfigKeys.GenerationBudgetByTier,
                "{\"A\":{\"candidates\":2.5},\"B\":{\"candidates\":3,\"ms\":900},\"C\":{\"candidates\":\"12\",\"ms\":999999,\"timeMs\":5}}"));
            GenerationBudgetTable t = RemoteConfigAdapters.ToGenerationBudgets(r.Values);
            Assert.Equal(GenerationBudget.TierA, t.Get(DeviceTier.A)); // fraction rejected, not rounded
            Assert.Equal(new GenerationBudget(3, 900), t.Get(DeviceTier.B));
            Assert.Equal(new GenerationBudget(8, RemoteConfigLimits.BudgetMsMax), t.Get(DeviceTier.C)); // quoted number rejected, ms clamped
            Assert.Contains(r.Issues, i => i.Message.Contains("timeMs"));
            var options = GenerationSessionOptions.ForMode(ModeCode.M01, DeviceTier.B, 1UL, 1800, null, t);
            Assert.Equal(new GenerationBudget(3, 900), options.Budget);
        }

        // ---- Generation weights / profiles ----

        [Fact]
        public void SpectacleProfilesFromDefaultsAreTheBuiltInTable()
        {
            var r = RemoteConfigAdapters.ToSpectacleProfiles(D);
            Assert.False(r.HasIssues);
            Assert.Same(SpectacleProfileTable.Default, r.Value);
        }

        [Fact]
        public void WeightLimitsOfConfigEqualTheGenerationModule()
        {
            Assert.Equal(SpectacleWeights.MaxWeight, RemoteConfigLimits.WeightMax);
            Assert.Equal(SpectacleProfile.MaxAbsThreshold, RemoteConfigLimits.QualityThresholdAbsMax);
        }

        [Fact]
        public void SpectacleProfilesMergeWeightsDensityAndThreshold()
        {
            var r = Parse((RemoteConfigKeys.GenerationWeights,
                "{\"ring_escape\":{\"w1\":2.5,\"w2\":0.8,\"w3\":0.6,\"w4\":0.5,\"w5\":1.5,\"densityMax\":9},"
                + "\"helix_drop\":{\"qualityThreshold\":1.1},\"M02\":[1,1,1,1,1]}"));
            Assert.Empty(r.Issues);
            var result = RemoteConfigAdapters.ToSpectacleProfiles(r.Values);
            Assert.False(result.HasIssues);
            SpectacleProfileTable t = result.Value;
            SpectacleProfile d01 = SpectacleProfileTable.Default.Get(ModeCode.M01);
            SpectacleProfile m01 = t.Get(ModeCode.M01);
            Assert.Equal(2.5, m01.Weights.W1);
            Assert.Equal(d01.Weights.W2, m01.Weights.W2);
            Assert.Equal(d01.Density.MinPerSecond, m01.Density.MinPerSecond);
            Assert.Equal(9.0, m01.Density.MaxPerSecond);
            Assert.Equal(d01.QualityThreshold, m01.QualityThreshold);
            SpectacleProfile m05 = t.Get(ModeCode.M05);
            Assert.Equal(1.1, m05.QualityThreshold);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M05).Weights, m05.Weights);
            Assert.Equal(new SpectacleWeights(1, 1, 1, 1, 1), t.Get(ModeCode.M02).Weights);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M03), t.Get(ModeCode.M03));
            var options = GenerationSessionOptions.ForMode(ModeCode.M05, DeviceTier.B, 1UL, 1800, t);
            Assert.Equal(1.1, options.QualityThreshold);
        }

        [Theory]
        [InlineData("{\"M01\":{\"w1\":\"0,5\",\"w2\":1,\"w3\":1,\"w4\":1,\"w5\":1}}")] // QA residual: "0,5" was read as 5
        [InlineData("{\"M01\":{\"qualityThreshold\":\"1,6\"}}")] // ... and "1,6" as a threshold of 16
        [InlineData("{\"M01\":{\"densityMax\":\"8,0\"}}")]
        [InlineData("{\"M01\":{\"w1\":1e308,\"w2\":1e308,\"w3\":1,\"w4\":1,\"w5\":1}}")]
        [InlineData("{\"M01\":{\"w1\":1,\"w2\":1,\"w3\":1,\"w4\":1,\"w5\":100.5}}")]
        [InlineData("{\"M01\":{\"qualityThreshold\":-1e6}}")]
        [InlineData("{\"M01\":{\"w3\":1.4,\"densityMin\":2.5}}")] // weights all together or none
        [InlineData("{\"M01\":{\"densityMin\":5,\"densityMax\":2}}")]
        [InlineData("{\"M01\":{\"densityMax\":0}}")]
        [InlineData("{\"M01\":{}}")]
        [InlineData("{\"M01\":5}")]
        [InlineData("{\"M01\":null}")]
        public void InvalidWeightEntriesKeepTheBuiltInProfile(string payload)
        {
            var r = Parse((RemoteConfigKeys.GenerationWeights, payload));
            Assert.Contains(r.Issues, i => i.Kind == RemoteConfigIssueKind.InvalidEntry && i.Severity == RemoteConfigIssueSeverity.Warning);
            Assert.Empty(r.Values.GenerationWeights);
            var t = RemoteConfigAdapters.ToSpectacleProfiles(r.Values).Value;
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M01), t.Get(ModeCode.M01));
        }

        [Fact]
        public void DensityBoundConflictingWithTheBuiltInOneIsReported()
        {
            // M01 built-in density is 1.5..8: a delivered densityMin of 12 alone is valid for Config but not after merging.
            var r = Parse((RemoteConfigKeys.GenerationWeights, "{\"M01\":{\"densityMin\":12},\"M02\":{\"qualityThreshold\":2}}"));
            Assert.Empty(r.Issues);
            var result = RemoteConfigAdapters.ToSpectacleProfiles(r.Values);
            Assert.Single(result.Issues);
            Assert.StartsWith("M01:", result.Issues[0]);
            Assert.Same(SpectacleProfileTable.Default.Get(ModeCode.M01), result.Value.Get(ModeCode.M01));
            Assert.Equal(2.0, result.Value.Get(ModeCode.M02).QualityThreshold);
        }

        [Fact]
        public void WeightEntriesRoundTripThroughTheConfigWriter()
        {
            var r = Parse((RemoteConfigKeys.GenerationWeights,
                "{\"M01\":{\"w1\":1,\"w2\":0.5,\"w3\":2,\"w4\":0.25,\"w5\":0.75,\"densityMin\":1,\"densityMax\":8,\"qualityThreshold\":1.6},"
                + "\"M05\":{\"densityMax\":5}}"));
            Assert.Empty(r.Issues);
            var raw = RemoteConfigWriter.ToRaw(r.Values).ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.Equal("{\"M01\":{\"w1\":1.0,\"w2\":0.5,\"w3\":2.0,\"w4\":0.25,\"w5\":0.75,\"densityMin\":1.0,\"densityMax\":8.0,\"qualityThreshold\":1.6},"
                + "\"M05\":{\"densityMax\":5.0}}", raw[RemoteConfigKeys.GenerationWeights]);
            var again = RemoteConfigParser.Parse(raw);
            Assert.Empty(again.Issues);
            Assert.Equal(raw, RemoteConfigWriter.ToRaw(again.Values).ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        // ---- Timeline ----

        [Fact]
        public void ExportBitratesFromDefaultsEqualTheSpecTable()
        {
            ExportBitrateTable t = RemoteConfigAdapters.ToExportBitrates(D);
            ExportBitrateTable d = ExportBitrateTable.Default;
            Assert.Equal(d.Video1080p60Bps, t.Video1080p60Bps);
            Assert.Equal(d.Video1080p30Bps, t.Video1080p30Bps);
            Assert.Equal(d.Video720p60Bps, t.Video720p60Bps);
            Assert.Equal(d.Video720p30Bps, t.Video720p30Bps);
            Assert.Equal(d.AudioBps, t.AudioBps);
        }

        [Fact]
        public void ExportBitrateLimitsOfConfigFitTheTimelineTable()
        {
            Assert.True(RemoteConfigLimits.BitrateKbpsMin * 1000L >= ExportBitrateTable.MinBps);
            Assert.True(RemoteConfigLimits.BitrateKbpsMax * 1000L <= ExportBitrateTable.MaxBps);
        }

        [Fact]
        public void ExportBitratesPayloadAndDefaultSettings()
        {
            // One schema: kbps per "720p30"…"1080p60" (the Timeline module's "video1080p60Bps" form is gone).
            var r = Parse((RemoteConfigKeys.ExportBitrates, "{\"1080p60\":14000,\"720p30\":999999,\"720p60\":12.5,\"video1080p60Bps\":1}"),
                (RemoteConfigKeys.ExportDefaultFpsByTier, "{\"B\":30}"));
            ExportBitrateTable t = RemoteConfigAdapters.ToExportBitrates(r.Values);
            Assert.Equal(14000000L, t.Video1080p60Bps);
            Assert.Equal(RemoteConfigLimits.BitrateKbpsMax * 1000L, t.Video720p30Bps); // clamped by Config
            Assert.Equal(ExportBitrateTable.DefaultVideo720p60Bps, t.Video720p60Bps); // fraction rejected
            Assert.Equal(ExportBitrateTable.DefaultAudioBps, t.AudioBps);
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));

            ExportSettings b = RemoteConfigAdapters.DefaultExportSettings(r.Values, DeviceTier.B);
            Assert.Equal(30, b.Fps);
            Assert.Equal(ExportResolution.P1080, b.Resolution);
            Assert.Equal(10000000L, b.VideoBitrateBps);
            ExportSettings a = RemoteConfigAdapters.DefaultExportSettings(r.Values, DeviceTier.A);
            Assert.Equal(60, a.Fps);
            Assert.Equal(14000000L, a.VideoBitrateBps);
            foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                Assert.Equal(ExportSettings.DefaultFps(tier), RemoteConfigAdapters.DefaultExportSettings(D, tier).Fps);
        }

        // ---- Layout ----

        [Fact]
        public void PlatformMasksFromDefaultsEqualLayoutDefaults()
        {
            PlatformUiMasks m = RemoteConfigAdapters.ToPlatformUiMasks(D);
            Assert.Equal(PlatformUiMasks.Default.PlatformIds, m.PlatformIds);
            foreach (string id in m.PlatformIds) Assert.Equal(PlatformUiMasks.Default.Get(id), m.Get(id));
        }

        [Fact]
        public void PlatformMasksPayloadMergesPerPlatform()
        {
            var r = Parse((RemoteConfigKeys.PlatformSafeZones,
                "{\"TikTok\":[{\"x\":0,\"y\":0,\"w\":1,\"h\":0.1}],\"snapchat\":[{\"x\":0,\"y\":0.9,\"w\":1,\"h\":0.2}],\"reels\":[]}"));
            PlatformUiMasks m = RemoteConfigAdapters.ToPlatformUiMasks(r.Values);
            Assert.Equal(new[] { "reels", "shorts", "snapchat", "tiktok" }, m.PlatformIds);
            Assert.Equal(new[] { new FloatRect(0f, 0f, 1f, 0.1f) }, m.Get("tiktok"));
            Assert.Empty(m.Get("reels")); // explicit empty array clears
            Assert.Equal(PlatformUiMasks.Default.Get("shorts"), m.Get("shorts"));
            FloatRect snap = Assert.Single(m.Get("snapchat"));
            Assert.Equal(0.9f, snap.Y);
            Assert.True(snap.Y + snap.Height <= 1f + 1e-6f); // clamped by Config
            var request = new FrameRequest(1080, 1920, LayoutSpec.Default, masks: m.Union(new[] { "tiktok" }));
            Assert.Single(request.MaskRects);
        }

        // ---- Params ----

        static ParamSchema SchemaFor(string modeId)
        {
            if (modeId == "ring_escape") return TestSchemas.RingEscape();
            if (modeId == "peg_drop") return TestSchemas.PegDrop();
            return null;
        }

        [Fact]
        public void RemotePresetsFromDefaultsAreEmpty()
        {
            var r = RemoteConfigAdapters.LoadRemotePresets(D, SchemaFor);
            Assert.Empty(r.Accepted);
            Assert.Empty(r.Rejected);
        }

        [Fact]
        public void RemotePresetsAreValidatedAgainstModeSchemasAndUnreleasedModesAreRejected()
        {
            var r = Parse((RemoteConfigKeys.RemotePresets,
                "[{\"id\":\"remote.rush\",\"name\":\"Rush\",\"modeId\":\"ring_escape\",\"values\":{\"ringCount\":250,\"trail\":\"rainbow\"}},"
                + "{\"id\":\"remote.bad\",\"name\":\"Bad\",\"modeId\":\"ring_escape\",\"values\":{\"ringCount\":\"many\"}},"
                + "{\"id\":\"remote.peg\",\"name\":\"Peg\",\"modeId\":\"peg_drop\",\"values\":{}},"
                + "{\"id\":\"remote.tiny\",\"name\":\"Tiny\",\"modeId\":\"ring_escape\",\"values\":{\"timeScale\":1e-30}}]"));
            Assert.Empty(r.Issues);
            RemotePresetLoadResult load = RemoteConfigAdapters.LoadRemotePresets(r.Values, SchemaFor);
            Assert.Equal(new[] { "remote.rush", "remote.tiny" }, load.Accepted.Select(p => p.Id));
            Preset rush = load.Accepted[0];
            Assert.True(rush.IsNew);
            Assert.Equal(PresetSource.Remote, rush.Source);
            Assert.Equal(100, rush.Values.GetInt("ringCount")); // clamped, reported as a warning
            Assert.Contains(load.Warnings, w => w.PresetId == "remote.rush");
            Assert.Equal(new[] { "remote.bad", "remote.peg" }, load.Rejected.Select(x => x.PresetId));
            Assert.Contains("Unknown mode", load.Rejected[1].Reason); // peg_drop is not in release 1, even with a schema
        }

        // ---- Audio ----

        [Fact]
        public void MelodiesFromDefaultsAreEmpty()
        {
            var r = RemoteConfigAdapters.ToMelodies(D);
            Assert.Empty(r.Value);
            Assert.False(r.HasIssues);
            Assert.Equal(CommonParams.Melodies, RemoteConfigAdapters.MelodyOptionIds(r.Value));
        }

        [Fact]
        public void MelodiesPayloadBuildsAudioMelodies()
        {
            var r = Parse((RemoteConfigKeys.RemoteMelodies,
                "[{\"id\":\" ode_remix \",\"name\":\"Ode Remix\",\"notes\":[{\"pitch\":64,\"durWeight\":1,\"velocity\":0.8},{\"pitch\":0,\"durWeight\":0.001,\"velocity\":0}]},"
                + "{\"id\":\"ode_to_joy\",\"name\":\"Ode\",\"notes\":[{\"pitch\":127,\"durWeight\":100,\"velocity\":1}]}]"));
            Assert.Empty(r.Issues);
            var m = RemoteConfigAdapters.ToMelodies(r.Values);
            Assert.False(m.HasIssues);
            Assert.Equal(new[] { "ode_remix", "ode_to_joy" }, m.Value.Select(x => x.Id));
            Assert.Equal(2, m.Value[0].Count);
            Assert.Equal(64, m.Value[0][0].Pitch);
            Assert.Equal(0f, m.Value[0][1].Velocity);
            Assert.Equal(MelodyJson.Serialize(m.Value[1], false), MelodyJson.Serialize(new Melody("ode_to_joy", "Ode", new[] { new MelodyNote(127, 100f, 1f) }), false));
            var ids = RemoteConfigAdapters.MelodyOptionIds(m.Value);
            Assert.Equal(CommonParams.Melodies.Count + 1, ids.Count); // ode_to_joy is built in, ode_remix is new
            Assert.Equal("ode_remix", ids[ids.Count - 1]);
            var schema = CommonParams.Build(melodies: ids);
            var preset = TestSchemas.Set(("soundPack", ParamValue.String("Melody")), ("melody", ParamValue.String("ode_remix")));
            Assert.Empty(preset.SanitizePartial(schema).Issues);
        }

        [Fact]
        public void ConfigAndAudioMelodyRulesAgree()
        {
            // QA residual (audio): Config keeps its own copy of the melody rules. Random notes must be accepted by both or by
            // neither, so a melody that passes Config always builds in Audio.
            var rng = Rng.FromSeed(0x3E10D1UL, 5);
            string[] pitches = { "0", "127", "128", "-1", "60.5", "60.0", "1e2", "\"64\"", "true", "null" };
            string[] durs = { "1", "0", "-1", "1e-60", "1e-45", "1e-30", "3.5e38", "3.5e39", "\"1\"", "null", "0.001" };
            string[] vels = { "0", "1", "1.0000001", "-0.0", "-0.1", "0.5", "\"0.5\"", "null", "1e-400" };
            int accepted = 0;
            for (int i = 0; i < 2000; i++)
            {
                string note = "{\"pitch\":" + pitches[rng.NextInt(0, pitches.Length)] + ",\"durWeight\":" + durs[rng.NextInt(0, durs.Length)]
                    + ",\"velocity\":" + vels[rng.NextInt(0, vels.Length)] + "}";
                string melody = "{\"id\":\"m\",\"name\":\"M\",\"notes\":[" + note + "]}";
                var r = Parse((RemoteConfigKeys.RemoteMelodies, "[" + melody + "]"));
                bool config = r.Values.RemoteMelodies.Count == 1;
                bool audio = MelodyJson.TryParse(melody, out Melody parsed, out _);
                Assert.True(config == audio, note + ": config " + config + ", audio " + audio);
                if (config)
                {
                    accepted++;
                    var adapted = RemoteConfigAdapters.ToMelodies(r.Values);
                    Assert.False(adapted.HasIssues);
                    Assert.Equal(MelodyJson.Serialize(parsed, false), MelodyJson.Serialize(adapted.Value[0], false));
                }
            }
            Assert.InRange(accepted, 50, 1950);
        }

        // ---- Watermark ----

        [Theory]
        [InlineData(true, false, false, true)]
        [InlineData(true, false, true, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, true, true)]
        [InlineData(false, false, false, false)]
        [InlineData(false, false, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(false, true, true, false)]
        public void WatermarkTruthTable(bool enabled, bool noWatermark, bool keep, bool expected)
        {
            Assert.Equal(expected, WatermarkPolicy.ShouldShow(enabled, noWatermark, keep));
        }

        [Fact]
        public void WatermarkFromConfig()
        {
            Assert.True(WatermarkPolicy.ShouldShow(D, hasNoWatermark: false));
            Assert.False(WatermarkPolicy.ShouldShow(D, hasNoWatermark: true)); // toggle defaults to "without watermark"
            Assert.True(WatermarkPolicy.ShouldShow(D, hasNoWatermark: true, keepWatermark: true));
            var off = Parse((RemoteConfigKeys.WatermarkEnabled, "false")).Values;
            Assert.False(WatermarkPolicy.ShouldShow(off, hasNoWatermark: false));
            Assert.False(WatermarkPolicy.IsToggleAvailable(false));
            Assert.True(WatermarkPolicy.IsToggleAvailable(true));
            Assert.Throws<ArgumentNullException>(() => WatermarkPolicy.ShouldShow(null, false));
        }

        [Fact]
        public void AdaptersRejectNull()
        {
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToAdPolicyConfig(null));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToGenerationBudgets(null));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToSpectacleProfiles(null));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToExportBitrates(null));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.DefaultExportSettings(null, DeviceTier.A));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToPlatformUiMasks(null));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.LoadRemotePresets(null, SchemaFor));
            Assert.Throws<ArgumentNullException>(() => RemoteConfigAdapters.ToMelodies(null));
        }
    }
}
