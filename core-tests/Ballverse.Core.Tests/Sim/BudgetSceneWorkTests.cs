using System;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Machine-independent regression guard of the release-1 budget scenes' step cost: the work the solver does
    /// (<see cref="PhysicsWorld.WorkCounters"/>: substeps, wall bounds tests and narrow-phase candidates, swept wall tests,
    /// sensor bounds / shape / end-of-tick overlap tests, radial index entries visited, ball pairs gathered and solved) is
    /// counted over the very ticks <see cref="BudgetScenePerformanceTests"/> times. The simulation is deterministic, so the
    /// counts are the same on every machine, under any load and in Debug and Release; no wall clock is involved.
    /// <para>
    /// Each count must stay at most 1.25× its reference (and at least 0.25×, so a scene cannot silently stop exercising
    /// the solver), so anything that doubles the work of a budget scene — a broadphase that stops culling, a wider ball-pair
    /// search, more substeps or ball iterations, a scene that changes shape — fails deterministically on any runner. (QA's
    /// check: the Ring Escape scene at 16 instead of 8 substeps doubles every per-substep count.) A slowdown of the work
    /// itself (a costlier narrow phase per candidate) is not visible here; the timing tests are the lenient backstop for that.
    /// A solver change that legitimately changes trajectories moves the counts a little (they are dominated by balls ×
    /// substeps); update the references when one moves by design.
    /// </para>
    /// </summary>
    public class BudgetSceneWorkTests
    {
        /// <summary>Steps <paramref name="warm"/> ticks, then counts the work of the next <paramref name="window"/> ticks.</summary>
        static SimWorkCounters Run(PhysicsWorld w, Action<PhysicsWorld, int> input, int warm, int window)
        {
            int tick = 0;
            for (; tick < warm; tick++)
            {
                input?.Invoke(w, tick);
                w.Step();
            }
            w.ResetWorkCounters();
            for (int t = 0; t < window; t++, tick++)
            {
                input?.Invoke(w, tick);
                w.Step();
            }
            SimWorkCounters c = w.WorkCounters;
            Assert.Equal(window, c.Ticks);
            return c;
        }

        static PhysicsWorld Build(string scene)
        {
            switch (scene)
            {
                case "M01-100": return BudgetScenePerformanceTests.RingEscape(100);
                case "M01-12": return BudgetScenePerformanceTests.RingEscape(12);
                case "M02-A8": return BudgetScenePerformanceTests.GeometryBounce(200);
                case "M02-A6": return BudgetScenePerformanceTests.GeometryBounce(200, substeps: 6);
                case "M02-B": return BudgetScenePerformanceTests.GeometryBounce(120);
                case "M02-C": return BudgetScenePerformanceTests.GeometryBounce(60);
                case "M02-A-spread": return BudgetScenePerformanceTests.GeometryBounce(200, diameterSpread: 1f);
                case "M05": return BudgetScenePerformanceTests.HelixDrop();
                default: throw new ArgumentException(scene);
            }
        }

        static Action<PhysicsWorld, int> Input(string scene)
        {
            return scene == "M05" ? BudgetScenePerformanceTests.HelixDropInput : (Action<PhysicsWorld, int>)null;
        }

        static void AssertCount(string scene, string what, long actual, long reference)
        {
            if (reference == 0)
            {
                Assert.True(actual == 0, scene + ": " + what + " " + actual + " (reference 0)");
                return;
            }
            double ratio = (double)actual / reference;
            Assert.True(ratio <= 1.25, scene + ": " + what + " " + actual + " is " + ratio.ToString("F3") + "× the reference " + reference + " (limit 1.25×)");
            Assert.True(ratio >= 0.25, scene + ": " + what + " " + actual + " is only " + ratio.ToString("F3") + "× the reference " + reference
                + " (the scene no longer exercises the solver as it did; update the reference if this is by design)");
        }

        /// <summary>
        /// Budget scenes over the timed window (120 warm-up ticks, 100 counted), and Ring Escape over its first 1 200 ticks
        /// (balls spread across the rings). References: the counts at the time of writing (identical in Debug and Release).
        /// </summary>
        [Theory]
        //          scene          warm  window substeps wallBounds wallCand  sweeps sensBounds sensShape ovChecks ovTests visits  pairCand pairSolves
        [InlineData("M01-100",      120,  100,     800,     17055,   17050,   17233,    5703,     5686,  200000,    710,  23091,   17803,    35606)]
        [InlineData("M01-100",        0, 1200,    9600,    301555,  301537,  302584,  134604,   127704, 2400000,  16801, 439341,  151733,   303466)]
        [InlineData("M01-12",         0, 1200,    9600,    306382,  306367,  307476,  135180,   130348,  288000,  16881, 444834,  155583,   311166)]
        [InlineData("M02-A8",       120,  100,     800,    160000,  160000,  160837,       0,        0,       0,      0,      0,  582197,  1164394)]
        [InlineData("M02-A6",       120,  100,     600,    120000,  120000,  120767,       0,        0,       0,      0,      0,  436895,   873790)]
        [InlineData("M02-B",        120,  100,     800,     96000,   96000,   96448,       0,        0,       0,      0,      0,  316599,   633198)]
        [InlineData("M02-C",        120,  100,     800,     48000,   48000,   48432,       0,        0,       0,      0,      0,  133061,   266122)]
        [InlineData("M02-A-spread", 120,  100,     800,    160000,  160000,  160682,       0,        0,       0,      0,      0, 1209727,  2419454)]
        [InlineData("M05",          120,  100,     800,      4000,     800,    1600,       0,        0,       0,      0,   4800,       0,        0)]
        public void BudgetSceneWorkStaysWithinItsReference(string scene, int warm, int window, long substeps, long wallBounds, long wallCandidates,
            long sweeps, long sensorBounds, long sensorShape, long overlapChecks, long overlapTests, long visits, long pairCandidates, long pairSolves)
        {
            SimWorkCounters c = Run(Build(scene), Input(scene), warm, window);
            string name = scene + " ticks " + warm + "–" + (warm + window);
            Assert.True(c.Substeps == substeps, name + ": " + c.Substeps + " substeps, expected " + substeps);
            AssertCount(name, "wall bounds tests", c.WallBoundsTests, wallBounds);
            AssertCount(name, "wall candidates", c.WallCandidates, wallCandidates);
            AssertCount(name, "swept wall tests", c.WallSweeps, sweeps);
            AssertCount(name, "sensor bounds tests", c.SensorBoundsTests, sensorBounds);
            AssertCount(name, "sensor shape tests", c.SensorShapeTests, sensorShape);
            AssertCount(name, "end-of-tick sensor pre-checks", c.SensorOverlapChecks, overlapChecks);
            AssertCount(name, "end-of-tick sensor overlap tests", c.SensorOverlapTests, overlapTests);
            AssertCount(name, "radial index visits", c.RadialIndexVisits, visits);
            AssertCount(name, "ball pair candidates", c.BallPairCandidates, pairCandidates);
            AssertCount(name, "ball pair solves", c.BallPairSolves, pairSolves);
        }

        /// <summary>
        /// The broadphase against the reference exhaustive scans (<see cref="PhysicsWorld.ExhaustiveBroadphase"/>) on the same
        /// ticks: the narrow-phase work must be identical (the culling only removes tests that cannot produce a contact or a
        /// sensor event, so the candidate sets are the same), and the bounds tests must shrink by at least
        /// <paramref name="minCulling"/>× (measured: 64× walls / 143× sensors at 100 rings, 7.6× / 17× at 12 rings, 11.8×
        /// for the Helix Drop tower).
        /// </summary>
        [Theory]
        [InlineData("M01-100", 600, 20.0)]
        [InlineData("M01-12", 600, 4.0)]
        [InlineData("M05", 220, 5.0)]
        public void BroadphaseCullsBoundsTestsAndKeepsTheNarrowPhaseWork(string scene, int ticks, double minCulling)
        {
            SimWorkCounters fast = Run(Build(scene), Input(scene), 0, ticks);
            PhysicsWorld slowWorld = Build(scene);
            slowWorld.ExhaustiveBroadphase = true;
            SimWorkCounters slow = Run(slowWorld, Input(scene), 0, ticks);

            Assert.Equal(slow.Substeps, fast.Substeps);
            Assert.Equal(slow.WallCandidates, fast.WallCandidates);
            Assert.Equal(slow.WallSweeps, fast.WallSweeps);
            Assert.Equal(slow.SensorShapeTests, fast.SensorShapeTests);
            Assert.Equal(slow.BallPairCandidates, fast.BallPairCandidates);
            Assert.Equal(slow.BallPairSolves, fast.BallPairSolves);
            Assert.Equal(0, slow.RadialIndexVisits);
            Assert.Equal(0, slow.SensorOverlapChecks);

            double wallCulling = (double)slow.WallBoundsTests / Math.Max(1, fast.WallBoundsTests);
            Assert.True(wallCulling >= minCulling, scene + ": wall bounds tests " + fast.WallBoundsTests + " vs " + slow.WallBoundsTests + " exhaustive (" + wallCulling.ToString("F1") + "×, need " + minCulling + "×)");
            if (slow.SensorBoundsTests > 0)
            {
                double sensorCulling = (double)slow.SensorBoundsTests / Math.Max(1, fast.SensorBoundsTests);
                Assert.True(sensorCulling >= minCulling, scene + ": sensor bounds tests " + fast.SensorBoundsTests + " vs " + slow.SensorBoundsTests + " exhaustive (" + sensorCulling.ToString("F1") + "×)");
                double overlapCulling = (double)slow.SensorOverlapTests / Math.Max(1, fast.SensorOverlapTests);
                Assert.True(overlapCulling >= minCulling, scene + ": end-of-tick sensor overlap tests " + fast.SensorOverlapTests + " vs " + slow.SensorOverlapTests + " exhaustive (" + overlapCulling.ToString("F1") + "×)");
            }
        }

        /// <summary>
        /// Ball pairs gathered by the grid stay local: per substep, at most 10 candidate partners per ball in the dense
        /// Geometry Bounce budget scenes (measured 2.8 for tier C, 3.6 for tier A, 7.6 with the full size spread, against (n − 1) / 2 = 99.5 for an all-pairs scan of 200 balls).
        /// </summary>
        [Theory]
        [InlineData("M02-A8", 200)]
        [InlineData("M02-C", 60)]
        [InlineData("M02-A-spread", 200)]
        public void BallPairSearchStaysLocal(string scene, int balls)
        {
            SimWorkCounters c = Run(Build(scene), null, 120, 100);
            double perBall = (double)c.BallPairCandidates / c.Substeps / balls;
            Assert.True(perBall <= 10.0, scene + ": " + perBall.ToString("F2") + " pair candidates per ball per substep");
        }
    }
}
