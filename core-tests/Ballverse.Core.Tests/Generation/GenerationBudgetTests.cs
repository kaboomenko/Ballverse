using System;
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
        public void WithReplacesOneTierOnly()
        {
            // Remote Config generation_budget_by_tier is parsed by the Config module; the integration adapter uses With
            // (see Integration/RemoteConfigAdapterTests).
            var t = GenerationBudgetTable.Default.With(DeviceTier.C, new GenerationBudget(6, 4000));
            Assert.Equal(new GenerationBudget(6, 4000), t.Get(DeviceTier.C));
            Assert.Equal(GenerationBudget.TierA, t.Get(DeviceTier.A));
            Assert.Equal(GenerationBudget.TierB, t.Get(DeviceTier.B));
            Assert.Equal(GenerationBudget.TierC, GenerationBudgetTable.Default.Get(DeviceTier.C));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationBudgetTable.Default.With((DeviceTier)5, GenerationBudget.TierA));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GenerationBudgetTable.Default.With(DeviceTier.A, new GenerationBudget(GenerationBudget.MaxConfiguredCandidates + 1, 100)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GenerationBudgetTable.Default.With(DeviceTier.A, new GenerationBudget(1, GenerationBudget.MaxConfiguredTimeMs + 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationBudgetTable.Default.With(DeviceTier.A, default(GenerationBudget)));
        }
    }
}
