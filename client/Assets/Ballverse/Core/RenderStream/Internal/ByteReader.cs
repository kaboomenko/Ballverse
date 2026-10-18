using System;
using System.IO;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>Bounds-checked little-endian reader over a byte array; every overrun throws <see cref="InvalidDataException"/>.</summary>
    internal struct ByteReader
    {
        readonly byte[] _buffer;
        readonly int _end;
        int _offset;

        public ByteReader(byte[] buffer, int offset, int length)
        {
            _buffer = buffer;
            _offset = offset;
            _end = offset + length;
        }

        public int Offset => _offset;
        public int Remaining => _end - _offset;

        public byte ReadByte()
        {
            if (_offset >= _end) throw new InvalidDataException("Unexpected end of data");
            return _buffer[_offset++];
        }

        public ulong ReadUVarint()
        {
            var span = new ReadOnlySpan<byte>(_buffer, 0, _end);
            return VarInt.ReadULong(span, ref _offset);
        }

        public long ReadSVarint()
        {
            return VarInt.ZigZagDecode(ReadUVarint());
        }

        /// <summary>Reads an unsigned varint that must be &lt;= <paramref name="max"/>.</summary>
        public int ReadCount(int max, string what)
        {
            ulong v = ReadUVarint();
            if (v > (ulong)max) throw new InvalidDataException(what + " out of range: " + v);
            return (int)v;
        }

        public ushort ReadUInt16()
        {
            if (_end - _offset < 2) throw new InvalidDataException("Unexpected end of data");
            ushort v = (ushort)(_buffer[_offset] | (_buffer[_offset + 1] << 8));
            _offset += 2;
            return v;
        }

        public uint ReadUInt32()
        {
            if (_end - _offset < 4) throw new InvalidDataException("Unexpected end of data");
            uint v = (uint)(_buffer[_offset] | (_buffer[_offset + 1] << 8) | (_buffer[_offset + 2] << 16) | (_buffer[_offset + 3] << 24));
            _offset += 4;
            return v;
        }

        public ulong ReadUInt64()
        {
            ulong lo = ReadUInt32();
            ulong hi = ReadUInt32();
            return lo | (hi << 32);
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || _end - _offset < count) throw new InvalidDataException("Unexpected end of data");
            var span = new ReadOnlySpan<byte>(_buffer, _offset, count);
            _offset += count;
            return span;
        }
    }
}
