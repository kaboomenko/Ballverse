using System.Diagnostics;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    [Collection(SerialMeasurementCollection.Name)] // allocation counts and timing; see SerialMeasurementCollection
    public class RingEscapeMeasurementTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet BudgetScene()
        {
            // Card «Физика и бюджет», tier A: 20 balls, 100 rings («Сжатие»), long run (unlimited target).
            ParamSet v = Mode.Schema.CreateDefaults()
                .With(CommonParams.BallCount, ParamValue.Int(20))
                .With(RingEscapeParams.RingCount, ParamValue.Int(100))
                .With(RingEscapeParams.RingSpacing, ParamValue.Float(0.55))
                .With(RingEscapeParams.GapSize, ParamValue.Float(30))
                .With(CommonParams.Labels, ParamValue.String("numbers"));
            return ModeParams.Prepare(Mode, v, DeviceTier.A).Values;
        }

        [Fact]
        public void LiveSteppingDoesNotAllocateInSteadyState()
        {
            ParamSet single = ModeParams.Defaults(Mode).With(RingEscapeParams.RingCount, ParamValue.Int(60));
            Assert.Equal(0, ModeConformance.MeasureSteadyStateAllocation(Mode, single, 3, 120, 900));
            // 20 balls: the solver's per-tick event buffers grow once to their high-water mark during the first seconds
            // (≈ 8 KB at tick 567 for this seed); steady state starts after that.
            Assert.Equal(0, ModeConformance.MeasureSteadyStateAllocation(Mode, BudgetScene(), 3, 900, 600));
        }

        [Fact]
        public void BudgetSceneStepsWellWithinTheFrameBudget()
        {
            // Performance smoke (spec §8.5): the tier-A budget scene must average well under 1 ms per tick on desktop CI.
            // Best of three runs: a loaded CI machine can stall one run, a real regression slows all of them.
            double perTickMs = double.MaxValue;
            int ticks = 0;
            for (int attempt = 0; attempt < 3 && perTickMs >= 1.0; attempt++)
            {
                var s = new ModeSession(Mode, BudgetScene(), 9, new ModeSessionOptions { BuildFrames = true });
                s.RunHeadless(60); // warm-up (JIT)
                var sw = Stopwatch.StartNew();
                ticks = s.RunHeadless(600);
                sw.Stop();
                perTickMs = System.Math.Min(perTickMs, sw.Elapsed.TotalMilliseconds / System.Math.Max(1, ticks));
            }
            Assert.True(perTickMs < 1.0, "average " + perTickMs + " ms per tick over " + ticks + " ticks");
        }
    }
}
