using System;
using System.Buffers.Binary;
using System.IO;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Bounds-checked cursor over one chunk payload. Every read past the end throws
    /// <see cref="InvalidDataException"/> (spec §7.4; reader robustness contract).
    /// </summary>
    internal sealed class BvrPayloadReader
    {
        readonly byte[] _buffer;
        readonly int _length;
        int _offset;

        internal BvrPayloadReader(byte[] buffer, int length)
        {
            _buffer = buffer;
            _length = length;
        }

        internal bool AtEnd => _offset >= _length;

        internal int Remaining => _length - _offset;

        internal byte ReadU8()
        {
            Require(1);
            return _buffer[_offset++];
        }

        internal ushort ReadU16()
        {
            Require(2);
            ushort v = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(_buffer, _offset, 2));
            _offset += 2;
            return v;
        }

        internal ulong ReadU64()
        {
            Require(8);
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(_buffer, _offset, 8));
            _offset += 8;
            return v;
        }

        internal ulong ReadVarULong()
        {
            return VarInt.ReadULong(new ReadOnlySpan<byte>(_buffer, 0, _length), ref _offset);
        }

        internal uint ReadVarUInt32(string what)
        {
            ulong v = ReadVarULong();
            if (v > uint.MaxValue) throw new InvalidDataException(what + " does not fit in 32 bits");
            return (uint)v;
        }

        internal int ReadVarNonNegativeInt32(string what)
        {
            ulong v = ReadVarULong();
            if (v > int.MaxValue) throw new InvalidDataException(what + " is out of range");
            return (int)v;
        }

        internal int ReadZigZagInt32(string what)
        {
            long v = VarInt.ZigZagDecode(ReadVarULong());
            if (v < int.MinValue || v > int.MaxValue) throw new InvalidDataException(what + " does not fit in 32 bits");
            return (int)v;
        }

        internal long ReadZigZagInt64()
        {
            return VarInt.ZigZagDecode(ReadVarULong());
        }

        /// <summary>Reads a list count; rejects counts above <paramref name="max"/> or above the bytes left (each item takes ≥ 1 byte).</summary>
        internal int ReadCount(int max, string what)
        {
            ulong v = ReadVarULong();
            if (v > (ulong)max) throw new InvalidDataException(what + " count " + v + " exceeds limit " + max);
            if (v > (ulong)Remaining) throw new InvalidDataException(what + " count exceeds remaining payload");
            return (int)v;
        }

        internal string ReadString(int maxBytes, string what)
        {
            ulong len = ReadVarULong();
            if (len > (ulong)maxBytes) throw new InvalidDataException(what + " is longer than " + maxBytes + " bytes");
            if (len > (ulong)Remaining) throw new InvalidDataException("Truncated " + what);
            int n = (int)len;
            string s = BvrEncoding.DecodeString(_buffer, _offset, n);
            _offset += n;
            return s;
        }

        void Require(int count)
        {
            if (_length - _offset < count) throw new InvalidDataException("Truncated chunk record");
        }
    }
}
