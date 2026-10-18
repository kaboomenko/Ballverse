using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ballverse.Core.Config;
using Newtonsoft.Json;
using Xunit;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>
    /// A realistic full release-1 payload as Firebase would deliver it (all values are strings). Regenerated for release 1
    /// (Р18): free_modes / gallery_order list only the released modes, generation_weights use the new mode ids.
    /// </summary>
    public class RemoteConfigSampleTests
    {
        static Dictionary<string, string> Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "TestData", "Config", "remote_config_sample.json");
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
        }

        [Fact]
        public void SampleParsesWithOnlyUnknownKeyInfo()
        {
            var (v, issues) = RemoteConfigParser.Parse(Load());
            var issue = Assert.Single(issues);
            Assert.Equal("experiment_banner_color", issue.Key);
            Assert.Equal(RemoteConfigIssueKind.UnknownKey, issue.Kind);

            Assert.Equal(3, v.FreeModes.Count);
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 }, v.EffectiveFreeModes);
            Assert.True(v.IsModeDisabled(ModeCode.M15));
            Assert.Equal(new[] { ModeCode.M05, ModeCode.M01, ModeCode.M02 }, v.GalleryOrder);
            Assert.Equal(4, v.InterstitialEveryNGames);
            Assert.Equal(90, v.InterstitialMinIntervalSec);
            Assert.Equal(3, v.InterstitialGraceGames);
            Assert.True(v.RewardedExportEnabled);
            Assert.Equal(6, v.RewardedWaitTimeoutSec);
            Assert.Equal(15, v.RewardedRetryWindowMin);
            Assert.False(v.PaidModeTrialEnabled);
            Assert.Equal(20, v.PaidModeTrialSeconds);
            Assert.Equal(18000, v.ExportBitrates.GetKbps(ExportResolution.P1080, 60));
            Assert.Equal(30, v.ExportDefaultFpsByTier.Get(DeviceTier.B));
            Assert.True(v.TryGetGenerationWeights(ModeCode.M02, out var w));
            Assert.Equal(1.5, w.W3);
            Assert.True(v.TryGetGenerationWeights(ModeCode.M05, out var tuning));
            Assert.False(tuning.HasWeights);
            Assert.Equal(1.4, tuning.QualityThreshold);
            Assert.Equal(12, v.GenerationBudgetByTier.B.Candidates);
            Assert.True(v.TryGetTierOverride("SM-A155F", out var tier));
            Assert.Equal(DeviceTier.C, tier);
            Assert.True(v.TryGetSafeZones("tiktok", out var zones));
            Assert.Equal(0.86f, zones[1].X);
            Assert.True(v.TryGetSafeZones("reels", out _));
            Assert.Contains("Галактика", Assert.Single(v.RemotePresets));
            Assert.Equal(4, Assert.Single(v.RemoteMelodies).Notes.Count);
            Assert.Equal(new[] { "de", "en", "ru" }, v.TextTemplates.Select(s => s.Language));
            Assert.True(v.WatermarkEnabled);
            Assert.Equal(3, v.RatePromptAfterExports);
        }
    }
}
