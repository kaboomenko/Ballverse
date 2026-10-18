using System;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>Growable little-endian byte buffer with varint helpers; reused across blocks to avoid allocations.</summary>
    internal sealed class ByteWriter
    {
        byte[] _buffer;
        int _length;

        public ByteWriter(int capacity)
        {
            _buffer = new byte[Math.Max(16, capacity)];
        }

        public int Length => _length;
        public byte[] Buffer => _buffer;
        public ReadOnlySpan<byte> Span => new ReadOnlySpan<byte>(_buffer, 0, _length);

        public void Reset()
        {
            _length = 0;
        }

        public void WriteByte(byte value)
        {
            if (_length == _buffer.Length) Grow(1);
            _buffer[_length++] = value;
        }

        public void WriteUVarint(ulong value)
        {
            if (_buffer.Length - _length < VarInt.MaxBytes64) Grow(VarInt.MaxBytes64);
            _length += VarInt.WriteULong(new Span<byte>(_buffer, _length, VarInt.MaxBytes64), value);
        }

        public void WriteSVarint(long value)
        {
            WriteUVarint(VarInt.ZigZagEncode(value));
        }

        public void WriteUInt16(int value)
        {
            if (_buffer.Length - _length < 2) Grow(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteUInt32(uint value)
        {
            if (_buffer.Length - _length < 4) Grow(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)value);
            WriteUInt32((uint)(value >> 32));
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes)
        {
            if (_buffer.Length - _length < bytes.Length) Grow(bytes.Length);
            bytes.CopyTo(new Span<byte>(_buffer, _length, bytes.Length));
            _length += bytes.Length;
        }

        void Grow(int extra)
        {
            int needed = checked(_length + extra);
            int size = Math.Max(needed, _buffer.Length * 2);
            Array.Resize(ref _buffer, size);
        }
    }
}
