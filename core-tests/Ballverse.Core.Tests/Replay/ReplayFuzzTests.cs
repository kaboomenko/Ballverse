using System;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    /// <summary>
    /// Damaged files must either parse or throw <see cref="InvalidDataException"/> / <see cref="UnsupportedReplayVersionException"/>,
    /// nothing else (no IndexOutOfRange, Overflow, OutOfMemory, ArgumentException…).
    /// </summary>
    public class ReplayFuzzTests
    {
        static void ReadMustFailCleanly(byte[] bytes, bool nonSeekable)
        {
            try
            {
                Stream s = new MemoryStream(bytes, false);
                if (nonSeekable) s = new NonSeekableStream(s);
                ReplayReader.Read(s);
            }
            catch (InvalidDataException)
            {
            }
            catch (UnsupportedReplayVersionException)
            {
            }
        }

        static byte[] Valid(ulong seed)
        {
            var rng = Rng.FromSeed(seed, 3);
            return ReplayWriter.WriteToArray(ReplayTestUtil.Random(seed, 120), rng.NextInt(1, 40));
        }

        [Fact]
        public void EveryTruncationOfASampleFailsCleanly()
        {
            byte[] full = ReplayWriter.WriteToArray(ReplayTestUtil.Sample(), 3);
            int invalid = 0;
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                try
                {
                    ReplayReader.Read(cut);
                }
                catch (InvalidDataException)
                {
                    invalid++;
                }
            }
            // Only cuts at chunk boundaries can parse; everything else must be detected.
            Assert.True(invalid > full.Length - 20, "truncations detected: " + invalid + " of " + full.Length);
        }

        [Fact]
        public void RandomTruncationsFailCleanly()
        {
            var rng = Rng.FromSeed(2024, 11);
            for (int i = 0; i < 300; i++)
            {
                byte[] full = Valid((ulong)i);
                int len = rng.NextInt(0, full.Length);
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                ReadMustFailCleanly(cut, (i & 1) == 0);
            }
        }

        [Fact]
        public void RandomByteFlipsFailCleanly()
        {
            var rng = Rng.FromSeed(77, 12);
            for (int i = 0; i < 1500; i++)
            {
                byte[] bytes = Valid((ulong)(i % 50));
                int flips = rng.NextInt(1, 5);
                for (int k = 0; k < flips; k++)
                {
                    int pos = rng.NextInt(0, bytes.Length);
                    if (rng.NextBool()) bytes[pos] ^= (byte)(1 << rng.NextInt(0, 8));
                    else bytes[pos] = (byte)rng.NextUInt(256);
                }
                ReadMustFailCleanly(bytes, (i & 1) == 1);
            }
        }

        [Fact]
        public void Fuzz_InsertDeleteMutations()
        {
            var rng = Rng.FromSeed(31337, 14);
            for (int i = 0; i < 1500; i++)
            {
                var bytes = new System.Collections.Generic.List<byte>(Valid((ulong)(i % 40)));
                int mutations = rng.NextInt(1, 4);
                for (int k = 0; k < mutations && bytes.Count > 0; k++)
                {
                    int pos = rng.NextInt(0, bytes.Count);
                    switch (rng.NextInt(0, 3))
                    {
                        case 0:
                            int insert = rng.NextInt(1, 9);
                            for (int n = 0; n < insert; n++) bytes.Insert(pos, (byte)rng.NextUInt(256));
                            break;
                        case 1:
                            bytes.RemoveRange(pos, Math.Min(rng.NextInt(1, 9), bytes.Count - pos));
                            break;
                        default:
                            bytes[pos] ^= (byte)(1 << rng.NextInt(0, 8));
                            break;
                    }
                }
                ReadMustFailCleanly(bytes.ToArray(), (i & 1) == 0);
            }
        }

        [Fact]
        public void FlipsInHeaderFailCleanly()
        {
            byte[] valid = ReplayWriter.WriteToArray(ReplayTestUtil.Sample());
            int headerLength = (int)ReplayReaderTests.HeaderLength(valid);
            for (int pos = 0; pos < headerLength; pos++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    var b = (byte[])valid.Clone();
                    b[pos] ^= (byte)(1 << bit);
                    ReadMustFailCleanly(b, false);
                }
            }
        }

        [Fact]
        public void RandomGarbageAfterMagicFailsCleanly()
        {
            var rng = Rng.FromSeed(5, 13);
            for (int i = 0; i < 300; i++)
            {
                var b = new byte[rng.NextInt(4, 200)];
                for (int k = 0; k < b.Length; k++) b[k] = (byte)rng.NextUInt(256);
                b[0] = (byte)'B';
                b[1] = (byte)'V';
                b[2] = (byte)'R';
                b[3] = (byte)'1';
                if (b.Length > 5 && rng.NextBool())
                {
                    b[4] = 1;
                    b[5] = 0;
                }
                ReadMustFailCleanly(b, (i & 1) == 0);
            }
        }
    }
}
