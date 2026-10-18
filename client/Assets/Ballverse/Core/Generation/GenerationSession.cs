using System;
using System.Collections.Generic;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Pure, iterative planner of one "🎲 Generate" press (spec §2.4 steps 2–5, §2.5). Core has no clocks or threads: the caller
    /// asks <see cref="NextRequest"/> for candidates, runs them headless with the returned seeds, and calls <see cref="Report"/>
    /// with each run summary and the total generation time measured so far.
    /// <para>
    /// Parallel runs (spec §2.4 step 3, §6.5, §7.11 "headless candidates in separate PhysicsWorlds"): every
    /// <see cref="NextRequest"/> call hands out the next candidate index until the current candidate limit is reached (then it
    /// returns <see cref="GenerationStepKind.Wait"/>), and <see cref="Report"/> accepts any outstanding index in any order.
    /// Reports are buffered and evaluated strictly in index order. A phase ends only when no handed-out run is outstanding: by
    /// count once every candidate of the limit is reported, by time once some report reached the time limit (no new candidates
    /// are handed out from then on) and every run already handed out has reported. The caller bounds that wait by aborting
    /// runs at <see cref="GenerationStep.DeadlineMs"/> and reporting them as aborted. So with the same handed-out candidates and
    /// the same summaries the choice never depends on which worker finished first or on the order of reports at the deadline.
    /// A sequential caller simply alternates NextRequest / Report.
    /// </para>
    /// <para>
    /// Flow: run candidates until the budget (count or time, whichever first) is exhausted. If a candidate passes the quality
    /// threshold (and matches the forced winner, if any), the best one wins. Otherwise the budget is doubled once (total
    /// 2 × candidates and 2 × time) — with a forced winner only when some viable candidate has that winner, so "winner not found"
    /// is offered right after the first ×N phase. If still nothing passes: the best viable candidate with the right winner is chosen
    /// with <see cref="GenerationResult.QualityBelowThreshold"/> set; with a forced winner and no viable match the session waits
    /// for <see cref="SearchLonger"/> (one more phase budget) or <see cref="TakeNearest"/>. Stuck and aborted candidates are never
    /// chosen.
    /// </para>
    /// <para>
    /// Ranking (spec §2.5 director goals): LastSecond — highest finishTension first ("maximize finishTension"), then total;
    /// Comeback — comeback candidates first, then total; None — total. Remaining ties go to the lowest candidate index, so the
    /// same session seed and the same reported summaries always give the same choice.
    /// </para>
    /// <para>Not thread-safe: drive a session from one thread (e.g. the main thread polling the workers).</para>
    /// </summary>
    public sealed class GenerationSession
    {
        /// <summary>Forced winner multiplies the candidate budget by the participant count, at most ×4 (spec §2.5).</summary>
        public const int MaxForcedWinnerMultiplier = 4;

        /// <summary>Headless runs stop at target × 1.3 (spec §2.4 step 3).</summary>
        public const int RunLimitNumerator = 13;
        public const int RunLimitDenominator = 10;

        /// <summary>finishTension at or above which the "last second" goal counts as met.</summary>
        public const double LastSecondTensionGoal = 0.8;

        readonly GenerationSessionOptions _options;
        readonly bool _interactive;
        readonly GenerationBudget _phaseBudget;
        readonly int _maxTicks;
        readonly List<CandidateEvaluation> _evaluations = new List<CandidateEvaluation>();
        readonly Dictionary<int, CandidateRunSummary> _buffered = new Dictionary<int, CandidateRunSummary>();

        int _issued;
        int _countLimit;
        long _timeLimitMs;
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
            _interactive = _options.EffectiveInteractive;

            int multiplier = BudgetMultiplier(_options.ForcedWinner, _options.ParticipantCount);
            // Spec §2.5 multiplies only the candidate budget; the time budget stays the tier's unless explicitly overridden.
            long phaseTime = HasForcedWinner && _options.ForcedWinnerTimeMs.HasValue
                ? _options.ForcedWinnerTimeMs.Value
                : _options.Budget.TimeMs;
            _phaseBudget = new GenerationBudget(checked(_options.Budget.Candidates * multiplier), phaseTime);
            _maxTicks = RunLimitTicks(_options.TargetDurationTicks);
            _countLimit = _phaseBudget.Candidates;
            _timeLimitMs = _phaseBudget.TimeMs;
            _status = GenerationStatus.Running;
        }

        public GenerationStatus Status => _status;

        /// <summary>Null while running; set once the session completes, is cancelled, or waits for the forced-winner decision.</summary>
        public GenerationResult Result => _result;

        /// <summary>Every evaluated candidate, in index order (buffered out-of-order reports appear once their predecessors arrive).</summary>
        public IReadOnlyList<CandidateEvaluation> Evaluations => _evaluations;

        /// <summary>
        /// Budget of one phase: candidates × the forced-winner multiplier (spec §2.5); time = the base budget's, or
        /// <see cref="GenerationSessionOptions.ForcedWinnerTimeMs"/> with a forced winner when set (the doubling and "Search longer"
        /// each add one more phase).
        /// </summary>
        public GenerationBudget PhaseBudget => _phaseBudget;

        /// <summary>Current cumulative candidate limit.</summary>
        public int CandidateLimit => _countLimit;

        /// <summary>Current cumulative time limit, in ms of reported generation time.</summary>
        public long TimeLimitMs => _timeLimitMs;

        public bool BudgetDoubled => _doubled;

        /// <summary>Headless run limit for every candidate (spec §2.4 step 3, §2.1).</summary>
        public int MaxTicks => _maxTicks;

        /// <summary>The session is interactive (option or interactive mode): duration is not scored, no winner or goals (spec §2.4).</summary>
        public bool IsInteractive => _interactive;

        /// <summary>Candidates handed out but not yet evaluated (running, or reported ahead of a lower index).</summary>
        public int OutstandingCount => _issued - _evaluations.Count;

        /// <summary>
        /// Budget multiplier for a forced winner: the participant count capped at ×4 (spec §2.5); 1 without a forced winner.
        /// It multiplies the candidate count only; the time budget is the base one (or <see cref="GenerationSessionOptions.ForcedWinnerTimeMs"/>).
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
        /// The next instruction. While running, each call hands out a new candidate (index order) until the current count limit is
        /// reached or a report reached the time limit; then <see cref="GenerationStepKind.Wait"/> until outstanding runs are reported.
        /// </summary>
        public GenerationStep NextRequest()
        {
            switch (_status)
            {
                case GenerationStatus.AwaitingDecision:
                    return new GenerationStep(GenerationStepKind.AwaitDecision, -1, 0UL, _maxTicks, _timeLimitMs);
                case GenerationStatus.Completed:
                case GenerationStatus.Cancelled:
                    return new GenerationStep(GenerationStepKind.Done, -1, 0UL, _maxTicks, _timeLimitMs);
            }
            if (_issued >= _countLimit || _lastElapsedMs >= _timeLimitMs)
                return new GenerationStep(GenerationStepKind.Wait, -1, 0UL, _maxTicks, _timeLimitMs);
            int index = _issued++;
            return new GenerationStep(GenerationStepKind.RunCandidate, index, CandidateSeeds.ForCandidate(_options.SessionSeed, index),
                _maxTicks, _timeLimitMs);
        }

        /// <summary>
        /// Reports an outstanding candidate's run, in any order. <paramref name="elapsedMsSoFar"/> is the total generation time
        /// measured by the caller since the session started (excluding time spent waiting for the user's decision); it must not
        /// decrease between calls. Returns false, ignoring the report, when the session is no longer running (completed, cancelled
        /// or awaiting the decision). A phase only ends once every handed-out run has reported, so runs are abandoned only by
        /// <see cref="Cancel"/>; "Search longer" never re-issues an index.
        /// <para>
        /// Every exception is thrown before any state changes: the report is not recorded and the candidate stays outstanding,
        /// which blocks the phase from ending. After an <see cref="ArgumentException"/> (a Sim bug) the caller must report the
        /// same index again with a valid summary, e.g. an aborted one (<c>finished = false</c>, <c>endedByLimit = false</c>,
        /// rejected as <see cref="RejectReason.Incomplete"/>), or <see cref="Cancel"/> the session.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">The candidate was not handed out or was already reported.</exception>
        /// <exception cref="ArgumentNullException">summary is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The elapsed time went backwards.</exception>
        /// <exception cref="ArgumentException">
        /// The summary contradicts the session: longer than <see cref="MaxTicks"/>, or a participant count different from
        /// <see cref="GenerationSessionOptions.ParticipantCount"/> (when that is set).
        /// </exception>
        public bool Report(int candidateIndex, CandidateRunSummary summary, long elapsedMsSoFar)
        {
            if (_status != GenerationStatus.Running) return false;
            if (candidateIndex < 0 || candidateIndex >= _issued)
                throw new InvalidOperationException("Candidate " + candidateIndex + " was not requested");
            if (candidateIndex < _evaluations.Count || _buffered.ContainsKey(candidateIndex))
                throw new InvalidOperationException("Candidate " + candidateIndex + " was already reported");
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (elapsedMsSoFar < _lastElapsedMs)
                throw new ArgumentOutOfRangeException(nameof(elapsedMsSoFar), "Elapsed time must not decrease");
            if (summary.DurationTicks > _maxTicks)
                throw new ArgumentException("The run is longer than the headless limit " + _maxTicks + " ticks", nameof(summary));
            if (_options.ParticipantCount > 0 && summary.ParticipantCount != _options.ParticipantCount)
                throw new ArgumentException("The run has " + summary.ParticipantCount + " participants, the session "
                    + _options.ParticipantCount, nameof(summary));

            _buffered.Add(candidateIndex, summary);
            _lastElapsedMs = elapsedMsSoFar;
            while (_buffered.TryGetValue(_evaluations.Count, out CandidateRunSummary next))
            {
                _buffered.Remove(_evaluations.Count);
                _evaluations.Add(Evaluate(_evaluations.Count, next));
            }

            // Count limit: every candidate of the limit is evaluated (hence none is outstanding). Time limit: wait until every
            // handed-out run has reported, so the phase ranks all of them whatever the report order at the deadline.
            if (OutstandingCount == 0 && (_evaluations.Count >= _countLimit || _lastElapsedMs >= _timeLimitMs)) EndPhase();
            return true;
        }

        /// <summary>
        /// "Search longer" (spec §2.5): one more phase budget from the current position (new candidate indices), then the same
        /// evaluation again.
        /// </summary>
        /// <exception cref="InvalidOperationException">The session is not waiting for a decision.</exception>
        public void SearchLonger()
        {
            if (_status != GenerationStatus.AwaitingDecision) throw new InvalidOperationException("No decision is pending");
            _searchLongerCount++;
            _countLimit = SaturatingAdd(_evaluations.Count, _phaseBudget.Candidates);
            _timeLimitMs = SaturatingAdd(_lastElapsedMs, _phaseBudget.TimeMs);
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

        /// <summary>
        /// The user left the screen (spec §2.4). No-op once completed or already cancelled. Runs still in flight are abandoned;
        /// their late <see cref="Report"/> calls return false.
        /// </summary>
        public void Cancel()
        {
            if (_status == GenerationStatus.Completed || _status == GenerationStatus.Cancelled) return;
            _status = GenerationStatus.Cancelled;
            _issued = _evaluations.Count;
            _buffered.Clear();
            _result = new GenerationResult(GenerationOutcome.Cancelled, null, -1, _evaluations.Count, _lastElapsedMs, _doubled,
                _searchLongerCount, HasForcedWinner, false);
        }

        bool HasForcedWinner => _options.ForcedWinner >= 0;

        /// <summary>a + b for non-negative operands, saturating at <see cref="int.MaxValue"/> (limits never wrap negative).</summary>
        static int SaturatingAdd(int a, int b)
        {
            long sum = (long)a + b;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>a + b for non-negative operands, saturating at <see cref="long.MaxValue"/>.</summary>
        static long SaturatingAdd(long a, long b)
        {
            return a > long.MaxValue - b ? long.MaxValue : a + b;
        }

        CandidateEvaluation Evaluate(int index, CandidateRunSummary summary)
        {
            // Interactive modes: only the arena is generated, so duration is not scored (spec §2.4).
            int? target = _interactive ? null : _options.TargetDurationTicks;
            SpectacleBreakdown breakdown = SpectacleScorer.Score(summary, target, _options.Weights, _options.Density);
            bool matches = !HasForcedWinner || DirectorAnalysis.MatchesWinner(summary, _options.ForcedWinner);
            bool comeback = DirectorAnalysis.IsComeback(summary);
            bool goalMet;
            switch (_options.Goal)
            {
                case DirectorGoal.LastSecond: goalMet = breakdown.FinishTension >= LastSecondTensionGoal; break;
                case DirectorGoal.Comeback: goalMet = comeback; break;
                default: goalMet = true; break;
            }
            bool passes = !breakdown.IsRejected && breakdown.Total >= _options.QualityThreshold;
            return new CandidateEvaluation(index, CandidateSeeds.ForCandidate(_options.SessionSeed, index), summary, breakdown,
                matches, comeback, goalMet, passes);
        }

        /// <summary>Ends a phase; only called when no handed-out run is outstanding, so every reported candidate is ranked.</summary>
        void EndPhase()
        {
            CandidateEvaluation bestMatching;
            while (true)
            {
                CandidateEvaluation best = Best(e => e.PassesThreshold && e.MatchesWinner);
                if (best != null)
                {
                    Complete(GenerationOutcome.Success, best);
                    return;
                }
                bestMatching = Best(e => !e.IsRejected && e.MatchesWinner);
                // Spec §2.4 step 5: double the budget once. Total becomes 2× candidates and 2× time. With a forced winner and no
                // viable candidate with that winner, the "winner not found" choice (spec §2.5) is offered instead.
                if (!_doubled && (!HasForcedWinner || bestMatching != null))
                {
                    _doubled = true;
                    _countLimit = SaturatingAdd(_countLimit, _phaseBudget.Candidates);
                    _timeLimitMs = SaturatingAdd(_timeLimitMs, _phaseBudget.TimeMs);
                    if (_lastElapsedMs < _timeLimitMs) return; // keep running
                    continue; // the time already ran past the doubled budget
                }
                break;
            }

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
                HasForcedWinner, selected != null && selected.GoalMet);
        }

        /// <summary>Best candidate matching the filter by the goal-aware ranking; ties go to the lowest index.</summary>
        CandidateEvaluation Best(Func<CandidateEvaluation, bool> filter)
        {
            CandidateEvaluation best = null;
            foreach (CandidateEvaluation e in _evaluations)
            {
                if (!filter(e)) continue;
                if (best == null || Ranks(e, best) > 0) best = e;
            }
            return best;
        }

        /// <summary>Positive when <paramref name="a"/> ranks above <paramref name="b"/> (ignoring the index).</summary>
        int Ranks(CandidateEvaluation a, CandidateEvaluation b)
        {
            int c = 0;
            if (_options.Goal == DirectorGoal.LastSecond) c = a.Breakdown.FinishTension.CompareTo(b.Breakdown.FinishTension);
            else if (_options.Goal == DirectorGoal.Comeback) c = a.IsComeback.CompareTo(b.IsComeback);
            if (c != 0) return c;
            return a.Breakdown.Total.CompareTo(b.Breakdown.Total);
        }
    }
}
