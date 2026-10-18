using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class GenerationBudgetTests
    {
        [Fact]
        public void TierDefaultsMatchSpec()
        {
            Assert.Equal(new GenerationBudget(24, 1500), GenerationBudget.ForTier(DeviceTier.A));
            Assert.Equal(new GenerationBudget(16, 2500), GenerationBudget.ForTier(DeviceTier.B));
            Assert.Equal(new GenerationBudget(8, 3500), GenerationBudget.ForTier(DeviceTier.C));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationBudget.ForTier((DeviceTier)7));
            foreach (DeviceTier t in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                Assert.Equal(GenerationBudget.ForTier(t), GenerationBudgetTable.Default.Get(t));
        }

        [Fact]
        public void ConstructorAndScaleValidate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudget(0, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GenerationBudget(1, 0));
            Assert.Equal(new GenerationBudget(96, 6000), GenerationBudget.TierA.Scale(4));
            Assert.Equal(GenerationBudget.TierC, GenerationBudget.TierC.Scale(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationBudget.TierC.Scale(0));
            Assert.Equal("24 candidates / 1500 ms", GenerationBudget.TierA.ToString());
        }

        [Fact]
        public void RemoteJsonOverridesTiers()
        {
            var t = GenerationBudgetTable.Default.ApplyRemoteJson("{ \"A\": { \"candidates\": 32 }, \"C\": { \"candidates\": 6, \"timeMs\": 4000 } }",
                out IReadOnlyList<string> warnings);
            Assert.Empty(warnings);
            Assert.Equal(new GenerationBudget(32, 1500), t.Get(DeviceTier.A));
            Assert.Equal(GenerationBudget.TierB, t.Get(DeviceTier.B));
            Assert.Equal(new GenerationBudget(6, 4000), t.Get(DeviceTier.C));
            Assert.Equal(GenerationBudget.TierA, GenerationBudgetTable.Default.Get(DeviceTier.A));
        }

        [Fact]
        public void RemoteJsonSkipsInvalidEntries()
        {
            var t = GenerationBudgetTable.Default.ApplyRemoteJson(
                "{ \"D\": {}, \"B\": { \"candidates\": 0 }, \"A\": { \"timeMs\": 999999 }, \"C\": null, \"a\": {} }",
                out IReadOnlyList<string> warnings);
            Assert.Equal(5, warnings.Count);
            Assert.StartsWith("generation_budget_by_tier.A:", warnings[0]);
            Assert.StartsWith("generation_budget_by_tier.B:", warnings[1]);
            Assert.StartsWith("generation_budget_by_tier.C:", warnings[2]);
            Assert.StartsWith("generation_budget_by_tier.D:", warnings[3]);
            Assert.StartsWith("generation_budget_by_tier.a:", warnings[4]);
            foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                Assert.Equal(GenerationBudgetTable.Default.Get(tier), t.Get(tier));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("\"text\"")]
        [InlineData("[1, 2]")]
        public void MalformedRemoteJsonKeepsTable(string json)
        {
            var t = GenerationBudgetTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Same(GenerationBudgetTable.Default, t);
            Assert.Single(warnings);
            Assert.Contains("malformed", warnings[0]);
        }

        [Fact]
        public void RemoteBudgetOneBadEntryKeepsOthers()
        {
            var t = GenerationBudgetTable.Default.ApplyRemoteJson(
                "{ \"A\": { \"candidates\": 1.5e40 }, \"B\": { \"timeMs\": \"slow\" }, \"C\": 3 , \"D\": {} }",
                out IReadOnlyList<string> warnings);
            Assert.Equal(4, warnings.Count);
            Assert.StartsWith("generation_budget_by_tier.A: invalid entry", warnings[0]);
            Assert.StartsWith("generation_budget_by_tier.B: invalid entry", warnings[1]);
            Assert.StartsWith("generation_budget_by_tier.C: entry must be an object", warnings[2]);
            Assert.StartsWith("generation_budget_by_tier.D: unknown tier", warnings[3]);

            t = GenerationBudgetTable.Default.ApplyRemoteJson("{ \"A\": { \"candidates\": \"x\" }, \"B\": { \"candidates\": 5 } }",
                out warnings);
            Assert.Single(warnings);
            Assert.Equal(GenerationBudget.TierA, t.Get(DeviceTier.A));
            Assert.Equal(new GenerationBudget(5, 2500), t.Get(DeviceTier.B));
        }

        [Theory]
        [InlineData("{\"A\":{\"candidates\":2.5}}")]
        [InlineData("{\"A\":{\"candidates\":24.7}}")]
        [InlineData("{\"A\":{\"candidates\":12.0}}")]
        [InlineData("{\"A\":{\"timeMs\":1500.9}}")]
        [InlineData("{\"A\":{\"candidates\":\"12\"}}")]
        [InlineData("{\"A\":{\"TimeMs\":\"1500\"}}")]
        [InlineData("{\"A\":{\"candidates\":true}}")]
        public void RemoteBudgetRejectsNonIntegerNumbersInsteadOfRounding(string json)
        {
            // QA round 1 #6: per-entry conversion used to round 2.5 -> 2 and 24.7 -> 25 silently.
            var t = GenerationBudgetTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Single(warnings);
            Assert.StartsWith("generation_budget_by_tier.A: invalid entry", warnings[0]);
            Assert.Equal(GenerationBudget.TierA, t.Get(DeviceTier.A));

            t = GenerationBudgetTable.Default.ApplyRemoteJson("{\"A\":{\"candidates\":12,\"timeMs\":null},\"B\":{\"candidates\":2.5}}",
                out warnings);
            Assert.Single(warnings);
            Assert.Equal(new GenerationBudget(12, 1500), t.Get(DeviceTier.A));
            Assert.Equal(GenerationBudget.TierB, t.Get(DeviceTier.B));
        }

        [Fact]
        public void EmptyRemoteJsonKeepsTable()
        {
            Assert.Same(GenerationBudgetTable.Default, GenerationBudgetTable.Default.ApplyRemoteJson("", out IReadOnlyList<string> w));
            Assert.Empty(w);
        }

        [Fact]
        public void ToJsonRoundTrips()
        {
            string json = GenerationBudgetTable.Default.ToJson(false);
            Assert.Equal("{\"A\":{\"candidates\":24,\"timeMs\":1500},\"B\":{\"candidates\":16,\"timeMs\":2500},\"C\":{\"candidates\":8,\"timeMs\":3500}}", json);
            var back = GenerationBudgetTable.Default.ApplyRemoteJson(json, out IReadOnlyList<string> warnings);
            Assert.Empty(warnings);
            Assert.Equal(json, back.ToJson(false));
        }
    }
}
