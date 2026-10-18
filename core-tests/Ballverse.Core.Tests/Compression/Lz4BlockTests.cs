using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Compression;
using K4os.Compression.LZ4;
using Xunit;

namespace Ballverse.Core.Tests.Compression
{
    public class Lz4BlockTests
    {
        // ------------------------------------------------------------------ inputs

        static byte[] RandomBytes(ref Rng rng, int length)
        {
            var b = new byte[length];
            for (int i = 0; i < length; i++) b[i] = (byte)rng.NextUInt();
            return b;
        }

        /// <summary>Low-entropy data: runs, repeated phrases and small alphabets (exercises long matches and overlaps).</summary>
        static byte[] RepetitiveBytes(ref Rng rng, int length)
        {
            var b = new byte[length];
            int i = 0;
            while (i < length)
            {
                int kind = rng.NextInt(0, 4);
                int run = rng.NextInt(1, 600);
                if (kind == 0)
                {
                    byte v = (byte)rng.NextUInt();
                    for (int k = 0; k < run && i < length; k++) b[i++] = v;
                }
                else if (kind == 1 && i > 0)
                {
                    int distance = rng.NextInt(1, Math.Min(i, 70000) + 1);
                    for (int k = 0; k < run && i < length; k++, i++) b[i] = b[i - distance];
                }
                else if (kind == 2)
                {
                    for (int k = 0; k < run && i < length; k++) b[i++] = (byte)('a' + rng.NextInt(0, 4));
                }
                else
                {
                    for (int k = 0; k < Math.Min(run, 40) && i < length; k++) b[i++] = (byte)rng.NextUInt();
                }
            }
            return b;
        }

        public static IEnumerable<object[]> Inputs()
        {
            var rng = Rng.FromSeed(0x4C5A34, 1);
            yield return new object[] { "empty", new byte[0] };
            yield return new object[] { "one", new byte[] { 0x42 } };
            yield return new object[] { "zeros12", new byte[12] };
            yield return new object[] { "zeros13", new byte[13] };
            yield return new object[] { "zeros1M", new byte[1 << 20] };
            yield return new object[] { "random1M", RandomBytes(ref rng, 1 << 20) };
            yield return new object[] { "repetitive1M", RepetitiveBytes(ref rng, 1 << 20) };
            yield return new object[] { "random200k", RandomBytes(ref rng, 200_000) };
            yield return new object[] { "repetitive200k", RepetitiveBytes(ref rng, 200_000) };
        }

        static IEnumerable<byte[]> ManySmallInputs()
        {
            var rng = Rng.FromSeed(0x4C5A34, 2);
            for (int len = 0; len <= 80; len++)
            {
                yield return RandomBytes(ref rng, len);
                yield return RepetitiveBytes(ref rng, len);
            }
            for (int i = 0; i < 150; i++)
            {
                int len = rng.NextInt(0, 5000);
                yield return rng.NextBool() ? RandomBytes(ref rng, len) : RepetitiveBytes(ref rng, len);
            }
        }

        // ------------------------------------------------------------------ helpers

        static byte[] K4osEncode(byte[] src, LZ4Level level)
        {
            var dst = new byte[LZ4Codec.MaximumOutputSize(src.Length)];
            int n = LZ4Codec.Encode(src, 0, src.Length, dst, 0, dst.Length, level);
            Assert.True(n >= 0, "K4os encode failed");
            var result = new byte[n];
            Array.Copy(dst, result, n);
            return result;
        }

        static byte[] K4osDecode(byte[] src, int length)
        {
            var dst = new byte[length];
            int n = LZ4Codec.Decode(src, 0, src.Length, dst, 0, dst.Length);
            Assert.Equal(length, n);
            return dst;
        }

        static void AssertInterop(byte[] input)
        {
            byte[] ours = Lz4Block.Compress(input);
            Assert.True(ours.Length <= Lz4Block.MaxCompressedLength(input.Length));
            Assert.Equal(input, Lz4Block.Decompress(ours, input.Length));
            if (input.Length > 0)
            {
                // K4os rejects zero-length targets, so the empty case is checked on our side only.
                Assert.Equal(input, K4osDecode(ours, input.Length));
            }

            foreach (var level in new[] { LZ4Level.L00_FAST, LZ4Level.L09_HC, LZ4Level.L12_MAX })
            {
                // The reference high-compression levels are slow on large inputs; the fast level covers those.
                if (level == LZ4Level.L09_HC && input.Length > 256 * 1024) continue;
                if (level == LZ4Level.L12_MAX && input.Length > 64 * 1024) continue;
                byte[] reference = K4osEncode(input, level);
                Assert.Equal(input, Lz4Block.Decompress(reference, input.Length));
            }
        }

        // ------------------------------------------------------------------ interop

        [Theory]
        [MemberData(nameof(Inputs))]
        public void InteropsWithReferenceImplementation(string name, byte[] input)
        {
            Assert.NotNull(name);
            AssertInterop(input);
        }

        [Fact]
        public void InteropsOnManyRngInputs()
        {
            foreach (var input in ManySmallInputs()) AssertInterop(input);
        }

        [Fact]
        public void EmptyInputCompressesToSingleZeroToken()
        {
            Assert.Equal(new byte[] { 0x00 }, Lz4Block.Compress(ReadOnlySpan<byte>.Empty));
            Assert.Empty(Lz4Block.Decompress(new byte[] { 0x00 }, 0));
            Assert.Empty(Lz4Block.Decompress(ReadOnlySpan<byte>.Empty, 0));
        }

        [Fact]
        public void ShortInputsAreLiteralOnly()
        {
            var input = new byte[12]; // below MFLIMIT + 1: must not contain a match even though it is all zeros
            byte[] c = Lz4Block.Compress(input);
            Assert.Equal(1 + 12, c.Length);
            Assert.Equal(0xC0, c[0]);
        }

        [Fact]
        public void CompressesRepetitiveDataWell()
        {
            byte[] c = Lz4Block.Compress(new byte[1 << 20]);
            Assert.True(c.Length < 5000, "1 MB of zeros compressed to " + c.Length);
        }

        [Fact]
        public void IncompressibleDataStaysWithinBound()
        {
            var rng = Rng.FromSeed(7);
            byte[] input = RandomBytes(ref rng, 100_000);
            byte[] c = Lz4Block.Compress(input);
            Assert.True(c.Length <= Lz4Block.MaxCompressedLength(input.Length));
            Assert.True(c.Length >= input.Length);
        }

        [Fact]
        public void OutputIsDeterministicAndIndependentOfScratchContents()
        {
            var rng = Rng.FromSeed(11);
            byte[] input = RepetitiveBytes(ref rng, 50_000);
            byte[] a = Lz4Block.Compress(input);

            var table = new int[Lz4Block.HashTableSize];
            for (int i = 0; i < table.Length; i++) table[i] = (int)rng.NextUInt();
            var dst = new byte[Lz4Block.MaxCompressedLength(input.Length)];
            int n = Lz4Block.Compress(input, dst, table);
            Assert.Equal(a, new ReadOnlySpan<byte>(dst, 0, n).ToArray());
        }

        // ------------------------------------------------------------------ hand-built streams

        [Fact]
        public void DecodesOverlappingMatch()
        {
            // 'a' literal, then match offset 1 length 4+6=10 -> 11 'a', then last literals "bcdef".
            var block = new byte[] { 0x16, (byte)'a', 0x01, 0x00, 0x50, (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f' };
            byte[] output = Lz4Block.Decompress(block, 16);
            Assert.Equal("aaaaaaaaaaabcdef", System.Text.Encoding.ASCII.GetString(output));
        }

        [Fact]
        public void DecodesMultiByteLengths()
        {
            // literal length 15 + 255 + 10 = 280, then match of length 4 + 15 + 255 + 255 + 1 = 530 at offset 280.
            var block = new List<byte> { 0xFF, 255, 10 };
            for (int i = 0; i < 280; i++) block.Add((byte)i);
            block.AddRange(new byte[] { 0x18, 0x01, 255, 255, 1 }); // offset 280 = 0x0118
            block.AddRange(new byte[] { 0x50, 1, 2, 3, 4, 5 }); // final sequence: 5 literals (reference decoders require them)
            byte[] output = Lz4Block.Decompress(block.ToArray(), 280 + 530 + 5);
            for (int i = 0; i < 810; i++) Assert.Equal((byte)(i % 280), output[i]);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, new ReadOnlySpan<byte>(output, 810, 5).ToArray());
            Assert.Equal(output, K4osDecode(block.ToArray(), output.Length));
        }

        [Fact]
        public void LongLiteralAndMatchLengthsRoundTrip()
        {
            var rng = Rng.FromSeed(5);
            var input = new byte[300 + 70_000 + 300];
            for (int i = 0; i < 300; i++) input[i] = (byte)rng.NextUInt();          // literal run >= 270
            for (int i = 300; i < 70_300; i++) input[i] = 0xAB;                      // very long match
            for (int i = 70_300; i < input.Length; i++) input[i] = (byte)rng.NextUInt();
            AssertInterop(input);
        }

        // ------------------------------------------------------------------ malformed input

        static void AssertInvalid(byte[] block, int length)
        {
            Assert.Throws<InvalidDataException>(() => Lz4Block.Decompress(block, length));
        }

        [Fact]
        public void RejectsMalformedBlocks()
        {
            AssertInvalid(new byte[0], 1);                                   // empty input, non-empty output
            AssertInvalid(new byte[] { 0x10 }, 1);                           // literal missing
            AssertInvalid(new byte[] { 0x10, 0x41, 0x00 }, 5);               // truncated offset
            AssertInvalid(new byte[] { 0x10, 0x41, 0x00, 0x00 }, 5);         // zero offset
            AssertInvalid(new byte[] { 0x10, 0x41, 0x02, 0x00 }, 5);         // offset before start
            AssertInvalid(new byte[] { 0x10, 0x41, 0x01, 0x00 }, 4);         // match overruns output
            AssertInvalid(new byte[] { 0x10, 0x41, 0x01, 0x00 }, 6);         // missing final sequence
            AssertInvalid(new byte[] { 0x20, 0x41, 0x42 }, 1);               // literals overrun output
            AssertInvalid(new byte[] { 0x10, 0x41 }, 2);                     // output shorter than expected
            AssertInvalid(new byte[] { 0xF0 }, 100);                         // truncated literal length
            AssertInvalid(new byte[] { 0xF0, 255, 255, 255, 255, 255 }, 100); // truncated, length grows past output
            AssertInvalid(new byte[] { 0x1F, 0x41, 0x01, 0x00 }, 100);       // truncated match length
            AssertInvalid(new byte[] { 0x00, 0x00 }, 0);                     // trailing garbage after empty literals
        }

        [Fact]
        public void RejectsHugeLengthsWithoutOverflow()
        {
            var block = new byte[2 + 20_000];
            block[0] = 0xF0;
            for (int i = 1; i < block.Length; i++) block[i] = 255; // would overflow int if not bounded
            AssertInvalid(block, 1_000_000);
        }

        [Fact]
        public void WrongKnownLengthIsRejected()
        {
            var rng = Rng.FromSeed(9);
            byte[] input = RepetitiveBytes(ref rng, 10_000);
            byte[] c = Lz4Block.Compress(input);
            AssertInvalid(c, input.Length - 1);
            AssertInvalid(c, input.Length + 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => Lz4Block.Decompress(c, -1));
        }

        [Fact]
        public void FuzzedBlocksOnlyThrowInvalidData()
        {
            var rng = Rng.FromSeed(0xF022);
            byte[] input = RepetitiveBytes(ref rng, 4000);
            byte[] valid = Lz4Block.Compress(input);
            for (int iter = 0; iter < 3000; iter++)
            {
                byte[] block;
                int mode = iter % 3;
                if (mode == 0)
                {
                    block = (byte[])valid.Clone();
                    int flips = rng.NextInt(1, 4);
                    for (int f = 0; f < flips; f++) block[rng.NextInt(0, block.Length)] ^= (byte)(1 << rng.NextInt(0, 8));
                }
                else if (mode == 1)
                {
                    block = new byte[rng.NextInt(0, valid.Length)];
                    Array.Copy(valid, block, block.Length);
                }
                else
                {
                    block = RandomBytes(ref rng, rng.NextInt(0, 64));
                }

                int length = mode == 2 ? rng.NextInt(0, 512) : input.Length;
                try
                {
                    byte[] output = Lz4Block.Decompress(block, length);
                    Assert.Equal(length, output.Length);
                }
                catch (InvalidDataException)
                {
                }
            }
        }

        [Fact]
        public void ArgumentValidation()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Lz4Block.MaxCompressedLength(-1));
            Assert.Throws<ArgumentNullException>(() => Lz4Block.Compress(new byte[10], new byte[100], null));
            Assert.Throws<ArgumentException>(() => Lz4Block.Compress(new byte[10], new byte[100], new int[10]));
            Assert.Throws<ArgumentException>(() => Lz4Block.Compress(new byte[100], new byte[100], new int[Lz4Block.HashTableSize]));
        }
    }
}
