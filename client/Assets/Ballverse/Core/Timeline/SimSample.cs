using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>What the simulation slot shows on one output frame (spec §3.4, §7.5 step a).</summary>
    public enum SimSampleKind
    {
        /// <summary>Draw <see cref="SimSample.Tick"/>; the simulation is in motion.</summary>
        Playing = 0,
        /// <summary>Draw <see cref="SimSample.Tick"/> as a still frame.</summary>
        Frozen = 1,
        /// <summary>
        /// Nothing to draw. Reserved: <see cref="StudioTimeline"/> never returns it today. Every <see cref="SimAfterEnd"/>
        /// shows a frame, and before the clip starts (video offset &lt; 0) the clip's first tick is shown frozen.
        /// </summary>
        Hidden = 2,
    }

    /// <summary>Result of <see cref="StudioTimeline.SampleSim"/>.</summary>
    public readonly struct SimSample : IEquatable<SimSample>
    {
        public SimSampleKind Kind { get; }
        /// <summary>Render-stream tick to draw (undefined for <see cref="SimSampleKind.Hidden"/>).</summary>
        public int Tick { get; }
        /// <summary>
        /// 0 for the first pass through the clip (including <see cref="SimAfterEnd.ContinueSim"/> and frozen frames);
        /// k ≥ 1 for the k-th repetition by <see cref="SimAfterEnd.Loop"/> or <see cref="SimAfterEnd.SlowMoReplay"/>.
        /// Ticks never decrease within one iteration.
        /// </summary>
        public int Iteration { get; }

        public SimSample(SimSampleKind kind, int tick, int iteration)
        {
            Kind = kind;
            Tick = tick;
            Iteration = iteration;
        }

        public static SimSample Playing(int tick, int iteration = 0)
        {
            return new SimSample(SimSampleKind.Playing, tick, iteration);
        }

        public static SimSample Frozen(int tick)
        {
            return new SimSample(SimSampleKind.Frozen, tick, 0);
        }

        public bool Equals(SimSample other)
        {
            return Kind == other.Kind && Tick == other.Tick && Iteration == other.Iteration;
        }

        public override bool Equals(object obj)
        {
            return obj is SimSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397 ^ Tick) * 397 ^ Iteration;
            }
        }

        public static bool operator ==(SimSample a, SimSample b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SimSample a, SimSample b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return Kind + "(" + Tick + ", it " + Iteration + ")";
        }
    }
}
