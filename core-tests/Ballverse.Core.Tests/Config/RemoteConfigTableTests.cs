using System.Linq;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Bitrates (spec §3.8), default fps per tier (§9.3), generation weights and budgets (§2.4).</summary>
    public class RemoteConfigTableTests
    {
        // ---- export_bitrates ----

        [Fact]
        public void BitratesFullReplace()
        {
            var r = Parse(RemoteConfigKeys.ExportBitrates, "{\"720p30\":5000,\"720p60\":8000,\"1080p30\":12000,\"1080p60\":20000}");
            AssertNoIssues(r);
            var b = r.Values.ExportBitrates;
            Assert.Equal(5000, b.GetKbps(ExportResolution.P720, 30));
            Assert.Equal(8000, b.GetKbps(ExportResolution.P720, 60));
            Assert.Equal(12000, b.GetKbps(ExportResolution.P1080, 30));
            Assert.Equal(20000, b.GetKbps(ExportResolution.P1080, 60));
        }

        [Fact]
        public void BitratesPartialMergesOverDefaults()
        {
            var r = Parse(RemoteConfigKeys.ExportBitrates, "{\"1080P60\":18000}");
            AssertNoIssues(r);
            var b = r.Values.ExportBitrates;
            Assert.Equal(18000, b.P1080Fps60Kbps);
            Assert.Equal(10000, b.P1080Fps30Kbps);
            Assert.Equal(10000, b.P720Fps60Kbps);
            Assert.Equal(6000, b.P720Fps30Kbps);
        }

        [Fact]
        public void BitratesClampAndDropInvalidFields()
        {
            var r = Parse(RemoteConfigKeys.ExportBitrates, "{\"720p30\":999,\"720p60\":50001,\"1080p30\":\"12000\",\"1080p60\":15000.5,\"4k60\":90000,\"720P30\":7000}");
            var b = r.Values.ExportBitrates;
            Assert.Equal(1000, b.P720Fps30Kbps);
            Assert.Equal(50000, b.P720Fps60Kbps);
            Assert.Equal(10000, b.P1080Fps30Kbps);
            Assert.Equal(16000, b.P1080Fps60Kbps);
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.OutOfRange));
            Assert.Equal(3, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void BitratesBoundsAccepted()
        {
            var r = Parse(RemoteConfigKeys.ExportBitrates, "{\"720p30\":1000,\"1080p60\":50000}");
            AssertNoIssues(r);
            Assert.Equal(1000, r.Values.ExportBitrates.P720Fps30Kbps);
            Assert.Equal(50000, r.Values.ExportBitrates.P1080Fps60Kbps);
        }

        [Theory]
        [InlineData("[6000,10000]", RemoteConfigIssueKind.InvalidType)]
        [InlineData("16000", RemoteConfigIssueKind.InvalidType)]
        [InlineData("{\"720p30\":", RemoteConfigIssueKind.InvalidJson)]
        [InlineData("{\"720p30\":1,\"720p30\":2}", RemoteConfigIssueKind.InvalidJson)]
        public void BitratesInvalidKeepsDefault(string text, RemoteConfigIssueKind kind)
        {
            var r = Parse(RemoteConfigKeys.ExportBitrates, text);
            Single(r, kind, RemoteConfigIssueSeverity.Error);
            Assert.Equal(Raw(RemoteConfigValues.Defaults, RemoteConfigKeys.ExportBitrates), Raw(r.Values, RemoteConfigKeys.ExportBitrates));
        }

        // ---- export_default_fps_by_tier ----

        [Fact]
        public void FpsByTierHappyAndPartial()
        {
            var r = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "{\"A\":60,\"B\":30,\"C\":30}");
            AssertNoIssues(r);
            Assert.Equal(30, r.Values.ExportDefaultFpsByTier.B);

            var p = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "{\"c\":60}");
            AssertNoIssues(p);
            Assert.Equal(60, p.Values.ExportDefaultFpsByTier.A);
            Assert.Equal(60, p.Values.ExportDefaultFpsByTier.B);
            Assert.Equal(60, p.Values.ExportDefaultFpsByTier.C);
        }

        [Fact]
        public void FpsOnly30Or60()
        {
            var r = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "{\"A\":45,\"B\":\"60\",\"C\":24,\"D\":60}");
            Assert.Equal(60, r.Values.ExportDefaultFpsByTier.A);
            Assert.Equal(60, r.Values.ExportDefaultFpsByTier.B);
            Assert.Equal(30, r.Values.ExportDefaultFpsByTier.C);
            Assert.Equal(4, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry && i.Severity == RemoteConfigIssueSeverity.Warning));

            var big = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "{\"A\":4294967356}");
            Assert.Equal(60, big.Values.ExportDefaultFpsByTier.A);
            Single(big, RemoteConfigIssueKind.InvalidEntry, RemoteConfigIssueSeverity.Warning);

            var dup = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "{\"A\":30,\"a\":60}");
            Assert.Equal(30, dup.Values.ExportDefaultFpsByTier.A);
            Single(dup, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);

            var arr = Parse(RemoteConfigKeys.ExportDefaultFpsByTier, "[60,60,30]");
            Single(arr, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
        }

        // ---- generation_budget_by_tier ----

        [Fact]
        public void BudgetHappyAndPartial()
        {
            var r = Parse(RemoteConfigKeys.GenerationBudgetByTier, "{\"A\":{\"candidates\":32,\"ms\":2000},\"C\":{\"ms\":5000}}");
            AssertNoIssues(r);
            var t = r.Values.GenerationBudgetByTier;
            Assert.Equal(32, t.A.Candidates);
            Assert.Equal(2000, t.A.Milliseconds);
            Assert.Equal(16, t.B.Candidates);
            Assert.Equal(2500, t.B.Milliseconds);
            Assert.Equal(8, t.C.Candidates);
            Assert.Equal(5000, t.C.Milliseconds);
            Assert.Same(t.B, t.Get(DeviceTier.B));
        }

        [Fact]
        public void BudgetClampsRanges()
        {
            var r = Parse(RemoteConfigKeys.GenerationBudgetByTier,
                "{\"A\":{\"candidates\":0,\"ms\":99},\"B\":{\"candidates\":65,\"ms\":10001},\"C\":{\"candidates\":1,\"ms\":10000}}");
            var t = r.Values.GenerationBudgetByTier;
            Assert.Equal(1, t.A.Candidates);
            Assert.Equal(100, t.A.Milliseconds);
            Assert.Equal(64, t.B.Candidates);
            Assert.Equal(10000, t.B.Milliseconds);
            Assert.Equal(1, t.C.Candidates);
            Assert.Equal(10000, t.C.Milliseconds);
            Assert.Equal(4, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.OutOfRange));
            Assert.Equal(4, r.Issues.Count);
        }

        [Fact]
        public void BudgetInvalidFieldsKeepDefaults()
        {
            var r = Parse(RemoteConfigKeys.GenerationBudgetByTier,
                "{\"A\":24,\"B\":{\"candidates\":\"16\",\"seconds\":3,\"ms\":2.5},\"X\":{\"ms\":100},\"b\":{\"ms\":100}}");
            var t = r.Values.GenerationBudgetByTier;
            Assert.Equal(24, t.A.Candidates);
            Assert.Equal(1500, t.A.Milliseconds);
            Assert.Equal(16, t.B.Candidates);
            Assert.Equal(2500, t.B.Milliseconds);
            Assert.Equal(5, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);

            var bad = Parse(RemoteConfigKeys.GenerationBudgetByTier, "nope");
            Single(bad, RemoteConfigIssueKind.InvalidJson, RemoteConfigIssueSeverity.Error);
        }

        // ---- generation_weights ----

        [Fact]
        public void WeightsObjectAndArrayForms()
        {
            var r = Parse(RemoteConfigKeys.GenerationWeights,
                "{\"M03\":{\"w1\":1,\"w2\":0.5,\"w3\":2,\"w4\":0.25,\"w5\":0.75},\"ring_escape\":[1,2,3,4,0]}");
            AssertNoIssues(r);
            var v = r.Values;
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M03 }, v.GenerationWeights.Select(kv => kv.Key));
            Assert.True(v.TryGetGenerationWeights(ModeCode.M03, out var w3));
            Assert.Equal(1f, w3.W1);
            Assert.Equal(0.5f, w3.W2);
            Assert.Equal(2f, w3.W3);
            Assert.Equal(0.25f, w3.W4);
            Assert.Equal(0.75f, w3.W5);
            Assert.True(v.TryGetGenerationWeights(ModeCode.M01, out var w1));
            Assert.Equal(4f, w1.W4);
            Assert.Equal(0f, w1.W5);
            Assert.False(v.TryGetGenerationWeights(ModeCode.M02, out _));
        }

        [Fact]
        public void WeightsInvalidEntriesDropped()
        {
            var r = Parse(RemoteConfigKeys.GenerationWeights,
                "{\"M01\":[1,2,3,4],\"M02\":{\"w1\":1,\"w2\":1,\"w3\":1,\"w4\":1},\"M03\":[1,-1,1,1,1],\"M04\":\"1,1,1,1,1\","
                + "\"M05\":[1,1,1,1,\"1\"],\"M06\":[1,1,1,1,1e39],\"M99\":[1,1,1,1,1],\"M07\":{\"w1\":1,\"w2\":1,\"w3\":1,\"w4\":1,\"w5\":1,\"w6\":9},"
                + "\"last_standing\":[2,2,2,2,2]}");
            var v = r.Values;
            Assert.Equal(new[] { ModeCode.M07 }, v.GenerationWeights.Select(kv => kv.Key));
            Assert.Equal(1f, v.GenerationWeights[0].Value.W5);
            Assert.Equal(7, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Single(r, RemoteConfigIssueKind.UnknownMode, RemoteConfigIssueSeverity.Warning);
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void WeightsInvalidJsonKeepsEmpty()
        {
            var r = Parse(RemoteConfigKeys.GenerationWeights, "[[1,1,1,1,1]]");
            Single(r, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
            Assert.Empty(r.Values.GenerationWeights);
        }
    }
}
