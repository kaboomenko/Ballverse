using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Ballverse.Media
{
    /// <summary>
    /// Moves the <c>moov</c> box of an MP4 in front of the media data ("fast start", spec §3.8) without re-encoding:
    /// the top-level boxes are rewritten as [boxes before the first mdat] [moov] [the rest], and every chunk offset in
    /// <c>stco</c>/<c>co64</c> that points into the moved range is shifted by the moov size. Used on Android, where
    /// MediaMuxer writes moov at the end (iOS writes it at the front itself). Pure C#; call from a worker thread.
    /// </summary>
    public static class Mp4FastStart
    {
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

        /// <summary>
        /// Rewrites <paramref name="path"/> in place (temp file + rename). Returns true if the file was rewritten,
        /// false if it already was fast-start.
        /// </summary>
        /// <exception cref="InvalidDataException">Malformed MP4, no moov, or a 32-bit chunk offset would overflow.</exception>
        public static bool Apply(string path, CancellationToken ct)
        {
            string tmp = path + ".faststart.tmp";
            bool rewritten;
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                List<Box> boxes = ReadTopLevel(src);
                int moovIndex = IndexOf(boxes, "moov");
                int mdatIndex = IndexOf(boxes, "mdat");
                if (moovIndex < 0) throw new InvalidDataException("no moov box: " + path);
                if (mdatIndex < 0 || moovIndex < mdatIndex) return false;

                Box moovBox = boxes[moovIndex];
                if (moovBox.Size > 64L * 1024 * 1024) throw new InvalidDataException("moov box too large: " + moovBox.Size);
                var moov = new byte[moovBox.Size];
                src.Position = moovBox.Offset;
                if (PcmFile.ReadFully(src, moov, moov.Length) != moov.Length) throw new InvalidDataException("truncated moov");

                long insertPos = boxes[mdatIndex].Offset;
                long oldMoovPos = moovBox.Offset;
                long shift = moovBox.Size;
                PatchOffsets(moov, 8 + HeaderExtra(moov, 0), moov.Length, insertPos, oldMoovPos, shift, true);
                PatchOffsets(moov, 8 + HeaderExtra(moov, 0), moov.Length, insertPos, oldMoovPos, shift, false);

                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var buffer = new byte[1 << 20];
                    Copy(src, dst, 0, insertPos, buffer, ct);
                    dst.Write(moov, 0, moov.Length);
                    Copy(src, dst, insertPos, oldMoovPos - insertPos, buffer, ct);
                    long tail = oldMoovPos + moovBox.Size;
                    Copy(src, dst, tail, src.Length - tail, buffer, ct);
                    dst.Flush();
                }
                rewritten = true;
            }
            if (rewritten)
            {
                File.Delete(path);
                File.Move(tmp, path);
            }
            return rewritten;
        }

        static void Copy(FileStream src, FileStream dst, long offset, long count, byte[] buffer, CancellationToken ct)
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
                long size = ReadU32(header, 0);
                string type = TypeOf(header, 4);
                if (size == 1)
                {
                    if (PcmFile.ReadFully(fs, header, 8) != 8) throw new InvalidDataException("truncated largesize");
                    size = (long)ReadU64(header, 0);
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
            return ReadU32(data, at) == 1 ? 8 : 0;
        }

        /// <summary>Walks child boxes in [start, end). First pass validates (checkOnly), second patches.</summary>
        static void PatchOffsets(byte[] data, int start, int end, long insertPos, long oldMoovPos, long shift, bool checkOnly)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                long size = ReadU32(data, pos);
                string type = TypeOf(data, pos + 4);
                int headerSize = 8;
                if (size == 1)
                {
                    size = (long)ReadU64(data, pos + 8);
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
                        PatchOffsets(data, body, boxEnd, insertPos, oldMoovPos, shift, checkOnly);
                        break;
                    case "stco":
                    {
                        long count = ReadU32(data, body + 4);
                        if (body + 8 + count * 4 > boxEnd) throw new InvalidDataException("stco overflows its box");
                        for (long i = 0; i < count; i++)
                        {
                            int at = (int)(body + 8 + i * 4);
                            long v = ReadU32(data, at);
                            long nv = Shifted(v, insertPos, oldMoovPos, shift);
                            if (nv > uint.MaxValue) throw new InvalidDataException("32-bit chunk offset overflow (file > 4 GB needs co64)");
                            if (!checkOnly) WriteU32(data, at, (uint)nv);
                        }
                        break;
                    }
                    case "co64":
                    {
                        long count = ReadU32(data, body + 4);
                        if (body + 8 + count * 8 > boxEnd) throw new InvalidDataException("co64 overflows its box");
                        for (long i = 0; i < count; i++)
                        {
                            int at = (int)(body + 8 + i * 8);
                            long v = (long)ReadU64(data, at);
                            if (!checkOnly) WriteU64(data, at, (ulong)Shifted(v, insertPos, oldMoovPos, shift));
                        }
                        break;
                    }
                }
                pos = boxEnd;
            }
        }

        static long Shifted(long offset, long insertPos, long oldMoovPos, long shift)
        {
            return offset >= insertPos && offset < oldMoovPos ? offset + shift : offset;
        }

        static string TypeOf(byte[] b, int at)
        {
            return new string(new[] { (char)b[at], (char)b[at + 1], (char)b[at + 2], (char)b[at + 3] });
        }

        static long ReadU32(byte[] b, int at)
        {
            return ((long)b[at] << 24) | ((long)b[at + 1] << 16) | ((long)b[at + 2] << 8) | b[at + 3];
        }

        static ulong ReadU64(byte[] b, int at)
        {
            return ((ulong)ReadU32(b, at) << 32) | (ulong)ReadU32(b, at + 4);
        }

        static void WriteU32(byte[] b, int at, uint v)
        {
            b[at] = (byte)(v >> 24);
            b[at + 1] = (byte)(v >> 16);
            b[at + 2] = (byte)(v >> 8);
            b[at + 3] = (byte)v;
        }

        static void WriteU64(byte[] b, int at, ulong v)
        {
            WriteU32(b, at, (uint)(v >> 32));
            WriteU32(b, at + 4, (uint)v);
        }
    }
}
