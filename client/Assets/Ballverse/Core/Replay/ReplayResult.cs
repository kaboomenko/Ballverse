using System;
using System.Collections.Generic;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Outcome of a run (RSLT record, spec §7.4; shown on the result card, spec §2.1): winner, scores and finish time. Immutable.
    /// </summary>
    public sealed class ReplayResult : IEquatable<ReplayResult>
    {
        readonly int[] _scores;

        /// <param name="winnerId">Winner id (participant/team id); empty when there is no winner. Null is treated as empty.</param>
        /// <param name="winnerIndex">Winner participant index, or -1 when there is no winner.</param>
        /// <param name="scores">Per-participant scores (≤ <see cref="BvrFormat.MaxResultScores"/>); null means none.</param>
        /// <param name="finishTick">Tick at which the run finished; must be ≤ durationTicks.</param>
        public ReplayResult(string winnerId, int winnerIndex, IReadOnlyList<int> scores, uint finishTick)
        {
            if (winnerIndex < -1) throw new ArgumentOutOfRangeException(nameof(winnerIndex), "winnerIndex must be >= -1");
            int n = scores == null ? 0 : scores.Count;
            if (n > BvrFormat.MaxResultScores)
                throw new ArgumentException("Result has " + n + " scores, limit is " + BvrFormat.MaxResultScores, nameof(scores));
            string id = winnerId ?? string.Empty;
            BvrEncoding.EncodeString(id, BvrFormat.MaxStringBytes, nameof(winnerId));
            _scores = new int[n];
            for (int i = 0; i < n; i++) _scores[i] = scores[i];
            WinnerId = id;
            WinnerIndex = winnerIndex;
            FinishTick = finishTick;
        }

        public string WinnerId { get; }

        public int WinnerIndex { get; }

        public bool HasWinner => WinnerIndex >= 0 || WinnerId.Length > 0;

        public IReadOnlyList<int> Scores => _scores;

        public uint FinishTick { get; }

        public bool Equals(ReplayResult other)
        {
            if (other == null || other.WinnerIndex != WinnerIndex || other.FinishTick != FinishTick
                || !string.Equals(other.WinnerId, WinnerId, StringComparison.Ordinal) || other._scores.Length != _scores.Length)
                return false;
            for (int i = 0; i < _scores.Length; i++)
            {
                if (_scores[i] != other._scores[i]) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayResult);
        }

        public override int GetHashCode()
        {
            int h = StringComparer.Ordinal.GetHashCode(WinnerId) * 31 + WinnerIndex;
            h = h * 31 + (int)FinishTick;
            for (int i = 0; i < _scores.Length; i++) h = h * 31 + _scores[i];
            return h;
        }
    }
}
