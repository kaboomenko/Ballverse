using System;
using System.IO;

namespace Ballverse.Core.Compression
{
    /// <summary>
    /// In-house LZ4 <b>block</b> format codec (no frame format, no checksums), used to compress render stream
    /// blocks (spec §7.4 "сжатие LZ4 по блокам"). Output is interoperable with the reference LZ4 implementation:
    /// the encoder honours the end-of-block rules (last 5 bytes are literals, the last match starts at least
    /// 12 bytes before the end), so any conforming decoder accepts it, and the decoder accepts any conforming stream.
    /// <para>
    /// The encoder is a greedy single-probe hash matcher (similar to LZ4 "fast", acceleration 1). Its output is a
    /// pure function of the input bytes (deterministic across runs and platforms).
    /// </para>
    /// <para>
    /// The decoder is fully bounds-checked: malformed, truncated or hostile input only ever throws
    /// <see cref="InvalidDataException"/>, never reads or writes out of range and never allocates more than the
    /// caller-provided decompressed length.
    /// </para>
    /// </summary>
    public static class Lz4Block
    {
        /// <summary>Number of entries in the encoder hash table (see <see cref="Compress(ReadOnlySpan{byte}, Span{byte}, int[])"/>).</summary>
        public const int HashTableSize = 1 << HashLog;

        const int HashLog = 12;
        const int MinMatch = 4;
        /// <summary>The last match must start at least this many bytes before the end of the block.</summary>
        const int MfLimit = 12;
        /// <summary>The last bytes of a block are always literals.</summary>
        const int LastLiterals = 5;
        const int MaxOffset = 65535;
        /// <summary>Inputs shorter than this are emitted as a single literal run.</summary>
        const int MinInputForMatch = MfLimit + 1;
        const int SkipTrigger = 6;

        /// <summary>Worst-case compressed size for an input of <paramref name="inputLength"/> bytes (same bound as LZ4_compressBound).</summary>
        public static int MaxCompressedLength(int inputLength)
        {
            if (inputLength < 0) throw new ArgumentOutOfRangeException(nameof(inputLength));
            if (inputLength > 0x7E000000) throw new ArgumentOutOfRangeException(nameof(inputLength), "Input too large for LZ4");
            return inputLength + inputLength / 255 + 16;
        }

        /// <summary>Compresses <paramref name="src"/> into a new array holding exactly the compressed block.</summary>
        public static byte[] Compress(ReadOnlySpan<byte> src)
        {
            var dst = new byte[MaxCompressedLength(src.Length)];
            int n = Compress(src, dst, new int[HashTableSize]);
            if (n == dst.Length) return dst;
            var result = new byte[n];
            Buffer.BlockCopy(dst, 0, result, 0, n);
            return result;
        }

        /// <summary>
        /// Compresses <paramref name="src"/> into <paramref name="dst"/> and returns the compressed length.
        /// <paramref name="dst"/> must hold at least <see cref="MaxCompressedLength"/> bytes. <paramref name="hashTable"/>
        /// is caller-owned scratch of <see cref="HashTableSize"/> entries, reused across calls to avoid allocations;
        /// its previous contents do not affect the output.
        /// </summary>
        public static int Compress(ReadOnlySpan<byte> src, Span<byte> dst, int[] hashTable)
        {
            if (hashTable == null) throw new ArgumentNullException(nameof(hashTable));
            if (hashTable.Length < HashTableSize) throw new ArgumentException("Hash table too small", nameof(hashTable));
            int n = src.Length;
            if (dst.Length < MaxCompressedLength(n)) throw new ArgumentException("Destination smaller than MaxCompressedLength", nameof(dst));

            int op = 0;
            int anchor = 0;
            if (n >= MinInputForMatch)
            {
                Array.Clear(hashTable, 0, HashTableSize);
                int matchLimit = n - LastLiterals;
                int lastMatchStart = n - MfLimit;
                int ip = 0;
                // Position 0 is "already in the table" because the table is zero-filled.
                ip++;
                int searchCount = 1 << SkipTrigger;
                while (ip <= lastMatchStart)
                {
                    uint seq = Read32(src, ip);
                    int h = Hash(seq);
                    int candidate = hashTable[h];
                    hashTable[h] = ip;
                    int distance = ip - candidate;
                    if (distance <= 0 || distance > MaxOffset || Read32(src, candidate) != seq)
                    {
                        ip += searchCount++ >> SkipTrigger;
                        continue;
                    }

                    // Extend backwards over pending literals.
                    while (ip > anchor && candidate > 0 && src[ip - 1] == src[candidate - 1])
                    {
                        ip--;
                        candidate--;
                    }

                    int matchLength = MinMatch;
                    while (ip + matchLength < matchLimit && src[candidate + matchLength] == src[ip + matchLength])
                    {
                        matchLength++;
                    }

                    op = WriteSequence(src, dst, op, anchor, ip - anchor, ip - candidate, matchLength);
                    ip += matchLength;
                    anchor = ip;
                    searchCount = 1 << SkipTrigger;

                    // Seed the table with a position inside the match to improve the next search.
                    if (ip - 2 <= lastMatchStart && ip - 2 > 0)
                    {
                        hashTable[Hash(Read32(src, ip - 2))] = ip - 2;
                    }
                }
            }

            // Final literal-only sequence (also the whole block for short inputs).
            int literalLength = n - anchor;
            op = WriteLiteralLengthToken(dst, op, literalLength, 0);
            src.Slice(anchor, literalLength).CopyTo(dst.Slice(op));
            op += literalLength;
            return op;
        }

        /// <summary>
        /// Decompresses an LZ4 block whose decompressed size is known to be exactly <paramref name="knownDecompressedLength"/>.
        /// Throws <see cref="InvalidDataException"/> if the data is malformed or does not decode to exactly that many bytes.
        /// An empty <paramref name="src"/> is accepted only when the expected length is 0 (some encoders emit nothing for empty input).
        /// </summary>
        public static byte[] Decompress(ReadOnlySpan<byte> src, int knownDecompressedLength)
        {
            if (knownDecompressedLength < 0) throw new ArgumentOutOfRangeException(nameof(knownDecompressedLength));
            var dst = new byte[knownDecompressedLength];
            Decompress(src, dst);
            return dst;
        }

        /// <summary>
        /// Decompresses <paramref name="src"/> into <paramref name="dst"/>, which must be exactly the decompressed size.
        /// Throws <see cref="InvalidDataException"/> on malformed input or on a size mismatch.
        /// </summary>
        public static void Decompress(ReadOnlySpan<byte> src, Span<byte> dst)
        {
            int srcLength = src.Length;
            int dstLength = dst.Length;
            if (srcLength == 0)
            {
                if (dstLength == 0) return;
                throw new InvalidDataException("LZ4: empty input");
            }

            int ip = 0;
            int op = 0;
            while (true)
            {
                if (ip >= srcLength) throw new InvalidDataException("LZ4: truncated block (missing token)");
                int token = src[ip++];

                int literalLength = token >> 4;
                if (literalLength == 15) literalLength = ReadExtraLength(src, ref ip, literalLength, dstLength);
                if (literalLength > srcLength - ip) throw new InvalidDataException("LZ4: literal run exceeds input");
                if (literalLength > dstLength - op) throw new InvalidDataException("LZ4: literal run exceeds output");
                src.Slice(ip, literalLength).CopyTo(dst.Slice(op));
                ip += literalLength;
                op += literalLength;

                if (ip == srcLength) break; // last sequence has no match part

                if (srcLength - ip < 2) throw new InvalidDataException("LZ4: truncated match offset");
                int offset = src[ip] | (src[ip + 1] << 8);
                ip += 2;
                if (offset == 0) throw new InvalidDataException("LZ4: zero match offset");
                if (offset > op) throw new InvalidDataException("LZ4: match offset before start of output");

                int matchLength = token & 15;
                if (matchLength == 15) matchLength = ReadExtraLength(src, ref ip, matchLength, dstLength);
                matchLength += MinMatch;
                if (matchLength > dstLength - op) throw new InvalidDataException("LZ4: match exceeds output");

                int from = op - offset;
                if (offset >= matchLength)
                {
                    dst.Slice(from, matchLength).CopyTo(dst.Slice(op));
                    op += matchLength;
                }
                else
                {
                    // Overlapping copy (run-length style); must go byte by byte.
                    int end = op + matchLength;
                    while (op < end) dst[op++] = dst[from++];
                }
            }

            if (op != dstLength) throw new InvalidDataException("LZ4: decompressed size " + op + " != expected " + dstLength);
        }

        static int ReadExtraLength(ReadOnlySpan<byte> src, ref int ip, int length, int limit)
        {
            while (true)
            {
                if (ip >= src.Length) throw new InvalidDataException("LZ4: truncated length");
                int b = src[ip++];
                length += b;
                // Any length above the output size is invalid; checking here also prevents int overflow.
                if (length > limit) throw new InvalidDataException("LZ4: length exceeds output");
                if (b != 255) return length;
            }
        }

        static int WriteSequence(ReadOnlySpan<byte> src, Span<byte> dst, int op, int literalStart, int literalLength, int offset, int matchLength)
        {
            int ml = matchLength - MinMatch;
            op = WriteLiteralLengthToken(dst, op, literalLength, ml >= 15 ? 15 : ml);
            src.Slice(literalStart, literalLength).CopyTo(dst.Slice(op));
            op += literalLength;
            dst[op++] = (byte)offset;
            dst[op++] = (byte)(offset >> 8);
            if (ml >= 15) op = WriteExtraLength(dst, op, ml - 15);
            return op;
        }

        static int WriteLiteralLengthToken(Span<byte> dst, int op, int literalLength, int matchNibble)
        {
            if (literalLength >= 15)
            {
                dst[op++] = (byte)(0xF0 | matchNibble);
                return WriteExtraLength(dst, op, literalLength - 15);
            }
            dst[op++] = (byte)((literalLength << 4) | matchNibble);
            return op;
        }

        static int WriteExtraLength(Span<byte> dst, int op, int remaining)
        {
            while (remaining >= 255)
            {
                dst[op++] = 255;
                remaining -= 255;
            }
            dst[op++] = (byte)remaining;
            return op;
        }

        static uint Read32(ReadOnlySpan<byte> s, int i)
        {
            return (uint)(s[i] | (s[i + 1] << 8) | (s[i + 2] << 16) | (s[i + 3] << 24));
        }

        static int Hash(uint sequence)
        {
            return (int)((sequence * 2654435761u) >> (32 - HashLog));
        }
    }
}
