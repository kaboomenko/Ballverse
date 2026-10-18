using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Ballverse.Core.Replay
{
    /// <summary>Low-level little-endian / varint / string writers for the .bvr format (spec §7.4).</summary>
    internal static class BvrEncoding
    {
        /// <summary>UTF-8 without BOM that throws on invalid input instead of substituting U+FFFD.</summary>
        internal static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        internal static void WriteU16(Stream s, ushort value)
        {
            Span<byte> tmp = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(tmp, value);
            s.Write(tmp);
        }

        internal static void WriteU32(Stream s, uint value)
        {
            Span<byte> tmp = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
            s.Write(tmp);
        }

        internal static void WriteU64(Stream s, ulong value)
        {
            Span<byte> tmp = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(tmp, value);
            s.Write(tmp);
        }

        /// <summary>Writes varint byte length + UTF-8 bytes produced by <see cref="EncodeString"/>.</summary>
        internal static void WriteBytesWithLength(Stream s, byte[] bytes)
        {
            VarInt.WriteULong(s, (ulong)bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Encodes a string as strict UTF-8 and checks its size.
        /// Throws <see cref="ArgumentException"/> for null, unpaired surrogates or oversize values.
        /// </summary>
        internal static byte[] EncodeString(string value, int maxBytes, string paramName)
        {
            if (value == null) throw new ArgumentNullException(paramName);
            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(value);
            }
            catch (EncoderFallbackException e)
            {
                throw new ArgumentException("String is not valid UTF-16 (unpaired surrogate)", paramName, e);
            }
            if (bytes.Length > maxBytes)
                throw new ArgumentException("String is " + bytes.Length + " UTF-8 bytes, limit is " + maxBytes, paramName);
            return bytes;
        }

        /// <summary>Decodes strict UTF-8; invalid sequences become <see cref="InvalidDataException"/>.</summary>
        internal static string DecodeString(byte[] buffer, int offset, int count)
        {
            try
            {
                return StrictUtf8.GetString(buffer, offset, count);
            }
            catch (DecoderFallbackException e)
            {
                throw new InvalidDataException("Invalid UTF-8 in string field", e);
            }
        }
    }
}
