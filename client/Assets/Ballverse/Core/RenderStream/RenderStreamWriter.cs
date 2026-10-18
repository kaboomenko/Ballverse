using System;
using System.IO;
using Ballverse.Core.Compression;
using Ballverse.Core.RenderStream.Internal;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Streaming writer of a .bvs render stream (spec §7.4 "Поток рендера: формат"). Frames are appended tick by tick
    /// during the live run; every <see cref="RenderStreamHeader.BlockTicks"/> frames the block (keyframe + deltas from
    /// linear prediction) is LZ4-compressed and written to the stream, so memory stays bounded regardless of clip length.
    /// <see cref="Finish"/> appends the block index and footer for random access.
    /// <para>
    /// The target stream only needs to be writable (no seeking); for the spec's write-behind recording the caller
    /// passes a stream that hands buffers to a background writer. A stream that was not finished is not readable.
    /// </para>
    /// <para>
    /// Memory profile: one raw block buffer (≈ bytes of 60 encoded frames; ~60–80 KB for 300 moving bodies), one
    /// compressed buffer of LZ4 worst-case size, the LZ4 hash table (16 KB), prediction state for the bodies of the last
    /// frame (two arrays of ~20 bytes per body), a sorted scratch frame (used only if the caller's frame is unsorted)
    /// and the index (28 bytes per block). All buffers grow geometrically and are reused, so steady-state appends do not allocate.
    /// </para>
    /// <para>
    /// CPU profile and threading: an ordinary <see cref="AppendFrame"/> only delta-encodes one frame (≈ 30 µs for 300
    /// bodies on desktop x64). The append that completes a block also LZ4-compresses it, hashes it (XXH64) and writes it to
    /// the stream, which costs roughly 10–20× an ordinary append (≈ 0.4–0.6 ms for 300 bodies on desktop x64), once per
    /// block. The writer is single-threaded and not thread-safe. The spec assigns render-stream writing to the background
    /// writing thread (§7.11), so the intended use is: the simulation copies each frame
    /// (<see cref="RenderFrame.CopyFrom"/>) into a queue and the background thread calls <see cref="AppendFrame"/>.
    /// </para>
    /// <para>
    /// Failure handling: if writing to the stream (or any other step after validation) throws, the writer becomes
    /// <see cref="IsFaulted"/> and rethrows; every later <see cref="AppendFrame"/> / <see cref="Finish"/> throws
    /// <see cref="InvalidOperationException"/>. The partial output never gets a footer, so readers reject it instead of
    /// misreading it; the caller rebuilds the stream from the replay (the render stream is a cache, spec §7.4).
    /// Argument errors (bad tick, invalid frame) are rejected before any state changes and do not fault the writer.
    /// </para>
    /// </summary>
    public sealed class RenderStreamWriter : IDisposable
    {
        readonly Stream _stream;
        readonly bool _leaveOpen;
        readonly RenderStreamHeader _header;
        readonly FrameCodec _codec = new FrameCodec();
        readonly ByteWriter _raw = new ByteWriter(64 * 1024);
        readonly ByteWriter _index = new ByteWriter(1024);
        readonly ByteWriter _scratch = new ByteWriter(256);
        readonly XxHash64 _metaHash = new XxHash64();
        readonly XxHash64 _blockHash = new XxHash64();
        readonly int[] _lz4Table = new int[Lz4Block.HashTableSize];
        readonly RenderFrame _sorted = new RenderFrame();
        byte[] _compressed = new byte[1024];
        long _position;
        int _tickCount;
        int _blockCount;
        int _blockFirstTick;
        bool _finished;
        bool _faulted;
        bool _disposed;

        /// <summary>Writes the header immediately. Offsets in the file are relative to the stream's current position.</summary>
        public RenderStreamWriter(Stream stream, RenderStreamHeader header, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Stream must be writable", nameof(stream));
            _stream = stream;
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _leaveOpen = leaveOpen;

            RenderStreamFormat.WriteHeader(_scratch, header);
            _metaHash.Append(_scratch.Span);
            _stream.Write(_scratch.Buffer, 0, _scratch.Length);
            _position = _scratch.Length;
        }

        public RenderStreamHeader Header => _header;

        /// <summary>Number of frames appended so far; the next frame must have this tick.</summary>
        public int TickCount => _tickCount;

        /// <summary>Bytes written so far (header + completed blocks, plus index and footer after <see cref="Finish"/>).</summary>
        public long BytesWritten => _position;

        public bool IsFinished => _finished;

        /// <summary>True after a stream write (or another post-validation step) failed; the writer is then unusable.</summary>
        public bool IsFaulted => _faulted;

        /// <summary>
        /// Appends the frame of <paramref name="tick"/>. Ticks start at 0 and must be consecutive.
        /// Bodies and HUD entries may be in any order (they are stored sorted by id / key); body ids and HUD keys must be
        /// unique, body kinds must be defined <see cref="BodyKind"/> values, HUD keys must lie in [0, <see cref="HudEntry.MaxKey"/>]
        /// and a frame may hold at most <see cref="HudEntry.MaxEntriesPerFrame"/> HUD entries.
        /// The caller's frame is not modified and may be reused immediately.
        /// </summary>
        public void AppendFrame(int tick, RenderFrame frame)
        {
            ThrowIfUnusable();
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tick != _tickCount) throw new ArgumentException("Expected tick " + _tickCount + ", got " + tick, nameof(tick));
            if (_tickCount == int.MaxValue) throw new InvalidOperationException("Render stream is full");

            RenderFrame canonical = Canonicalize(frame);
            try
            {
                if (_codec.FramesInBlock == 0) _blockFirstTick = tick;
                _codec.Encode(canonical, _raw);
                _tickCount++;
                if (_codec.FramesInBlock == _header.BlockTicks) FlushBlock();
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        /// <summary>
        /// Flushes the last partial block and writes the block index and footer. The writer cannot be used afterwards.
        /// A stream with zero frames is valid.
        /// </summary>
        public void Finish()
        {
            ThrowIfUnusable();
            try
            {
                WriteTail();
            }
            catch
            {
                _faulted = true;
                throw;
            }
            _finished = true;
        }

        void WriteTail()
        {
            if (_codec.FramesInBlock > 0) FlushBlock();

            long indexOffset = _position;
            _metaHash.Append(_index.Span);
            _stream.Write(_index.Buffer, 0, _index.Length);
            _position += _index.Length;

            _scratch.Reset();
            _scratch.WriteUInt64((ulong)indexOffset);
            _scratch.WriteUInt32((uint)_blockCount);
            _scratch.WriteUInt32((uint)_tickCount);
            _metaHash.Append(_scratch.Span);
            _scratch.WriteUInt64(_metaHash.Digest());
            _scratch.WriteBytes(RenderStreamFormat.FooterMagic);
            _stream.Write(_scratch.Buffer, 0, _scratch.Length);
            _position += _scratch.Length;
            _stream.Flush();
        }

        /// <summary>Releases the stream (unless leaveOpen). Does not call <see cref="Finish"/>.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_leaveOpen) _stream.Dispose();
        }

        void FlushBlock()
        {
            int rawLength = _raw.Length;
            if (rawLength > RenderStreamFormat.MaxRawBlockBytes) throw new InvalidOperationException("Render stream block exceeds " + RenderStreamFormat.MaxRawBlockBytes + " bytes");
            int bound = Lz4Block.MaxCompressedLength(rawLength);
            if (_compressed.Length < bound) _compressed = new byte[Math.Max(bound, _compressed.Length * 2)];
            int compressedLength = Lz4Block.Compress(_raw.Span, _compressed, _lz4Table);
            _blockHash.Reset();
            _blockHash.Append(_raw.Span);
            ulong rawHash = _blockHash.Digest();

            _stream.Write(_compressed, 0, compressedLength);

            _index.WriteUInt32((uint)_blockFirstTick);
            _index.WriteUInt64((ulong)_position);
            _index.WriteUInt32((uint)compressedLength);
            _index.WriteUInt32((uint)rawLength);
            _index.WriteUInt64(rawHash);

            _position += compressedLength;
            _blockCount++;
            _raw.Reset();
            _codec.ResetBlock();
        }

        RenderFrame Canonicalize(RenderFrame frame)
        {
            if (frame.BodyCount > FrameCodec.MaxBodiesPerFrame) throw new ArgumentException("Too many bodies in frame", nameof(frame));
            if (frame.HudCount > HudEntry.MaxEntriesPerFrame) throw new ArgumentException("More than " + HudEntry.MaxEntriesPerFrame + " HUD entries in frame", nameof(frame));
            RenderFrame f = frame;
            if (!frame.IsBodiesSorted() || !frame.IsHudSorted())
            {
                _sorted.CopyFrom(frame);
                _sorted.Sort();
                f = _sorted;
            }

            var bodies = f.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                if ((byte)bodies[i].Kind > (byte)BodyKind.Other) throw new ArgumentException("Undefined body kind " + bodies[i].Kind + " for id " + bodies[i].Id, nameof(frame));
                if (i > 0 && bodies[i].Id == bodies[i - 1].Id) throw new ArgumentException("Duplicate body id " + bodies[i].Id, nameof(frame));
            }
            var hud = f.Hud;
            for (int i = 0; i < hud.Length; i++)
            {
                if (hud[i].Key < 0 || hud[i].Key > HudEntry.MaxKey) throw new ArgumentException("HUD key out of range: " + hud[i].Key, nameof(frame));
                if (i > 0 && hud[i].Key == hud[i - 1].Key) throw new ArgumentException("Duplicate HUD key " + hud[i].Key, nameof(frame));
            }
            return f;
        }

        void ThrowIfUnusable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RenderStreamWriter));
            if (_finished) throw new InvalidOperationException("Render stream already finished");
            if (_faulted) throw new InvalidOperationException("Render stream writer faulted after an earlier write failure");
        }
    }
}
