using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Ballverse.Media
{
    /// <summary>
    /// MP4 post-processing without re-encoding: moves the <c>moov</c> box in front of the media data ("fast start",
    /// spec §3.8) and optionally stores <see cref="Mp4Metadata"/> (title/date) in <c>moov/udta</c>. The top-level boxes are
    /// rewritten as [boxes before the first mdat] [moov] [the rest]; every chunk offset in <c>stco</c>/<c>co64</c> is
    /// remapped to where its byte moved. Used on Android (MediaMuxer writes moov at the end and cannot store a title) and
    /// for the concatenated export. Pure C#; call from a worker thread.
    /// </summary>
    public static class Mp4FastStart
    {
        /// <summary>Suffix of the temp file next to the MP4 during a rewrite. It is deleted on any failure or cancellation.</summary>
        public const string TempSuffix = ".faststart.tmp";

        const int CopyChunkBytes = 1 << 20;
        const long MaxMoovBytes = 64L * 1024 * 1024;

        struct Box
        {
            public long Offset;
            public long Size;
            public string Type;
        }

        /// <summary>True if moov already precedes the first mdat (or there is no mdat).</summary>
        public static bool IsFastStart(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                List<Box> boxes = ReadTopLevel(fs);
                int moov = IndexOf(boxes, "moov");
                int mdat = IndexOf(boxes, "mdat");
                if (moov < 0) throw new InvalidDataException("no moov box: " + path);
                return mdat < 0 || moov < mdat;
            }
        }

        /// <summary>Fast start only (see <see cref="Apply(string, Mp4Metadata, CancellationToken, IProgress{long})"/>).</summary>
        public static bool Apply(string path, CancellationToken ct)
        {
            return Apply(path, null, ct);
        }

        /// <summary>
        /// Rewrites <paramref name="path"/> in place (temp file <c>path + <see cref="TempSuffix"/></c> + rename) so that moov
        /// precedes the media data and, when <paramref name="metadata"/> is not empty, carries its title/date (replacing an
        /// existing <c>udta/meta</c>). Returns false (file untouched) if moov already is at the front and there is no
        /// metadata to add. On cancellation or any failure the temp file is deleted and the original file is unchanged.
        /// </summary>
        /// <param name="progress">Optional: bytes copied so far, reported after every 1 MB chunk (on the calling thread).</param>
        /// <exception cref="InvalidDataException">Malformed MP4, no moov, or a 32-bit chunk offset would overflow.</exception>
        public static bool Apply(string path, Mp4Metadata metadata, CancellationToken ct, IProgress<long> progress = null)
        {
            if (metadata != null && metadata.IsEmpty) metadata = null;
            ct.ThrowIfCancellationRequested();
            string tmp = path + TempSuffix;
            try
            {
                bool rewritten = Rewrite(path, tmp, metadata, ct, progress);
                if (rewritten) PcmFile.ReplaceWith(tmp, path);
                return rewritten;
            }
            catch
            {
                PcmFile.DeleteQuietly(tmp);
                throw;
            }
        }

        static bool Rewrite(string path, string tmp, Mp4Metadata metadata, CancellationToken ct, IProgress<long> progress)
        {
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                List<Box> boxes = ReadTopLevel(src);
                int moovIndex = IndexOf(boxes, "moov");
                int mdatIndex = IndexOf(boxes, "mdat");
                if (moovIndex < 0) throw new InvalidDataException("no moov box: " + path);
                bool move = mdatIndex >= 0 && moovIndex > mdatIndex;
                if (!move && metadata == null) return false;

                Box moovBox = boxes[moovIndex];
                if (moovBox.Size > MaxMoovBytes) throw new InvalidDataException("moov box too large: " + moovBox.Size);
                var moov = new byte[moovBox.Size];
                src.Position = moovBox.Offset;
                if (PcmFile.ReadFully(src, moov, moov.Length) != moov.Length) throw new InvalidDataException("truncated moov");
                if (metadata != null) moov = Mp4MetadataBox.InsertInto(moov, metadata);

                // New layout: every box except moov keeps its order; moov goes right before the first mdat (or stays
                // where it was when there is no mdat).
                var order = new List<int>(boxes.Count);
                for (int i = 0; i < boxes.Count; i++) if (i != moovIndex) order.Add(i);
                int insertAt = mdatIndex >= 0 ? order.IndexOf(mdatIndex) : Math.Min(moovIndex, order.Count);
                order.Insert(insertAt, moovIndex);
                var newOffsets = new long[boxes.Count];
                long pos = 0;
                foreach (int i in order)
                {
                    newOffsets[i] = pos;
                    pos += i == moovIndex ? moov.Length : boxes[i].Size;
                }

                var map = new OffsetMap(boxes, newOffsets, moovIndex);
                int moovHeader = 8 + HeaderExtra(moov, 0);
                PatchOffsets(moov, moovHeader, moov.Length, map, true);
                PatchOffsets(moov, moovHeader, moov.Length, map, false);

                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var buffer = new byte[CopyChunkBytes];
                    long copied = 0;
                    foreach (int i in order)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (i == moovIndex)
                        {
                            dst.Write(moov, 0, moov.Length);
                            continue;
                        }
                        Copy(src, dst, boxes[i].Offset, boxes[i].Size, buffer, ct, progress, ref copied);
                    }
                    dst.Flush();
                }
                return true;
            }
        }

        /// <summary>Maps an offset inside an original top-level box to its position in the rewritten file.</summary>
        sealed class OffsetMap
        {
            readonly List<Box> _boxes;
            readonly long[] _newOffsets;
            readonly int _moovIndex;

            public OffsetMap(List<Box> boxes, long[] newOffsets, int moovIndex)
            {
                _boxes = boxes;
                _newOffsets = newOffsets;
                _moovIndex = moovIndex;
            }

            public long Map(long offset)
            {
                for (int i = 0; i < _boxes.Count; i++)
                {
                    Box b = _boxes[i];
                    if (offset < b.Offset || offset >= b.Offset + b.Size) continue;
                    if (i == _moovIndex) throw new InvalidDataException("chunk offset points into moov");
                    return _newOffsets[i] + (offset - b.Offset);
                }
                throw new InvalidDataException("chunk offset " + offset + " is outside the file's boxes");
            }
        }

        static void Copy(FileStream src, FileStream dst, long offset, long count, byte[] buffer, CancellationToken ct,
            IProgress<long> progress, ref long copied)
        {
            src.Position = offset;
            while (count > 0)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buffer.Length, count);
                int got = PcmFile.ReadFully(src, buffer, want);
                if (got != want) throw new InvalidDataException("unexpected end of file");
                dst.Write(buffer, 0, got);
                count -= got;
                copied += got;
                progress?.Report(copied);
            }
        }

        static int IndexOf(List<Box> boxes, string type)
        {
            for (int i = 0; i < boxes.Count; i++) if (boxes[i].Type == type) return i;
            return -1;
        }

        static List<Box> ReadTopLevel(FileStream fs)
        {
            var boxes = new List<Box>();
            long length = fs.Length;
            long pos = 0;
            var header = new byte[16];
            while (pos + 8 <= length)
            {
                fs.Position = pos;
                if (PcmFile.ReadFully(fs, header, 8) != 8) throw new InvalidDataException("truncated box header");
                long size = Mp4Bytes.ReadU32(header, 0);
                string type = Mp4Bytes.TypeOf(header, 4);
                if (size == 1)
                {
                    if (PcmFile.ReadFully(fs, header, 8) != 8) throw new InvalidDataException("truncated largesize");
                    size = (long)Mp4Bytes.ReadU64(header, 0);
                }
                else if (size == 0)
                {
                    size = length - pos;
                }
                if (size < 8 || pos + size > length) throw new InvalidDataException("bad box size for '" + type + "' at " + pos);
                boxes.Add(new Box { Offset = pos, Size = size, Type = type });
                pos += size;
            }
            return boxes;
        }

        /// <summary>Extra header bytes of the box at <paramref name="at"/> (8 for a 64-bit largesize).</summary>
        static int HeaderExtra(byte[] data, int at)
        {
            return Mp4Bytes.ReadU32(data, at) == 1 ? 8 : 0;
        }

        /// <summary>Walks child boxes in [start, end). First pass validates (checkOnly), second patches.</summary>
        static void PatchOffsets(byte[] data, int start, int end, OffsetMap map, bool checkOnly)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                long size = Mp4Bytes.ReadU32(data, pos);
                string type = Mp4Bytes.TypeOf(data, pos + 4);
                int headerSize = 8;
                if (size == 1)
                {
                    size = (long)Mp4Bytes.ReadU64(data, pos + 8);
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    size = end - pos;
                }
                if (size < headerSize || pos + size > end) throw new InvalidDataException("bad child box size for '" + type + "'");
                int body = pos + headerSize;
                int boxEnd = (int)(pos + size);
                switch (type)
                {
                    case "trak":
                    case "mdia":
                    case "minf":
                    case "stbl":
                        PatchOffsets(data, body, boxEnd, map, checkOnly);
                        break;
                    case "stco":
                    {
                        long count = Mp4Bytes.ReadU32(data, body + 4);
                        if (body + 8 + count * 4 > boxEnd) throw new InvalidDataException("stco overflows its box");
                        for (long i = 0; i < count; i++)
                        {
                            int at = (int)(body + 8 + i * 4);
                            long nv = map.Map(Mp4Bytes.ReadU32(data, at));
                            if (nv > uint.MaxValue) throw new InvalidDataException("32-bit chunk offset overflow (file > 4 GB needs co64)");
                            if (!checkOnly) Mp4Bytes.WriteU32(data, at, (uint)nv);
                        }
                        break;
                    }
                    case "co64":
                    {
                        long count = Mp4Bytes.ReadU32(data, body + 4);
                        if (body + 8 + count * 8 > boxEnd) throw new InvalidDataException("co64 overflows its box");
                        for (long i = 0; i < count; i++)
                        {
                            int at = (int)(body + 8 + i * 8);
                            long nv = map.Map((long)Mp4Bytes.ReadU64(data, at));
                            if (!checkOnly) Mp4Bytes.WriteU64(data, at, (ulong)nv);
                        }
                        break;
                    }
                }
                pos = boxEnd;
            }
        }
    }

    /// <summary>Big-endian helpers for MP4 boxes.</summary>
    internal static class Mp4Bytes
    {
        public static string TypeOf(byte[] b, int at)
        {
            return new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] });
        }

        public static long ReadU32(byte[] b, int at)
        {
            return ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];
        }

        public static ulong ReadU64(byte[] b, int at)
        {
            return ((ulong)ReadU32(b, at) << 32) | (ulong)ReadU32(b, at + 4);
        }

        public static void WriteU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        public static void WriteU64(byte[] b, int at, ulong v)
        {
            WriteU32(b, at, (uint)(v >> 32));
            WriteU32(b, at + 4, (uint)v);
        }
    }
}
