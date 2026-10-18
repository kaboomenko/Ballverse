using System;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    public class GenerationSessionTests
    {
        // ---- pass / budget ----

        [Fact]
        public void SelectsBestPassingCandidateAfterWholeBudget()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4));
            double[] tension = { 0.5, 0.9, 0.7, 0.2 };
            GenerationStep last = Runs.Drive(session, k => Runs.Good(tension[k]));

            Assert.Equal(GenerationStepKind.Done, last.Kind);
            Assert.Equal(GenerationStatus.Completed, session.Status);
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(1, r.SelectedIndex);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 1), r.SelectedSeed);
            Assert.Equal(4, r.CandidatesEvaluated);
            Assert.False(r.BudgetDoubled);
            Assert.False(r.QualityBelowThreshold);
            Assert.True(r.QualityOk);
            Assert.True(r.GoalMet);
            Assert.True(r.WinnerMatched);
            Assert.False(r.HasForcedWinner);
            Assert.Equal(40, r.ElapsedMs);
        }

        [Fact]
        public void TiesGoToLowestIndex()
        {
            var session = new GenerationSession(Runs.Options(candidates: 5));
            Runs.Drive(session, k => k == 0 ? Runs.Bad() : Runs.Good());
            Assert.Equal(1, session.Result.SelectedIndex);
        }

        [Fact]
        public void StopsWhenCandidateCountIsExhausted()
        {
            var session = new GenerationSession(Runs.Options(candidates: 3, timeMs: 100000));
            Runs.Drive(session, k => Runs.Good());
            Assert.Equal(3, session.Evaluations.Count);
            Assert.Equal(3, session.CandidateLimit);
        }

        [Fact]
        public void StopsWhenTimeIsExhausted()
        {
            var session = new GenerationSession(Runs.Options(candidates: 24, timeMs: 1000));
            Runs.Drive(session, k => Runs.Good(), k => 400L * (k + 1));
            // 400, 800, 1200 >= 1000 -> stop after the third candidate.
            Assert.Equal(3, session.Evaluations.Count);
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.Equal(1200, session.Result.ElapsedMs);
        }

        [Fact]
        public void ReachingTimeLimitExactlyStops()
        {
            var session = new GenerationSession(Runs.Options(candidates: 24, timeMs: 1000));
            Runs.Drive(session, k => Runs.Good(), k => 500L * (k + 1));
            Assert.Equal(2, session.Evaluations.Count);
        }

        [Fact]
        public void TierBudgetsDriveCandidateCount()
        {
            foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
            {
                var options = GenerationSessionOptions.ForMode(ModeCode.M01, tier, 1UL, Runs.Target);
                var session = new GenerationSession(options);
                Runs.Drive(session, k => Runs.Good(), k => 1L);
                Assert.Equal(GenerationBudget.ForTier(tier).Candidates, session.Evaluations.Count);
            }
        }

        // ---- doubling / below threshold ----

        [Fact]
        public void DoublesBudgetOnceWhenNothingPasses()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4));
            Runs.Drive(session, k => k == 5 ? Runs.Good() : Runs.Bad());
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(5, r.SelectedIndex);
            Assert.True(r.BudgetDoubled);
            Assert.Equal(8, r.CandidatesEvaluated);
            Assert.Equal(8, session.CandidateLimit);
            Assert.Equal(2000, session.TimeLimitMs);
        }

        [Fact]
        public void PicksBestAvailableWhenStillBelowThreshold()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4));
            Runs.Drive(session, k => Runs.Bad(tension: k == 6 ? 0.9 : 0.1));
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.BelowThreshold, r.Outcome);
            Assert.Equal(6, r.SelectedIndex);
            Assert.True(r.QualityBelowThreshold);
            Assert.False(r.QualityOk);
            Assert.True(r.BudgetDoubled);
            Assert.Equal(8, r.CandidatesEvaluated);
            Assert.Equal(GenerationStepKind.Done, session.NextRequest().Kind);
        }

        [Fact]
        public void DoublingByTimeExtendsTotalTime()
        {
            var session = new GenerationSession(Runs.Options(candidates: 100, timeMs: 1000));
            Runs.Drive(session, k => Runs.Bad(), k => 300L * (k + 1));
            // Phase 1 ends at 1200 ms (4 candidates); doubled limit 2000 ms ends at 2100 ms (7 candidates).
            Assert.Equal(7, session.Evaluations.Count);
            Assert.Equal(GenerationOutcome.BelowThreshold, session.Result.Outcome);
        }

        [Fact]
        public void DoublingIsSkippedWhenTimeAlreadyPastDoubledLimit()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            Runs.Drive(session, k => Runs.Bad(), k => 5000L);
            Assert.Single(session.Evaluations);
            Assert.True(session.Result.BudgetDoubled);
            Assert.Equal(GenerationOutcome.BelowThreshold, session.Result.Outcome);
        }

        [Fact]
        public void ThresholdIsInclusive()
        {
            double total = SpectacleScorer.Score(Runs.Good(), Runs.Target, SpectacleWeights.Default, new DensityRange(1, 8)).Total;
            var session = new GenerationSession(Runs.Options(candidates: 1, threshold: total));
            Runs.Drive(session, k => Runs.Good());
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.False(session.Result.BudgetDoubled);
        }

        // ---- rejection ----

        [Fact]
        public void StuckCandidateIsNeverSelected()
        {
            var session = new GenerationSession(Runs.Options(candidates: 3));
            Runs.Drive(session, k => k == 0 ? Runs.Make(tension: 1.0, coverage: 1.0, stuck: true) : Runs.Good(0.5));
            Assert.Equal(1, session.Result.SelectedIndex);
            Assert.True(session.Evaluations[0].IsRejected);
            Assert.False(session.Evaluations[0].PassesThreshold);
        }

        [Fact]
        public void AllRejectedGivesNoViableCandidate()
        {
            var session = new GenerationSession(Runs.Options(candidates: 2));
            Runs.Drive(session, k => k % 2 == 0 ? Runs.Make(stuck: true) : Runs.Make(finished: false));
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.NoViableCandidate, r.Outcome);
            Assert.Null(r.Selected);
            Assert.Equal(-1, r.SelectedIndex);
            Assert.Equal(0UL, r.SelectedSeed);
            Assert.True(r.QualityBelowThreshold);
            Assert.False(r.WinnerMatched);
            Assert.Equal(4, r.CandidatesEvaluated);
        }

        // ---- forced winner ----

        static GenerationSessionOptions Forced(int forcedWinner, int participants, int candidates = 4, long timeMs = 1000)
        {
            GenerationSessionOptions o = Runs.Options(candidates, timeMs);
            o.ForcedWinner = forcedWinner;
            o.ParticipantCount = participants;
            return o;
        }

        [Theory]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        [InlineData(10, 4)]
        public void ForcedWinnerMultipliesBudgetUpToFour(int participants, int multiplier)
        {
            var session = new GenerationSession(Forced(0, participants));
            // Spec §2.5: only the candidate budget is multiplied; the time budget stays the tier's (QA round 1 #2).
            Assert.Equal(new GenerationBudget(4 * multiplier, 1000L), session.PhaseBudget);
            Assert.Equal(multiplier, GenerationSession.BudgetMultiplier(0, participants));
        }

        [Fact]
        public void ForcedWinnerTimeCanBeSetExplicitly()
        {
            GenerationSessionOptions o = Forced(0, 4);
            o.ForcedWinnerTimeMs = 4000;
            Assert.Equal(new GenerationBudget(16, 4000), new GenerationSession(o).PhaseBudget);
            GenerationSessionOptions plain = Runs.Options();
            plain.ForcedWinnerTimeMs = 1500; // ignored without a forced winner
            Assert.Equal(new GenerationBudget(4, 1000), new GenerationSession(plain).PhaseBudget);
        }

        [Fact]
        public void ForcedWinnerNotFoundIsOfferedAfterTheFirstPhase()
        {
            // QA probes P16 / V7: M03, tier B, 8 participants, 150 ms per sequential candidate. The phase keeps the tier B time
            // budget (2.5 s, spec §2.4 table and acceptance); only the candidate count is ×4 (spec §2.5).
            GenerationSessionOptions o = GenerationSessionOptions.ForMode(ModeCode.M03, DeviceTier.B, 5UL, Runs.Target);
            o.ForcedWinner = 7;
            o.ParticipantCount = 8;
            var session = new GenerationSession(o);
            Assert.Equal(new GenerationBudget(64, 2500), session.PhaseBudget);
            Runs.Drive(session, k => Runs.Good(winner: k % 7, participants: 8), k => 150L * (k + 1));
            Assert.Equal(GenerationOutcome.WinnerNotFound, session.Result.Outcome);
            Assert.Equal(17, session.Result.CandidatesEvaluated);
            Assert.Equal(2550, session.Result.ElapsedMs);
            Assert.False(session.Result.BudgetDoubled);

            o.ForcedWinnerTimeMs = 10000; // opt-in longer forced-winner phase
            var longer = new GenerationSession(o);
            Assert.Equal(new GenerationBudget(64, 10000), longer.PhaseBudget);
            Runs.Drive(longer, k => Runs.Good(winner: k % 7, participants: 8), k => 150L * (k + 1));
            Assert.Equal(GenerationOutcome.WinnerNotFound, longer.Result.Outcome);
            Assert.Equal(64, longer.Result.CandidatesEvaluated);
            Assert.Equal(9600, longer.Result.ElapsedMs);
        }

        [Fact]
        public void SearchLongerAfterTimeLimitedPhaseAddsExactlyOnePhaseOfCandidates()
        {
            // Phase 1 (8 candidates ×2 / 1000 ms) ends by time after 3 candidates (1200 ms).
            var session = new GenerationSession(Forced(1, 2));
            Runs.Drive(session, k => Runs.Good(winner: 0, participants: 2), k => 400L * (k + 1));
            Assert.Equal(GenerationOutcome.WinnerNotFound, session.Result.Outcome);
            Assert.Equal(3, session.Evaluations.Count);
            session.SearchLonger();
            Assert.Equal(3 + session.PhaseBudget.Candidates, session.CandidateLimit);
            Assert.Equal(1200 + session.PhaseBudget.TimeMs, session.TimeLimitMs);
            Runs.Drive(session, k => Runs.Good(winner: 0, participants: 2), k => 1300L + k);
            Assert.Equal(11, session.Result.CandidatesEvaluated);
        }

        [Fact]
        public void NoForcedWinnerKeepsBudget()
        {
            Assert.Equal(1, GenerationSession.BudgetMultiplier(-1, 10));
            Assert.Equal(new GenerationBudget(4, 1000), new GenerationSession(Runs.Options()).PhaseBudget);
        }

        [Fact]
        public void ForcedWinnerFoundDiscardsOtherWinners()
        {
            var session = new GenerationSession(Forced(2, 3));
            // Winner rotates 0,1,2; non-matching candidates have higher tension.
            Runs.Drive(session, k => Runs.Good(tension: k % 3 == 2 ? 0.3 + 0.01 * k : 1.0, winner: k % 3, participants: 3));
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(11, r.SelectedIndex); // best among 2, 5, 8, 11
            Assert.True(r.WinnerMatched);
            Assert.True(r.HasForcedWinner);
            Assert.Equal(12, r.CandidatesEvaluated);
        }

        [Fact]
        public void ForcedWinnerMatchingBelowThresholdBeatsBetterNonMatching()
        {
            var session = new GenerationSession(Forced(1, 2));
            Runs.Drive(session, k => k == 3 ? Runs.Bad(winner: 1, participants: 2) : Runs.Good(winner: 0, participants: 2));
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.BelowThreshold, r.Outcome);
            Assert.Equal(3, r.SelectedIndex);
            Assert.True(r.WinnerMatched);
            Assert.True(r.QualityBelowThreshold);
            Assert.True(r.BudgetDoubled);
            Assert.Equal(16, r.CandidatesEvaluated); // (4 x 2) doubled
        }

        [Fact]
        public void ForcedWinnerNotFoundAwaitsDecision()
        {
            var session = new GenerationSession(Forced(1, 2));
            GenerationStep step = Runs.Drive(session, k => Runs.Good(tension: k == 5 ? 0.95 : 0.5, winner: 0, participants: 2));
            Assert.Equal(GenerationStepKind.AwaitDecision, step.Kind);
            Assert.Equal(GenerationStatus.AwaitingDecision, session.Status);
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.WinnerNotFound, r.Outcome);
            Assert.Null(r.Selected);
            Assert.Equal(5, r.NearestCandidateIndex);
            // No viable candidate with the forced winner: the choice is offered right after the ×N phase, without doubling.
            Assert.False(r.BudgetDoubled);
            Assert.Equal(8, r.CandidatesEvaluated);
            Assert.False(r.QualityBelowThreshold);
            Assert.False(session.Report(8, Runs.Good(winner: 0, participants: 2), 1000));
        }

        [Fact]
        public void SearchLongerRunsAnotherBudgetAndFindsWinner()
        {
            var session = new GenerationSession(Forced(1, 2));
            Func<int, CandidateRunSummary> summaries = k => Runs.Good(winner: k == 12 ? 1 : 0, participants: 2);
            Runs.Drive(session, summaries, k => 10L * (k + 1));
            session.SearchLonger();
            Assert.Equal(GenerationStatus.Running, session.Status);
            Assert.Null(session.Result);
            Assert.Equal(8 + 8, session.CandidateLimit);
            Assert.Equal(80 + 1000, session.TimeLimitMs);
            GenerationStep step = Runs.Drive(session, summaries, k => 10L * (k + 1));
            Assert.Equal(GenerationStepKind.Done, step.Kind);
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(12, r.SelectedIndex);
            Assert.Equal(1, r.SearchLongerCount);
            Assert.Equal(16, r.CandidatesEvaluated);
        }

        [Fact]
        public void SearchLongerWithoutMatchAsksAgain()
        {
            var session = new GenerationSession(Forced(1, 2));
            Func<int, CandidateRunSummary> summaries = k => Runs.Good(winner: 0, participants: 2);
            Runs.Drive(session, summaries);
            session.SearchLonger();
            GenerationStep step = Runs.Drive(session, summaries);
            Assert.Equal(GenerationStepKind.AwaitDecision, step.Kind);
            Assert.Equal(16, session.Result.CandidatesEvaluated);
            Assert.Equal(1, session.Result.SearchLongerCount);
        }

        [Fact]
        public void SearchLongerFindingOnlyWeakMatchGivesBelowThreshold()
        {
            var session = new GenerationSession(Forced(1, 2));
            Func<int, CandidateRunSummary> summaries = k => k == 10 ? Runs.Bad(winner: 1, participants: 2) : Runs.Good(winner: 0, participants: 2);
            Runs.Drive(session, summaries);
            Assert.False(session.Result.BudgetDoubled);
            session.SearchLonger();
            Runs.Drive(session, summaries);
            // The weak match makes the one-time doubling worthwhile: 8 + 8, then doubled by 8.
            Assert.Equal(GenerationOutcome.BelowThreshold, session.Result.Outcome);
            Assert.Equal(10, session.Result.SelectedIndex);
            Assert.True(session.Result.BudgetDoubled);
            Assert.Equal(24, session.Result.CandidatesEvaluated);
        }

        [Fact]
        public void TakeNearestPicksBestNonMatching()
        {
            var session = new GenerationSession(Forced(1, 3));
            Runs.Drive(session, k => k == 4 ? Runs.Make(tension: 1.0, stuck: true, winner: 2, participants: 3)
                : Runs.Good(tension: k == 7 ? 0.9 : 0.4, winner: k % 2 == 0 ? 0 : 2, participants: 3));
            Assert.Equal(7, session.Result.NearestCandidateIndex);
            session.TakeNearest();
            GenerationResult r = session.Result;
            Assert.Equal(GenerationStatus.Completed, session.Status);
            Assert.Equal(GenerationOutcome.NearestWinner, r.Outcome);
            Assert.Equal(7, r.SelectedIndex);
            Assert.False(r.WinnerMatched);
            Assert.True(r.QualityOk);
            Assert.False(r.QualityBelowThreshold);
            Assert.Equal(GenerationStepKind.Done, session.NextRequest().Kind);
        }

        [Fact]
        public void TakeNearestBelowThresholdSetsHint()
        {
            var session = new GenerationSession(Forced(1, 2));
            Runs.Drive(session, k => Runs.Bad(winner: 0, participants: 2));
            session.TakeNearest();
            Assert.Equal(GenerationOutcome.NearestWinner, session.Result.Outcome);
            Assert.True(session.Result.QualityBelowThreshold);
        }

        [Fact]
        public void TakeNearestWithAllRejectedGivesNoViable()
        {
            var session = new GenerationSession(Forced(1, 2));
            Runs.Drive(session, k => Runs.Make(stuck: true, winner: 1, participants: 2));
            Assert.Equal(GenerationOutcome.WinnerNotFound, session.Result.Outcome);
            Assert.Equal(-1, session.Result.NearestCandidateIndex);
            session.TakeNearest();
            Assert.Equal(GenerationOutcome.NoViableCandidate, session.Result.Outcome);
        }

        [Fact]
        public void DecisionsRequirePendingDecision()
        {
            var session = new GenerationSession(Runs.Options());
            Assert.Throws<InvalidOperationException>(() => session.SearchLonger());
            Assert.Throws<InvalidOperationException>(() => session.TakeNearest());
            Runs.Drive(session, k => Runs.Good());
            Assert.Throws<InvalidOperationException>(() => session.SearchLonger());
            Assert.Throws<InvalidOperationException>(() => session.TakeNearest());
        }

        [Fact]
        public void RunWithoutWinnerNeverMatchesForcedWinner()
        {
            var session = new GenerationSession(Forced(0, 2));
            Runs.Drive(session, k => Runs.Good(winner: -1, participants: 2));
            Assert.Equal(GenerationOutcome.WinnerNotFound, session.Result.Outcome);
        }

        // ---- director goals ----

        static int[] ComebackLeaders(int winner, int other)
        {
            var a = new int[30];
            for (int i = 0; i < a.Length; i++) a[i] = i < 15 ? other : winner;
            return a;
        }

        [Fact]
        public void ComebackGoalPrefersComebackCandidate()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 4);
            o.Goal = DirectorGoal.Comeback;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => k == 2
                ? Runs.Make(tension: 0.5, winner: 1, participants: 2, leaders: ComebackLeaders(1, 0))
                : Runs.Make(tension: 0.9, winner: 1, participants: 2, leaders: ComebackLeaders(1, 1)));
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(2, r.SelectedIndex);
            Assert.True(r.GoalMet);
            Assert.True(session.Evaluations[2].IsComeback);
            Assert.True(session.Evaluations[2].GoalMet);
            Assert.False(session.Evaluations[0].IsComeback);
            Assert.False(session.Evaluations[0].GoalMet);
            Assert.True(session.Evaluations[0].Breakdown.Total > session.Evaluations[2].Breakdown.Total);
        }

        [Fact]
        public void ComebackGoalWithoutComebackStillSucceeds()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 3);
            o.Goal = DirectorGoal.Comeback;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => Runs.Make(winner: 0, participants: 2, leaders: ComebackLeaders(0, 0)));
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.False(session.Result.GoalMet);
            Assert.True(session.Result.DirectorGoalMissed);
            Assert.False(session.Result.BudgetDoubled);
        }

        [Fact]
        public void ComebackDoesNotLiftCandidateOverThreshold()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 2);
            o.Goal = DirectorGoal.Comeback;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => Runs.Make(durationTicks: 1800, eventEveryTicks: 300, coverage: 0.1, tension: 0.1,
                winner: 1, participants: 2, leaders: ComebackLeaders(1, 0)));
            Assert.Equal(GenerationOutcome.BelowThreshold, session.Result.Outcome);
            Assert.True(session.Result.GoalMet);
        }

        [Fact]
        public void LastSecondGoalFavoursTension()
        {
            // Candidate 0: better coverage, low tension. Candidate 1: slightly lower total, high tension.
            GenerationSessionOptions o = Runs.Options(candidates: 2);
            Func<int, CandidateRunSummary> summaries = k => k == 0 ? Runs.Make(coverage: 1.0, tension: 0.5) : Runs.Make(coverage: 0.2, tension: 0.85);
            var plain = new GenerationSession(o);
            Runs.Drive(plain, summaries);
            Assert.Equal(0, plain.Result.SelectedIndex);
            Assert.True(plain.Result.GoalMet);

            o.Goal = DirectorGoal.LastSecond;
            var director = new GenerationSession(o);
            Runs.Drive(director, summaries);
            Assert.Equal(1, director.Result.SelectedIndex);
            Assert.True(director.Result.GoalMet);
        }

        [Fact]
        public void LastSecondGoalMetNeedsHighTension()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 1);
            o.Goal = DirectorGoal.LastSecond;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => Runs.Make(coverage: 1.0, tension: 0.79));
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.False(session.Result.GoalMet);
            Assert.True(session.Result.DirectorGoalMissed);
        }

        [Fact]
        public void LastSecondPrefersPassingHighTensionCandidate()
        {
            // QA probe P4: candidate 0 passes with tension 1.0 but a lower total than candidate 1 (tension 0.3).
            GenerationSessionOptions o = Runs.Options(candidates: 2, threshold: 1.6);
            o.Goal = DirectorGoal.LastSecond;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => k == 0
                ? Runs.Make(durationTicks: 1500, coverage: 0.0, tension: 1.0)
                : Runs.Make(coverage: 1.0, tension: 0.3));
            Assert.True(session.Evaluations[0].PassesThreshold);
            Assert.True(session.Evaluations[1].PassesThreshold);
            Assert.True(session.Evaluations[1].Breakdown.Total > session.Evaluations[0].Breakdown.Total);
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.Equal(0, session.Result.SelectedIndex);
            Assert.True(session.Result.GoalMet);
            Assert.False(session.Result.DirectorGoalMissed);
        }

        [Fact]
        public void LastSecondMaximizesTensionThenTotal()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 4, threshold: 1.0);
            o.Goal = DirectorGoal.LastSecond;
            var session = new GenerationSession(o);
            double[] tension = { 0.9, 0.95, 0.95, 0.5 };
            double[] coverage = { 1.0, 0.2, 0.6, 1.0 };
            Runs.Drive(session, k => Runs.Make(tension: tension[k], coverage: coverage[k]));
            Assert.Equal(2, session.Result.SelectedIndex); // tied tension 0.95, higher total
        }

        [Fact]
        public void LastSecondNeverPrefersAFailingCandidate()
        {
            // Tension only ranks among candidates that pass the threshold.
            GenerationSessionOptions o = Runs.Options(candidates: 2);
            o.Goal = DirectorGoal.LastSecond;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => k == 0 ? Runs.Bad(tension: 1.0) : Runs.Good(tension: 0.2));
            Assert.Equal(1, session.Result.SelectedIndex);
            Assert.False(session.Result.GoalMet);
        }

        // ---- interactive ----

        [Fact]
        public void InteractiveModeIgnoresDuration()
        {
            GenerationSessionOptions o = GenerationSessionOptions.ForMode(ModeCode.M04, DeviceTier.C, 9UL, Runs.Target);
            Assert.True(o.Interactive);
            var session = new GenerationSession(o);
            Assert.Equal(2340, session.MaxTicks);
            Runs.Drive(session, k => Runs.Make(durationTicks: 600 + 60 * k, eventEveryTicks: 10));
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            foreach (CandidateEvaluation e in session.Evaluations) Assert.Equal(1.0, e.Breakdown.FitDuration);
            Assert.Equal(8, session.Result.CandidatesEvaluated);
        }

        [Fact]
        public void ModeInteractiveWithoutFlagRejectsForcedWinner()
        {
            // QA probe P1: Mode = M10 (interactive, has a winner in the catalog) with Interactive left false.
            var o = Runs.Options();
            o.Mode = ModeCode.M10;
            o.ForcedWinner = 0;
            o.ParticipantCount = 2;
            Assert.False(o.Interactive);
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.ForcedWinner = GenerationSessionOptions.NoForcedWinner;
            var session = new GenerationSession(o);
            Assert.True(session.IsInteractive);
        }

        [Fact]
        public void ModeInteractiveWithoutFlagIgnoresDuration()
        {
            // QA probe P1b: Mode = M04, a 900-tick run against a 1800-tick target still gets fit 1.
            var o = Runs.Options(candidates: 1, threshold: 0.0);
            o.Mode = ModeCode.M04;
            var session = new GenerationSession(o);
            Runs.Drive(session, k => Runs.Make(durationTicks: 900));
            Assert.Equal(1.0, session.Evaluations[0].Breakdown.FitDuration);
        }

        [Fact]
        public void InteractiveFlagForNonInteractiveModeIsRejected()
        {
            var o = Runs.Options();
            o.Mode = ModeCode.M03;
            o.Interactive = true;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.Interactive = false;
            Assert.False(new GenerationSession(o).IsInteractive);
        }

        [Fact]
        public void InteractiveModeRejectsWinnerAndGoals()
        {
            GenerationSessionOptions o = Runs.Options();
            o.Interactive = true;
            o.ForcedWinner = 0;
            o.ParticipantCount = 2;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.ForcedWinner = GenerationSessionOptions.NoForcedWinner;
            o.Goal = DirectorGoal.LastSecond;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.Goal = DirectorGoal.None;
            new GenerationSession(o);
        }

        // ---- options / validation ----

        [Fact]
        public void ForModeFillsProfileAndBudget()
        {
            GenerationSessionOptions o = GenerationSessionOptions.ForMode(ModeCode.M03, DeviceTier.A, 77UL, 1800);
            SpectacleProfile p = SpectacleProfileTable.Default.Get(ModeCode.M03);
            Assert.Equal(ModeCode.M03, o.Mode);
            Assert.Equal(77UL, o.SessionSeed);
            Assert.Equal(GenerationBudget.TierA, o.Budget);
            Assert.Same(p.Weights, o.Weights);
            Assert.Equal(p.Density, o.Density);
            Assert.Equal(p.QualityThreshold, o.QualityThreshold);
            Assert.False(o.Interactive);
            Assert.Equal(1800, o.TargetDurationTicks);

            var budgets = GenerationBudgetTable.Default.ApplyRemoteJson("{\"B\":{\"candidates\":3}}", out _);
            Assert.Equal(3, GenerationSessionOptions.ForMode(ModeCode.M03, DeviceTier.B, 1UL, null, null, budgets).Budget.Candidates);
        }

        [Fact]
        public void ModeWithoutWinnerRejectsForcedWinnerAndComeback()
        {
            GenerationSessionOptions o = GenerationSessionOptions.ForMode(ModeCode.M01, DeviceTier.B, 1UL, 1800);
            o.ForcedWinner = 0;
            o.ParticipantCount = 3;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.ForcedWinner = GenerationSessionOptions.NoForcedWinner;
            o.Goal = DirectorGoal.Comeback;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
            o.Goal = DirectorGoal.LastSecond;
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));

            GenerationSessionOptions race = GenerationSessionOptions.ForMode(ModeCode.M03, DeviceTier.B, 1UL, 1800);
            race.ForcedWinner = 2;
            race.ParticipantCount = 6;
            race.Goal = DirectorGoal.Comeback;
            Assert.Equal(new GenerationBudget(64, 2500), new GenerationSession(race).PhaseBudget);
        }

        [Fact]
        public void InvalidOptionsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new GenerationSession(null));
            Check(o => o.Budget = default(GenerationBudget));
            Check(o => o.QualityThreshold = double.NaN);
            Check(o => o.TargetDurationTicks = 0);
            Check(o => o.TargetDurationTicks = 60); // QA probe P9: 1 s
            Check(o => o.TargetDurationTicks = GenerationSessionOptions.MinTargetDurationTicks - 1);
            Check(o => { o.ForcedWinner = 0; o.ParticipantCount = 2; o.ForcedWinnerTimeMs = 0; });
            Check(o => o.TargetDurationTicks = SimConstants.MaxDurationTicks + 1);
            Check(o => o.Weights = null);
            Check(o => o.Density = default(DensityRange));
            Check(o => o.Goal = (DirectorGoal)9);
            Check(o => o.ParticipantCount = -1);
            Check(o => { o.ForcedWinner = 0; o.ParticipantCount = 1; });
            Check(o => { o.ForcedWinner = 3; o.ParticipantCount = 3; });
            Check(o => { o.ForcedWinner = -5; o.ParticipantCount = 3; });
        }

        [Fact]
        public void TargetBoundsAreTenToOneHundredEightySeconds()
        {
            GenerationSessionOptions o = Runs.Options();
            o.TargetDurationTicks = GenerationSessionOptions.MinTargetDurationTicks;
            new GenerationSession(o);
            o.TargetDurationTicks = SimConstants.MaxDurationTicks;
            new GenerationSession(o);
        }

        [Fact]
        public void GoalScopeRaceAndBattleOnly()
        {
            // QA probes P10 (M01 + LastSecond) and P11 (M07 + Comeback): spec §2.5 "for race and battle".
            foreach (ModeInfo mode in ModeCatalog.All)
            {
                foreach (DirectorGoal goal in new[] { DirectorGoal.LastSecond, DirectorGoal.Comeback })
                {
                    GenerationSessionOptions o = GenerationSessionOptions.ForMode(mode.Code, DeviceTier.B, 1UL, Runs.Target);
                    o.Goal = goal;
                    bool allowed = mode.Code == ModeCode.M03 || mode.Code == ModeCode.M06;
                    Assert.Equal(allowed, GenerationSessionOptions.SupportsDirectorGoals(mode.Code));
                    if (allowed) new GenerationSession(o);
                    else Assert.Throws<ArgumentException>(() => new GenerationSession(o));
                }
            }
            // Without a mode the caller is trusted.
            GenerationSessionOptions free = Runs.Options();
            free.Goal = DirectorGoal.Comeback;
            new GenerationSession(free);
        }

        static void Check(Action<GenerationSessionOptions> mutate)
        {
            GenerationSessionOptions o = Runs.Options();
            mutate(o);
            Assert.Throws<ArgumentException>(() => new GenerationSession(o));
        }

        [Fact]
        public void OptionsAreCopiedAtConstruction()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 2);
            var session = new GenerationSession(o);
            o.Budget = new GenerationBudget(50, 1000);
            o.QualityThreshold = 100.0;
            o.SessionSeed = 1UL;
            Runs.Drive(session, k => Runs.Good());
            Assert.Equal(2, session.Evaluations.Count);
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 0), session.Evaluations[0].Seed);
        }

        [Fact]
        public void UnlimitedTargetScoresFitAsOne()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 1);
            o.TargetDurationTicks = null;
            var session = new GenerationSession(o);
            Assert.Equal(SimConstants.MaxDurationTicks, session.MaxTicks);
            Runs.Drive(session, k => Runs.Make(durationTicks: 6000, eventEveryTicks: 30));
            Assert.Equal(1.0, session.Evaluations[0].Breakdown.FitDuration);
        }

        [Theory]
        [InlineData(1800, 2340)]
        [InlineData(7, 10)]
        [InlineData(600, 780)]
        [InlineData(9000, SimConstants.MaxDurationTicks)]
        [InlineData(SimConstants.MaxDurationTicks, SimConstants.MaxDurationTicks)]
        public void RunLimitIsTargetTimesOnePointThreeCappedAtMax(int target, int expected)
        {
            Assert.Equal(expected, GenerationSession.RunLimitTicks(target));
        }

        [Fact]
        public void RunLimitWithoutTargetIsMaxDuration()
        {
            Assert.Equal(SimConstants.MaxDurationTicks, GenerationSession.RunLimitTicks(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => GenerationSession.RunLimitTicks(0));
        }

        // ---- protocol ----

        [Fact]
        public void NextRequestHandsOutNewCandidatesUpToTheLimit()
        {
            var session = new GenerationSession(Runs.Options(candidates: 3));
            GenerationStep a = session.NextRequest();
            GenerationStep b = session.NextRequest();
            Assert.Equal(GenerationStepKind.RunCandidate, a.Kind);
            Assert.Equal(0, a.CandidateIndex);
            Assert.Equal(1, b.CandidateIndex);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 0), a.Seed);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 1), b.Seed);
            Assert.Equal(2340, a.MaxTicks);
            Assert.Equal(1000, a.DeadlineMs);
            Assert.Equal(2, session.OutstandingCount);
            Assert.Equal(2, session.NextRequest().CandidateIndex);
            GenerationStep wait = session.NextRequest();
            Assert.Equal(GenerationStepKind.Wait, wait.Kind);
            Assert.Equal(-1, wait.CandidateIndex);
            Assert.False(wait.IsRun);
            Assert.True(session.Report(1, Runs.Good(), 5));
            Assert.Empty(session.Evaluations); // buffered until candidate 0 arrives
            Assert.True(session.Report(0, Runs.Good(), 6));
            Assert.Equal(2, session.Evaluations.Count);
            Assert.Equal(GenerationStepKind.Wait, session.NextRequest().Kind);
            Assert.True(session.Report(2, Runs.Good(), 7));
            Assert.Equal(GenerationStepKind.Done, session.NextRequest().Kind);
            Assert.Equal(0, session.Result.SelectedIndex);
        }

        [Fact]
        public void OutOfOrderReportsGiveTheSameChoiceAsSequential()
        {
            var rng = Rng.FromSeed(4242UL, 1);
            for (int round = 0; round < 50; round++)
            {
                int candidates = rng.NextInt(2, 12);
                ulong scenario = rng.NextULong();
                Func<int, CandidateRunSummary> summaryFor = k =>
                {
                    Rng r = Rng.FromSeed(scenario, (ulong)k);
                    return Runs.Make(durationTicks: r.NextInt(1200, 2340), coverage: r.NextInt(0, 5) / 4.0,
                        tension: r.NextInt(0, 5) / 4.0, winner: r.NextInt(0, 3), participants: 3, stuck: r.NextUInt(6) == 0);
                };
                GenerationSessionOptions o = Runs.Options(candidates: candidates, timeMs: 1000000);
                o.ForcedWinner = rng.NextBool() ? rng.NextInt(0, 3) : GenerationSessionOptions.NoForcedWinner;
                o.ParticipantCount = 3;
                o.Goal = (DirectorGoal)rng.NextInt(0, 3);

                var sequential = new GenerationSession(o);
                Runs.Drive(sequential, summaryFor, k => 1L);

                var parallel = new GenerationSession(o);
                for (int guard = 0; guard < 1000 && parallel.Status == GenerationStatus.Running; guard++)
                {
                    // Hand out up to 4 candidates at a time ("workers"), report them in a shuffled order.
                    var batch = new System.Collections.Generic.List<int>();
                    for (int w = 0; w < 4; w++)
                    {
                        GenerationStep step = parallel.NextRequest();
                        if (!step.IsRun) break;
                        Assert.Equal(CandidateSeeds.ForCandidate(o.SessionSeed, step.CandidateIndex), step.Seed);
                        batch.Add(step.CandidateIndex);
                    }
                    for (int i = batch.Count - 1; i > 0; i--)
                    {
                        int j = rng.NextInt(0, i + 1);
                        int t = batch[i];
                        batch[i] = batch[j];
                        batch[j] = t;
                    }
                    foreach (int k in batch) parallel.Report(k, summaryFor(k), 1L);
                }
                Assert.Equal(sequential.Status, parallel.Status);
                Assert.Equal(sequential.Result.Outcome, parallel.Result.Outcome);
                Assert.Equal(sequential.Result.SelectedIndex, parallel.Result.SelectedIndex);
                Assert.Equal(sequential.Result.CandidatesEvaluated, parallel.Result.CandidatesEvaluated);
                Assert.Equal(sequential.Result.BudgetDoubled, parallel.Result.BudgetDoubled);
            }
        }

        [Fact]
        public void TimeEndedPhaseWaitsForEveryHandedOutRun()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            session.NextRequest();
            session.NextRequest();
            // Candidate 1 (good) arrives first, after the time budget, while candidate 0 is still running: no new candidates,
            // and the phase waits for candidate 0 instead of ending on an empty prefix (QA round 1 #1).
            Assert.True(session.Report(1, Runs.Good(), 1100));
            Assert.Empty(session.Evaluations);
            Assert.False(session.BudgetDoubled);
            Assert.Equal(GenerationStatus.Running, session.Status);
            Assert.Equal(GenerationStepKind.Wait, session.NextRequest().Kind);
            Assert.True(session.Report(0, Runs.Bad(), 1200));
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.Equal(1, session.Result.SelectedIndex);
            Assert.Equal(2, session.Result.CandidatesEvaluated);
            Assert.False(session.Result.BudgetDoubled);
            Assert.Equal(1000, session.TimeLimitMs);
        }

        [Fact]
        public void ReportsAfterTheDeadlineAreAllRankedAndLateOnesAfterCompletionIgnored()
        {
            var session = new GenerationSession(Runs.Options(candidates: 10, timeMs: 1000));
            for (int i = 0; i < 3; i++) session.NextRequest();
            Assert.True(session.Report(2, Runs.Good(tension: 1.0), 900));
            Assert.True(session.Report(0, Runs.Good(), 1500)); // time is up; candidate 1 is still outstanding
            Assert.Equal(GenerationStatus.Running, session.Status);
            Assert.Equal(2, session.OutstandingCount); // 1 running, 2 buffered
            Assert.True(session.Report(1, Runs.Bad(), 1600));
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
            Assert.Equal(2, session.Result.SelectedIndex); // the buffered best candidate is not thrown away
            Assert.Equal(3, session.Result.CandidatesEvaluated);
            Assert.Equal(0, session.OutstandingCount);
            Assert.False(session.Report(3, Runs.Good(), 1700));
            Assert.Equal(3, session.Evaluations.Count);
        }

        [Fact]
        public void ReportOrderAtTheDeadlineDoesNotChangeTheOutcome()
        {
            // QA probe V1: 0 and 1 issued, 1 (good) reported at 600 ms, 2 issued; at the 1000 ms deadline 0 and 2 are aborted and
            // reported at the same elapsed time in either order.
            CandidateRunSummary aborted = Runs.Make(durationTicks: 300, finished: false);
            var a = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            var b = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            foreach (GenerationSession s in new[] { a, b })
            {
                Assert.Equal(0, s.NextRequest().CandidateIndex);
                Assert.Equal(1, s.NextRequest().CandidateIndex);
                s.Report(1, Runs.Good(), 600);
                Assert.Equal(2, s.NextRequest().CandidateIndex);
            }
            a.Report(0, aborted, 1000);
            a.Report(2, aborted, 1000);
            b.Report(2, aborted, 1000);
            b.Report(0, aborted, 1000);
            foreach (GenerationSession s in new[] { a, b })
            {
                Assert.Equal(GenerationStatus.Completed, s.Status);
                Assert.Equal(GenerationOutcome.Success, s.Result.Outcome);
                Assert.Equal(1, s.Result.SelectedIndex);
                Assert.Equal(3, s.Result.CandidatesEvaluated);
                Assert.False(s.Result.BudgetDoubled);
                Assert.Equal(1000, s.TimeLimitMs);
            }
        }

        [Fact]
        public void PassingCandidateReportedAtTheDoubledDeadlineIsKept()
        {
            // QA probe V2: phase 1 ends by count with nothing passing, the budget doubles to 4 / 2000 ms; candidate 3 (good) is
            // reported at the deadline while candidate 2 is still being aborted.
            var s = new GenerationSession(Runs.Options(candidates: 2, timeMs: 1000));
            s.NextRequest();
            s.NextRequest();
            s.Report(0, Runs.Bad(), 100);
            s.Report(1, Runs.Bad(), 200);
            Assert.True(s.BudgetDoubled);
            Assert.Equal(2, s.NextRequest().CandidateIndex);
            Assert.Equal(3, s.NextRequest().CandidateIndex);
            s.Report(3, Runs.Good(), 2000);
            Assert.Equal(GenerationStatus.Running, s.Status);
            s.Report(2, Runs.Make(durationTicks: 300, finished: false), 2000);
            Assert.Equal(GenerationOutcome.Success, s.Result.Outcome);
            Assert.Equal(3, s.Result.SelectedIndex);
            Assert.False(s.Result.QualityBelowThreshold);
        }

        [Fact]
        public void WinnerNotFoundWaitsForInFlightRunsAndSearchLongerUsesNewIndices()
        {
            // QA probes V3 / K16: forced winner 1 of 2 (phase 8 / 1000 ms); 3 handed out, 0 and 1 reported, the second past the
            // deadline. The decision is offered only after candidate 2 reports, so nothing is abandoned and "Search longer" never
            // re-issues an index: a stale report cannot be credited to a new run.
            var s = new GenerationSession(Forced(1, 2));
            for (int i = 0; i < 3; i++) s.NextRequest();
            s.Report(0, Runs.Good(winner: 0, participants: 2), 900);
            s.Report(1, Runs.Good(winner: 0, participants: 2), 1100);
            Assert.Equal(GenerationStatus.Running, s.Status);
            Assert.Equal(GenerationStepKind.Wait, s.NextRequest().Kind);
            s.Report(2, Runs.Make(durationTicks: 300, finished: false, winner: 1, participants: 2), 1100);
            Assert.Equal(GenerationOutcome.WinnerNotFound, s.Result.Outcome);
            Assert.Equal(3, s.Result.CandidatesEvaluated);
            Assert.Equal(0, s.OutstandingCount);
            s.SearchLonger();
            GenerationStep step = s.NextRequest();
            Assert.Equal(3, step.CandidateIndex);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 3), step.Seed);
            Assert.Throws<InvalidOperationException>(() => s.Report(2, Runs.Good(winner: 1, participants: 2), 1150));
            Assert.True(s.Report(3, Runs.Good(winner: 1, participants: 2), 1200)); // limits now 11 candidates / 2100 ms
            GenerationStep last = Runs.Drive(s, k => Runs.Good(winner: 0, participants: 2), k => 1300L + k);
            Assert.Equal(GenerationStepKind.Done, last.Kind);
            Assert.Equal(GenerationOutcome.Success, s.Result.Outcome);
            Assert.Equal(3, s.Result.SelectedIndex);
        }

        [Fact]
        public void LastSecondGoalIsMetAtExactlyTheTensionGoal()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 1);
            o.Goal = DirectorGoal.LastSecond;
            var s = new GenerationSession(o);
            Runs.Drive(s, k => Runs.Make(tension: GenerationSession.LastSecondTensionGoal));
            Assert.True(s.Evaluations[0].GoalMet);
            Assert.True(s.Result.GoalMet);
        }

        [Fact]
        public void ReportThatThrowsChangesNothingAndTheIndexCanBeReportedAgain()
        {
            // QA probe V4: an inconsistent summary throws before any state changes; the index stays outstanding until it is
            // reported again (here as an aborted run), then the session completes.
            var s = new GenerationSession(Runs.Options(candidates: 2, timeMs: 1000));
            s.NextRequest();
            s.NextRequest();
            Assert.Throws<ArgumentException>(() => s.Report(0, Runs.Make(durationTicks: 5000, finished: false, endedByLimit: true), 10));
            Assert.Equal(2, s.OutstandingCount);
            Assert.True(s.Report(1, Runs.Good(), 20));
            Assert.Equal(GenerationStepKind.Wait, s.NextRequest().Kind);
            Assert.True(s.Report(0, Runs.Make(durationTicks: 300, finished: false), 30));
            Assert.Equal(RejectReason.Incomplete, s.Evaluations[0].Breakdown.Rejected);
            Assert.Equal(GenerationOutcome.Success, s.Result.Outcome);
            Assert.Equal(1, s.Result.SelectedIndex);
        }

        [Fact]
        public void ReportValidatesProtocol()
        {
            var session = new GenerationSession(Runs.Options());
            Assert.Throws<InvalidOperationException>(() => session.Report(0, Runs.Good(), 1));
            session.NextRequest();
            Assert.Throws<InvalidOperationException>(() => session.Report(1, Runs.Good(), 1));
            Assert.Throws<InvalidOperationException>(() => session.Report(-1, Runs.Good(), 1));
            Assert.Throws<ArgumentNullException>(() => session.Report(0, null, 1));
            session.Report(0, Runs.Good(), 100);
            session.NextRequest();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Report(1, Runs.Good(), 99));
            Assert.Throws<InvalidOperationException>(() => session.Report(0, Runs.Good(), 200));
            session.Report(1, Runs.Good(), 100);
            Assert.Equal(2, session.Evaluations.Count);
            session.NextRequest();
            session.NextRequest();
            session.Report(3, Runs.Good(), 100);
            Assert.Throws<InvalidOperationException>(() => session.Report(3, Runs.Good(), 100)); // buffered duplicate
        }

        [Fact]
        public void ReportRejectsSummaryInconsistentWithSession()
        {
            // QA probe P7: target 600 -> MaxTicks 780, a 5000-tick run cannot come from this session.
            GenerationSessionOptions o = Runs.Options();
            o.TargetDurationTicks = 600;
            var session = new GenerationSession(o);
            Assert.Equal(780, session.MaxTicks);
            session.NextRequest();
            Assert.Throws<ArgumentException>(() => session.Report(0, Runs.Make(durationTicks: 5000, finished: false, endedByLimit: true), 1));
            session.Report(0, Runs.Make(durationTicks: 780, finished: false, endedByLimit: true), 1);

            // QA probe P8: forced winner 3 of 4, but the run reports 2 participants.
            GenerationSessionOptions f = Runs.Options();
            f.ForcedWinner = 3;
            f.ParticipantCount = 4;
            var forced = new GenerationSession(f);
            forced.NextRequest();
            Assert.Throws<ArgumentException>(() => forced.Report(0, Runs.Good(winner: 1, participants: 2), 1));
            forced.Report(0, Runs.Good(winner: 3, participants: 4), 1);
        }

        [Fact]
        public void ReportAfterCompletionIsIgnored()
        {
            var session = new GenerationSession(Runs.Options(candidates: 1));
            Runs.Drive(session, k => Runs.Good());
            Assert.False(session.Report(1, Runs.Good(), 1000));
            Assert.Equal(GenerationStatus.Completed, session.Status);
        }

        [Fact]
        public void DeadlineFollowsTheCurrentTimeLimit()
        {
            var session = new GenerationSession(Runs.Options(candidates: 4, timeMs: 1000));
            Runs.Drive(session, k => Runs.Bad(), k => 300L * (k + 1));
            Assert.Equal(2000, session.TimeLimitMs);
            // An aborted run (neither finished nor stopped by the limit) is Incomplete and never chosen.
            var aborted = new GenerationSession(Runs.Options(candidates: 2, timeMs: 1000));
            GenerationStep step = aborted.NextRequest();
            Assert.Equal(1000, step.DeadlineMs);
            aborted.Report(step.CandidateIndex, Runs.Make(durationTicks: 300, finished: false), step.DeadlineMs);
            Assert.Equal(RejectReason.Incomplete, aborted.Evaluations[0].Breakdown.Rejected);
        }

        [Fact]
        public void CancelStopsSession()
        {
            var session = new GenerationSession(Runs.Options());
            session.NextRequest();
            session.Report(0, Runs.Good(), 10);
            session.NextRequest();
            session.Cancel();
            Assert.Equal(GenerationStatus.Cancelled, session.Status);
            Assert.Equal(GenerationOutcome.Cancelled, session.Result.Outcome);
            Assert.Equal(1, session.Result.CandidatesEvaluated);
            Assert.Null(session.Result.Selected);
            Assert.False(session.Result.QualityBelowThreshold);
            Assert.Equal(GenerationStepKind.Done, session.NextRequest().Kind);
            // QA probe P17: a background run still in flight reports after the user left: ignored, no exception.
            Assert.False(session.Report(1, Runs.Good(), 20));
            Assert.Equal(0, session.OutstandingCount);
            session.Cancel(); // idempotent
        }

        [Fact]
        public void CancelAfterCompletionIsNoOp()
        {
            var session = new GenerationSession(Runs.Options(candidates: 1));
            Runs.Drive(session, k => Runs.Good());
            session.Cancel();
            Assert.Equal(GenerationStatus.Completed, session.Status);
            Assert.Equal(GenerationOutcome.Success, session.Result.Outcome);
        }

        [Fact]
        public void CancelWhileAwaitingDecision()
        {
            var session = new GenerationSession(Forced(1, 2));
            Runs.Drive(session, k => Runs.Good(winner: 0, participants: 2));
            session.Cancel();
            Assert.Equal(GenerationOutcome.Cancelled, session.Result.Outcome);
            Assert.True(session.Result.HasForcedWinner);
        }
    }
}
