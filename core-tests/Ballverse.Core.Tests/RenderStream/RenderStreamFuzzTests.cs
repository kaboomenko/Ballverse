using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Compression;
using Ballverse.Core.RenderStream;
using Ballverse.Core.RenderStream.Internal;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Corrupt or truncated render streams must only ever throw <see cref="InvalidDataException"/>.</summary>
    public class RenderStreamFuzzTests
    {
        static readonly List<RenderFrame> s_frames = StreamScenes.Churn(130, 42);
        static readonly List<RenderFrame> s_expected = StreamScenes.Canonical(s_frames);
        static readonly byte[] s_file = StreamScenes.Write(s_frames);

        /// <summary>Opens and fully reads the stream (sequentially and with seeks). Returns false on InvalidDataException.</summary>
        static bool TryReadAll(byte[] data, out List<RenderFrame> frames)
        {
            frames = new List<RenderFrame>();
            try
            {
                using (var reader = StreamScenes.Open(data))
                {
                    foreach (var f in reader.EnumerateFrames())
                    {
                        var copy = new RenderFrame();
                        copy.CopyFrom(f);
                        frames.Add(copy);
                    }
                    if (reader.TickCount > 0)
                    {
                        reader.ReadFrame(reader.TickCount - 1);
                        reader.ReadFrame(0);
                        reader.ReadFrame(reader.TickCount / 2);
                    }
                }
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        static void AssertUnchangedIfAccepted(byte[] data, string what)
        {
            if (!TryReadAll(data, out var frames)) return;
            Assert.True(frames.Count == s_expected.Count, what + ": corrupt file accepted with different tick count");
            for (int t = 0; t < frames.Count; t++) Assert.True(s_expected[t].ContentEquals(frames[t]), what + ": corrupt file accepted with different content");
        }

        [Fact]
        public void OriginalFileReads()
        {
            Assert.True(TryReadAll(s_file, out var frames));
            Assert.Equal(s_expected.Count, frames.Count);
        }

        [Fact]
        public void EveryTruncationThrowsInvalidData()
        {
            for (int len = 0; len < s_file.Length; len++)
            {
                var data = new byte[len];
                Array.Copy(s_file, data, len);
                Assert.False(TryReadAll(data, out _), "truncated to " + len + " bytes was accepted");
            }
        }

        [Fact]
        public void TrailingGarbageThrowsInvalidData()
        {
            var data = new byte[s_file.Length + 3];
            Array.Copy(s_file, data, s_file.Length);
            Assert.False(TryReadAll(data, out _));
        }

        [Fact]
        public void EverySingleBitFlipOutsideBlocksIsDetected()
        {
            long blocksStart, blocksEnd;
            using (var r = StreamScenes.Open(s_file))
            {
                blocksStart = r.Blocks[0].Offset;
                var last = r.Blocks[r.BlockCount - 1];
                blocksEnd = last.Offset + last.CompressedLength;
            }
            for (int pos = 0; pos < s_file.Length; pos++)
            {
                if (pos >= blocksStart && pos < blocksEnd) continue;
                for (int bit = 0; bit < 8; bit++)
                {
                    var data = (byte[])s_file.Clone();
                    data[pos] ^= (byte)(1 << bit);
                    Assert.False(TryReadAll(data, out _), "flip at " + pos + ":" + bit + " was accepted");
                }
            }
        }

        [Fact]
        public void RandomBitFlipsOnlyThrowInvalidData()
        {
            var rng = Rng.FromSeed(0xB17);
            for (int iter = 0; iter < 600; iter++)
            {
                var data = (byte[])s_file.Clone();
                int flips = rng.NextInt(1, 5);
                for (int f = 0; f < flips; f++) data[rng.NextInt(0, data.Length)] ^= (byte)(1 << rng.NextInt(0, 8));
                AssertUnchangedIfAccepted(data, "flip iteration " + iter);
            }
        }

        [Fact]
        public void RandomGarbageOnlyThrowsInvalidData()
        {
            var rng = Rng.FromSeed(0x6A7);
            for (int iter = 0; iter < 300; iter++)
            {
                var data = new byte[rng.NextInt(0, 200)];
                for (int i = 0; i < data.Length; i++) data[i] = (byte)rng.NextUInt();
                if (data.Length >= 4 && rng.NextBool())
                {
                    data[0] = (byte)'B';
                    data[1] = (byte)'V';
                    data[2] = (byte)'S';
                    data[3] = (byte)'1';
                }
                Assert.False(TryReadAll(data, out _));
            }
        }

        // ------------------------------------------------------------------ corrupt content behind valid checksums

        /// <summary>
        /// Rebuilds the file with each block's decompressed bytes passed through <paramref name="mutate"/>, recompressing and
        /// recomputing every checksum, so that the corruption reaches the frame decoder.
        /// </summary>
        static byte[] Reseal(byte[] file, Func<int, byte[], byte[]> mutate)
        {
            using (var reader = StreamScenes.Open(file))
            {
                int headerLength = (int)reader.Blocks[0].Offset;
                var output = new MemoryStream();
                output.Write(file, 0, headerLength);
                var index = new MemoryStream();
                var iw = new BinaryWriter(index);
                for (int i = 0; i < reader.BlockCount; i++)
                {
                    var info = reader.Blocks[i];
                    byte[] raw = Lz4Block.Decompress(new ReadOnlySpan<byte>(file, (int)info.Offset, info.CompressedLength), info.RawLength);
                    raw = mutate(i, raw);
                    byte[] compressed = Lz4Block.Compress(raw);
                    iw.Write((uint)info.FirstTick);
                    iw.Write((ulong)output.Length);
                    iw.Write((uint)compressed.Length);
                    iw.Write((uint)raw.Length);
                    iw.Write(XxHash64.Hash(raw));
                    output.Write(compressed, 0, compressed.Length);
                }
                iw.Flush();
                long indexOffset = output.Length;
                byte[] indexBytes = index.ToArray();
                output.Write(indexBytes, 0, indexBytes.Length);

                var footer = new MemoryStream();
                var fw = new BinaryWriter(footer);
                fw.Write((ulong)indexOffset);
                fw.Write((uint)reader.BlockCount);
                fw.Write((uint)reader.TickCount);
                fw.Flush();
                var meta = new XxHash64();
                meta.Append(new ReadOnlySpan<byte>(file, 0, headerLength));
                meta.Append(indexBytes);
                meta.Append(footer.ToArray());
                fw.Write(meta.Digest());
                fw.Write(new[] { (byte)'B', (byte)'V', (byte)'S', (byte)'X' });
                fw.Flush();
                byte[] footerBytes = footer.ToArray();
                output.Write(footerBytes, 0, footerBytes.Length);
                return output.ToArray();
            }
        }

        [Fact]
        public void ResealWithoutMutationIsByteIdentical()
        {
            // Pins the documented byte layout (header · blocks · index · footer) from the outside.
            Assert.Equal(s_file, Reseal(s_file, (i, raw) => raw));
        }

        [Fact]
        public void CorruptBlockContentOnlyThrowsInvalidData()
        {
            var rng = Rng.FromSeed(0xC0DE);
            for (int iter = 0; iter < 250; iter++)
            {
                int target = rng.NextInt(0, 3);
                int mode = iter % 4;
                var local = Rng.FromSeed(rng.NextULong());
                byte[] data = Reseal(s_file, (i, raw) =>
                {
                    if (i != target) return raw;
                    var r = local;
                    var copy = (byte[])raw.Clone();
                    switch (mode)
                    {
                        case 0:
                            int flips = r.NextInt(1, 4);
                            for (int f = 0; f < flips; f++) copy[r.NextInt(0, copy.Length)] ^= (byte)(1 << r.NextInt(0, 8));
                            return copy;
                        case 1:
                            Array.Resize(ref copy, r.NextInt(0, copy.Length));
                            return copy;
                        case 2:
                            copy[r.NextInt(0, copy.Length)] = (byte)r.NextUInt();
                            return copy;
                        default:
                            Array.Resize(ref copy, copy.Length + r.NextInt(1, 8));
                            return copy;
                    }
                });
                // Content may decode to different (still valid) frames — that is acceptable; only the exception type matters.
                TryReadAll(data, out _);
            }
        }

        [Fact]
        public void TrailingBytesInBlockAreRejected()
        {
            byte[] data = Reseal(s_file, (i, raw) =>
            {
                if (i != 1) return raw;
                var copy = new byte[raw.Length + 1];
                Array.Copy(raw, copy, raw.Length);
                return copy;
            });
            using (var reader = StreamScenes.Open(data))
            {
                reader.ReadFrame(60);
                Assert.Throws<InvalidDataException>(() => reader.ReadFrame(119));
                // After a failure the reader recovers for other blocks.
                Assert.True(s_expected[5].ContentEquals(reader.ReadFrame(5)));
            }
        }

        [Fact]
        public void ReadFrameFailureDoesNotTouchDestination()
        {
            byte[] data = Reseal(s_file, (i, raw) =>
            {
                if (i != 1) return raw;
                var copy = new byte[raw.Length + 1];
                Array.Copy(raw, copy, raw.Length);
                return copy;
            });
            using (var reader = StreamScenes.Open(data))
            {
                var dest = new RenderFrame();
                dest.AddBody(new BodyState(-42, BodyKind.Other, 1, 2, 3, 4, 5, BodyFlags.Visible));
                dest.AddHud(9, 9);
                var sentinel = new RenderFrame();
                sentinel.CopyFrom(dest);
                Assert.Throws<InvalidDataException>(() => reader.ReadFrame(119, dest));
                Assert.True(sentinel.ContentEquals(dest), "destination modified by a failed read");
            }
        }

        [Fact]
        public void DecoderRejectsTooManyHudEntries()
        {
            int max = HudEntry.MaxEntriesPerFrame;
            var dest = new RenderFrame();

            // Keyframe declaring max + 1 entries.
            var w = new ByteWriter(4096);
            w.WriteUVarint(0);
            w.WriteUVarint((ulong)(max + 1));
            for (int k = 0; k <= max; k++)
            {
                w.WriteUVarint(0);
                w.WriteSVarint(k);
            }
            byte[] data = w.Span.ToArray();
            var r = new ByteReader(data, 0, data.Length);
            Assert.Throws<InvalidDataException>(() => new FrameCodec().Decode(ref r, dest));

            // Keyframe with max entries, then a delta adding one new key.
            w = new ByteWriter(4096);
            w.WriteUVarint(0);
            w.WriteUVarint((ulong)max);
            for (int k = 0; k < max; k++)
            {
                w.WriteUVarint(0);
                w.WriteSVarint(k);
            }
            w.WriteUVarint(0);           // despawns
            w.WriteUVarint(0);           // spawns
            w.WriteUVarint(0);           // HUD removed
            w.WriteUVarint(1);           // HUD set
            w.WriteUVarint((ulong)max);  // new key
            w.WriteSVarint(1);
            data = w.Span.ToArray();
            var codec = new FrameCodec();
            var r1 = new ByteReader(data, 0, data.Length);
            codec.Decode(ref r1, dest);
            Assert.Equal(max, dest.HudCount);
            Assert.Throws<InvalidDataException>(() => codec.Decode(ref r1, dest));
        }

        [Fact]
        public void BlockChecksumMismatchIsRejected()
        {
            byte[] data = (byte[])s_file.Clone();
            long offset;
            using (var r = StreamScenes.Open(s_file)) offset = r.Blocks[2].Offset;
            // Corrupt the start of block 2: either the LZ4 decoder or the raw hash must catch it.
            data[offset + 1] ^= 0x01;
            using (var reader = StreamScenes.Open(data))
            {
                Assert.True(s_expected[0].ContentEquals(reader.ReadFrame(0)));
                Assert.Throws<InvalidDataException>(() => reader.ReadFrame(125));
            }
        }

        // ------------------------------------------------------------------ frame codec directly

        [Fact]
        public void FrameCodecFuzzOnlyThrowsInvalidData()
        {
            // Encode one block with the internal codec, then decode mutated copies with a fresh codec.
            var codec = new FrameCodec();
            var w = new ByteWriter(1024);
            for (int t = 0; t < 60; t++) codec.Encode(s_expected[t], w);
            byte[] raw = w.Span.ToArray();

            var rng = Rng.FromSeed(0xFC);
            var dest = new RenderFrame();
            for (int iter = 0; iter < 1500; iter++)
            {
                byte[] data;
                if (iter % 5 == 4)
                {
                    data = new byte[rng.NextInt(0, 64)];
                    for (int i = 0; i < data.Length; i++) data[i] = (byte)rng.NextUInt();
                }
                else
                {
                    data = (byte[])raw.Clone();
                    int edits = rng.NextInt(1, 4);
                    for (int e = 0; e < edits; e++) data[rng.NextInt(0, data.Length)] = (byte)rng.NextUInt();
                    if (rng.NextBool()) Array.Resize(ref data, rng.NextInt(0, data.Length));
                }

                var decoder = new FrameCodec();
                var reader = new ByteReader(data, 0, data.Length);
                try
                {
                    for (int t = 0; t < 60; t++) decoder.Decode(ref reader, dest);
                }
                catch (InvalidDataException)
                {
                }
            }
        }
    }
}
