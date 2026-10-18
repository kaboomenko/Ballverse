using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Xunit;

namespace Ballverse.Core.Tests.Generation
{
    /// <summary>Rng-driven invariants, determinism and golden cases of <see cref="GenerationSession"/>.</summary>
    public class GenerationSessionPropertyTests
    {
        /// <summary>Summary of candidate k, a pure function of (scenario seed, k) so it can be replayed.</summary>
        static CandidateRunSummary RandomCandidate(ulong scenarioSeed, int k, int participants, int maxTicks)
        {
            Rng rng = Rng.FromSeed(scenarioSeed, 1000UL + (ulong)k);
            int duration = Math.Min(rng.NextInt(600, 3000), maxTicks);
            int every = rng.NextInt(10, 400);
            bool stuck = rng.NextUInt(8) == 0;
            int winner = participants > 0 ? rng.NextInt(-1, participants) : -1;
            var leaders = new int[duration / SimConstants.TickRate];
            for (int i = 0; i < leaders.Length; i++) leaders[i] = participants > 0 ? rng.NextInt(-1, participants) : -1;
            bool finished = rng.NextUInt(5) != 0;
            // Quantized metrics make exact ties likely, exercising the tie-break.
            double coverage = rng.NextInt(0, 5) / 4.0;
            double tension = rng.NextInt(0, 5) / 4.0;
            int[] events = Runs.EvenEvents(duration, every);
            return new CandidateRunSummary(duration, finished, !finished, events, events.Length, coverage, tension,
                winner, participants, leaders, stuck);
        }

        static GenerationSessionOptions RandomOptions(ref Rng rng, ulong sessionSeed)
        {
            var o = Runs.Options(candidates: rng.NextInt(1, 10), timeMs: rng.NextInt(50, 500), threshold: rng.NextFloat(1.0f, 3.0f));
            o.SessionSeed = sessionSeed;
            o.TargetDurationTicks = rng.NextBool() ? (int?)rng.NextInt(600, 2400) : null;
            o.ParticipantCount = rng.NextInt(0, 6);
            if (o.ParticipantCount >= 2 && rng.NextBool()) o.ForcedWinner = rng.NextInt(0, o.ParticipantCount);
            o.Goal = (DirectorGoal)rng.NextInt(0, 3);
            return o;
        }

        sealed class Outcome
        {
            public readonly List<string> Trace = new List<string>();
        }

        /// <summary>Drives a session to completion, choosing decisions with the rng, and checks invariants on the way.</summary>
        static GenerationSession Run(GenerationSessionOptions o, ulong scenarioSeed, ref Rng decisions, Outcome trace)
        {
            var session = new GenerationSession(o);
            long elapsed = 0;
            for (int guard = 0; guard < 10000; guard++)
            {
                GenerationStep step = session.NextRequest();
                if (step.Kind == GenerationStepKind.RunCandidate)
                {
                    Assert.Equal(session.Evaluations.Count, step.CandidateIndex);
                    Assert.Equal(CandidateSeeds.ForCandidate(o.SessionSeed, step.CandidateIndex), step.Seed);
                    elapsed += Rng.FromSeed(scenarioSeed, 5000UL + (ulong)step.CandidateIndex).NextInt(1, 120);
                    session.Report(step.CandidateIndex, RandomCandidate(scenarioSeed, step.CandidateIndex, o.ParticipantCount, step.MaxTicks), elapsed);
                    Assert.True(session.Evaluations.Count <= session.CandidateLimit);
                    continue;
                }
                CheckInvariants(session, o);
                if (step.Kind == GenerationStepKind.Done) return session;
                // Awaiting a forced-winner decision.
                Assert.True(o.ForcedWinner >= 0);
                bool searchLonger = session.Result.SearchLongerCount < 2 && decisions.NextBool();
                trace.Trace.Add(searchLonger ? "longer" : "nearest");
                if (searchLonger) session.SearchLonger();
                else session.TakeNearest();
            }
            throw new InvalidOperationException("Session did not finish");
        }

        /// <summary>Independent restatement of the ranking: goal key, then total, then the lowest index.</summary>
        static bool Better(CandidateEvaluation a, CandidateEvaluation b, DirectorGoal goal)
        {
            if (goal == DirectorGoal.LastSecond && a.Breakdown.FinishTension != b.Breakdown.FinishTension)
                return a.Breakdown.FinishTension > b.Breakdown.FinishTension;
            if (goal == DirectorGoal.Comeback && a.IsComeback != b.IsComeback) return a.IsComeback;
            if (a.Breakdown.Total != b.Breakdown.Total) return a.Breakdown.Total > b.Breakdown.Total;
            return a.Index < b.Index;
        }

        static CandidateEvaluation BestOf(GenerationSession s, DirectorGoal goal, Func<CandidateEvaluation, bool> filter)
        {
            CandidateEvaluation best = null;
            foreach (CandidateEvaluation e in s.Evaluations)
            {
                if (!filter(e)) continue;
                if (best == null || Better(e, best, goal)) best = e;
            }
            return best;
        }

        static void CheckInvariants(GenerationSession s, GenerationSessionOptions o)
        {
            GenerationResult r = s.Result;
            Assert.NotNull(r);
            Assert.Equal(s.Evaluations.Count, r.CandidatesEvaluated);
            if (r.Selected != null) Assert.False(r.Selected.IsRejected);
            foreach (CandidateEvaluation e in s.Evaluations)
            {
                Assert.Equal(e.GoalMet, o.Goal == DirectorGoal.None
                    || (o.Goal == DirectorGoal.LastSecond && e.Breakdown.FinishTension >= GenerationSession.LastSecondTensionGoal)
                    || (o.Goal == DirectorGoal.Comeback && e.IsComeback));
                Assert.Equal(e.PassesThreshold, !e.IsRejected && e.Breakdown.Total >= o.QualityThreshold);
                Assert.Equal(e.MatchesWinner, o.ForcedWinner < 0 || e.Summary.WinnerIndex == o.ForcedWinner);
            }
            CandidateEvaluation bestPassing = BestOf(s, o.Goal, e => e.PassesThreshold && e.MatchesWinner);
            CandidateEvaluation bestMatching = BestOf(s, o.Goal, e => !e.IsRejected && e.MatchesWinner);
            CandidateEvaluation bestAny = BestOf(s, o.Goal, e => !e.IsRejected);
            switch (r.Outcome)
            {
                case GenerationOutcome.Success:
                    Assert.Same(bestPassing, r.Selected);
                    Assert.True(r.QualityOk);
                    // A goal-meeting candidate is always preferred when one passed (spec §2.5).
                    if (BestOf(s, o.Goal, e => e.PassesThreshold && e.MatchesWinner && e.GoalMet) != null) Assert.True(r.GoalMet);
                    Assert.Equal(!r.GoalMet, r.DirectorGoalMissed);
                    break;
                case GenerationOutcome.BelowThreshold:
                    Assert.Null(bestPassing);
                    Assert.True(r.BudgetDoubled);
                    Assert.Same(bestMatching, r.Selected);
                    Assert.True(r.QualityBelowThreshold);
                    break;
                case GenerationOutcome.WinnerNotFound:
                    Assert.Null(bestMatching);
                    Assert.Equal(0, s.OutstandingCount);
                    Assert.Equal(bestAny?.Index ?? -1, r.NearestCandidateIndex);
                    break;
                case GenerationOutcome.NearestWinner:
                    Assert.Null(bestMatching);
                    Assert.Same(bestAny, r.Selected);
                    break;
                case GenerationOutcome.NoViableCandidate:
                    Assert.Null(bestMatching);
                    Assert.True(o.ForcedWinner >= 0 || bestAny == null);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected outcome " + r.Outcome);
            }
        }

        [Fact]
        public void RandomSessionsSatisfyInvariantsAndAreDeterministic()
        {
            var outer = Rng.FromSeed(99UL, 3);
            var outcomes = new int[6];
            for (int n = 0; n < 300; n++)
            {
                ulong sessionSeed = outer.NextULong();
                ulong scenarioSeed = outer.NextULong();
                ulong decisionSeed = outer.NextULong();
                GenerationSessionOptions o = RandomOptions(ref outer, sessionSeed);

                var d1 = Rng.FromSeed(decisionSeed);
                var d2 = Rng.FromSeed(decisionSeed);
                var t1 = new Outcome();
                var t2 = new Outcome();
                GenerationSession a = Run(o, scenarioSeed, ref d1, t1);
                GenerationSession b = Run(o, scenarioSeed, ref d2, t2);

                Assert.Equal(t1.Trace, t2.Trace);
                Assert.Equal(a.Result.Outcome, b.Result.Outcome);
                Assert.Equal(a.Result.SelectedIndex, b.Result.SelectedIndex);
                Assert.Equal(a.Result.SelectedSeed, b.Result.SelectedSeed);
                Assert.Equal(a.Result.CandidatesEvaluated, b.Result.CandidatesEvaluated);
                Assert.Equal(a.Result.GoalMet, b.Result.GoalMet);
                outcomes[(int)a.Result.Outcome]++;
            }
            // The random scenarios reach every terminal branch.
            Assert.True(outcomes[(int)GenerationOutcome.Success] > 0);
            Assert.True(outcomes[(int)GenerationOutcome.BelowThreshold] > 0);
            Assert.True(outcomes[(int)GenerationOutcome.NearestWinner] > 0);
        }

        /// <summary>Order in which reports sharing one elapsed time reach the session.</summary>
        enum TieOrder
        {
            Ascending,
            Descending,
            Shuffled,
        }

        /// <summary>
        /// Event-driven parallel driver: <paramref name="workers"/> headless runs at once, candidate k takes a quantized time, a run
        /// still going at its <see cref="GenerationStep.DeadlineMs"/> is aborted and reported at the deadline (spec §2.4 time
        /// budget). Reports sharing one timestamp are delivered in <paramref name="order"/>. Decisions: Search longer once, then
        /// Take nearest.
        /// </summary>
        static GenerationSession RunParallel(GenerationSessionOptions o, ulong scenarioSeed, int workers, TieOrder order, ref Rng shuffle)
        {
            var session = new GenerationSession(o);
            var inFlight = new List<KeyValuePair<int, long>>(); // index, report time
            var aborted = new Dictionary<int, bool>();
            long now = 0;
            for (int guard = 0; guard < 100000; guard++)
            {
                while (inFlight.Count < workers)
                {
                    GenerationStep step = session.NextRequest();
                    if (!step.IsRun) break;
                    long runMs = 100L * Rng.FromSeed(scenarioSeed, 7000UL + (ulong)step.CandidateIndex).NextInt(1, 6);
                    long end = Math.Min(now + runMs, Math.Max(now, step.DeadlineMs));
                    aborted[step.CandidateIndex] = now + runMs > step.DeadlineMs;
                    inFlight.Add(new KeyValuePair<int, long>(step.CandidateIndex, end));
                }
                if (inFlight.Count == 0)
                {
                    GenerationStep step = session.NextRequest();
                    if (step.Kind == GenerationStepKind.Done) return session;
                    Assert.Equal(GenerationStepKind.AwaitDecision, step.Kind);
                    CheckInvariants(session, o);
                    if (session.Result.SearchLongerCount == 0) session.SearchLonger();
                    else session.TakeNearest();
                    continue;
                }
                long t = long.MaxValue;
                foreach (KeyValuePair<int, long> f in inFlight) t = Math.Min(t, f.Value);
                var due = new List<int>();
                for (int i = inFlight.Count - 1; i >= 0; i--)
                {
                    if (inFlight[i].Value != t) continue;
                    due.Add(inFlight[i].Key);
                    inFlight.RemoveAt(i);
                }
                due.Sort();
                if (order == TieOrder.Descending) due.Reverse();
                if (order == TieOrder.Shuffled)
                {
                    for (int i = due.Count - 1; i > 0; i--)
                    {
                        int j = shuffle.NextInt(0, i + 1);
                        int tmp = due[i];
                        due[i] = due[j];
                        due[j] = tmp;
                    }
                }
                now = t;
                foreach (int k in due)
                {
                    CandidateRunSummary summary = aborted[k]
                        ? new CandidateRunSummary(60, false, false, new int[0], 0, 0.0, 0.0, -1, o.ParticipantCount, null, false)
                        : RandomCandidate(scenarioSeed, k, o.ParticipantCount, session.MaxTicks);
                    Assert.True(session.Report(k, summary, now));
                }
            }
            throw new InvalidOperationException("Session did not finish");
        }

        [Fact]
        public void ParallelRunsWithSharedDeadlineTimestampsAreOrderIndependent()
        {
            // QA round 1 #1 / #3: reports at the same elapsed time (normal finishes and aborts at the deadline) arrive in an
            // order set by thread timing; the outcome must not depend on it.
            var outer = Rng.FromSeed(2024UL, 9);
            int deadlineHits = 0;
            for (int n = 0; n < 200; n++)
            {
                ulong sessionSeed = outer.NextULong();
                ulong scenarioSeed = outer.NextULong();
                GenerationSessionOptions o = RandomOptions(ref outer, sessionSeed);
                o.Budget = new GenerationBudget(outer.NextInt(2, 16), 100L * outer.NextInt(2, 10));
                int workers = outer.NextInt(1, 5);
                var shuffle = Rng.FromSeed(outer.NextULong());

                GenerationSession asc = RunParallel(o, scenarioSeed, workers, TieOrder.Ascending, ref shuffle);
                GenerationSession desc = RunParallel(o, scenarioSeed, workers, TieOrder.Descending, ref shuffle);
                GenerationSession mixed = RunParallel(o, scenarioSeed, workers, TieOrder.Shuffled, ref shuffle);
                foreach (GenerationSession other in new[] { desc, mixed })
                {
                    Assert.Equal(asc.Result.Outcome, other.Result.Outcome);
                    Assert.Equal(asc.Result.SelectedIndex, other.Result.SelectedIndex);
                    Assert.Equal(asc.Result.CandidatesEvaluated, other.Result.CandidatesEvaluated);
                    Assert.Equal(asc.Result.BudgetDoubled, other.Result.BudgetDoubled);
                    Assert.Equal(asc.Result.ElapsedMs, other.Result.ElapsedMs);
                    Assert.Equal(asc.Result.SearchLongerCount, other.Result.SearchLongerCount);
                }
                CheckInvariants(mixed, o);
                Assert.Equal(0, mixed.OutstandingCount);
                foreach (CandidateEvaluation e in asc.Evaluations)
                {
                    if (e.Breakdown.Rejected == RejectReason.Incomplete) deadlineHits++;
                }
            }
            Assert.True(deadlineHits > 50); // the time budget, not only the count, ends many phases
        }

        [Fact]
        public void CandidateOrderOfEqualScoresDecidesByIndex()
        {
            // Every candidate identical: the first always wins, whatever the budget.
            for (int candidates = 1; candidates <= 12; candidates++)
            {
                var session = new GenerationSession(Runs.Options(candidates: candidates));
                Runs.Drive(session, k => Runs.Good());
                Assert.Equal(0, session.Result.SelectedIndex);
            }
        }

        public sealed class SessionCase
        {
            public string Name { get; set; }
            public int Candidates { get; set; }
            public long TimeMs { get; set; }
            public double Threshold { get; set; }
            public int? TargetTicks { get; set; }
            public double DensityMin { get; set; }
            public double DensityMax { get; set; }
            public int ForcedWinner { get; set; }
            public int Participants { get; set; }
            public DirectorGoal Goal { get; set; }
            public List<CandidateRunSummary> Summaries { get; set; }
            public GenerationOutcome ExpectedOutcome { get; set; }
            public int ExpectedIndex { get; set; }
            public int ExpectedEvaluated { get; set; }
            public bool ExpectedDoubled { get; set; }
        }

        [Fact]
        public void GoldenCasesFromTestData()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Generation", "session_cases.json"));
            List<SessionCase> cases = CoreJson.Deserialize<List<SessionCase>>(json);
            Assert.Equal(3, cases.Count);
            foreach (SessionCase c in cases)
            {
                var o = new GenerationSessionOptions
                {
                    SessionSeed = 1UL,
                    Budget = new GenerationBudget(c.Candidates, c.TimeMs),
                    QualityThreshold = c.Threshold,
                    TargetDurationTicks = c.TargetTicks,
                    Density = new DensityRange(c.DensityMin, c.DensityMax),
                    ForcedWinner = c.ForcedWinner,
                    ParticipantCount = c.Participants,
                    Goal = c.Goal,
                };
                var session = new GenerationSession(o);
                Runs.Drive(session, k => c.Summaries[k]);
                Assert.True(c.ExpectedOutcome == session.Result.Outcome, c.Name);
                Assert.True(c.ExpectedIndex == session.Result.SelectedIndex, c.Name);
                Assert.True(c.ExpectedEvaluated == session.Result.CandidatesEvaluated, c.Name);
                Assert.True(c.ExpectedDoubled == session.Result.BudgetDoubled, c.Name);
            }
        }
    }
}
