using System;

namespace Ballverse.Core
{
    /// <summary>
    /// Deterministic PCG32 generator (O'Neill, pcg32 / XSH-RR 64/32).
    /// The only randomness source allowed in simulation and generation logic (spec §7.4, rule 2).
    /// Mutable struct: copy it to fork a stream, pass by ref to advance it.
    /// </summary>
    public struct Rng
    {
        const ulong Multiplier = 6364136223846793005UL;

        ulong _state;
        ulong _inc;

        /// <summary>Same seeding as the reference pcg32_srandom_r(initState, initSeq).</summary>
        public Rng(ulong initState, ulong initSeq)
        {
            _state = 0UL;
            _inc = (initSeq << 1) | 1UL;
            NextUInt();
            _state += initState;
            NextUInt();
        }

        /// <summary>
        /// Independent stream for a subsystem (generation, rules, VFX…) derived from one seed.
        /// Both values are mixed with SplitMix64 so neighbouring seeds and stream ids do not correlate.
        /// </summary>
        public static Rng FromSeed(ulong seed, ulong streamId = 0UL)
        {
            ulong s = seed;
            ulong state = SplitMix64(ref s);
            ulong seq = SplitMix64(ref s) ^ (streamId * 0x9E3779B97F4A7C15UL);
            return new Rng(state, seq);
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _inc);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        public ulong NextULong()
        {
            ulong hi = NextUInt();
            ulong lo = NextUInt();
            return (hi << 32) | lo;
        }

        /// <summary>Uniform integer in [0, bound). Unbiased (rejection sampling).</summary>
        public uint NextUInt(uint bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound), "bound must be > 0");
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be > minInclusive");
            uint range = (uint)((long)maxExclusive - minInclusive);
            return (int)(minInclusive + NextUInt(range));
        }

        /// <summary>Uniform float in [0, 1) with 24 bits of precision.</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float NextFloat(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>Uniform double in [0, 1) with 53 bits of precision.</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        public bool NextBool()
        {
            return (NextUInt() & 1u) != 0;
        }

        public static ulong SplitMix64(ref ulong x)
        {
            x = unchecked(x + 0x9E3779B97F4A7C15UL);
            ulong z = x;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }
}
