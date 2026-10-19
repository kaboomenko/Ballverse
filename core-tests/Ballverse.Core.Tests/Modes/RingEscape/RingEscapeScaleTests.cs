using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>
    /// The §8.5 per-commit volumes of M01, split over several test classes so xUnit runs them in parallel (one class = one
    /// collection):
    /// <list type="bullet">
    /// <item>«Детерминизм»: 20 seeds × 2 runs → identical hashes, replay and render-stream bytes; replay → re-simulation →
    /// identical hashes, render stream, render events and sound (whole runs, a built-in preset per seed).</item>
    /// <item>Card test (б): 1 000 random valid sets (8 batches × 125), every invariant of <see cref="RingEscapeProbe"/>.</item>
    /// <item>Card test (в): generator results (all styles, targetDuration 15–120 s) escape before 180 s in ≥ 99 %: 200 per
    /// commit (2 × 100, at most one miss in each hundred); the card's full 1 000 with BALLVERSE_LONG_TESTS=1.</item>
    /// </list>
    /// </summary>
    internal static class RingEscapeScale
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        public static bool Long => Environment.GetEnvironmentVariable("BALLVERSE_LONG_TESTS") == "1";

        public const int InvariantBatches = 8;
        public const int SetsPerBatch = 125;

        public static void DeterministicSeed(int index)
        {
            IReadOnlyList<Preset> presets = Mode.BuiltInPresets;
            ParamSet values = ModeParams.ApplyPreset(Mode, presets[index % presets.Count], DeviceTier.A).Values;
            ulong seed = 9000UL + (ulong)index * 7919UL;
            ModeConformance.Recording rec = ModeConformance.AssertDeterministic(Mode, values, seed, SimConstants.MaxDurationTicks);
            Assert.True(rec.Session.IsFinished);
            ModeConformance.AssertReplayRoundTrip(Mode, rec);
        }

        public static void InvariantBatch(int batch)
        {
            var rng = Rng.FromSeed(4242, (ulong)batch);
            int crossings = 0;
            for (int i = 0; i < SetsPerBatch; i++)
            {
                ParamSet raw = ModeConformance.RandomSet(Mode.Schema, ref rng);
                ParamSet v = ModeParams.Prepare(Mode, raw, DeviceTier.B).Values;
                RingEscapeValidationTests.AssertAllRules(v);
                RingEscapeProbe.Result r = RingEscapeProbe.RunChecked(v, rng.NextULong());
                crossings += r.Crossings;
            }
            Assert.True(crossings > 0, "no ring was ever crossed");
        }

        public static void GeneratorBatch(int first)
        {
            int n = Long ? 500 : 100;
            int[] targets = { 15, 30, 45, 60, 90, 120 };
            int escaped = 0;
            for (int k = 0; k < n; k++)
            {
                int i = first * (Long ? 5 : 1) + k;
                ParamSet b = Mode.Schema.CreateDefaults().With(CommonParams.TargetDuration, ParamValue.Int(targets[i % targets.Length]));
                ModeGenerationResult g = ModeGenerator.Generate(Mode, new ModeGenerationRequest
                {
                    BaseValues = ModeParams.Prepare(Mode, b, DeviceTier.B).Values,
                    Tier = DeviceTier.C,
                    SessionSeed = (ulong)(5000 + i),
                    Style = (GenerationStyle)(i % 6),
                });
                if (!g.HasSelection) continue;
                var s = new ModeSession(Mode, g.Values, g.Seed, new ModeSessionOptions { BuildFrames = false });
                s.RunToEnd();
                var scene = (RingEscapeScene)s.Scene;
                if (scene.EscapeTick >= 0 && scene.EscapeTick < SimConstants.MaxDurationTicks) escaped++;
            }
            Assert.True(escaped >= n * 99 / 100, "escaped " + escaped + " of " + n);
        }
    }

    public class RingEscapeDeterminismSeedsA
    {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
        public void SeedReplaysIdentically(int index) => RingEscapeScale.DeterministicSeed(index);
    }

    public class RingEscapeDeterminismSeedsB
    {
        [Theory]
        [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
        [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)]
        public void SeedReplaysIdentically(int index) => RingEscapeScale.DeterministicSeed(index);
    }

    public class RingEscapeInvariantsA
    {
        [Theory]
        [InlineData(0)] [InlineData(1)]
        public void RandomValidSetsKeepEveryInvariant(int batch) => RingEscapeScale.InvariantBatch(batch);
    }

    public class RingEscapeInvariantsB
    {
        [Theory]
        [InlineData(2)] [InlineData(3)]
        public void RandomValidSetsKeepEveryInvariant(int batch) => RingEscapeScale.InvariantBatch(batch);
    }

    public class RingEscapeInvariantsC
    {
        [Theory]
        [InlineData(4)] [InlineData(5)]
        public void RandomValidSetsKeepEveryInvariant(int batch) => RingEscapeScale.InvariantBatch(batch);
    }

    public class RingEscapeInvariantsD
    {
        [Theory]
        [InlineData(6)] [InlineData(7)]
        public void RandomValidSetsKeepEveryInvariant(int batch) => RingEscapeScale.InvariantBatch(batch);
    }

    public class RingEscapeGeneratorEscapesA
    {
        [Fact]
        public void GeneratorResultsEscapeBeforeTheCap() => RingEscapeScale.GeneratorBatch(0);
    }

    public class RingEscapeGeneratorEscapesB
    {
        [Fact]
        public void GeneratorResultsEscapeBeforeTheCap() => RingEscapeScale.GeneratorBatch(100);
    }
}
