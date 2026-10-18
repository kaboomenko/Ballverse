using System;
using System.Collections.Generic;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Compares the HASH records of a replay with hashes recomputed by re-simulating it, e.g. when a render stream is regenerated
    /// from the replay (spec §7.4 "восстанавливать его из реплея пересчётом с проверкой хэшей", §8.5 determinism test).
    /// <para>
    /// Incremental: <see cref="Feed"/> each recomputed (tick, hash) in strictly increasing tick order; ticks with no recorded hash
    /// are ignored (the re-simulation may hash every tick). A recorded tick passed over without a recomputed hash is a
    /// <see cref="HashMismatchKind.Missing"/> mismatch. The first mismatch is sticky so the caller can abort early.
    /// </para>
    /// </summary>
    public sealed class HashVerifier
    {
        readonly ReplayStateHash[] _recorded;
        int _index;
        int _verified;
        bool _hasFed;
        uint _lastFed;
        HashVerificationResult? _failure;

        /// <param name="recorded">Recorded hashes in non-decreasing tick order (as read from the replay).</param>
        public HashVerifier(IReadOnlyList<ReplayStateHash> recorded)
        {
            if (recorded == null) throw new ArgumentNullException(nameof(recorded));
            _recorded = new ReplayStateHash[recorded.Count];
            for (int i = 0; i < _recorded.Length; i++)
            {
                _recorded[i] = recorded[i];
                if (i > 0 && _recorded[i].Tick < _recorded[i - 1].Tick)
                    throw new ArgumentException("Recorded hashes must be in non-decreasing tick order", nameof(recorded));
            }
        }

        /// <summary>False once a mismatch has been found.</summary>
        public bool IsOk => _failure == null;

        /// <summary>
        /// Feeds one recomputed hash. Returns <see cref="IsOk"/>. Throws ArgumentException if ticks are not strictly increasing.
        /// </summary>
        public bool Feed(uint tick, ulong hash)
        {
            if (_hasFed && tick <= _lastFed)
                throw new ArgumentException("Recomputed ticks must be strictly increasing: " + tick + " after " + _lastFed, nameof(tick));
            _hasFed = true;
            _lastFed = tick;
            if (_failure != null) return false;

            if (_index < _recorded.Length && _recorded[_index].Tick < tick)
            {
                Fail(HashMismatchKind.Missing, _recorded[_index], 0UL);
                return false;
            }
            while (_index < _recorded.Length && _recorded[_index].Tick == tick)
            {
                if (_recorded[_index].Hash != hash)
                {
                    Fail(HashMismatchKind.HashDiffers, _recorded[_index], hash);
                    return false;
                }
                _index++;
                _verified++;
            }
            return true;
        }

        /// <summary>Feeds one recomputed hash.</summary>
        public bool Feed(ReplayStateHash recomputed)
        {
            return Feed(recomputed.Tick, recomputed.Hash);
        }

        /// <summary>Final result; every recorded hash must have been covered.</summary>
        public HashVerificationResult Finish()
        {
            return Finish(uint.MaxValue);
        }

        /// <summary>
        /// Final result for a re-simulation that stopped at <paramref name="lastSimulatedTick"/> (e.g. only up to the clip's trim-out):
        /// recorded hashes after that tick are not required.
        /// </summary>
        public HashVerificationResult Finish(uint lastSimulatedTick)
        {
            if (_failure != null) return _failure.Value;
            if (_index < _recorded.Length && _recorded[_index].Tick <= lastSimulatedTick)
            {
                Fail(HashMismatchKind.Missing, _recorded[_index], 0UL);
                return _failure.Value;
            }
            return new HashVerificationResult(HashMismatchKind.None, 0, 0, 0, _verified);
        }

        /// <summary>One-shot comparison of recorded vs recomputed (tick, hash) pairs; all recorded hashes must be covered.</summary>
        public static HashVerificationResult Verify(IReadOnlyList<ReplayStateHash> recorded, IEnumerable<ReplayStateHash> recomputed)
        {
            return Verify(recorded, recomputed, uint.MaxValue);
        }

        /// <summary>One-shot comparison for a re-simulation that stopped at <paramref name="lastSimulatedTick"/>.</summary>
        public static HashVerificationResult Verify(IReadOnlyList<ReplayStateHash> recorded, IEnumerable<ReplayStateHash> recomputed,
            uint lastSimulatedTick)
        {
            if (recomputed == null) throw new ArgumentNullException(nameof(recomputed));
            var v = new HashVerifier(recorded);
            foreach (var h in recomputed)
            {
                if (!v.Feed(h)) break;
            }
            return v.Finish(lastSimulatedTick);
        }

        void Fail(HashMismatchKind kind, ReplayStateHash expected, ulong actual)
        {
            _failure = new HashVerificationResult(kind, expected.Tick, expected.Hash, actual, _verified);
        }
    }
}
