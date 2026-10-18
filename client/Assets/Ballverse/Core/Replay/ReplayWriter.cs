using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        ReplayResult _result;
        ReplayHeader _finishedHeader;

        /// <param name="output">Writable destination; seekable streams are patched in place, others are buffered.</param>
        /// <param name="header">Header; its <see cref="ReplayHeader.DurationTicks"/> is ignored (set by <see cref="Finish"/>).</param>
        /// <param name="recordsPerChunk">Records per chunk before a stream is flushed (≥ 1).</param>
        public ReplayWriter(Stream output, ReplayHeader header, int recordsPerChunk = BvrFormat.DefaultRecordsPerChunk)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (!output.CanWrite) throw new ArgumentException("Stream must be writable", nameof(output));
            if (recordsPerChunk < 1) throw new ArgumentOutOfRangeException(nameof(recordsPerChunk), "recordsPerChunk must be >= 1");
            ValidateJson(header.ParamsJson, "header.ParamsJson");

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
            EnsureOpen();
            _inputs.CheckTick(input.Tick);
            var s = _inputs.BeginRecord(input.Tick);
            s.WriteByte(input.Type);
            VarInt.WriteULong(s, (ulong)input.Values.Count);
            for (int i = 0; i < input.Values.Count; i++) VarInt.WriteLong(s, input.Values[i]);
            EndRecord(_inputs);
        }

        /// <summary>Appends an input event (INPT, spec §2.6).</summary>
        public void AddInput(uint tick, byte type, IReadOnlyList<int> values)
        {
            AddInput(new ReplayInput(tick, type, values));
        }

        /// <summary>Appends a live parameter change (PARM). <c>ValueJson</c> must be valid JSON (ArgumentException otherwise).</summary>
        public void AddParamChange(ReplayParamChange change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            EnsureOpen();
            _params.CheckTick(change.Tick);
            byte[] id = BvrEncoding.EncodeString(change.ParamId, BvrFormat.MaxStringBytes, nameof(change));
            byte[] value = BvrEncoding.EncodeString(change.ValueJson, BvrFormat.MaxStringBytes, nameof(change));
            ValidateJson(change.ValueJson, nameof(change));
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
        /// Flushes all streams (INPT, PARM, HASH, EVNT, then RSLT), patches durationTicks and, for non-seekable outputs, copies the
        /// buffered file to the stream. Throws ArgumentException (leaving the writer open) when any record tick or the result's
        /// finishTick exceeds <paramref name="durationTicks"/>. Returns the final header.
        /// </summary>
        public ReplayHeader Finish(uint durationTicks)
        {
            EnsureOpen();
            uint maxTick = Math.Max(Math.Max(_inputs.LastTick, _params.LastTick), Math.Max(_hashes.LastTick, _events.LastTick));
            if (maxTick > durationTicks)
                throw new ArgumentException("A record at tick " + maxTick + " lies beyond durationTicks " + durationTicks, nameof(durationTicks));
            if (_result != null && _result.FinishTick > durationTicks)
                throw new ArgumentException("Result finishTick " + _result.FinishTick + " lies beyond durationTicks " + durationTicks, nameof(durationTicks));

            FlushStream(_inputs);
            FlushStream(_params);
            FlushStream(_hashes);
            FlushStream(_events);
            if (_result != null) WriteChunk(BvrFormat.TagResult, EncodeResult(_result));

            long end = _target.Position;
            _target.Position = _durationPosition;
            BvrEncoding.WriteU32(_target, durationTicks);
            _target.Position = end;

            if (_fileBuffer != null) _fileBuffer.WriteTo(_output);
            _output.Flush();

            _finishedHeader = _header.WithDurationTicks(durationTicks);
            return _finishedHeader;
        }

        /// <summary>Writes a whole in-memory replay (header duration taken from <c>replay.Header.DurationTicks</c>).</summary>
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

        static void ValidateJson(string json, string paramName)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    JToken.ReadFrom(reader);
                    if (reader.Read()) throw new ArgumentException("Unexpected content after the JSON value", paramName);
                }
            }
            catch (JsonException e)
            {
                throw new ArgumentException("Not valid JSON: " + e.Message, paramName, e);
            }
        }

        void EnsureOpen()
        {
            if (_finishedHeader != null) throw new InvalidOperationException("ReplayWriter is finished");
        }

        void EndRecord(RecordStream stream)
        {
            stream.Count++;
            if (stream.Count >= _recordsPerChunk || stream.Buffer.Length >= BvrFormat.FlushThresholdBytes) FlushStream(stream);
        }

        void FlushStream(RecordStream stream)
        {
            if (stream.Count == 0) return;
            WriteChunk(stream.Tag, stream.Buffer.GetBuffer(), (int)stream.Buffer.Length);
            stream.Buffer.SetLength(0);
            stream.Count = 0;
        }

        void WriteChunk(string tag, byte[] payload)
        {
            WriteChunk(tag, payload, payload.Length);
        }

        void WriteChunk(string tag, byte[] payload, int length)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(tag);
            _target.Write(tagBytes, 0, 4);
            BvrEncoding.WriteU32(_target, (uint)length);
            _target.Write(payload, 0, length);
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
