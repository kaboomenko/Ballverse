using System;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Compression;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>
    /// Rebuilds .bvs files from the outside (independently of the writer) so corrupt content can be placed behind valid
    /// checksums. Mirrors the documented v1 layout: header · blocks · shape table · label table · index · footer.
    /// </summary>
    internal static class BvsSurgery
    {
        public const int FooterSize = 44;

        public sealed class Parts
        {
            public byte[] Header;
            public byte[][] RawBlocks;
            public int[] FirstTicks;
            public byte[] ShapeTable;
            public byte[] LabelTable;
            public uint TickCount;
        }

        public static Parts Split(byte[] file)
        {
            int f = file.Length - FooterSize;
            long tablesOffset = (long)BitConverter.ToUInt64(file, f);
            int shapeLength = (int)BitConverter.ToUInt32(file, f + 8);
            int labelLength = (int)BitConverter.ToUInt32(file, f + 12);
            long indexOffset = (long)BitConverter.ToUInt64(file, f + 16);
            int blockCount = (int)BitConverter.ToUInt32(file, f + 24);
            var parts = new Parts
            {
                TickCount = BitConverter.ToUInt32(file, f + 28),
                RawBlocks = new byte[blockCount][],
                FirstTicks = new int[blockCount],
                ShapeTable = Slice(file, tablesOffset, shapeLength),
                LabelTable = Slice(file, tablesOffset + shapeLength, labelLength),
            };
            long headerLength = blockCount > 0 ? (long)BitConverter.ToUInt64(file, (int)indexOffset + 4) : tablesOffset;
            parts.Header = Slice(file, 0, (int)headerLength);
            for (int i = 0; i < blockCount; i++)
            {
                int e = (int)indexOffset + i * 28;
                parts.FirstTicks[i] = (int)BitConverter.ToUInt32(file, e);
                long offset = (long)BitConverter.ToUInt64(file, e + 4);
                int compressed = (int)BitConverter.ToUInt32(file, e + 12);
                int raw = (int)BitConverter.ToUInt32(file, e + 16);
                parts.RawBlocks[i] = Lz4Block.Decompress(new ReadOnlySpan<byte>(file, (int)offset, compressed), raw);
            }
            return parts;
        }

        public static byte[] Join(Parts parts)
        {
            var output = new MemoryStream();
            output.Write(parts.Header, 0, parts.Header.Length);
            var index = new MemoryStream();
            var iw = new BinaryWriter(index);
            for (int i = 0; i < parts.RawBlocks.Length; i++)
            {
                byte[] raw = parts.RawBlocks[i];
                byte[] compressed = Lz4Block.Compress(raw);
                iw.Write((uint)parts.FirstTicks[i]);
                iw.Write((ulong)output.Length);
                iw.Write((uint)compressed.Length);
                iw.Write((uint)raw.Length);
                iw.Write(XxHash64.Hash(raw));
                output.Write(compressed, 0, compressed.Length);
            }
            iw.Flush();
            long tablesOffset = output.Length;
            output.Write(parts.ShapeTable, 0, parts.ShapeTable.Length);
            output.Write(parts.LabelTable, 0, parts.LabelTable.Length);
            long indexOffset = output.Length;
            byte[] indexBytes = index.ToArray();
            output.Write(indexBytes, 0, indexBytes.Length);

            var footer = new MemoryStream();
            var fw = new BinaryWriter(footer);
            fw.Write((ulong)tablesOffset);
            fw.Write((uint)parts.ShapeTable.Length);
            fw.Write((uint)parts.LabelTable.Length);
            fw.Write((ulong)indexOffset);
            fw.Write((uint)parts.RawBlocks.Length);
            fw.Write(parts.TickCount);
            fw.Flush();
            var meta = new XxHash64();
            meta.Append(parts.Header);
            meta.Append(parts.ShapeTable);
            meta.Append(parts.LabelTable);
            meta.Append(indexBytes);
            meta.Append(footer.ToArray());
            fw.Write(meta.Digest());
            fw.Write(new[] { (byte)'B', (byte)'V', (byte)'S', (byte)'X' });
            fw.Flush();
            byte[] footerBytes = footer.ToArray();
            output.Write(footerBytes, 0, footerBytes.Length);
            return output.ToArray();
        }

        /// <summary>Rebuilds the file with each block's decompressed bytes passed through <paramref name="mutate"/>.</summary>
        public static byte[] Reseal(byte[] file, Func<int, byte[], byte[]> mutate)
        {
            var parts = Split(file);
            for (int i = 0; i < parts.RawBlocks.Length; i++) parts.RawBlocks[i] = mutate(i, parts.RawBlocks[i]);
            return Join(parts);
        }

        /// <summary>Rebuilds the file with replaced shape and/or label table bytes (null keeps the original).</summary>
        public static byte[] ResealTables(byte[] file, byte[] shapeTable, byte[] labelTable)
        {
            var parts = Split(file);
            if (shapeTable != null) parts.ShapeTable = shapeTable;
            if (labelTable != null) parts.LabelTable = labelTable;
            return Join(parts);
        }

        static byte[] Slice(byte[] data, long offset, int length)
        {
            var result = new byte[length];
            Array.Copy(data, offset, result, 0, length);
            return result;
        }
    }
}
