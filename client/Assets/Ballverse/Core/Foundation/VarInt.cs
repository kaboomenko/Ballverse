using System;
using System.IO;

namespace Ballverse.Core
{
    /// <summary>
    /// LEB128 variable-length integers and ZigZag encoding, shared by the replay (.bvr) and render stream (.bvs) formats.
    /// </summary>
    public static class VarInt
    {
        public const int MaxBytes64 = 10;

        public static void WriteULong(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }

        public static void WriteLong(Stream s, long value)
        {
            WriteULong(s, ZigZagEncode(value));
        }

        /// <summary>Reads an unsigned varint. Throws <see cref="InvalidDataException"/> on truncation or overflow.</summary>
        public static ulong ReadULong(Stream s)
        {
            ulong result = 0;
            int shift = 0;
            for (int i = 0; i < MaxBytes64; i++)
            {
                int b = s.ReadByte();
                if (b < 0) throw new InvalidDataException("Truncated varint");
                if (i == MaxBytes64 - 1 && b > 1) throw new InvalidDataException("Varint overflows 64 bits");
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidDataException("Varint longer than 10 bytes");
        }

        public static long ReadLong(Stream s)
        {
            return ZigZagDecode(ReadULong(s));
        }

        /// <summary>Writes into a span; returns bytes written. Span must have room for <see cref="MaxBytes64"/> bytes.</summary>
        public static int WriteULong(Span<byte> dst, ulong value)
        {
            int i = 0;
            while (value >= 0x80)
            {
                dst[i++] = (byte)(value | 0x80);
                value >>= 7;
            }
            dst[i++] = (byte)value;
            return i;
        }

        /// <summary>Reads from a span starting at <paramref name="offset"/>, advancing it.</summary>
        public static ulong ReadULong(ReadOnlySpan<byte> src, ref int offset)
        {
            ulong result = 0;
            int shift = 0;
            for (int i = 0; i < MaxBytes64; i++)
            {
                if (offset >= src.Length) throw new InvalidDataException("Truncated varint");
                byte b = src[offset++];
                if (i == MaxBytes64 - 1 && b > 1) throw new InvalidDataException("Varint overflows 64 bits");
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidDataException("Varint longer than 10 bytes");
        }

        public static ulong ZigZagEncode(long value)
        {
            return (ulong)((value << 1) ^ (value >> 63));
        }

        public static long ZigZagDecode(ulong value)
        {
            return (long)(value >> 1) ^ -(long)(value & 1);
        }
    }
}
