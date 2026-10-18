using System;
using System.Collections.Generic;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// One input event of an interactive mode (INPT record, spec §2.6, §7.4): tick, mode-defined type and a small list
    /// of quantized ints (see <see cref="ReplayQuantization"/>). Immutable; the values are copied.
    /// </summary>
    public sealed class ReplayInput : IEquatable<ReplayInput>
    {
        readonly int[] _values;

        /// <param name="tick">Tick at whose boundary the input is applied (spec §7.4 rule 6).</param>
        /// <param name="type">Mode-defined input type, e.g. tap, swipe, gravity, shot.</param>
        /// <param name="values">Quantized payload, at most <see cref="BvrFormat.MaxInputValues"/> values; null means empty.</param>
        public ReplayInput(uint tick, byte type, IReadOnlyList<int> values)
        {
            int n = values == null ? 0 : values.Count;
            if (n > BvrFormat.MaxInputValues)
                throw new ArgumentException("Input payload has " + n + " values, limit is " + BvrFormat.MaxInputValues, nameof(values));
            _values = new int[n];
            for (int i = 0; i < n; i++) _values[i] = values[i];
            Tick = tick;
            Type = type;
        }

        public uint Tick { get; }

        public byte Type { get; }

        public IReadOnlyList<int> Values => _values;

        public bool Equals(ReplayInput other)
        {
            if (other == null || other.Tick != Tick || other.Type != Type || other._values.Length != _values.Length) return false;
            for (int i = 0; i < _values.Length; i++)
            {
                if (_values[i] != other._values[i]) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayInput);
        }

        public override int GetHashCode()
        {
            int h = (int)Tick * 31 + Type;
            for (int i = 0; i < _values.Length; i++) h = h * 31 + _values[i];
            return h;
        }

        public override string ToString()
        {
            return "Input@" + Tick + " type=" + Type + " [" + string.Join(",", _values) + "]";
        }
    }
}
