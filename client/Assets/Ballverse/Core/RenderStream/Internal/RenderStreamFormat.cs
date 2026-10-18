using System;
using System.IO;
using System.Text;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>
    /// Byte layout of a .bvs file, format v1 (spec §7.4). All integers little-endian.
    /// <code>
    /// Header:  magic "BVS1" · formatVersion u16 · tickRate u16 · blockTicks u16
    ///          positionRange f32 · scaleMax f32 · gridWidth u16 · gridHeight u16
    ///          [gridTopology u8 · gridMinX i16 · gridMinY i16 · gridMaxX i16 · gridMaxY i16]   (only when gridWidth > 0)
    ///          modeIdLength u8 · modeId UTF-8
    /// Blocks:  LZ4 block-compressed frame data (see FrameCodec), contiguous, in tick order
    /// Tables:  shape table · label table (see TableCodec), uncompressed, immutable
    /// Index:   blockCount × (firstTick u32 · offset u64 · compressedLength u32 · rawLength u32 · rawHash u64)
    /// Footer:  tablesOffset u64 · shapeTableLength u32 · labelTableLength u32
    ///          indexOffset u64 · blockCount u32 · tickCount u32 · metaHash u64 · magic "BVSX"
    /// </code>
    /// Offsets are relative to the start of the header. Sections are contiguous: blocks end at tablesOffset,
    /// tablesOffset + shapeTableLength + labelTableLength = indexOffset, the footer follows the index and ends the file.
    /// The tables are written at the end because shapes and labels may be defined while frames are streamed out; the
    /// reader loads them on open. gridWidth = gridHeight = 0 means no grid layer.
    /// rawHash = XXH64 of the decompressed block; metaHash = XXH64 of the header bytes, the table bytes, the index bytes
    /// and the first 32 footer bytes, so every byte outside the blocks is covered by a checksum and every block byte by its
    /// rawHash (after decompression).
    /// <para>
    /// Format history: v1 was revised in place before release (shape table, label table and grid layer added; the
    /// per-body kind byte replaced by shape and label ids) because nothing consumed .bvs files yet. Any further change
    /// must bump <see cref="RenderStreamHeader.CurrentFormatVersion"/>.
    /// </para>
    /// </summary>
    internal static class RenderStreamFormat
    {
        public const int MinHeaderSize = 4 + 2 + 2 + 2 + 4 + 4 + 2 + 2 + 1;
        public const int GridHeaderSize = 1 + 2 + 2 + 2 + 2;
        public const int MaxHeaderSize = MinHeaderSize + GridHeaderSize + RenderStreamHeader.MaxModeIdBytes;
        public const int IndexEntrySize = 4 + 8 + 4 + 4 + 8;
        public const int FooterSize = 8 + 4 + 4 + 8 + 4 + 4 + 8 + 4;
        /// <summary>Footer bytes covered by metaHash (everything before metaHash).</summary>
        public const int FooterHashedSize = 8 + 4 + 4 + 8 + 4 + 4;
        /// <summary>Allocation guard for a single decompressed block.</summary>
        public const int MaxRawBlockBytes = 256 * 1024 * 1024;

        static readonly byte[] s_headerMagic = { (byte)'B', (byte)'V', (byte)'S', (byte)'1' };
        static readonly byte[] s_footerMagic = { (byte)'B', (byte)'V', (byte)'S', (byte)'X' };
        static readonly UTF8Encoding s_strictUtf8 = new UTF8Encoding(false, true);

        public static ReadOnlySpan<byte> FooterMagic => s_footerMagic;

        public static void WriteHeader(ByteWriter w, RenderStreamHeader header)
        {
            w.WriteBytes(s_headerMagic);
            w.WriteUInt16(RenderStreamHeader.CurrentFormatVersion);
            w.WriteUInt16(header.TickRate);
            w.WriteUInt16(header.BlockTicks);
            w.WriteUInt32((uint)BitConverter.SingleToInt32Bits(header.Quantization.PositionRange));
            w.WriteUInt32((uint)BitConverter.SingleToInt32Bits(header.Quantization.ScaleMax));
            RenderGridSpec grid = header.Grid;
            w.WriteUInt16(grid == null ? 0 : grid.Width);
            w.WriteUInt16(grid == null ? 0 : grid.Height);
            if (grid != null)
            {
                w.WriteByte((byte)grid.Topology);
                w.WriteUInt16(grid.MinX);
                w.WriteUInt16(grid.MinY);
                w.WriteUInt16(grid.MaxX);
                w.WriteUInt16(grid.MaxY);
            }
            byte[] mode = Encoding.UTF8.GetBytes(header.ModeId);
            w.WriteByte((byte)mode.Length);
            w.WriteBytes(mode);
        }

        /// <summary>Parses a header from the start of <paramref name="data"/>; returns it and its byte length.</summary>
        public static RenderStreamHeader ReadHeader(byte[] data, int length, out int headerLength)
        {
            var r = new ByteReader(data, 0, length);
            var magic = r.ReadBytes(4);
            if (!magic.SequenceEqual(s_headerMagic)) throw new InvalidDataException("Not a .bvs render stream (bad magic)");
            int version = r.ReadUInt16();
            if (version != RenderStreamHeader.CurrentFormatVersion) throw new InvalidDataException("Unsupported .bvs format version " + version);
            int tickRate = r.ReadUInt16();
            int blockTicks = r.ReadUInt16();
            if (tickRate < 1 || tickRate > RenderStreamHeader.MaxTickRate) throw new InvalidDataException("Invalid tick rate " + tickRate);
            if (blockTicks < 1 || blockTicks > RenderStreamHeader.MaxBlockTicks) throw new InvalidDataException("Invalid block length " + blockTicks);
            float positionRange = BitConverter.Int32BitsToSingle((int)r.ReadUInt32());
            float scaleMax = BitConverter.Int32BitsToSingle((int)r.ReadUInt32());
            if (!RenderQuantization.IsValidParameter(positionRange) || !RenderQuantization.IsValidParameter(scaleMax))
            {
                throw new InvalidDataException("Invalid quantization parameters");
            }

            int gridWidth = r.ReadUInt16();
            int gridHeight = r.ReadUInt16();
            RenderGridSpec grid = null;
            if (gridWidth != 0 || gridHeight != 0)
            {
                if (gridWidth < 1 || gridWidth > RenderStreamLimits.MaxGridWidth || gridHeight < 1 || gridHeight > RenderStreamLimits.MaxGridHeight)
                {
                    throw new InvalidDataException("Invalid grid size " + gridWidth + "x" + gridHeight);
                }
                byte topology = r.ReadByte();
                if (topology > (byte)GridTopology.HexOddRowsShifted) throw new InvalidDataException("Invalid grid topology " + topology);
                short minX = (short)r.ReadUInt16();
                short minY = (short)r.ReadUInt16();
                short maxX = (short)r.ReadUInt16();
                short maxY = (short)r.ReadUInt16();
                if (minX >= maxX || minY >= maxY) throw new InvalidDataException("Invalid grid bounds");
                grid = new RenderGridSpec(gridWidth, gridHeight, (GridTopology)topology, minX, minY, maxX, maxY);
            }

            int modeLength = r.ReadByte();
            if (modeLength > RenderStreamHeader.MaxModeIdBytes) throw new InvalidDataException("Mode id too long");
            var modeBytes = r.ReadBytes(modeLength);
            string modeId;
            try
            {
                modeId = s_strictUtf8.GetString(modeBytes.ToArray());
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException("Mode id is not valid UTF-8", e);
            }
            headerLength = r.Offset;
            return new RenderStreamHeader(modeId, tickRate, blockTicks, new RenderQuantization(positionRange, scaleMax), grid);
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes or throws <see cref="InvalidDataException"/>.</summary>
        public static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int n = stream.Read(buffer, offset, count);
                if (n <= 0) throw new InvalidDataException("Unexpected end of render stream");
                offset += n;
                count -= n;
            }
        }
    }
}
