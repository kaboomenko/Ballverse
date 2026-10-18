using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Spec §9.3 default column, plus §3.8 / §2.4 tables it refers to.</summary>
    public class RemoteConfigDefaultsTests
    {
        static readonly RemoteConfigValues D = RemoteConfigValues.Defaults;

        [Fact]
        public void KeyTableMatchesSpec()
        {
            var expected = new[]
            {
                "free_modes", "modes_disabled", "gallery_order", "interstitial_every_n_games", "interstitial_min_interval_sec",
                "interstitial_grace_games", "rewarded_export_enabled", "rewarded_wait_timeout_sec", "rewarded_retry_window_min",
                "paid_mode_trial_enabled", "paid_mode_trial_seconds", "export_bitrates", "export_default_fps_by_tier",
                "generation_weights", "generation_budget_by_tier", "tier_overrides", "platform_safe_zones", "remote_presets",
                "remote_melodies", "text_templates", "watermark_enabled", "rate_prompt_after_exports",
            };
            Assert.Equal(expected, RemoteConfigKeys.All);
        }

        [Fact]
        public void ModeDefaults()
        {
            Assert.Equal(Codes(1, 2, 3, 4, 5), D.FreeModes);
            Assert.Empty(D.ModesDisabled);
            Assert.Equal(AllCodes(), D.GalleryOrder);
            Assert.Equal(Codes(1, 2, 3, 4, 5), D.EffectiveFreeModes);
        }

        [Fact]
        public void AdDefaults()
        {
            Assert.Equal(3, D.InterstitialEveryNGames);
            Assert.Equal(60, D.InterstitialMinIntervalSec);
            Assert.Equal(0, D.InterstitialGraceGames);
            Assert.True(D.RewardedExportEnabled);
            Assert.Equal(5, D.RewardedWaitTimeoutSec);
            Assert.Equal(10, D.RewardedRetryWindowMin);
            Assert.True(D.PaidModeTrialEnabled);
            Assert.Equal(30, D.PaidModeTrialSeconds);
        }

        [Fact]
        public void ExportDefaultsMatchSpec38()
        {
            Assert.Equal(16000, D.ExportBitrates.GetKbps(ExportResolution.P1080, 60));
            Assert.Equal(10000, D.ExportBitrates.GetKbps(ExportResolution.P1080, 30));
            Assert.Equal(10000, D.ExportBitrates.GetKbps(ExportResolution.P720, 60));
            Assert.Equal(6000, D.ExportBitrates.GetKbps(ExportResolution.P720, 30));
            Assert.Equal(60, D.ExportDefaultFpsByTier.Get(DeviceTier.A));
            Assert.Equal(60, D.ExportDefaultFpsByTier.Get(DeviceTier.B));
            Assert.Equal(30, D.ExportDefaultFpsByTier.Get(DeviceTier.C));
        }

        [Fact]
        public void GenerationDefaultsMatchSpec24()
        {
            Assert.Empty(D.GenerationWeights);
            Assert.False(D.TryGetGenerationWeights(ModeCode.M01, out _));
            Assert.Equal(24, D.GenerationBudgetByTier.A.Candidates);
            Assert.Equal(1500, D.GenerationBudgetByTier.A.Milliseconds);
            Assert.Equal(16, D.GenerationBudgetByTier.B.Candidates);
            Assert.Equal(2500, D.GenerationBudgetByTier.B.Milliseconds);
            Assert.Equal(8, D.GenerationBudgetByTier.C.Candidates);
            Assert.Equal(3500, D.GenerationBudgetByTier.C.Milliseconds);
        }

        [Fact]
        public void OtherDefaults()
        {
            Assert.Empty(D.TierOverrides);
            Assert.Empty(D.RemotePresets);
            Assert.Empty(D.RemoteMelodies);
            Assert.True(D.WatermarkEnabled);
            Assert.Equal(2, D.RatePromptAfterExports);
        }

        [Fact]
        public void SafeZoneDefaultsCoverThreePlatformsInsideFrame()
        {
            Assert.Equal(new[] { "reels", "shorts", "tiktok" }, D.PlatformSafeZones.Select(p => p.Platform));
            foreach (var p in D.PlatformSafeZones)
            {
                Assert.NotEmpty(p.Rects);
                foreach (var r in p.Rects) Assert.True(NormalizedRect.IsValid(r.X, r.Y, r.W, r.H));
            }
            Assert.True(D.TryGetSafeZones("TikTok", out var rects));
            Assert.Equal(3, rects.Count);
            Assert.False(D.TryGetSafeZones("snapchat", out _));
            Assert.False(D.TryGetSafeZones(null, out _));
        }

        [Fact]
        public void TextTemplateDefaultsMatchSpec35()
        {
            Assert.Equal(new[] { "en", "ru" }, D.TextTemplates.Select(s => s.Language));
            Assert.True(D.TryGetTextTemplates("ru", out var ru));
            Assert.Contains(ru, t => t.Text == "Какой шар победит?");
            Assert.Contains(ru, t => t.Text == "Часть 2");
            Assert.Contains(ru, t => t.Text == "Кто выживет?");
            Assert.Contains(ru, t => t.Text == "Попробуй не залипнуть");
            Assert.True(D.TryGetTextTemplates(" EN ", out var en));
            Assert.Equal(ru.Select(t => t.Id), en.Select(t => t.Id));
            Assert.False(D.TryGetTextTemplates("de", out _));
        }

        [Fact]
        public void EmptyAndNullInputGiveDefaultsWithoutIssues()
        {
            var r = RemoteConfigParser.Parse(new Dictionary<string, string>());
            AssertNoIssues(r);
            Assert.Equal(RawDictionary(D), RawDictionary(r.Values));

            var n = RemoteConfigParser.Parse(null);
            AssertNoIssues(n);
            Assert.Same(D, RemoteConfigValues.Defaults);
            Assert.Equal(RawDictionary(D), RawDictionary(n.Values));
        }

        [Fact]
        public void DefaultsRoundTripThroughRawStrings()
        {
            var raw = RawDictionary(D);
            Assert.Equal(RemoteConfigKeys.All.Count, raw.Count);
            Assert.Equal("[\"M01\",\"M02\",\"M03\",\"M04\",\"M05\"]", raw["free_modes"]);
            Assert.Equal("[]", raw["modes_disabled"]);
            Assert.Equal("{\"A\":60,\"B\":60,\"C\":30}", raw["export_default_fps_by_tier"]);
            Assert.Equal("{\"720p30\":6000,\"720p60\":10000,\"1080p30\":10000,\"1080p60\":16000}", raw["export_bitrates"]);
            Assert.Equal("{\"A\":{\"candidates\":24,\"ms\":1500},\"B\":{\"candidates\":16,\"ms\":2500},\"C\":{\"candidates\":8,\"ms\":3500}}", raw["generation_budget_by_tier"]);
            Assert.Equal("true", raw["watermark_enabled"]);
            Assert.Equal("3", raw["interstitial_every_n_games"]);

            var r = RemoteConfigParser.Parse(raw);
            AssertNoIssues(r);
            Assert.Equal(raw, RawDictionary(r.Values));
        }

        [Fact]
        public void ParseResultDeconstructs()
        {
            var (values, issues) = RemoteConfigParser.Parse(new Dictionary<string, string>());
            Assert.NotNull(values);
            Assert.Empty(issues);
        }
    }
}
