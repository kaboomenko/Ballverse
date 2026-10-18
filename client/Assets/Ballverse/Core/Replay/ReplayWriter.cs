using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Streaming writer of the <c>.bvr</c> replay format (spec §7.4).
    /// <para>
    /// The header is written immediately with durationTicks = 0; <see cref="Finish"/> flushes the remaining records and patches
    /// durationTicks in place (its offset is fixed once the header is written). If <c>output.CanSeek</c> the file is written straight
    /// to the stream and patched by seeking; otherwise the whole file is buffered in memory and copied to the stream in
    /// <see cref="Finish"/> (replays are small: ≤ 200 KB/min, spec §3.1). The writer never closes the stream.
    /// </para>
    /// <para>
    /// Each record stream (INPT, PARM, HASH, EVNT) is delta-encoded by tick and must be appended in non-decreasing tick order
    /// (ArgumentException otherwise). A stream is emitted as a chunk every <c>recordsPerChunk</c> records (or 256 KiB), so one stream
    /// may span several chunks of the same tag; the tick delta continues across chunks (first record of a stream is relative to 0).
    /// Identical calls produce identical bytes.
    /// </para>
    /// <para>
    /// The replay must be the last thing in the stream: for a seekable output, <see cref="Finish"/> truncates anything after the
    /// replay's end (e.g. a longer older replay in a reused stream), so the reader never sees a stale tail.
    /// </para>
    /// <para>
    /// Failures: if <see cref="Finish"/> throws an I/O error it may be called again with the same durationTicks; the trailer
    /// (remaining records and RSLT) is encoded once and rewritten at the same offset, so a retry never duplicates RSLT. If writing
    /// a mid-run chunk fails, or copying the buffer to a non-seekable output fails part-way, the output is in an unknown state
    /// and the writer becomes faulted: every later call throws <see cref="InvalidOperationException"/>.
    /// </para>
    /// </summary>
    public sealed class ReplayWriter
    {
        readonly Stream _output;
        readonly Stream _target;
        readonly MemoryStream _fileBuffer;
        readonly long _durationPosition;
        readonly int _recordsPerChunk;
        readonly ReplayHeader _header;
        readonly RecordStream _inputs;
        readonly RecordStream _params;
        readonly RecordStream _hashes;
        readonly RecordStream _events;
        readonly int[] _inputScratch = new int[BvrFormat.MaxInputValues];
        ReplayResult _result;
        ReplayHeader _finishedHeader;
        bool _faulted;
        MemoryStream _trailer;
        long _trailerStart;
        uint _trailerDuration;
        bool _copied;

        /// <param name="output">Writable destination; seekable streams are patched in place, others are buffered.</param>
        /// <param name="header">Header; its <see cref="ReplayHeader.DurationTicks"/> is ignored (set by <see cref="Finish"/>).</param>
        /// <param name="recordsPerChunk">Records per chunk before a stream is flushed (≥ 1).</param>
        public ReplayWriter(Stream output, ReplayHeader header, int recordsPerChunk = BvrFormat.DefaultRecordsPerChunk)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (!output.CanWrite) throw new ArgumentException("Stream must be writable", nameof(output));
            if (recordsPerChunk < 1) throw new ArgumentOutOfRangeException(nameof(recordsPerChunk), "recordsPerChunk must be >= 1");
            string jsonError = StrictJson.Validate(header.ParamsJson, false);
            if (jsonError != null) throw new ArgumentException("paramsJson is not valid JSON: " + jsonError, nameof(header));

            _output = output;
            _header = header;
            _recordsPerChunk = recordsPerChunk;
            if (output.CanSeek)
            {
                _target = output;
            }
            else
            {
                _fileBuffer = new MemoryStream();
                _target = _fileBuffer;
            }

            _inputs = new RecordStream(BvrFormat.TagInput);
            _params = new RecordStream(BvrFormat.TagParam);
            _hashes = new RecordStream(BvrFormat.TagHash);
            _events = new RecordStream(BvrFormat.TagEvent);

            _durationPosition = WriteHeader(_target, header);
        }

        /// <summary>True after <see cref="Finish"/>; further writes throw <see cref="InvalidOperationException"/>.</summary>
        public bool IsFinished => _finishedHeader != null;

        /// <summary>Appends an input event (INPT, spec §2.6).</summary>
        public void AddInput(ReplayInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            AddInput(input.Tick, input.Type, input.Values);
        }

        /// <summary>
        /// Appends an input event (INPT, spec §2.6) without allocating, so a caller may reuse one array per tick (e.g. M10 gravity,
        /// spec §2.6). At most <see cref="BvrFormat.MaxInputValues"/> values; null means empty. The values are read into a reusable
        /// scratch array before anything is written, so an exception from <paramref name="values"/> leaves the stream unchanged.
        /// </summary>
        public void AddInput(uint tick, byte type, IReadOnlyList<int> values)
        {
            EnsureOpen();
            int n = values == null ? 0 : values.Count;
            if (n > BvrFormat.MaxInputValues)
                throw new ArgumentException("Input payload has " + n + " values, limit is " + BvrFormat.MaxInputValues, nameof(values));
            _inputs.CheckTick(tick);
            int[] scratch = _inputScratch;
            for (int i = 0; i < n; i++) scratch[i] = values[i];
            var s = _inputs.BeginRecord(tick);
            s.WriteByte(type);
            VarInt.WriteULong(s, (ulong)n);
            for (int i = 0; i < n; i++) VarInt.WriteLong(s, scratch[i]);
            EndRecord(_inputs);
        }

        /// <summary>
        /// Appends a live parameter change (PARM). <c>ValueJson</c> must be strict, compact RFC 8259 JSON: no whitespace outside
        /// strings, no comments, single quotes, unquoted names, trailing commas or NaN/Infinity (ArgumentException otherwise).
        /// </summary>
        public void AddParamChange(ReplayParamChange change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            EnsureOpen();
            _params.CheckTick(change.Tick);
            byte[] id = BvrEncoding.EncodeString(change.ParamId, BvrFormat.MaxStringBytes, nameof(change));
            byte[] value = BvrEncoding.EncodeString(change.ValueJson, BvrFormat.MaxStringBytes, nameof(change));
            string jsonError = StrictJson.Validate(change.ValueJson, true);
            if (jsonError != null) throw new ArgumentException("PARM value is not compact JSON: " + jsonError, nameof(change));
            var s = _params.BeginRecord(change.Tick);
            BvrEncoding.WriteBytesWithLength(s, id);
            BvrEncoding.WriteBytesWithLength(s, value);
            EndRecord(_params);
        }

        /// <summary>Appends a live parameter change (PARM) with the value given as JSON text.</summary>
        public void AddParamChange(uint tick, string paramId, string valueJson)
        {
            AddParamChange(new ReplayParamChange(tick, paramId, valueJson));
        }

        /// <summary>Appends a live parameter change (PARM), serializing <paramref name="value"/> with <see cref="CoreJson.CompactSettings"/>.</summary>
        public void AddParamValue(uint tick, string paramId, object value)
        {
            AddParamChange(new ReplayParamChange(tick, paramId, CoreJson.Serialize(value, false)));
        }

        /// <summary>Appends a state hash (HASH, spec §7.4).</summary>
        public void AddHash(uint tick, ulong hash)
        {
            EnsureOpen();
            _hashes.CheckTick(tick);
            var s = _hashes.BeginRecord(tick);
            BvrEncoding.WriteU64(s, hash);
            EndRecord(_hashes);
        }

        /// <summary>Appends a state hash (HASH, spec §7.4).</summary>
        public void AddHash(ReplayStateHash hash)
        {
            AddHash(hash.Tick, hash.Hash);
        }

        /// <summary>Appends a simulation event (EVNT, spec §2.7).</summary>
        public void AddEvent(ReplayEvent e)
        {
            EnsureOpen();
            if (e.Type > ReplayEventType.Custom || e.BodyId < 0) throw new ArgumentException("Invalid event", nameof(e));
            _events.CheckTick(e.Tick);
            var s = _events.BeginRecord(e.Tick);
            s.WriteByte((byte)e.Type);
            VarInt.WriteULong(s, (ulong)e.BodyId);
            BvrEncoding.WriteU16(s, e.Impulse);
            VarInt.WriteLong(s, e.Extra);
            EndRecord(_events);
        }

        /// <summary>Appends a simulation event (EVNT, spec §2.7).</summary>
        public void AddEvent(uint tick, ReplayEventType type, int bodyId, ushort impulse, long extra)
        {
            AddEvent(new ReplayEvent(tick, type, bodyId, impulse, extra));
        }

        /// <summary>Sets the run result (RSLT). At most once; written by <see cref="Finish"/>.</summary>
        public void SetResult(ReplayResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            EnsureOpen();
            if (_result != null) throw new InvalidOperationException("Result already set");
            _result = result;
        }

        /// <summary>
        /// Flushes all streams (INPT, PARM, HASH, EVNT, then RSLT), patches durationTicks, truncates a seekable output after the
        /// replay and, for non-seekable outputs, copies the buffered file to the stream. Throws ArgumentException (leaving the
        /// writer open) when any record tick or the result's finishTick exceeds <paramref name="durationTicks"/>. If an I/O error
        /// escapes, Finish may be retried with the same durationTicks (see the class remarks). Returns the final header.
        /// </summary>
        public ReplayHeader Finish(uint durationTicks)
        {
            if (_finishedHeader != null) throw new InvalidOperationException("ReplayWriter is finished");
            EnsureNotFaulted();
            if (_trailer == null)
            {
                uint maxTick = Math.Max(Math.Max(_inputs.LastTick, _params.LastTick), Math.Max(_hashes.LastTick, _events.LastTick));
                if (maxTick > durationTicks)
                    throw new ArgumentException("A record at tick " + maxTick + " lies beyond durationTicks " + durationTicks, nameof(durationTicks));
                if (_result != null && _result.FinishTick > durationTicks)
                    throw new ArgumentException("Result finishTick " + _result.FinishTick + " lies beyond durationTicks " + durationTicks, nameof(durationTicks));

                // Encode the trailer once, off the output, so a failed write can be redone without duplicating records or RSLT.
                var trailer = new MemoryStream();
                FlushStream(_inputs, trailer);
                FlushStream(_params, trailer);
                FlushStream(_hashes, trailer);
                FlushStream(_events, trailer);
                if (_result != null)
                {
                    byte[] payload = EncodeResult(_result);
                    WriteChunk(trailer, BvrFormat.TagResult, payload, payload.Length);
                }
                _trailerStart = _target.Position;
                _trailerDuration = durationTicks;
                _trailer = trailer;
            }
            else if (durationTicks != _trailerDuration)
            {
                throw new ArgumentException("A retried Finish must pass the same durationTicks (" + _trailerDuration + ")", nameof(durationTicks));
            }

            if (!_copied)
            {
                _target.Position = _trailerStart;
                _target.Write(_trailer.GetBuffer(), 0, (int)_trailer.Length);
                long end = _target.Position;
                _target.Position = _durationPosition;
                BvrEncoding.WriteU32(_target, durationTicks);
                _target.Position = end;
                if (_fileBuffer == null)
                {
                    Truncate(end);
                }
                else
                {
                    try
                    {
                        _fileBuffer.WriteTo(_output);
                    }
                    catch
                    {
                        _faulted = true;
                        throw;
                    }
                }
                _copied = true;
            }
            _output.Flush();

            _finishedHeader = _header.WithDurationTicks(durationTicks);
            return _finishedHeader;
        }

        /// <summary>
        /// Writes a whole in-memory replay (header duration taken from <c>replay.Header.DurationTicks</c>).
        /// Chunks with unknown tags that the reader skipped are not part of <see cref="ReplayFile"/> and are not written back:
        /// re-encoding a file from a newer build drops them, so storage code must copy <c>.bvr</c> files byte for byte.
        /// </summary>
        public static void Write(Stream output, ReplayFile replay, int recordsPerChunk = BvrFormat.DefaultRecordsPerChunk)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            var w = new ReplayWriter(output, replay.Header, recordsPerChunk);
            for (int i = 0; i < replay.Inputs.Count; i++) w.AddInput(replay.Inputs[i]);
            for (int i = 0; i < replay.ParamChanges.Count; i++) w.AddParamChange(replay.ParamChanges[i]);
            for (int i = 0; i < replay.Hashes.Count; i++) w.AddHash(replay.Hashes[i]);
            for (int i = 0; i < replay.Events.Count; i++) w.AddEvent(replay.Events[i]);
            if (replay.Result != null) w.SetResult(replay.Result);
            w.Finish(replay.Header.DurationTicks);
        }

        /// <summary>Serializes a whole in-memory replay to a byte array.</summary>
        public static byte[] WriteToArray(ReplayFile replay, int recordsPerChunk = BvrFormat.DefaultRecordsPerChunk)
        {
            var ms = new MemoryStream();
            Write(ms, replay, recordsPerChunk);
            return ms.ToArray();
        }

        static long WriteHeader(Stream s, ReplayHeader h)
        {
            byte[] magic = Encoding.ASCII.GetBytes(BvrFormat.Magic);
            s.Write(magic, 0, magic.Length);
            BvrEncoding.WriteU16(s, BvrFormat.CurrentVersion);
            BvrEncoding.WriteBytesWithLength(s, BvrEncoding.EncodeString(h.ModeId, BvrFormat.MaxStringBytes, "modeId"));
            BvrEncoding.WriteU16(s, h.ModeSimVersion);
            BvrEncoding.WriteBytesWithLength(s, BvrEncoding.EncodeString(h.AppVersion, BvrFormat.MaxStringBytes, "appVersion"));
            s.WriteByte((byte)h.PhysicsBackend);
            BvrEncoding.WriteU16(s, h.TickRate);
            BvrEncoding.WriteU64(s, h.Seed);
            BvrEncoding.WriteBytesWithLength(s, BvrEncoding.EncodeString(h.ParamsJson, BvrFormat.MaxParamsJsonBytes, "paramsJson"));
            BvrEncoding.WriteU64(s, h.ParamsHash);
            long durationPosition = s.Position;
            BvrEncoding.WriteU32(s, 0u);
            return durationPosition;
        }

        static byte[] EncodeResult(ReplayResult r)
        {
            var s = new MemoryStream();
            BvrEncoding.WriteBytesWithLength(s, BvrEncoding.EncodeString(r.WinnerId, BvrFormat.MaxStringBytes, "winnerId"));
            VarInt.WriteLong(s, r.WinnerIndex);
            VarInt.WriteULong(s, (ulong)r.Scores.Count);
            for (int i = 0; i < r.Scores.Count; i++) VarInt.WriteLong(s, r.Scores[i]);
            VarInt.WriteULong(s, r.FinishTick);
            return s.ToArray();
        }

        void EnsureOpen()
        {
            if (_finishedHeader != null) throw new InvalidOperationException("ReplayWriter is finished");
            EnsureNotFaulted();
            if (_trailer != null) throw new InvalidOperationException("Finish has started; call Finish again to complete it");
        }

        void EnsureNotFaulted()
        {
            if (_faulted) throw new InvalidOperationException("ReplayWriter is faulted after an output error; the output is incomplete");
        }

        void EndRecord(RecordStream stream)
        {
            stream.Count++;
            if (stream.Count < _recordsPerChunk && stream.Buffer.Length < BvrFormat.FlushThresholdBytes) return;
            try
            {
                FlushStream(stream, _target);
            }
            catch
            {
                _faulted = true;
                throw;
            }
        }

        static void FlushStream(RecordStream stream, Stream destination)
        {
            if (stream.Count == 0) return;
            WriteChunk(destination, stream.Tag, stream.Buffer.GetBuffer(), (int)stream.Buffer.Length);
            stream.Buffer.SetLength(0);
            stream.Count = 0;
        }

        static void WriteChunk(Stream destination, string tag, byte[] payload, int length)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
            destination.Write(tagBytes, 0, 4);
            BvrEncoding.WriteU32(destination, (uint)length);
            destination.Write(payload, 0, length);
        }

        /// <summary>Cuts a seekable output at the replay's end so stale bytes of an older, longer content are not read back.</summary>
        void Truncate(long end)
        {
            if (end >= _output.Length) return;
            try
            {
                _output.SetLength(end);
            }
            catch (NotSupportedException e)
            {
                throw new IOException("The output holds " + (_output.Length - end) + " stale bytes after the replay and cannot be truncated", e);
            }
        }

        /// <summary>Buffered, delta-encoded record stream of one tag.</summary>
        sealed class RecordStream
        {
            internal readonly string Tag;
            internal readonly MemoryStream Buffer = new MemoryStream();
            internal int Count;
            internal uint LastTick;

            internal RecordStream(string tag)
            {
                Tag = tag;
            }

            internal void CheckTick(uint tick)
            {
                if (tick < LastTick)
                    throw new ArgumentException(Tag + " ticks must be non-decreasing: " + tick + " after " + LastTick, nameof(tick));
            }

            /// <summary>Writes the tick delta and returns the buffer for the rest of the record. Call <see cref="CheckTick"/> first.</summary>
            internal MemoryStream BeginRecord(uint tick)
            {
                VarInt.WriteULong(Buffer, tick - LastTick);
                LastTick = tick;
                return Buffer;
            }
        }
    }
}
