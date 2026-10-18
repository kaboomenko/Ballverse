using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Ballverse.Core.Compression;
using Ballverse.Core.RenderStream.Internal;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Random-access reader of a .bvs render stream. Preview, scrubbing and export draw frames from it
    /// (spec §7.4 "из него рисуются предпросмотр и экспорт", §7.5 step a, §7.7).
    /// <para>
    /// <see cref="ReadFrame(int, RenderFrame)"/> decodes the containing block from its keyframe: at most one block
    /// (≤ 1 s of frames) is decompressed and decoded per seek, which keeps scrubbing far below the 100 ms budget
    /// (spec §3.7). The last decoded block and the decoder position inside it are cached, so sequential playback
    /// decodes each frame exactly once and seeking backwards within the cached block skips decompression.
    /// </para>
    /// <para>
    /// The whole file is validated on open (header fields, footer, index contiguity, XXH64 of header + index);
    /// each block is verified against its XXH64 when loaded. Corrupt or truncated data throws
    /// <see cref="InvalidDataException"/> — from the constructor or from the read that touches the bad block.
    /// </para>
    /// <para>
    /// Memory profile: the index (≈ 40 bytes per second of stream), one compressed and one raw buffer of the largest
    /// block read so far, decoder state for the bodies of one frame, and the output frame. No per-frame allocations
    /// once buffers reached their peak size.
    /// </para>
    /// </summary>
    public sealed class RenderStreamReader : IDisposable
    {
        readonly Stream _stream;
        readonly bool _leaveOpen;
        readonly long _base;
        readonly RenderStreamHeader _header;
        readonly RenderStreamBlockInfo[] _blocks;
        readonly ReadOnlyCollection<RenderStreamBlockInfo> _blocksView;
        readonly int _tickCount;
        readonly FrameCodec _codec = new FrameCodec();
        readonly XxHash64 _blockHash = new XxHash64();
        byte[] _compressed = new byte[1024];
        byte[] _raw = new byte[1024];
        int _cachedBlock = -1;
        int _decodedLocal = -1;
        ByteReader _blockReader;
        bool _disposed;

        /// <summary>
        /// Opens a finished render stream occupying <paramref name="stream"/> from its current position to its end.
        /// The stream must be readable and seekable.
        /// </summary>
        public RenderStreamReader(Stream stream, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("Stream must be readable and seekable", nameof(stream));
            _stream = stream;
            _leaveOpen = leaveOpen;
            _base = stream.Position;
            long length = stream.Length - _base;
            if (length < RenderStreamFormat.MinHeaderSize + RenderStreamFormat.FooterSize) throw new InvalidDataException("Render stream too short");

            // Header.
            int headerProbe = (int)Math.Min(RenderStreamFormat.MaxHeaderSize, length - RenderStreamFormat.FooterSize);
            var headerBytes = new byte[headerProbe];
            RenderStreamFormat.ReadExactly(stream, headerBytes, 0, headerProbe);
            _header = RenderStreamFormat.ReadHeader(headerBytes, headerProbe, out int headerLength);

            // Footer.
            var footer = new byte[RenderStreamFormat.FooterSize];
            stream.Position = _base + length - RenderStreamFormat.FooterSize;
            RenderStreamFormat.ReadExactly(stream, footer, 0, footer.Length);
            var fr = new ByteReader(footer, 0, footer.Length);
            ulong indexOffset = fr.ReadUInt64();
            uint blockCount = fr.ReadUInt32();
            uint tickCount = fr.ReadUInt32();
            ulong metaHash = fr.ReadUInt64();
            if (!fr.ReadBytes(4).SequenceEqual(RenderStreamFormat.FooterMagic)) throw new InvalidDataException("Bad .bvs footer magic (file truncated or not finished?)");
            if (tickCount > int.MaxValue) throw new InvalidDataException("Invalid tick count");
            long expectedBlocks = ((long)tickCount + _header.BlockTicks - 1) / _header.BlockTicks;
            if (blockCount != expectedBlocks) throw new InvalidDataException("Block count does not match tick count");
            long indexLength = (long)blockCount * RenderStreamFormat.IndexEntrySize;
            if (indexOffset < (ulong)headerLength || indexOffset > (ulong)length || indexOffset + (ulong)indexLength + RenderStreamFormat.FooterSize != (ulong)length)
            {
                throw new InvalidDataException("Invalid index offset");
            }

            // Index.
            var index = new byte[indexLength];
            stream.Position = _base + (long)indexOffset;
            RenderStreamFormat.ReadExactly(stream, index, 0, index.Length);
            var hash = new XxHash64();
            hash.Append(new ReadOnlySpan<byte>(headerBytes, 0, headerLength));
            hash.Append(index);
            hash.Append(new ReadOnlySpan<byte>(footer, 0, 16));
            if (hash.Digest() != metaHash) throw new InvalidDataException("Render stream header/index checksum mismatch");

            _tickCount = (int)tickCount;
            _blocks = new RenderStreamBlockInfo[blockCount];
            var ir = new ByteReader(index, 0, index.Length);
            long expectedOffset = headerLength;
            for (int i = 0; i < _blocks.Length; i++)
            {
                uint firstTick = ir.ReadUInt32();
                ulong offset = ir.ReadUInt64();
                uint compressedLength = ir.ReadUInt32();
                uint rawLength = ir.ReadUInt32();
                ulong rawHash = ir.ReadUInt64();
                long expectedFirst = (long)i * _header.BlockTicks;
                if (firstTick != expectedFirst) throw new InvalidDataException("Block " + i + " has wrong first tick");
                if (offset != (ulong)expectedOffset) throw new InvalidDataException("Block " + i + " has wrong offset");
                if (compressedLength == 0 || compressedLength > indexOffset - (ulong)expectedOffset) throw new InvalidDataException("Block " + i + " has invalid compressed length");
                if (rawLength > RenderStreamFormat.MaxRawBlockBytes || rawLength > (ulong)compressedLength * 256UL) throw new InvalidDataException("Block " + i + " has invalid raw length");
                _blocks[i] = new RenderStreamBlockInfo
                {
                    FirstTick = (int)firstTick,
                    FrameCount = (int)Math.Min(_header.BlockTicks, tickCount - firstTick),
                    Offset = (long)offset,
                    CompressedLength = (int)compressedLength,
                    RawLength = (int)rawLength,
                    RawHash = rawHash,
                };
                expectedOffset += compressedLength;
            }
            if ((ulong)expectedOffset != indexOffset) throw new InvalidDataException("Blocks do not end at the index");
            _blocksView = Array.AsReadOnly(_blocks);
        }

        public RenderStreamHeader Header => _header;

        /// <summary>Number of frames (ticks) in the stream; valid ticks are [0, TickCount).</summary>
        public int TickCount => _tickCount;

        public int BlockCount => _blocks.Length;

        /// <summary>The validated block index (read-only view; the reader's internal copy cannot be modified through it).</summary>
        public IReadOnlyList<RenderStreamBlockInfo> Blocks => _blocksView;

        /// <summary>
        /// Decodes the frame of <paramref name="tick"/> into <paramref name="destination"/> (bodies sorted by id, HUD by key).
        /// On <see cref="InvalidDataException"/> the destination is left unchanged.
        /// </summary>
        public void ReadFrame(int tick, RenderFrame destination)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RenderStreamReader));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (tick < 0 || tick >= _tickCount) throw new ArgumentOutOfRangeException(nameof(tick));

            int block = tick / _header.BlockTicks;
            int local = tick - block * _header.BlockTicks;
            try
            {
                if (block != _cachedBlock)
                {
                    LoadBlock(block);
                }
                else if (local < _decodedLocal)
                {
                    RewindBlock();
                }

                if (local == _decodedLocal)
                {
                    _codec.CopyCurrent(destination);
                    return;
                }
                while (_decodedLocal < local - 1)
                {
                    _codec.Skip(ref _blockReader);
                    _decodedLocal++;
                }
                _codec.Skip(ref _blockReader);
                _decodedLocal++;
                if (_decodedLocal == _blocks[block].FrameCount - 1 && _blockReader.Remaining != 0)
                {
                    throw new InvalidDataException("Trailing bytes after the last frame of block " + block);
                }
                _codec.CopyCurrent(destination);
            }
            catch
            {
                _cachedBlock = -1;
                _decodedLocal = -1;
                throw;
            }
        }

        /// <summary>Decodes the frame of <paramref name="tick"/> into a new <see cref="RenderFrame"/>.</summary>
        public RenderFrame ReadFrame(int tick)
        {
            var frame = new RenderFrame();
            ReadFrame(tick, frame);
            return frame;
        }

        /// <summary>
        /// Sequential iteration from <paramref name="startTick"/> to the end; the i-th element is tick startTick + i.
        /// The same <see cref="RenderFrame"/> instance is yielded each time and overwritten by the next step.
        /// </summary>
        public IEnumerable<RenderFrame> EnumerateFrames(int startTick = 0)
        {
            if (startTick < 0 || startTick > _tickCount) throw new ArgumentOutOfRangeException(nameof(startTick));
            return Enumerate(startTick);
        }

        IEnumerable<RenderFrame> Enumerate(int startTick)
        {
            var frame = new RenderFrame();
            for (int t = startTick; t < _tickCount; t++)
            {
                ReadFrame(t, frame);
                yield return frame;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_leaveOpen) _stream.Dispose();
        }

        void LoadBlock(int block)
        {
            _cachedBlock = -1;
            RenderStreamBlockInfo info = _blocks[block];
            if (_compressed.Length < info.CompressedLength) _compressed = new byte[Math.Max(info.CompressedLength, _compressed.Length * 2)];
            if (_raw.Length < info.RawLength) _raw = new byte[Math.Max(info.RawLength, _raw.Length * 2)];

            _stream.Position = _base + info.Offset;
            RenderStreamFormat.ReadExactly(_stream, _compressed, 0, info.CompressedLength);
            var raw = new Span<byte>(_raw, 0, info.RawLength);
            Lz4Block.Decompress(new ReadOnlySpan<byte>(_compressed, 0, info.CompressedLength), raw);
            _blockHash.Reset();
            _blockHash.Append(raw);
            if (_blockHash.Digest() != info.RawHash) throw new InvalidDataException("Block " + block + " checksum mismatch");
            _cachedBlock = block;
            RewindBlock();
        }

        void RewindBlock()
        {
            _codec.ResetBlock();
            _blockReader = new ByteReader(_raw, 0, _blocks[_cachedBlock].RawLength);
            _decodedLocal = -1;
        }
    }
}
