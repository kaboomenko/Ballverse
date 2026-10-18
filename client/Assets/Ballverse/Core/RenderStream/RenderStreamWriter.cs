using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Compression;
using Ballverse.Core.RenderStream.Internal;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Streaming writer of a .bvs render stream (spec §7.4 "Поток рендера: формат"). Frames are appended tick by tick
    /// during the live run; every <see cref="RenderStreamHeader.BlockTicks"/> frames the block (keyframe + deltas from
    /// linear prediction) is LZ4-compressed and written to the stream, so memory stays bounded regardless of clip length.
    /// <see cref="Finish"/> appends the shape and label tables, the block index and the footer for random access.
    /// <para>
    /// Static data (spec §7.7): body geometry is registered once with <see cref="DefineShape"/> and names / numbers drawn
    /// on bodies with <see cref="DefineLabel"/>; bodies reference them by id. Both may be called at any time before
    /// <see cref="Finish"/>, but an id must be defined before the first frame that uses it. Equal definitions return the
    /// same id, and ids are assigned in definition order, so the output is deterministic.
    /// </para>
    /// <para>
    /// The target stream only needs to be writable (no seeking); for the spec's write-behind recording the caller
    /// passes a stream that hands buffers to a background writer. A stream that was not finished is not readable.
    /// </para>
    /// <para>
    /// Memory profile: one raw block buffer (≈ bytes of 60 encoded frames; ~60–80 KB for 300 moving bodies), one
    /// compressed buffer of LZ4 worst-case size, the LZ4 hash table (16 KB), prediction state for the bodies of the last
    /// frame (two arrays of ~36 bytes per body), the previous grid (≤ 32 KB), a sorted scratch frame (used only if the
    /// caller's frame is unsorted), the encoded shape / label tables and the index (28 bytes per block). All buffers grow
    /// geometrically and are reused, so steady-state appends do not allocate.
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
    /// Failure handling: if the header write in the constructor throws, the constructor disposes the stream (unless
    /// leaveOpen) and rethrows. If writing to the stream (or any other step after validation) throws in
    /// <see cref="AppendFrame"/> or <see cref="Finish"/>, the writer becomes <see cref="IsFaulted"/> and rethrows; every
    /// later <see cref="AppendFrame"/> / <see cref="DefineShape"/> / <see cref="DefineLabel"/> / <see cref="Finish"/> throws
    /// <see cref="InvalidOperationException"/>. A failure before the footer write completes leaves output without a valid
    /// footer, which readers reject. <see cref="Finish"/> calls <see cref="Stream.Flush"/> last, after the footer: if that
    /// flush throws, the writer is faulted and <see cref="IsFinished"/> stays false, but the footer may already have
    /// reached the stream, so the output can be complete and readable — or not, if the flush lost buffered bytes. Either
    /// way the caller must treat the file as failed and discard it, then rebuild it from the replay (the render stream is
    /// a cache, spec §7.4). Argument errors (bad tick, invalid frame, invalid or excess definition) are rejected before
    /// any state changes and do not fault the writer.
    /// </para>
    /// </summary>
    public sealed class RenderStreamWriter : IDisposable
    {
        readonly Stream _stream;
        readonly bool _leaveOpen;
        readonly RenderStreamHeader _header;
        readonly int _gridWidth;
        readonly int _gridHeight;
        readonly FrameCodec _codec;
        readonly ByteWriter _raw = new ByteWriter(64 * 1024);
        readonly ByteWriter _index = new ByteWriter(1024);
        readonly ByteWriter _scratch = new ByteWriter(256);
        readonly ByteWriter _shapeTable = new ByteWriter(256);
        readonly ByteWriter _shapeScratch = new ByteWriter(256);
        readonly ByteWriter _labelTable = new ByteWriter(256);
        readonly Dictionary<ShapeDefinition, int> _shapeIds = new Dictionary<ShapeDefinition, int>();
        readonly Dictionary<string, int> _labelIds = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly XxHash64 _metaHash = new XxHash64();
        readonly XxHash64 _blockHash = new XxHash64();
        readonly int[] _lz4Table = new int[Lz4Block.HashTableSize];
        readonly RenderFrame _sorted = new RenderFrame();
        byte[] _compressed = new byte[1024];
        long _position;
        int _tickCount;
        int _blockCount;
        int _blockFirstTick;
        int _shapeCount;
        int _labelCount;
        bool _finished;
        bool _faulted;
        bool _disposed;

        /// <summary>
        /// Writes the header immediately. Offsets in the file are relative to the stream's current position.
        /// If the header write throws, the stream is disposed (unless <paramref name="leaveOpen"/>) and the exception rethrown.
        /// </summary>
        public RenderStreamWriter(Stream stream, RenderStreamHeader header, bool leaveOpen = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("Stream must be writable", nameof(stream));
            _stream = stream;
            _header = header ?? throw new ArgumentNullException(nameof(header));
            _leaveOpen = leaveOpen;
            if (header.Grid != null)
            {
                _gridWidth = header.Grid.Width;
                _gridHeight = header.Grid.Height;
            }
            _codec = new FrameCodec(_gridWidth, _gridHeight);

            try
            {
                RenderStreamFormat.WriteHeader(_scratch, header);
                _metaHash.Append(_scratch.Span);
                _stream.Write(_scratch.Buffer, 0, _scratch.Length);
                _position = _scratch.Length;
            }
            catch
            {
                _disposed = true;
                if (!leaveOpen) stream.Dispose();
                throw;
            }
        }

        public RenderStreamHeader Header => _header;

        /// <summary>Number of frames appended so far; the next frame must have this tick.</summary>
        public int TickCount => _tickCount;

        /// <summary>Bytes written so far (header + completed blocks, plus tables, index and footer after <see cref="Finish"/>).</summary>
        public long BytesWritten => _position;

        public bool IsFinished => _finished;

        /// <summary>True after a stream write (or another post-validation step) failed; the writer is then unusable.</summary>
        public bool IsFaulted => _faulted;

        /// <summary>Number of distinct shapes defined so far; valid shape ids are [0, ShapeCount).</summary>
        public int ShapeCount => _shapeCount;

        /// <summary>Number of distinct labels defined so far; valid label ids are [1, LabelCount] plus <see cref="BodyState.NoLabel"/>.</summary>
        public int LabelCount => _labelCount;

        /// <summary>
        /// Registers an immutable shape and returns its id for <see cref="BodyState.ShapeId"/> (spec §7.7 shape table).
        /// A shape equal to an already defined one returns the existing id; new ids are 0, 1, 2… in definition order.
        /// Throws <see cref="InvalidOperationException"/> (without faulting the writer) once
        /// <see cref="RenderStreamLimits.MaxShapes"/> shapes or <see cref="RenderStreamLimits.MaxShapeTableBytes"/> bytes are used,
        /// and <see cref="ArgumentException"/> for a non-circle shape when the header's
        /// <see cref="RenderQuantization.ScaleMax"/> is below 1 (scale factor 1 would not be representable).
        /// </summary>
        public int DefineShape(ShapeDefinition shape)
        {
            ThrowIfUnusable();
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (shape.Kind != ShapeKind.Circle && _header.Quantization.ScaleMax < 1f)
            {
                throw new ArgumentException("Non-circle shapes need a quantization ScaleMax of at least 1 (got " + _header.Quantization.ScaleMax + ")", nameof(shape));
            }
            if (_shapeIds.TryGetValue(shape, out int existing)) return existing;
            if (_shapeCount == RenderStreamLimits.MaxShapes) throw new InvalidOperationException("Shape table is full (" + RenderStreamLimits.MaxShapes + " shapes)");
            _shapeScratch.Reset();
            TableCodec.WriteShape(_shapeScratch, shape);
            // Reserve 3 bytes for the count prefix (uvarint of MaxShapes).
            if ((long)_shapeTable.Length + _shapeScratch.Length > RenderStreamLimits.MaxShapeTableBytes - 3)
            {
                throw new InvalidOperationException("Shape table exceeds " + RenderStreamLimits.MaxShapeTableBytes + " bytes");
            }
            _shapeTable.WriteBytes(_shapeScratch.Span);
            int id = _shapeCount++;
            _shapeIds.Add(shape, id);
            return id;
        }

        /// <summary>
        /// Registers a label string drawn on bodies (names / numbers, spec §2.9 M03, M06, M07) and returns its id for
        /// <see cref="BodyState.LabelId"/>: 1, 2, 3… in definition order; an equal (ordinal) string returns the existing id.
        /// A label is 1..<see cref="RenderStreamLimits.MaxLabelLength"/> UTF-16 code units of well-formed text.
        /// The same string table holds skin keys: pass the returned id as <see cref="BodyState.SkinId"/> to name the flag /
        /// image drawn on a body (spec §2.9 M03, M08).
        /// Throws <see cref="InvalidOperationException"/> (without faulting) beyond <see cref="RenderStreamLimits.MaxLabels"/> labels.
        /// </summary>
        public int DefineLabel(string label)
        {
            ThrowIfUnusable();
            if (label == null) throw new ArgumentNullException(nameof(label));
            string error = TableCodec.ValidateLabel(label);
            if (error != null) throw new ArgumentException(error, nameof(label));
            if (_labelIds.TryGetValue(label, out int existing)) return existing;
            if (_labelCount == RenderStreamLimits.MaxLabels) throw new InvalidOperationException("Label table is full (" + RenderStreamLimits.MaxLabels + " labels)");
            TableCodec.WriteLabel(_labelTable, label);
            int id = ++_labelCount;
            _labelIds.Add(label, id);
            return id;
        }

        /// <summary>
        /// Appends the frame of <paramref name="tick"/>. Ticks start at 0 and must be consecutive.
        /// Bodies and HUD entries may be in any order (they are stored sorted by id / key); body ids and HUD keys must be
        /// unique, shape ids must be defined (<see cref="DefineShape"/>), label and skin ids defined (<see cref="DefineLabel"/>)
        /// or 0 (<see cref="BodyState.NoLabel"/> / <see cref="BodyState.NoSkin"/>),
        /// HUD keys must lie in [0, <see cref="HudEntry.MaxKey"/>] and a frame may hold at most
        /// <see cref="HudEntry.MaxEntriesPerFrame"/> HUD entries. The frame must carry a grid of exactly the header's
        /// <see cref="RenderStreamHeader.Grid"/> size, or no grid when the header declares none.
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
        /// Flushes the last partial block and writes the shape and label tables, the block index and the footer, then
        /// flushes the stream. The writer cannot be used afterwards. A stream with zero frames is valid.
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

            // Tables (one write).
            long tablesOffset = _position;
            var tables = new ByteWriter(_shapeTable.Length + _labelTable.Length + 16);
            tables.WriteUVarint((ulong)_shapeCount);
            tables.WriteBytes(_shapeTable.Span);
            int shapeTableLength = tables.Length;
            tables.WriteUVarint((ulong)_labelCount);
            tables.WriteBytes(_labelTable.Span);
            int labelTableLength = tables.Length - shapeTableLength;
            _metaHash.Append(tables.Span);
            _stream.Write(tables.Buffer, 0, tables.Length);
            _position += tables.Length;

            long indexOffset = _position;
            _metaHash.Append(_index.Span);
            _stream.Write(_index.Buffer, 0, _index.Length);
            _position += _index.Length;

            _scratch.Reset();
            _scratch.WriteUInt64((ulong)tablesOffset);
            _scratch.WriteUInt32((uint)shapeTableLength);
            _scratch.WriteUInt32((uint)labelTableLength);
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
            if (frame.GridWidth != _gridWidth || frame.GridHeight != _gridHeight)
            {
                throw new ArgumentException("Frame grid " + frame.GridWidth + "x" + frame.GridHeight + " does not match the header grid "
                    + _gridWidth + "x" + _gridHeight, nameof(frame));
            }
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
                if ((uint)bodies[i].ShapeId >= (uint)_shapeCount) throw new ArgumentException("Undefined shape id " + bodies[i].ShapeId + " for body " + bodies[i].Id, nameof(frame));
                if ((uint)bodies[i].LabelId > (uint)_labelCount) throw new ArgumentException("Undefined label id " + bodies[i].LabelId + " for body " + bodies[i].Id, nameof(frame));
                if ((uint)bodies[i].SkinId > (uint)_labelCount) throw new ArgumentException("Undefined skin string id " + bodies[i].SkinId + " for body " + bodies[i].Id, nameof(frame));
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
