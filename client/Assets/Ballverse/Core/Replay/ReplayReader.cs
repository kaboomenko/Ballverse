using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Reader of the <c>.bvr</c> replay format (spec §7.4).
    /// <list type="bullet">
    /// <item>Validates magic (<see cref="InvalidDataException"/>) and formatVersion (<see cref="UnsupportedReplayVersionException"/>).</item>
    /// <item>Skips chunks with unknown tags (forward compatibility) and concatenates same-tag chunks in file order.</item>
    /// <item>Any truncation or corruption throws <see cref="InvalidDataException"/>, never IndexOutOfRange/Overflow/OutOfMemory:
    /// strings ≤ 1 MiB, paramsJson ≤ 256 KiB, chunk length ≤ remaining stream length (seekable) and ≤ 16 MiB for known tags,
    /// list counts ≤ remaining payload, ticks ≤ durationTicks, paramsHash must match paramsJson, tickRate must be 60,
    /// at most one RSLT chunk.</item>
    /// </list>
    /// Non-seekable streams are supported: payloads are read in bounded blocks so a lying length cannot force a huge allocation.
    /// </summary>
    public static class ReplayReader
    {
        const int ReadBlockBytes = 64 << 10;

        /// <summary>Reads a whole replay from a byte array.</summary>
        public static ReplayFile Read(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Read(new MemoryStream(data, false));
        }

        /// <summary>Reads a whole replay from the current position to the end of the stream. The stream is not closed.</summary>
        public static ReplayFile Read(Stream stream)
        {
            CheckStream(stream);
            ReplayHeader header = ReadHeaderCore(stream);
            uint duration = header.DurationTicks;

            var inputs = new List<ReplayInput>();
            var parms = new List<ReplayParamChange>();
            var hashes = new List<ReplayStateHash>();
            var events = new List<ReplayEvent>();
            ReplayResult result = null;
            int skipped = 0;
            uint inputTick = 0, paramTick = 0, hashTick = 0, eventTick = 0;

            var tagBuf = new byte[4];
            while (true)
            {
                int n = ReadUpTo(stream, tagBuf, 4);
                if (n == 0) break;
                if (n < 4) throw new InvalidDataException("Truncated chunk tag");
                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(tagBuf);
                uint length = ReadU32(stream);
                if (stream.CanSeek && length > stream.Length - stream.Position)
                    throw new InvalidDataException("Chunk length " + length + " exceeds the remaining stream");

                if (tag != BvrFormat.InputTagValue && tag != BvrFormat.ParamTagValue && tag != BvrFormat.HashTagValue
                    && tag != BvrFormat.EventTagValue && tag != BvrFormat.ResultTagValue)
                {
                    Skip(stream, length);
                    skipped++;
                    continue;
                }

                if (length > BvrFormat.MaxChunkPayloadBytes)
                    throw new InvalidDataException("Chunk length " + length + " exceeds " + BvrFormat.MaxChunkPayloadBytes);
                byte[] payload = ReadPayload(stream, (int)length);
                var r = new BvrPayloadReader(payload, (int)length);

                if (tag == BvrFormat.InputTagValue)
                {
                    while (!r.AtEnd)
                    {
                        uint tick = Advance(ref inputTick, r, duration);
                        byte type = r.ReadU8();
                        int count = r.ReadCount(BvrFormat.MaxInputValues, "Input payload");
                        var values = new int[count];
                        for (int i = 0; i < count; i++) values[i] = r.ReadZigZagInt32("Input value");
                        inputs.Add(new ReplayInput(tick, type, values));
                    }
                }
                else if (tag == BvrFormat.ParamTagValue)
                {
                    while (!r.AtEnd)
                    {
                        uint tick = Advance(ref paramTick, r, duration);
                        string id = r.ReadString(BvrFormat.MaxStringBytes, "paramId");
                        string value = r.ReadString(BvrFormat.MaxStringBytes, "param value");
                        if (id.Length == 0 || value.Length == 0) throw new InvalidDataException("Empty paramId or value in PARM");
                        parms.Add(new ReplayParamChange(tick, id, value));
                    }
                }
                else if (tag == BvrFormat.HashTagValue)
                {
                    while (!r.AtEnd)
                    {
                        uint tick = Advance(ref hashTick, r, duration);
                        hashes.Add(new ReplayStateHash(tick, r.ReadU64()));
                    }
                }
                else if (tag == BvrFormat.EventTagValue)
                {
                    while (!r.AtEnd)
                    {
                        uint tick = Advance(ref eventTick, r, duration);
                        byte type = r.ReadU8();
                        if (type > (byte)ReplayEventType.Custom) throw new InvalidDataException("Unknown event type " + type);
                        int bodyId = r.ReadVarNonNegativeInt32("bodyId");
                        ushort impulse = r.ReadU16();
                        long extra = r.ReadZigZagInt64();
                        events.Add(new ReplayEvent(tick, (ReplayEventType)type, bodyId, impulse, extra));
                    }
                }
                else
                {
                    if (result != null) throw new InvalidDataException("More than one RSLT chunk");
                    result = ReadResult(r, duration);
                }
            }

            return new ReplayFile(header, inputs, parms, hashes, events, result, skipped);
        }

        /// <summary>Reads and validates only the header (cheap; e.g. for a library listing).</summary>
        public static ReplayHeader ReadHeader(Stream stream)
        {
            CheckStream(stream);
            return ReadHeaderCore(stream);
        }

        static void CheckStream(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("Stream must be readable", nameof(stream));
        }

        static ReplayHeader ReadHeaderCore(Stream s)
        {
            var magic = new byte[4];
            ReadExact(s, magic, 4);
            if (BinaryPrimitives.ReadUInt32LittleEndian(magic) != BvrFormat.MagicValue)
                throw new InvalidDataException("Not a .bvr replay (bad magic)");
            ushort version = ReadU16(s);
            if (version != BvrFormat.CurrentVersion) throw new UnsupportedReplayVersionException(version);

            string modeId = ReadString(s, BvrFormat.MaxStringBytes, "modeId");
            if (modeId.Length == 0) throw new InvalidDataException("Empty modeId");
            ushort modeSimVersion = ReadU16(s);
            string appVersion = ReadString(s, BvrFormat.MaxStringBytes, "appVersion");
            int backend = s.ReadByte();
            if (backend < 0) throw new InvalidDataException("Truncated header");
            ushort tickRate = ReadU16(s);
            if (tickRate != SimConstants.TickRate)
                throw new InvalidDataException("tickRate " + tickRate + " is not " + SimConstants.TickRate);
            ulong seed = ReadU64(s);
            string paramsJson = ReadString(s, BvrFormat.MaxParamsJsonBytes, "paramsJson");
            ulong paramsHash = ReadU64(s);
            uint duration = ReadU32(s);

            var header = new ReplayHeader(modeId, modeSimVersion, appVersion, (PhysicsBackend)backend, seed, paramsJson, duration);
            if (header.ParamsHash != paramsHash) throw new InvalidDataException("paramsHash does not match paramsJson");
            return header;
        }

        static ReplayResult ReadResult(BvrPayloadReader r, uint duration)
        {
            string winnerId = r.ReadString(BvrFormat.MaxStringBytes, "winnerId");
            int winnerIndex = r.ReadZigZagInt32("winnerIndex");
            if (winnerIndex < -1) throw new InvalidDataException("winnerIndex " + winnerIndex + " < -1");
            int count = r.ReadCount(BvrFormat.MaxResultScores, "Scores");
            var scores = new int[count];
            for (int i = 0; i < count; i++) scores[i] = r.ReadZigZagInt32("Score");
            uint finishTick = r.ReadVarUInt32("finishTick");
            if (finishTick > duration) throw new InvalidDataException("finishTick beyond durationTicks");
            if (!r.AtEnd) throw new InvalidDataException("Trailing bytes in RSLT chunk");
            return new ReplayResult(winnerId, winnerIndex, scores, finishTick);
        }

        /// <summary>Applies a tick delta; the resulting tick must stay within 0..durationTicks.</summary>
        static uint Advance(ref uint lastTick, BvrPayloadReader r, uint duration)
        {
            ulong delta = r.ReadVarULong();
            if (delta > (ulong)(duration - lastTick)) throw new InvalidDataException("Record tick beyond durationTicks");
            lastTick += (uint)delta;
            return lastTick;
        }

        static string ReadString(Stream s, int maxBytes, string what)
        {
            ulong len = VarInt.ReadULong(s);
            if (len > (ulong)maxBytes) throw new InvalidDataException(what + " is longer than " + maxBytes + " bytes");
            if (s.CanSeek && len > (ulong)(s.Length - s.Position)) throw new InvalidDataException("Truncated " + what);
            var buf = new byte[(int)len];
            ReadExact(s, buf, buf.Length);
            return BvrEncoding.DecodeString(buf, 0, buf.Length);
        }

        static ushort ReadU16(Stream s)
        {
            Span<byte> tmp = stackalloc byte[2];
            ReadExact(s, tmp);
            return BinaryPrimitives.ReadUInt16LittleEndian(tmp);
        }

        static uint ReadU32(Stream s)
        {
            Span<byte> tmp = stackalloc byte[4];
            ReadExact(s, tmp);
            return BinaryPrimitives.ReadUInt32LittleEndian(tmp);
        }

        static ulong ReadU64(Stream s)
        {
            Span<byte> tmp = stackalloc byte[8];
            ReadExact(s, tmp);
            return BinaryPrimitives.ReadUInt64LittleEndian(tmp);
        }

        static void ReadExact(Stream s, Span<byte> dst)
        {
            int off = 0;
            while (off < dst.Length)
            {
                int n = s.Read(dst.Slice(off));
                if (n <= 0) throw new InvalidDataException("Unexpected end of replay");
                off += n;
            }
        }

        static void ReadExact(Stream s, byte[] dst, int count)
        {
            ReadExact(s, new Span<byte>(dst, 0, count));
        }

        static int ReadUpTo(Stream s, byte[] dst, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n = s.Read(dst, off, count - off);
                if (n <= 0) break;
                off += n;
            }
            return off;
        }

        /// <summary>Reads a payload; non-seekable streams grow the buffer block by block so a false length fails before allocating it.</summary>
        static byte[] ReadPayload(Stream s, int length)
        {
            if (s.CanSeek || length <= ReadBlockBytes)
            {
                var buf = new byte[length];
                ReadExact(s, buf, length);
                return buf;
            }
            var ms = new MemoryStream();
            var block = new byte[ReadBlockBytes];
            int left = length;
            while (left > 0)
            {
                int want = Math.Min(left, block.Length);
                ReadExact(s, block, want);
                ms.Write(block, 0, want);
                left -= want;
            }
            return ms.ToArray();
        }

        static void Skip(Stream s, uint length)
        {
            if (s.CanSeek)
            {
                s.Seek(length, SeekOrigin.Current);
                return;
            }
            var block = new byte[(int)Math.Min(length, (uint)ReadBlockBytes)];
            long left = length;
            while (left > 0)
            {
                int want = (int)Math.Min(left, block.Length);
                ReadExact(s, block, want);
                left -= want;
            }
        }
    }
}
