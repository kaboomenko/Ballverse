using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Generation;

namespace Ballverse.Core.Tests.Generation
{
    /// <summary>Test factories for candidate run summaries and a session driver.</summary>
    internal static class Runs
    {
        public const int Target = 30 * SimConstants.TickRate; // 1800

        /// <summary>Events evenly spaced every <paramref name="eventEveryTicks"/> (first at that tick), up to the end.</summary>
        public static int[] EvenEvents(int durationTicks, int eventEveryTicks)
        {
            var list = new List<int>();
            if (eventEveryTicks > 0)
            {
                for (int t = eventEveryTicks; t <= durationTicks; t += eventEveryTicks) list.Add(t);
            }
            return list.ToArray();
        }

        public static CandidateRunSummary Make(
            int durationTicks = Target,
            int eventEveryTicks = 30,
            double coverage = 0.8,
            double tension = 0.8,
            int winner = -1,
            int participants = 0,
            bool finished = true,
            bool endedByLimit = false,
            bool stuck = false,
            int[] leaders = null,
            int[] lastPlace = null,
            int collisions = -1)
        {
            int[] events = EvenEvents(durationTicks, eventEveryTicks);
            return new CandidateRunSummary(durationTicks, finished, endedByLimit, events, collisions >= 0 ? collisions : events.Length,
                coverage, tension, winner, participants, leaders, stuck, lastPlace);
        }

        /// <summary>Default weights, density 1..8: total = 1 + 0.8 + 0.8·tension + 0.5·coverage = 2.84 with defaults.</summary>
        public static CandidateRunSummary Good(double tension = 0.8, int winner = -1, int participants = 0)
        {
            return Make(tension: tension, winner: winner, participants: participants);
        }

        /// <summary>Way off target with long silences: total well below 2.</summary>
        public static CandidateRunSummary Bad(double tension = 0.1, int winner = -1, int participants = 0)
        {
            return Make(durationTicks: 900, eventEveryTicks: 300, coverage: 0.1, tension: tension, winner: winner, participants: participants);
        }

        public static GenerationSessionOptions Options(int candidates = 4, long timeMs = 1000, double threshold = 2.0)
        {
            return new GenerationSessionOptions
            {
                SessionSeed = 12345UL,
                Budget = new GenerationBudget(candidates, timeMs),
                QualityThreshold = threshold,
                TargetDurationTicks = Target,
                Weights = SpectacleWeights.Default,
                Density = new DensityRange(1.0, 8.0),
            };
        }

        /// <summary>Runs every requested candidate sequentially; elapsed defaults to 10 ms per candidate. Returns the first non-run step.</summary>
        public static GenerationStep Drive(GenerationSession session, Func<int, CandidateRunSummary> summaryFor,
            Func<int, long> elapsedAfter = null, int safety = 10000)
        {
            for (int n = 0; n < safety; n++)
            {
                GenerationStep step = session.NextRequest();
                if (!step.IsRun) return step;
                long elapsed = elapsedAfter != null ? elapsedAfter(step.CandidateIndex) : 10L * (step.CandidateIndex + 1);
                session.Report(step.CandidateIndex, summaryFor(step.CandidateIndex), elapsed);
            }
            throw new InvalidOperationException("Session did not stop");
        }
    }
}
