using System;
using System.IO;
using System.Text;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>
    /// Byte layout of a .bvs file (spec §7.4). All integers little-endian.
    /// <code>
    /// Header:  magic "BVS1" · formatVersion u16 · tickRate u16 · blockTicks u16
    ///          positionRange f32 · scaleMax f32 · modeIdLength u8 · modeId UTF-8
    /// Blocks:  LZ4 block-compressed frame data (see FrameCodec), contiguous, in tick order
    /// Index:   blockCount × (firstTick u32 · offset u64 · compressedLength u32 · rawLength u32 · rawHash u64)
    /// Footer:  indexOffset u64 · blockCount u32 · tickCount u32 · metaHash u64 · magic "BVSX"
    /// </code>
    /// Offsets are relative to the start of the header. rawHash = XXH64 of the decompressed block;
    /// metaHash = XXH64 of the header bytes, the index bytes and the first 16 footer bytes (indexOffset, blockCount, tickCount),
    /// so every byte outside the blocks is covered by a checksum and every block byte by its rawHash (after decompression).
    /// </summary>
    internal static class RenderStreamFormat
    {
        public const int MinHeaderSize = 4 + 2 + 2 + 2 + 4 + 4 + 1;
        public const int MaxHeaderSize = MinHeaderSize + RenderStreamHeader.MaxModeIdBytes;
        public const int IndexEntrySize = 4 + 8 + 4 + 4 + 8;
        public const int FooterSize = 8 + 4 + 4 + 8 + 4;
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
            return new RenderStreamHeader(modeId, tickRate, blockTicks, new RenderQuantization(positionRange, scaleMax));
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
