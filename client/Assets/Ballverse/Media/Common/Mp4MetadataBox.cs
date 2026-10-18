using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ballverse.Media
{
    /// <summary>
    /// Builds the iTunes-style metadata box used by MP4 players and ffprobe ("title", "date"):
    /// <c>udta / meta (hdlr 'mdir') / ilst / {©nam, ©day} / data (UTF-8)</c>, and inserts it into a <c>moov</c> box.
    /// No location or personal data is ever written (spec §3.8). Pure C#.
    /// </summary>
    internal static class Mp4MetadataBox
    {
        const byte Copyright = 0xA9; // '©' in box types

        /// <summary>
        /// Returns a copy of <paramref name="moov"/> (a complete moov box) whose <c>udta</c> holds the metadata: an existing
        /// udta keeps its other children (any old <c>meta</c> is replaced); otherwise a new udta is appended to moov.
        /// Chunk offsets inside are left as they are (the caller remaps them).
        /// </summary>
        public static byte[] InsertInto(byte[] moov, Mp4Metadata metadata)
        {
            if (moov == null) throw new ArgumentNullException(nameof(moov));
            if (metadata == null || metadata.IsEmpty) return moov;
            if (moov.Length < 8 || Mp4Bytes.TypeOf(moov, 4) != "moov") throw new InvalidDataException("not a moov box");
            int header = Mp4Bytes.ReadU32(moov, 0) == 1 ? 16 : 8;

            byte[] meta = BuildMeta(metadata);
            var children = new List<(int offset, int size, string type)>();
            ReadChildren(moov, header, moov.Length, children);

            var udtaBody = new MemoryStream();
            var moovBody = new MemoryStream();
            foreach (var c in children)
            {
                if (c.type != "udta")
                {
                    moovBody.Write(moov, c.offset, c.size);
                    continue;
                }
                int udtaHeader = Mp4Bytes.ReadU32(moov, c.offset) == 1 ? 16 : 8;
                var udtaChildren = new List<(int offset, int size, string type)>();
                ReadChildren(moov, c.offset + udtaHeader, c.offset + c.size, udtaChildren);
                foreach (var u in udtaChildren)
                {
                    if (u.type != "meta") udtaBody.Write(moov, u.offset, u.size);
                }
            }
            udtaBody.Write(meta, 0, meta.Length);
            byte[] udta = Box("udta", udtaBody.ToArray());
            moovBody.Write(udta, 0, udta.Length);
            return Box("moov", moovBody.ToArray());
        }

        /// <summary>meta (FullBox) = hdlr 'mdir' + ilst.</summary>
        internal static byte[] BuildMeta(Mp4Metadata metadata)
        {
            var ilst = new MemoryStream();
            if (metadata.Title != null) WriteItem(ilst, "nam", metadata.Title);
            if (metadata.CreationTimeUtc.HasValue) WriteItem(ilst, "day", metadata.CreationTimeIso8601);

            var hdlr = new MemoryStream();
            hdlr.Write(new byte[4], 0, 4);                          // version + flags
            hdlr.Write(new byte[4], 0, 4);                          // pre_defined
            WriteAscii(hdlr, "mdir");                               // handler_type
            WriteAscii(hdlr, "appl");                               // reserved[0] (as written by iTunes/ffmpeg)
            hdlr.Write(new byte[8], 0, 8);                          // reserved[1..2]
            hdlr.WriteByte(0);                                      // empty name
            byte[] hdlrBox = Box("hdlr", hdlr.ToArray());
            byte[] ilstBox = Box("ilst", ilst.ToArray());

            var meta = new MemoryStream();
            meta.Write(new byte[4], 0, 4);                          // FullBox version + flags
            meta.Write(hdlrBox, 0, hdlrBox.Length);
            meta.Write(ilstBox, 0, ilstBox.Length);
            return Box("meta", meta.ToArray());
        }

        static void WriteItem(MemoryStream ilst, string name3, string value)
        {
            byte[] text = Encoding.UTF8.GetBytes(value);
            var data = new MemoryStream();
            data.Write(new byte[] { 0, 0, 0, 1 }, 0, 4);            // type indicator: UTF-8 text
            data.Write(new byte[4], 0, 4);                          // locale
            data.Write(text, 0, text.Length);
            byte[] dataBox = Box("data", data.ToArray());
            var item = new byte[8 + dataBox.Length];
            Mp4Bytes.WriteU32(item, 0, (uint)item.Length);
            item[4] = Copyright;
            item[5] = (byte)name3[0];
            item[6] = (byte)name3[1];
            item[7] = (byte)name3[2];
            Buffer.BlockCopy(dataBox, 0, item, 8, dataBox.Length);
            ilst.Write(item, 0, item.Length);
        }

        static void WriteAscii(MemoryStream s, string fourCc)
        {
            for (int i = 0; i < 4; i++) s.WriteByte((byte)fourCc[i]);
        }

        static byte[] Box(string type, byte[] body)
        {
            long size = 8L + body.Length;
            if (size > uint.MaxValue) throw new InvalidDataException(type + " box too large");
            var box = new byte[size];
            Mp4Bytes.WriteU32(box, 0, (uint)size);
            for (int i = 0; i < 4; i++) box[4 + i] = (byte)type[i];
            Buffer.BlockCopy(body, 0, box, 8, body.Length);
            return box;
        }

        static void ReadChildren(byte[] data, int start, int end, List<(int offset, int size, string type)> into)
        {
            int pos = start;
            while (pos + 8 <= end)
            {
                long size = Mp4Bytes.ReadU32(data, pos);
                if (size == 1) size = (long)Mp4Bytes.ReadU64(data, pos + 8);
                else if (size == 0) size = end - pos;
                if (size < 8 || pos + size > end) throw new InvalidDataException("bad child box size at " + pos);
                into.Add((pos, (int)size, Mp4Bytes.TypeOf(data, pos + 4)));
                pos += (int)size;
            }
        }
    }
}
