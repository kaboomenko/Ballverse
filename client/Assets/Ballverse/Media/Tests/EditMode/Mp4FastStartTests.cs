using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
    public sealed class Mp4FastStartTests
    {
        // ---------------------------------------------------------------------------------------- synthetic MP4 helpers

        static byte[] Box(string type, params byte[][] parts)
        {
            int size = 8;
            foreach (byte[] p in parts) size += p.Length;
            var b = new byte[size];
            Mp4Bytes.WriteU32(b, 0, (uint)size);
            for (int i = 0; i < 4; i++) b[4 + i] = (byte)type[i];
            int at = 8;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, b, at, p.Length);
                at += p.Length;
            }
            return b;
        }

        static byte[] U32(uint v)
        {
            var b = new byte[4];
            Mp4Bytes.WriteU32(b, 0, v);
            return b;
        }

        static byte[] Stco(IList<long> offsets, bool co64)
        {
            var body = new List<byte>();
            body.AddRange(new byte[4]);
            body.AddRange(U32((uint)offsets.Count));
            foreach (long o in offsets)
            {
                if (co64)
                {
                    var b = new byte[8];
                    Mp4Bytes.WriteU64(b, 0, (ulong)o);
                    body.AddRange(b);
                }
                else
                {
                    body.AddRange(U32((uint)o));
                }
            }
            return Box(co64 ? "co64" : "stco", body.ToArray());
        }

        static byte[] Moov(IList<long> offsets, bool co64, params byte[][] extra)
        {
            byte[] stbl = Box("stbl", Stco(offsets, co64));
            byte[] trak = Box("trak", Box("mdia", Box("minf", stbl)));
            var parts = new List<byte[]> { Box("mvhd", new byte[100]), trak };
            parts.AddRange(extra);
            return Box("moov", parts.ToArray());
        }

        sealed class Sample
        {
            public long Offset;
            public byte[] Bytes;
        }

        /// <summary>ftyp | free | mdat (chunks) | moov at the end (MediaMuxer layout). Returns the chunk samples.</summary>
        static List<Sample> WriteMoovAtEnd(string path, int chunks, int chunkBytes, bool co64, params byte[][] moovExtra)
        {
            byte[] ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom"), U32(0x200), Encoding.ASCII.GetBytes("isomiso2mp41"));
            byte[] free = Box("free", new byte[16]);
            var samples = new List<Sample>();
            var mdatBody = new byte[chunks * chunkBytes];
            long mdatStart = ftyp.Length + free.Length;
            for (int c = 0; c < chunks; c++)
            {
                var bytes = new byte[chunkBytes];
                for (int i = 0; i < chunkBytes; i++) bytes[i] = (byte)(c * 31 + i * 7 + (i >> 8));
                Buffer.BlockCopy(bytes, 0, mdatBody, c * chunkBytes, chunkBytes);
                samples.Add(new Sample { Offset = mdatStart + 8 + (long)c * chunkBytes, Bytes = bytes });
            }
            byte[] mdat = Box("mdat", mdatBody);
            var offsets = new List<long>();
            foreach (Sample s in samples) offsets.Add(s.Offset);
            byte[] moov = Moov(offsets, co64, moovExtra);
            using (var fs = new FileStream(path, FileMode.Create))
            {
                fs.Write(ftyp, 0, ftyp.Length);
                fs.Write(free, 0, free.Length);
                fs.Write(mdat, 0, mdat.Length);
                fs.Write(moov, 0, moov.Length);
            }
            return samples;
        }

        static List<(string type, long offset, long size)> TopLevel(byte[] file)
        {
            var list = new List<(string, long, long)>();
            long pos = 0;
            while (pos + 8 <= file.Length)
            {
                long size = Mp4Bytes.ReadU32(file, (int)pos);
                list.Add((Mp4Bytes.TypeOf(file, (int)pos + 4), pos, size));
                pos += size;
            }
            return list;
        }

        static (int offset, int size) Find(byte[] data, int start, int end, string type)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                int size = (int)Mp4Bytes.ReadU32(data, pos);
                if (Mp4Bytes.TypeOf(data, pos + 4) == type) return (pos, size);
                pos += size;
            }
            return (-1, 0);
        }

        static List<long> ChunkOffsets(byte[] file, bool co64)
        {
            var moov = TopLevel(file).Find(b => b.type == "moov");
            int m = (int)moov.offset;
            var trak = Find(file, m + 8, m + (int)moov.size, "trak");
            var mdia = Find(file, trak.offset + 8, trak.offset + trak.size, "mdia");
            var minf = Find(file, mdia.offset + 8, mdia.offset + mdia.size, "minf");
            var stbl = Find(file, minf.offset + 8, minf.offset + minf.size, "stbl");
            var stco = Find(file, stbl.offset + 8, stbl.offset + stbl.size, co64 ? "co64" : "stco");
            int count = (int)Mp4Bytes.ReadU32(file, stco.offset + 12);
            var result = new List<long>();
            for (int i = 0; i < count; i++)
            {
                result.Add(co64 ? (long)Mp4Bytes.ReadU64(file, stco.offset + 16 + i * 8) : Mp4Bytes.ReadU32(file, stco.offset + 16 + i * 4));
            }
            return result;
        }

        static void AssertSamplesIntact(byte[] file, List<Sample> samples, bool co64)
        {
            List<long> offsets = ChunkOffsets(file, co64);
            Assert.AreEqual(samples.Count, offsets.Count);
            for (int c = 0; c < samples.Count; c++)
            {
                var actual = new byte[samples[c].Bytes.Length];
                Buffer.BlockCopy(file, (int)offsets[c], actual, 0, actual.Length);
                CollectionAssert.AreEqual(samples[c].Bytes, actual, "chunk " + c + " does not point at its data");
            }
        }

        static string IlstText(byte[] file, string name3)
        {
            var moov = TopLevel(file).Find(b => b.type == "moov");
            int m = (int)moov.offset;
            var udta = Find(file, m + 8, m + (int)moov.size, "udta");
            Assert.GreaterOrEqual(udta.offset, 0, "no udta");
            var meta = Find(file, udta.offset + 8, udta.offset + udta.size, "meta");
            Assert.GreaterOrEqual(meta.offset, 0, "no udta/meta");
            var hdlr = Find(file, meta.offset + 12, meta.offset + meta.size, "hdlr");
            Assert.AreEqual("mdir", Mp4Bytes.TypeOf(file, hdlr.offset + 16));
            var ilst = Find(file, meta.offset + 12, meta.offset + meta.size, "ilst");
            int pos = ilst.offset + 8;
            while (pos < ilst.offset + ilst.size)
            {
                int size = (int)Mp4Bytes.ReadU32(file, pos);
                if (file[pos + 4] == 0xA9 && Encoding.ASCII.GetString(file, pos + 5, 3) == name3)
                {
                    int dataSize = (int)Mp4Bytes.ReadU32(file, pos + 8);
                    Assert.AreEqual("data", Mp4Bytes.TypeOf(file, pos + 12));
                    Assert.AreEqual(1u, (uint)Mp4Bytes.ReadU32(file, pos + 16), "type indicator must be UTF-8");
                    return Encoding.UTF8.GetString(file, pos + 24, dataSize - 16);
                }
                pos += size;
            }
            return null;
        }

        static int CountChildren(byte[] file, string parent, string child)
        {
            var moov = TopLevel(file).Find(b => b.type == "moov");
            int m = (int)moov.offset;
            var p = parent == "moov" ? (m, (int)moov.size) : Find(file, m + 8, m + (int)moov.size, parent);
            Assert.GreaterOrEqual(p.Item1, 0, "no " + parent);
            int n = 0;
            int pos = p.Item1 + 8;
            while (pos < p.Item1 + p.Item2)
            {
                int size = (int)Mp4Bytes.ReadU32(file, pos);
                if (Mp4Bytes.TypeOf(file, pos + 4) == child) n++;
                pos += size;
            }
            return n;
        }

        // ---------------------------------------------------------------------------------------- tests

        [TestCase(false)]
        [TestCase(true)]
        public void MoovAtEnd_IsMovedToFront_AndChunkOffsetsFollowTheirData(bool co64)
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("a.mp4");
                List<Sample> samples = WriteMoovAtEnd(path, 12, 1000, co64);
                Assert.IsFalse(Mp4FastStart.IsFastStart(path));
                long before = new FileInfo(path).Length;

                Assert.IsTrue(Mp4FastStart.Apply(path, CancellationToken.None));

                byte[] file = File.ReadAllBytes(path);
                Assert.AreEqual(before, file.Length);
                var boxes = TopLevel(file);
                Assert.Less(boxes.FindIndex(b => b.type == "moov"), boxes.FindIndex(b => b.type == "mdat"));
                Assert.IsTrue(Mp4FastStart.IsFastStart(path));
                AssertSamplesIntact(file, samples, co64);
                Assert.IsFalse(Mp4FastStart.Apply(path, CancellationToken.None), "second run must be a no-op");
                Assert.IsFalse(File.Exists(path + Mp4FastStart.TempSuffix));
            }
        }

        [Test]
        public void Metadata_IsWrittenAsTitleAndDate_AndOffsetsStayValid()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("m.mp4");
                List<Sample> samples = WriteMoovAtEnd(path, 5, 777, false);
                var metadata = new Mp4Metadata("Ring Escape — финал", new DateTime(2026, 10, 18, 9, 30, 15, DateTimeKind.Utc));

                Assert.IsTrue(Mp4FastStart.Apply(path, metadata, CancellationToken.None));
                byte[] file = File.ReadAllBytes(path);
                AssertSamplesIntact(file, samples, false);
                Assert.AreEqual("Ring Escape — финал", IlstText(file, "nam"));
                Assert.AreEqual("2026-10-18T09:30:15Z", IlstText(file, "day"));

                // Already fast start: adding metadata again still rewrites (moov grows in front of mdat) and replaces it.
                Assert.IsTrue(Mp4FastStart.Apply(path, new Mp4Metadata("Second", null), CancellationToken.None));
                file = File.ReadAllBytes(path);
                AssertSamplesIntact(file, samples, false);
                Assert.AreEqual("Second", IlstText(file, "nam"));
                Assert.IsNull(IlstText(file, "day"));
                Assert.AreEqual(1, CountChildren(file, "moov", "udta"));
                Assert.AreEqual(1, CountChildren(file, "udta", "meta"));
            }
        }

        [Test]
        public void Metadata_KeepsOtherUserDataBoxes()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("u.mp4");
                byte[] oldMeta = Box("meta", new byte[4], Box("hdlr", new byte[25]));
                byte[] other = Box("Xtra", new byte[] { 1, 2, 3, 4 });
                List<Sample> samples = WriteMoovAtEnd(path, 3, 500, true, Box("udta", other, oldMeta));
                Assert.IsTrue(Mp4FastStart.Apply(path, new Mp4Metadata("T", null), CancellationToken.None));
                byte[] file = File.ReadAllBytes(path);
                AssertSamplesIntact(file, samples, true);
                Assert.AreEqual(1, CountChildren(file, "udta", "Xtra"));
                Assert.AreEqual(1, CountChildren(file, "udta", "meta"));
                Assert.AreEqual("T", IlstText(file, "nam"));
            }
        }

        [Test]
        public void FastStartWithoutMetadata_IsNotRewritten()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("f.mp4");
                WriteMoovAtEnd(path, 3, 100, false);
                Mp4FastStart.Apply(path, CancellationToken.None);
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                Assert.IsFalse(Mp4FastStart.Apply(path, new Mp4Metadata("  ", null), CancellationToken.None), "empty metadata = nothing to do");
                Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(path));
            }
        }

        // QA finding 3: cancellation left "<file>.faststart.tmp" behind (up to the full MP4 size).
        [Test]
        public void PreCancelled_LeavesNoTempFile_AndOriginalUntouched()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("c.mp4");
                WriteMoovAtEnd(path, 4, 1000, false);
                byte[] original = File.ReadAllBytes(path);
                var cts = new CancellationTokenSource();
                cts.Cancel();
                Assert.Catch<OperationCanceledException>(() => Mp4FastStart.Apply(path, null, cts.Token));
                Assert.IsFalse(File.Exists(path + Mp4FastStart.TempSuffix));
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            }
        }

        [Test]
        public void CancelledMidCopy_LeavesNoTempFile_AndOriginalUntouched()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("big.mp4");
                WriteMoovAtEnd(path, 6, 700000, false); // 4.2 MB of media: several 1 MB copy chunks
                byte[] original = File.ReadAllBytes(path);
                var cts = new CancellationTokenSource();
                bool tempSeen = false;
                var progress = new InlineProgress<long>(copied =>
                {
                    tempSeen |= File.Exists(path + Mp4FastStart.TempSuffix);
                    if (copied >= 1 << 20) cts.Cancel();
                });
                Assert.Catch<OperationCanceledException>(() => Mp4FastStart.Apply(path, new Mp4Metadata("x", null), cts.Token, progress));
                Assert.IsTrue(tempSeen, "the temp file should have existed during the copy");
                Assert.IsFalse(File.Exists(path + Mp4FastStart.TempSuffix), "temp file left behind");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            }
        }

        [Test]
        public void MalformedFile_Throws_AndLeavesNoTempFile()
        {
            using (var dir = new TempDir())
            {
                string path = dir.File("bad.mp4");
                File.WriteAllBytes(path, new byte[] { 0, 0, 0, 100, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 1, 2 });
                Assert.Throws<InvalidDataException>(() => Mp4FastStart.Apply(path, CancellationToken.None));
                Assert.IsFalse(File.Exists(path + Mp4FastStart.TempSuffix));
            }
        }

        [Test]
        public void Mp4Metadata_CleansTitle_AndFormatsUtcDate()
        {
            var m = new Mp4Metadata("  a\tb\n ", new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc));
            Assert.AreEqual("a b", m.Title);
            Assert.AreEqual("2026-01-02T03:04:05Z", m.CreationTimeIso8601);
            Assert.IsTrue(new Mp4Metadata(" ", null).IsEmpty);
            Assert.AreEqual(Mp4Metadata.MaxTitleLength, new Mp4Metadata(new string('x', 400), null).Title.Length);
        }
    }
}
