using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Control hash of the simulation state at a tick (HASH record, spec §7.4): XXH64 of quantized positions and velocities,
    /// recorded every <see cref="SimConstants.HashIntervalTicks"/> ticks (see <see cref="StateHashBuilder"/>).
    /// </summary>
    public readonly struct ReplayStateHash : IEquatable<ReplayStateHash>
    {
        public ReplayStateHash(uint tick, ulong hash)
        {
            Tick = tick;
            Hash = hash;
        }

        public uint Tick { get; }

        public ulong Hash { get; }

        public bool Equals(ReplayStateHash other)
        {
            return Tick == other.Tick && Hash == other.Hash;
        }

        public override bool Equals(object obj)
        {
            return obj is ReplayStateHash other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (int)Tick * 31 + Hash.GetHashCode();
        }

        public override string ToString()
        {
            return "Hash@" + Tick + " " + Hash.ToString("X16");
        }
    }
}
