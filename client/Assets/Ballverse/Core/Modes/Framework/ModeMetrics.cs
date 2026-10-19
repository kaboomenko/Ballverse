using System;
using Ballverse.Core.Generation;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Allocation-free collector of the generation metrics of one run (spec §2.4 step 4 table): collisions for eventDensity,
    /// significant-event ticks for stagnation, leader / last place once per second for the director (spec §2.5). Scenes record
    /// into it while running and build their <see cref="CandidateRunSummary"/> with <see cref="ToSummary"/>. All storage is
    /// allocated once in the constructor (≈ 43 KB of event ticks plus two per-second arrays).
    /// </summary>
    public sealed class ModeMetrics
    {
        readonly int[] _significant = new int[SimConstants.MaxDurationTicks + 1];
        readonly int[] _leader = new int[SimConstants.MaxDurationSeconds];
        readonly int[] _last = new int[SimConstants.MaxDurationSeconds];
        int _significantCount;
        int _leaderCount;
        int _lastCount;

        public ModeMetrics(int participantCount)
        {
            if (participantCount < 0) throw new ArgumentOutOfRangeException(nameof(participantCount));
            ParticipantCount = participantCount;
        }

        public int ParticipantCount { get; }

        /// <summary>Collisions counted for eventDensity ("столкновения в секунду"; the card says which events count).</summary>
        public int CollisionCount { get; private set; }

        public int SignificantCount => _significantCount;

        /// <summary>Last recorded significant-event tick, −1 when none.</summary>
        public int LastSignificantTick => _significantCount == 0 ? -1 : _significant[_significantCount - 1];

        public void AddCollisions(int count = 1)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            long sum = (long)CollisionCount + count;
            CollisionCount = sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        /// <summary>Records a significant event (card list: breaks, eliminations, near misses…) at a tick ≥ every earlier one; duplicates collapse.</summary>
        public void AddSignificant(int tick)
        {
            if (tick < 0 || tick > SimConstants.MaxDurationTicks) throw new ArgumentOutOfRangeException(nameof(tick));
            if (_significantCount > 0)
            {
                int last = _significant[_significantCount - 1];
                if (tick == last) return;
                if (tick < last) throw new ArgumentException("Significant events must be recorded in tick order", nameof(tick));
            }
            _significant[_significantCount++] = tick;
        }

        /// <summary>
        /// Samples the leader (and optionally last place) of second <c>tick / 60</c>; call at ticks that are multiples of 60
        /// (others are ignored). −1 = no clear leader. Ignored when the run has no participants.
        /// </summary>
        public void SampleStandings(int tick, int leader, int lastPlace = -1)
        {
            if (ParticipantCount == 0 || tick <= 0 || tick % SimConstants.TickRate != 0) return;
            int second = tick / SimConstants.TickRate;
            if (second > _leader.Length || second != _leaderCount + 1) return;
            _leader[_leaderCount++] = Clamp(leader);
            _last[_lastCount++] = Clamp(lastPlace);
        }

        /// <summary>
        /// Builds the summary of a run stopped by the generation driver. Duration = finale tick when reached, else the current
        /// tick. Events after the duration are dropped. <paramref name="rejected"/> marks a mode-level rejection of the card
        /// (reported as <see cref="CandidateRunSummary.Stuck"/>, which the session never selects).
        /// </summary>
        public CandidateRunSummary ToSummary(SimRunner runner, bool endedByLimit, double coverage, double finishTension, int winner, bool rejected)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            bool finished = runner.FinaleTick >= 0;
            int duration = finished ? runner.FinaleTick : runner.Tick;
            if (duration > SimConstants.MaxDurationTicks) duration = SimConstants.MaxDurationTicks;
            int n = 0;
            while (n < _significantCount && _significant[n] <= duration) n++;
            var events = new int[n];
            Array.Copy(_significant, events, n);
            int seconds = Math.Min(_leaderCount, duration / SimConstants.TickRate);
            var leaders = new int[seconds];
            Array.Copy(_leader, leaders, seconds);
            var lasts = new int[Math.Min(_lastCount, seconds)];
            Array.Copy(_last, lasts, lasts.Length);
            int w = winner >= 0 && winner < ParticipantCount ? winner : -1;
            return new CandidateRunSummary(duration, finished, !finished && endedByLimit, events, CollisionCount,
                Unit(coverage), Unit(finishTension), w, ParticipantCount, leaders, rejected, lasts);
        }

        int Clamp(int p)
        {
            return p >= 0 && p < ParticipantCount ? p : -1;
        }

        static double Unit(double v)
        {
            if (double.IsNaN(v) || v < 0.0) return 0.0;
            return v > 1.0 ? 1.0 : v;
        }
    }
}
