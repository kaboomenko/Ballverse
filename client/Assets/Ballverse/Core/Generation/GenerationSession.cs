using System;
using System.Collections.Generic;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Pure, iterative planner of one "🎲 Generate" press (spec §2.4 steps 2–5, §2.5). Core has no clocks or threads: the caller
    /// asks <see cref="NextRequest"/> for a candidate, runs it headless with the returned seed, and calls <see cref="Report"/> with
    /// the run summary and the total generation time measured so far. Candidates are processed one at a time, in index order.
    /// <para>
    /// Flow: run candidates until the budget (count or time, whichever first) is exhausted. If a candidate passes the quality
    /// threshold (and matches the forced winner, if any), the best one wins. Otherwise the budget is doubled once (total
    /// 2 × candidates and 2 × time). If still nothing passes: with a forced winner and no candidate with that winner, the session
    /// waits for <see cref="SearchLonger"/> (one more budget) or <see cref="TakeNearest"/>; otherwise the best available candidate
    /// is chosen with <see cref="GenerationResult.QualityBelowThreshold"/> set. Stuck and aborted candidates are never chosen.
    /// </para>
    /// <para>
    /// Ranking: <see cref="CandidateEvaluation.SelectionScore"/> descending, ties broken by the lowest candidate index, so the same
    /// session seed and the same reported summaries always give the same choice.
    /// </para>
    /// </summary>
    public sealed class GenerationSession
    {
        /// <summary>Forced winner multiplies the budget by the participant count, at most ×4 (spec §2.5).</summary>
        public const int MaxForcedWinnerMultiplier = 4;

        /// <summary>Headless runs stop at target × 1.3 (spec §2.4 step 3).</summary>
        public const int RunLimitNumerator = 13;
        public const int RunLimitDenominator = 10;

        /// <summary>finishTension at or above which the "last second" goal counts as met.</summary>
        public const double LastSecondTensionGoal = 0.8;

        readonly GenerationSessionOptions _options;
        readonly GenerationBudget _phaseBudget;
        readonly int _maxTicks;
        readonly List<CandidateEvaluation> _evaluations = new List<CandidateEvaluation>();

        int _countLimit;
        long _timeLimitMs;
        int _outstanding = -1;
        long _lastElapsedMs;
        bool _doubled;
        int _searchLongerCount;
        GenerationStatus _status;
        GenerationResult _result;

        /// <exception cref="ArgumentNullException">options is null.</exception>
        /// <exception cref="ArgumentException">The options are inconsistent (see <see cref="GenerationSessionOptions"/>).</exception>
        public GenerationSession(GenerationSessionOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = options.Clone();
            _options.Validate();

            _phaseBudget = _options.Budget.Scale(BudgetMultiplier(_options.ForcedWinner, _options.ParticipantCount));
            _maxTicks = RunLimitTicks(_options.TargetDurationTicks);
            _countLimit = _phaseBudget.Candidates;
            _timeLimitMs = _phaseBudget.TimeMs;
            _status = GenerationStatus.Running;
        }

        public GenerationStatus Status => _status;

        /// <summary>Null while running; set once the session completes, is cancelled, or waits for the forced-winner decision.</summary>
        public GenerationResult Result => _result;

        /// <summary>Every reported candidate, in index order.</summary>
        public IReadOnlyList<CandidateEvaluation> Evaluations => _evaluations;

        /// <summary>Budget of one phase after the forced-winner multiplier (the doubling and "Search longer" each add one more).</summary>
        public GenerationBudget PhaseBudget => _phaseBudget;

        /// <summary>Current cumulative candidate limit.</summary>
        public int CandidateLimit => _countLimit;

        /// <summary>Current cumulative time limit, in ms of reported generation time.</summary>
        public long TimeLimitMs => _timeLimitMs;

        public bool BudgetDoubled => _doubled;

        /// <summary>Headless run limit for every candidate (spec §2.4 step 3, §2.1).</summary>
        public int MaxTicks => _maxTicks;

        /// <summary>
        /// Budget multiplier for a forced winner: the participant count capped at ×4 (spec §2.5); 1 without a forced winner.
        /// Both the candidate count and the time budget are multiplied, otherwise the extra candidates could never fit in time.
        /// </summary>
        public static int BudgetMultiplier(int forcedWinner, int participantCount)
        {
            if (forcedWinner < 0) return 1;
            return Math.Max(1, Math.Min(participantCount, MaxForcedWinnerMultiplier));
        }

        /// <summary>
        /// Headless run limit: ceil(target × 1.3) capped at 180 s, or 180 s without a target (spec §2.4 step 3, §2.1).
        /// Interactive modes still use the target here to bound the autopilot run, although they are not scored on it.
        /// </summary>
        public static int RunLimitTicks(int? targetDurationTicks)
        {
            if (targetDurationTicks == null) return SimConstants.MaxDurationTicks;
            int target = targetDurationTicks.Value;
            if (target <= 0) throw new ArgumentOutOfRangeException(nameof(targetDurationTicks));
            long limit = ((long)target * RunLimitNumerator + RunLimitDenominator - 1) / RunLimitDenominator;
            return (int)Math.Min(limit, SimConstants.MaxDurationTicks);
        }

        /// <summary>
        /// The next instruction. While a candidate is outstanding (requested but not reported) the same request is returned again.
        /// </summary>
        public GenerationStep NextRequest()
        {
            switch (_status)
            {
                case GenerationStatus.AwaitingDecision:
                    return new GenerationStep(GenerationStepKind.AwaitDecision, -1, 0UL, _maxTicks);
                case GenerationStatus.Completed:
                case GenerationStatus.Cancelled:
                    return new GenerationStep(GenerationStepKind.Done, -1, 0UL, _maxTicks);
            }
            if (_outstanding < 0) _outstanding = _evaluations.Count;
            return new GenerationStep(GenerationStepKind.RunCandidate, _outstanding,
                CandidateSeeds.ForCandidate(_options.SessionSeed, _outstanding), _maxTicks);
        }

        /// <summary>
        /// Reports the outstanding candidate's run. <paramref name="elapsedMsSoFar"/> is the total generation time measured by the
        /// caller since the session started (excluding time spent waiting for the user's decision); it must not decrease.
        /// </summary>
        /// <exception cref="InvalidOperationException">The session is not running or <paramref name="candidateIndex"/> was not requested.</exception>
        /// <exception cref="ArgumentException">summary is null or the elapsed time went backwards.</exception>
        public void Report(int candidateIndex, CandidateRunSummary summary, long elapsedMsSoFar)
        {
            if (_status != GenerationStatus.Running) throw new InvalidOperationException("The session is not running");
            if (_outstanding < 0 || candidateIndex != _outstanding)
                throw new InvalidOperationException("Candidate " + candidateIndex + " was not requested");
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (elapsedMsSoFar < _lastElapsedMs)
                throw new ArgumentOutOfRangeException(nameof(elapsedMsSoFar), "Elapsed time must not decrease");

            _evaluations.Add(Evaluate(candidateIndex, summary));
            _outstanding = -1;
            _lastElapsedMs = elapsedMsSoFar;

            if (_evaluations.Count >= _countLimit || _lastElapsedMs >= _timeLimitMs) EndPhase();
        }

        /// <summary>"Search longer" (spec §2.5): one more phase budget from the current position, then the same evaluation again.</summary>
        /// <exception cref="InvalidOperationException">The session is not waiting for a decision.</exception>
        public void SearchLonger()
        {
            if (_status != GenerationStatus.AwaitingDecision) throw new InvalidOperationException("No decision is pending");
            _searchLongerCount++;
            _countLimit = _evaluations.Count + _phaseBudget.Candidates;
            _timeLimitMs = _lastElapsedMs + _phaseBudget.TimeMs;
            _status = GenerationStatus.Running;
            _result = null;
        }

        /// <summary>
        /// "Take the nearest option" (spec §2.5): the best non-rejected candidate regardless of winner. Completes with
        /// <see cref="GenerationOutcome.NearestWinner"/>, or <see cref="GenerationOutcome.NoViableCandidate"/> if all were rejected.
        /// </summary>
        /// <exception cref="InvalidOperationException">The session is not waiting for a decision.</exception>
        public void TakeNearest()
        {
            if (_status != GenerationStatus.AwaitingDecision) throw new InvalidOperationException("No decision is pending");
            CandidateEvaluation nearest = Best(e => !e.IsRejected);
            Complete(nearest != null ? GenerationOutcome.NearestWinner : GenerationOutcome.NoViableCandidate, nearest);
        }

        /// <summary>The user left the screen (spec §2.4). No-op once completed or already cancelled.</summary>
        public void Cancel()
        {
            if (_status == GenerationStatus.Completed || _status == GenerationStatus.Cancelled) return;
            _outstanding = -1;
            _status = GenerationStatus.Cancelled;
            _result = new GenerationResult(GenerationOutcome.Cancelled, null, -1, _evaluations.Count, _lastElapsedMs, _doubled,
                _searchLongerCount, HasForcedWinner, false);
        }

        bool HasForcedWinner => _options.ForcedWinner >= 0;

        CandidateEvaluation Evaluate(int index, CandidateRunSummary summary)
        {
            // Interactive modes: only the arena is generated, so duration is not scored (spec §2.4).
            int? target = _options.Interactive ? null : _options.TargetDurationTicks;
            SpectacleBreakdown breakdown = SpectacleScorer.Score(summary, target, _options.Weights, _options.Density);
            bool matches = !HasForcedWinner || DirectorAnalysis.MatchesWinner(summary, _options.ForcedWinner);
            bool comeback = DirectorAnalysis.IsComeback(summary);

            double bonus = 0.0;
            if (_options.Goal == DirectorGoal.LastSecond) bonus = _options.GoalBonusWeight * breakdown.FinishTension;
            else if (_options.Goal == DirectorGoal.Comeback && comeback) bonus = _options.GoalBonusWeight;

            bool passes = !breakdown.IsRejected && breakdown.Total >= _options.QualityThreshold;
            return new CandidateEvaluation(index, CandidateSeeds.ForCandidate(_options.SessionSeed, index), summary, breakdown,
                breakdown.Total + bonus, matches, comeback, passes);
        }

        void EndPhase()
        {
            while (true)
            {
                CandidateEvaluation best = Best(e => e.PassesThreshold && e.MatchesWinner);
                if (best != null)
                {
                    Complete(GenerationOutcome.Success, best);
                    return;
                }
                if (!_doubled)
                {
                    // Spec §2.4 step 5: double the budget once. Total becomes 2× candidates and 2× time.
                    _doubled = true;
                    _countLimit += _phaseBudget.Candidates;
                    _timeLimitMs += _phaseBudget.TimeMs;
                    if (_lastElapsedMs < _timeLimitMs) return; // keep running
                    continue; // the time already ran past the doubled budget
                }
                break;
            }

            CandidateEvaluation bestMatching = Best(e => !e.IsRejected && e.MatchesWinner);
            if (bestMatching != null)
            {
                Complete(GenerationOutcome.BelowThreshold, bestMatching);
                return;
            }
            if (HasForcedWinner)
            {
                CandidateEvaluation nearest = Best(e => !e.IsRejected);
                _status = GenerationStatus.AwaitingDecision;
                _result = new GenerationResult(GenerationOutcome.WinnerNotFound, null, nearest?.Index ?? -1, _evaluations.Count,
                    _lastElapsedMs, _doubled, _searchLongerCount, true, false);
                return;
            }
            Complete(GenerationOutcome.NoViableCandidate, null);
        }

        void Complete(GenerationOutcome outcome, CandidateEvaluation selected)
        {
            _status = GenerationStatus.Completed;
            _result = new GenerationResult(outcome, selected, -1, _evaluations.Count, _lastElapsedMs, _doubled, _searchLongerCount,
                HasForcedWinner, IsGoalMet(selected));
        }

        bool IsGoalMet(CandidateEvaluation selected)
        {
            if (selected == null) return false;
            switch (_options.Goal)
            {
                case DirectorGoal.LastSecond: return selected.Breakdown.FinishTension >= LastSecondTensionGoal;
                case DirectorGoal.Comeback: return selected.IsComeback;
                default: return true;
            }
        }

        /// <summary>Highest selection score among candidates matching the filter; ties go to the lowest index.</summary>
        CandidateEvaluation Best(Func<CandidateEvaluation, bool> filter)
        {
            CandidateEvaluation best = null;
            foreach (CandidateEvaluation e in _evaluations)
            {
                if (!filter(e)) continue;
                if (best == null || e.SelectionScore > best.SelectionScore) best = e;
            }
            return best;
        }
    }
}
