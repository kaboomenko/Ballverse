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
            Assert.Equal(new GenerationBudget(4 * multiplier, 1000L * multiplier), session.PhaseBudget);
            Assert.Equal(multiplier, GenerationSession.BudgetMultiplier(0, participants));
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
            GenerationStep step = Runs.Drive(session, k => Runs.Good(tension: k == 9 ? 0.95 : 0.5, winner: 0, participants: 2));
            Assert.Equal(GenerationStepKind.AwaitDecision, step.Kind);
            Assert.Equal(GenerationStatus.AwaitingDecision, session.Status);
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.WinnerNotFound, r.Outcome);
            Assert.Null(r.Selected);
            Assert.Equal(9, r.NearestCandidateIndex);
            Assert.True(r.BudgetDoubled);
            Assert.Equal(16, r.CandidatesEvaluated);
            Assert.False(r.QualityBelowThreshold);
            Assert.Throws<InvalidOperationException>(() => session.Report(16, Runs.Good(), 1000));
        }

        [Fact]
        public void SearchLongerRunsAnotherBudgetAndFindsWinner()
        {
            var session = new GenerationSession(Forced(1, 2));
            Func<int, CandidateRunSummary> summaries = k => Runs.Good(winner: k == 20 ? 1 : 0, participants: 2);
            Runs.Drive(session, summaries, k => 10L * (k + 1));
            session.SearchLonger();
            Assert.Equal(GenerationStatus.Running, session.Status);
            Assert.Null(session.Result);
            Assert.Equal(16 + 8, session.CandidateLimit);
            Assert.Equal(160 + 2000, session.TimeLimitMs);
            GenerationStep step = Runs.Drive(session, summaries, k => 10L * (k + 1));
            Assert.Equal(GenerationStepKind.Done, step.Kind);
            GenerationResult r = session.Result;
            Assert.Equal(GenerationOutcome.Success, r.Outcome);
            Assert.Equal(20, r.SelectedIndex);
            Assert.Equal(1, r.SearchLongerCount);
            Assert.Equal(24, r.CandidatesEvaluated);
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
            Assert.Equal(24, session.Result.CandidatesEvaluated);
            Assert.Equal(1, session.Result.SearchLongerCount);
        }

        [Fact]
        public void SearchLongerFindingOnlyWeakMatchGivesBelowThreshold()
        {
            var session = new GenerationSession(Forced(1, 2));
            Func<int, CandidateRunSummary> summaries = k => k == 18 ? Runs.Bad(winner: 1, participants: 2) : Runs.Good(winner: 0, participants: 2);
            Runs.Drive(session, summaries);
            session.SearchLonger();
            Runs.Drive(session, summaries);
            Assert.Equal(GenerationOutcome.BelowThreshold, session.Result.Outcome);
            Assert.Equal(18, session.Result.SelectedIndex);
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
            Assert.False(session.Evaluations[0].IsComeback);
            Assert.Equal(session.Evaluations[2].Breakdown.Total + 1.0, session.Evaluations[2].SelectionScore, 12);
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
            Assert.False(session.Result.BudgetDoubled);
        }

        [Fact]
        public void ComebackBonusDoesNotLiftCandidateOverThreshold()
        {
            GenerationSessionOptions o = Runs.Options(candidates: 2);
            o.Goal = DirectorGoal.Comeback;
            o.GoalBonusWeight = 10.0;
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
            new GenerationSession(o);

            GenerationSessionOptions race = GenerationSessionOptions.ForMode(ModeCode.M03, DeviceTier.B, 1UL, 1800);
            race.ForcedWinner = 2;
            race.ParticipantCount = 6;
            race.Goal = DirectorGoal.Comeback;
            Assert.Equal(new GenerationBudget(64, 10000), new GenerationSession(race).PhaseBudget);
        }

        [Fact]
        public void InvalidOptionsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new GenerationSession(null));
            Check(o => o.Budget = default(GenerationBudget));
            Check(o => o.QualityThreshold = double.NaN);
            Check(o => o.TargetDurationTicks = 0);
            Check(o => o.TargetDurationTicks = SimConstants.MaxDurationTicks + 1);
            Check(o => o.Weights = null);
            Check(o => o.Density = default(DensityRange));
            Check(o => o.GoalBonusWeight = -1);
            Check(o => o.Goal = (DirectorGoal)9);
            Check(o => o.ParticipantCount = -1);
            Check(o => { o.ForcedWinner = 0; o.ParticipantCount = 1; });
            Check(o => { o.ForcedWinner = 3; o.ParticipantCount = 3; });
            Check(o => { o.ForcedWinner = -5; o.ParticipantCount = 3; });
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
        public void NextRequestRepeatsOutstandingCandidate()
        {
            var session = new GenerationSession(Runs.Options());
            GenerationStep a = session.NextRequest();
            GenerationStep b = session.NextRequest();
            Assert.Equal(GenerationStepKind.RunCandidate, a.Kind);
            Assert.Equal(0, a.CandidateIndex);
            Assert.Equal(a.CandidateIndex, b.CandidateIndex);
            Assert.Equal(a.Seed, b.Seed);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 0), a.Seed);
            Assert.Equal(2340, a.MaxTicks);
            session.Report(0, Runs.Good(), 5);
            GenerationStep c = session.NextRequest();
            Assert.Equal(1, c.CandidateIndex);
            Assert.Equal(CandidateSeeds.ForCandidate(12345UL, 1), c.Seed);
        }

        [Fact]
        public void ReportValidatesProtocol()
        {
            var session = new GenerationSession(Runs.Options());
            Assert.Throws<InvalidOperationException>(() => session.Report(0, Runs.Good(), 1));
            session.NextRequest();
            Assert.Throws<InvalidOperationException>(() => session.Report(1, Runs.Good(), 1));
            Assert.Throws<ArgumentNullException>(() => session.Report(0, null, 1));
            session.Report(0, Runs.Good(), 100);
            session.NextRequest();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Report(1, Runs.Good(), 99));
            Assert.Throws<InvalidOperationException>(() => session.Report(0, Runs.Good(), 200));
            session.Report(1, Runs.Good(), 100);
            Assert.Equal(2, session.Evaluations.Count);
        }

        [Fact]
        public void ReportAfterCompletionThrows()
        {
            var session = new GenerationSession(Runs.Options(candidates: 1));
            Runs.Drive(session, k => Runs.Good());
            Assert.Throws<InvalidOperationException>(() => session.Report(1, Runs.Good(), 1000));
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
            Assert.Throws<InvalidOperationException>(() => session.Report(1, Runs.Good(), 20));
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
