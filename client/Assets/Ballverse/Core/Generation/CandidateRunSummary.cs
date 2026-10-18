using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// What one headless candidate run reports to the generator (spec §2.4 step 3–4). Pure, immutable data:
    /// the Sim layer fills it, <see cref="SpectacleScorer"/> and <see cref="GenerationSession"/> only read it.
    /// Ticks are simulation steps of 1/60 s counted from the start of <c>Running</c> (spec §2.1).
    /// </summary>
    public sealed class CandidateRunSummary
    {
        static readonly int[] s_empty = new int[0];

        readonly int[] _significantEventTicks;
        readonly int[] _leaderBySecond;
        readonly int[] _lastPlaceBySecond;

        /// <summary>Length of the run in ticks, 0..<see cref="SimConstants.MaxDurationTicks"/>.</summary>
        public int DurationTicks { get; }

        /// <summary>The run reached its finale (spec §2.1 <c>Finale</c>).</summary>
        public bool Finished { get; }

        /// <summary>
        /// The run was stopped by the headless limit <see cref="GenerationSession.MaxTicks"/> (target × 1.3, spec §2.4 step 3)
        /// without a finale. When the limit is the global 180 s cap (<see cref="SimConstants.MaxDurationTicks"/>), the mode forces
        /// a <c>Finale</c> "by points" there (spec §2.1); the Sim may report that run either as <see cref="Finished"/> or as
        /// EndedByLimit with <see cref="DurationTicks"/> == MaxDurationTicks, and both count as a finale at 180 s
        /// (<see cref="ReachedFinale"/>).
        /// </summary>
        public bool EndedByLimit { get; }

        /// <summary>
        /// The run has a finale whose time can be scored: <see cref="Finished"/>, or stopped at the global 180 s cap where the mode
        /// forces a finale (spec §2.1).
        /// </summary>
        [JsonIgnore]
        public bool ReachedFinale => Finished || (EndedByLimit && DurationTicks == SimConstants.MaxDurationTicks);

        /// <summary>
        /// Distinct ticks of significant events (eliminations, breaks, gate passes, notable hits…), ascending, each in
        /// 0..DurationTicks; used only for <c>stagnation</c> (spec §2.4). Duplicate ticks are collapsed on construction, so the list
        /// holds at most DurationTicks + 1 entries (≤ 10 801). The Sim should report significant events only, not every contact.
        /// </summary>
        public IReadOnlyList<int> SignificantEventTicks => _significantEventTicks;

        /// <summary>
        /// Number of collisions during the run, the basis of <c>eventDensity</c> ("collisions per second", spec §2.4). Kept as a
        /// count, so it costs no memory however many bodies collide.
        /// </summary>
        public int CollisionCount { get; }

        /// <summary>Fraction of the arena the bodies passed through, 0..1 (spec §2.4 <c>coverage</c>).</summary>
        public double Coverage { get; }

        /// <summary>Mode-provided closeness of the finale, 0..1 (spec §2.4 <c>finishTension</c>).</summary>
        public double FinishTension { get; }

        /// <summary>Winning participant, or -1 when the mode has no winner or nobody won.</summary>
        public int WinnerIndex { get; }

        /// <summary>Number of participants (balls, fighters, teams) the winner index refers to; 0 when not applicable.</summary>
        public int ParticipantCount { get; }

        /// <summary>
        /// Leader sampled once per second: element i is the leader at tick (i + 1) × 60; -1 means no clear leader.
        /// Used for comeback detection (spec §2.5).
        /// </summary>
        public IReadOnlyList<int> LeaderBySecond => _leaderBySecond;

        /// <summary>
        /// Optional last-place participant sampled with the same cadence as <see cref="LeaderBySecond"/>; empty when the mode
        /// does not report it. Sharpens comeback detection (spec §2.5 "winner was last in the first half").
        /// </summary>
        public IReadOnlyList<int> LastPlaceBySecond => _lastPlaceBySecond;

        /// <summary>A body did not move for too long (e.g. 5 s in M03, spec §2.9); such candidates are rejected outright.</summary>
        public bool Stuck { get; }

        /// <summary>Creates a validated summary. Lists are copied (event ticks deduplicated); null lists are treated as empty.</summary>
        /// <exception cref="ArgumentException">Any value is out of range or event ticks are not sorted.</exception>
        [JsonConstructor]
        public CandidateRunSummary(
            int durationTicks,
            bool finished,
            bool endedByLimit,
            IReadOnlyList<int> significantEventTicks,
            int collisionCount,
            double coverage,
            double finishTension,
            int winnerIndex,
            int participantCount,
            IReadOnlyList<int> leaderBySecond,
            bool stuck,
            IReadOnlyList<int> lastPlaceBySecond = null)
        {
            if (durationTicks < 0 || durationTicks > SimConstants.MaxDurationTicks)
                throw new ArgumentOutOfRangeException(nameof(durationTicks), "Duration must be within 0..MaxDurationTicks (spec §2.1)");
            if (finished && endedByLimit)
                throw new ArgumentException("A run either finished or ended by the limit, not both", nameof(endedByLimit));
            if (collisionCount < 0) throw new ArgumentOutOfRangeException(nameof(collisionCount), "Collision count must be non-negative");
            if (!IsUnit(coverage)) throw new ArgumentOutOfRangeException(nameof(coverage), "Coverage must be within 0..1");
            if (!IsUnit(finishTension)) throw new ArgumentOutOfRangeException(nameof(finishTension), "Finish tension must be within 0..1");
            if (participantCount < 0) throw new ArgumentOutOfRangeException(nameof(participantCount));
            if (winnerIndex < -1 || winnerIndex >= participantCount)
                throw new ArgumentOutOfRangeException(nameof(winnerIndex), "Winner must be -1 or a participant index");

            _significantEventTicks = CopyDistinctEvents(significantEventTicks, durationTicks);

            _leaderBySecond = CopyParticipants(leaderBySecond, participantCount, nameof(leaderBySecond));
            _lastPlaceBySecond = CopyParticipants(lastPlaceBySecond, participantCount, nameof(lastPlaceBySecond));

            DurationTicks = durationTicks;
            Finished = finished;
            EndedByLimit = endedByLimit;
            CollisionCount = collisionCount;
            Coverage = coverage;
            FinishTension = finishTension;
            WinnerIndex = winnerIndex;
            ParticipantCount = participantCount;
            Stuck = stuck;
        }

        static bool IsUnit(double v)
        {
            return v >= 0.0 && v <= 1.0;
        }

        static int[] Copy(IReadOnlyList<int> list)
        {
            if (list == null || list.Count == 0) return s_empty;
            var a = new int[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }

        static int[] CopyDistinctEvents(IReadOnlyList<int> list, int durationTicks)
        {
            if (list == null || list.Count == 0) return s_empty;
            var distinct = new List<int>(list.Count);
            int prev = 0;
            for (int i = 0; i < list.Count; i++)
            {
                int t = list[i];
                if (t < prev || t > durationTicks)
                    throw new ArgumentException("Event ticks must be sorted and within 0..durationTicks", "significantEventTicks");
                if (distinct.Count == 0 || t != prev) distinct.Add(t);
                prev = t;
            }
            return distinct.ToArray();
        }

        static int[] CopyParticipants(IReadOnlyList<int> list, int participantCount, string paramName)
        {
            int[] a = Copy(list);
            foreach (int p in a)
            {
                if (p < -1 || p >= participantCount)
                    throw new ArgumentOutOfRangeException(paramName, "Samples must be -1 or a participant index");
            }
            return a;
        }
    }
}
