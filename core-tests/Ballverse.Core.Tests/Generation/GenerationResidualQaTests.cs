using System;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    /// <summary>Residual QA findings of the merged Generation module (overflow guards, two untested documented behaviours).</summary>
    public class GenerationResidualQaTests
    {
        [Fact]
        public void HugeBudgetsAreRejectedWithArgumentException()
        {
            // QA W1: 1073741825 candidates wrapped the doubled count limit negative and the session hung in Wait.
            var w1 = Runs.Options(candidates: 1073741825, timeMs: 1000);
            Assert.Throws<ArgumentException>(() => new GenerationSession(w1));
            // QA W2: a forced-winner time of long.MaxValue wrapped on "Search longer".
            var w2 = Runs.Options();
            w2.ForcedWinner = 0;
            w2.ParticipantCount = 2;
            w2.ForcedWinnerTimeMs = long.MaxValue;
            Assert.Throws<ArgumentException>(() => new GenerationSession(w2));
            // QA W3: int.MaxValue candidates with a forced winner threw OverflowException instead of the documented ArgumentException.
            var w3 = Runs.Options(candidates: int.MaxValue);
            w3.ForcedWinner = 0;
            w3.ParticipantCount = 4;
            Assert.Throws<ArgumentException>(() => new GenerationSession(w3));
            var w4 = Runs.Options(timeMs: GenerationSessionOptions.MaxBudgetTimeMs + 1);
            Assert.Throws<ArgumentException>(() => new GenerationSession(w4));

            // The largest accepted budget with the ×4 multiplier still works and doubles without wrapping.
            var max = Runs.Options(candidates: GenerationSessionOptions.MaxBudgetCandidates, timeMs: GenerationSessionOptions.MaxBudgetTimeMs);
            max.ForcedWinner = 0;
            max.ParticipantCount = 4;
            max.ForcedWinnerTimeMs = GenerationSessionOptions.MaxBudgetTimeMs;
            var s = new GenerationSession(max);
            Assert.Equal(GenerationSessionOptions.MaxBudgetCandidates * 4, s.PhaseBudget.Candidates);
        }

        [Fact]
        public void DoublingAfterTimeEndedPhaseGivesTwiceTheCandidateCount()
        {
            // QA mutant MP: the doubled total is 2 × candidates even when phase 1 ended on time with fewer evaluated.
            var s = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            Runs.Drive(s, k => Runs.Bad(), k => 1000 + k);
            Assert.True(s.Result.BudgetDoubled);
            Assert.Equal(8, s.CandidateLimit);
            Assert.Equal(8, s.Evaluations.Count);
        }

        [Fact]
        public void ThrowingReportDoesNotAdvanceTheClock()
        {
            // QA mutant MV: "every exception is thrown before any state changes" includes the elapsed time.
            var s = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            Assert.True(s.NextRequest().IsRun);
            Assert.Throws<ArgumentException>(() => s.Report(0, Runs.Make(durationTicks: 5000, finished: false, endedByLimit: true), 1500));
            Assert.Equal(GenerationStepKind.RunCandidate, s.NextRequest().Kind);
            Assert.True(s.Report(0, Runs.Good(), 20));
            Assert.Single(s.Evaluations);
        }
    }
}
